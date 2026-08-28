using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Worker;

/// <summary>
/// Turns owed markings into bands.
///
/// <b>Written 2026-08-28. What stood here was the project template</b> — a loop
/// that logged the time once a second and did nothing, which is worse than an
/// empty file because it looks like a running system.
///
/// ── What this has to get right, and why each one is here ──────────────────
///
/// <b>Claim atomically.</b> Finding a due job and then marking it running is two
/// statements, and two workers fit between them. For this queue that means two
/// paid provider calls for one essay, and a band that can move after a learner
/// has already seen it. The claim is one filtered update.
///
/// <b>Renew the lease while working.</b> A lease chosen against an estimate is a
/// bet on how long a provider takes; a heartbeat removes the need to guess. So
/// the lease bounds <i>death</i> — a process that stops renewing has stopped —
/// rather than bounding duration, which it was never good at.
///
/// <b>Back off, and give up in public.</b> A transient failure is retried with
/// growing delay and jitter; an exhausted budget moves the job to
/// <see cref="MarkingJobState.Failed"/> with the reason stored, not logged. A
/// learner looking at a dash is owed the difference between "no evaluator is
/// wired" and "we tried nine times and stopped".
///
/// <b>Shut down without abandoning work.</b> A deploy cancels the token; the
/// loop stops claiming and lets the job in hand finish. Killing it mid-provider
/// call would leave a lease to expire and an evaluation already paid for.
///
/// <b>And it does not log a heartbeat.</b> An idle queue should be silent. A
/// line a second is how a log stops being read.
/// </summary>
public sealed class MarkingWorker(
    IServiceScopeFactory scopes,
    IClock clock,
    ILogger<MarkingWorker> logger) : BackgroundService
{
    /// <summary>
    /// How long to wait when there is nothing to do.
    ///
    /// <b>Five seconds, and the number is a trade against how long a learner
    /// waits for a band.</b> Marking is not interactive — the results screen
    /// already reports "waiting" honestly — so this can be generous. Polling
    /// faster would spend a query per second per worker for a queue that is
    /// empty almost all the time.
    /// </summary>
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a claim is honoured before another worker may take it over.
    ///
    /// Two minutes, renewed every forty seconds while the job runs. The renewal
    /// is what makes the number safe: it only has to outlive a stall in the
    /// heartbeat, not the slowest provider call.
    /// </summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(40);

    /// <summary>
    /// How many times a job is attempted before it is given up on.
    ///
    /// <b>Five, and every one of them may cost money.</b> The budget exists
    /// because the alternatives are both bad: retrying for ever turns a
    /// permanently broken provider into an unbounded bill, and not retrying at
    /// all turns one timeout into a missing band for the life of the sitting.
    /// </summary>
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        logger.LogInformation("Marking worker started.");

        while (!stopping.IsCancellationRequested)
        {
            bool worked;

            try
            {
                worked = await PumpAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                /*
                 * <b>The loop survives anything one iteration can throw.</b> A
                 * database blip that killed the worker would leave every sitting
                 * unmarked until somebody noticed the process was gone — and
                 * nothing here restarts it. Logged loudly, waited out, retried.
                 */
                logger.LogError(e, "Marking worker iteration failed. Continuing.");
                worked = false;
            }

            // Only pause when there was nothing to do. A queue with a backlog
            // should drain at the speed of the provider, not of this timer.
            if (!worked)
            {
                try
                {
                    await Task.Delay(Idle, stopping);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        logger.LogInformation("Marking worker stopped.");
    }

    /// <returns>Whether a job was claimed, so the caller knows not to sleep.</returns>
    private async Task<bool> PumpAsync(CancellationToken stopping)
    {
        /*
         * <b>A scope per job, not per process.</b> The stores are scoped
         * services — they hold a Mongo context and, in time, a request-shaped
         * unit of work — and a singleton worker resolving them once would
         * share one instance across every job for the life of the process.
         */
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;

        var outbox = services.GetRequiredService<IMarkingOutbox>();
        var leaseToken = Guid.NewGuid().ToString("n");

        var job = await outbox.ClaimAsync(leaseToken, clock.UtcNow, Lease, stopping);
        if (job is null) return false;

        logger.LogInformation(
            "Marking {Module} for {Session}, attempt {Attempt} of {Max}.",
            job.Module, job.SessionId.Value, job.Attempts, MaxAttempts);

        using var beating = new CancellationTokenSource();
        var heartbeat = RenewAsync(outbox, job, leaseToken, beating.Token);

        try
        {
            await RunAsync(services, job, stopping);

            await outbox.CompleteAsync(
                job.OperationId, leaseToken, clock.UtcNow, CancellationToken.None);

            logger.LogInformation(
                "Marked {Module} for {Session}.", job.Module, job.SessionId.Value);
        }
        catch (Exception e)
        {
            await GiveUpOrRetryAsync(outbox, job, leaseToken, e);
        }
        finally
        {
            await beating.CancelAsync();
            await heartbeat;
        }

        return true;
    }

    /// <summary>
    /// Marks the section this job names.
    ///
    /// <b>The runner is the same one the API calls at closure</b>, deliberately:
    /// two implementations of "what marking means" would drift, and the one
    /// that drifts is always the one nobody has filed a bug against yet. It is
    /// also already idempotent — it reads what is marked before it marks — so a
    /// retry after a lost response does not buy a second evaluation.
    /// </summary>
    private static async Task RunAsync(
        IServiceProvider services, MarkingJob job, CancellationToken ct)
    {
        var catalogue = services.GetRequiredService<IExamCatalogue>();
        var sessions = services.GetRequiredService<IExamSessionRepository>();
        var answers = services.GetRequiredService<IAnswerSheetStore>();
        var marker = services.GetRequiredService<SectionMarkingRunner>();

        var session = await sessions.FindAsync(job.SessionId, ct)
            ?? throw new InvalidOperationException(
                $"Sitting {job.SessionId.Value} no longer exists.");

        var version = await catalogue.FindAsync(session.ExamVersionId, ct)
            ?? throw new InvalidOperationException(
                $"Exam version {session.ExamVersionId.Value} no longer exists.");

        var outcomes = await marker.RunAsync(version, job.Module, job.SessionId, answers, ct);

        /*
         * <b>An outcome that is not a marking is not a success.</b>
         *
         * "Awaiting evaluator" and "awaiting transcript" describe a product
         * that is not finished, and completing the job on them would delete the
         * record that this section is still owed a band — quietly, and for
         * good. They are thrown so the job stays in the queue, backs off, and
         * eventually dead-letters with the reason visible on the results
         * screen. → `MarkingAvailability`
         *
         * `NothingSubmitted` is the exception: the learner wrote nothing, there
         * is no band to produce, and retrying will not change that.
         */
        var unresolved = outcomes
            .Where(o => o.Availability is not
                (MarkingAvailability.Marked or MarkingAvailability.NothingSubmitted))
            .ToList();

        if (unresolved.Count > 0)
        {
            throw new MarkingNotReadyException(
                string.Join(
                    "; ",
                    unresolved.Select(o => $"{o.Availability}{(o.Detail is null ? "" : $": {o.Detail}")}")));
        }
    }

    private async Task GiveUpOrRetryAsync(
        IMarkingOutbox outbox, MarkingJob job, string leaseToken, Exception failure)
    {
        var reason = failure is MarkingNotReadyException ? failure.Message : failure.ToString();

        if (job.Attempts >= MaxAttempts)
        {
            logger.LogError(
                failure,
                "Marking {Module} for {Session} failed {Attempts} times and is being given up "
                + "on. The results screen will report it rather than showing a bare dash.",
                job.Module, job.SessionId.Value, job.Attempts);

            await outbox.FailAsync(job.OperationId, leaseToken, reason, CancellationToken.None);
            return;
        }

        var wait = BackoffFor(job.Attempts);

        logger.LogWarning(
            failure,
            "Marking {Module} for {Session} failed on attempt {Attempt}. Retrying in {Wait}.",
            job.Module, job.SessionId.Value, job.Attempts, wait);

        await outbox.RetryAsync(
            job.OperationId, leaseToken, clock.UtcNow.Add(wait), reason, CancellationToken.None);
    }

    /// <summary>
    /// Exponential, capped, jittered.
    ///
    /// <b>Jitter is not decoration here.</b> A provider outage fails every job
    /// in the queue at once; without jitter they all come back at the same
    /// instant, fail together, and double together — a thundering herd against
    /// something that is already struggling.
    /// </summary>
    private static TimeSpan BackoffFor(int attempts)
    {
        var seconds = Math.Min(300, Math.Pow(4, Math.Max(1, attempts)));
        var jittered = seconds * (0.75 + (Random.Shared.NextDouble() * 0.5));

        return TimeSpan.FromSeconds(jittered);
    }

    private async Task RenewAsync(
        IMarkingOutbox outbox, MarkingJob job, string leaseToken, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(Heartbeat, stop);

                var kept = await outbox.RenewAsync(
                    job.OperationId, leaseToken, clock.UtcNow.Add(Lease), CancellationToken.None);

                if (kept) continue;

                /*
                 * <b>Somebody took this job over while we were inside it.</b>
                 * Which means it is running twice, and one of those runs is
                 * buying a second evaluation. Said out loud, because a lease
                 * that is too short stays too short until somebody sees this.
                 */
                logger.LogError(
                    "Lost the lease on {Module} for {Session}: another worker has taken it "
                    + "over while this one is still running it. The evaluation is being "
                    + "performed twice.",
                    job.Module, job.SessionId.Value);

                return;
            }
        }
        catch (OperationCanceledException)
        {
            // The job finished. Ordinary.
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Lease renewal failed; the lease will expire normally.");
        }
    }
}

/// <summary>
/// The section could not be marked yet, for a reason that is not an error.
///
/// <b>Thrown so the job stays owed.</b> "Awaiting evaluator" is a true
/// statement about a product that is not finished; completing the job on it
/// would delete the record that a band is still due, and the learner's dash
/// would then have no explanation behind it at all.
/// </summary>
public sealed class MarkingNotReadyException(string reason) : Exception(reason);
