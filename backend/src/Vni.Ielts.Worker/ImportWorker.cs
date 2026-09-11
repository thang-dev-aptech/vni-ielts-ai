using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Infrastructure.Content.Import;
using Vni.Ielts.Infrastructure.Observability;

namespace Vni.Ielts.Worker;

/// <summary>
/// Turns uploaded exam packages into review drafts, out of band.
///
/// <b>Twin of <see cref="MarkingWorker"/>, deliberately.</b> Claim atomically,
/// renew the lease while working, back off on a transient failure, give up in
/// public when the budget is spent, and let a job in hand finish through a
/// shutdown. Every one of those exists here for the reason that class records,
/// and one more besides: an import pays for several things separately — a
/// parse, a transcription, forty explanations — so a job that restarts from
/// the beginning does not merely waste time, it re-buys a Cambridge paper.
/// That is what <see cref="ImportJobStage"/> and <c>AdvanceAsync</c> are for,
/// and why this worker reports its stage as it goes rather than at the end.
///
/// ── The three things this worker has to get right that the twin does not ──
///
/// <b>The archive lives somewhere else.</b> The API and this process do not
/// share memory or a request. The ZIP was parked in
/// <see cref="IImportArchiveStore"/> before the job was enqueued, and this
/// worker reads it back by key. An archive that is gone is a <i>terminal</i>
/// failure with a sentence, never an exception out of the loop.
///
/// <b>The stream from that store is not seekable.</b> An object-storage
/// response body is forward-only;
/// <see cref="ExamPackageImportPipeline.ImportAsync"/> reads the ZIP central
/// directory, which lives at the end of the file, and then re-reads the whole
/// archive. So it is spooled to a temporary file first. Skipping that produces
/// a truncated read that looks exactly like a corrupt package.
///
/// <b>The attempt budget is enforced here, because only here can it be.</b>
/// <see cref="ImportJob.MayRetry"/> existed and nothing called it; the
/// outbox deliberately does not decide, because "did not work this time" and
/// "will not work however many times" is a judgement only the caller can make.
/// A promise in a comment that no code keeps is how a budget quietly becomes
/// infinite — and every retry here may be a paid parse.
/// </summary>
public sealed class ImportWorker(
    IServiceScopeFactory scopes,
    IClock clock,
    WorkerHealthState health,
    ILogger<ImportWorker> logger) : BackgroundService
{
    /// <summary>
    /// How long to wait when there is nothing to do. Five seconds, matching
    /// the twin: an import is not interactive, and polling faster spends a
    /// query a second on a queue that is empty almost all the time.
    /// </summary>
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a claim is honoured before another worker may take it over.
    ///
    /// <b>Ten minutes, where marking uses two, and the difference is not
    /// drift.</b> A marking job is one provider call; an import is an
    /// extraction, a parse of a whole paper, key injection and four layers of
    /// cross-check. The renewal below is still what makes the number safe —
    /// it only has to outlive a stall in the heartbeat, not the slowest
    /// import — but a first claim happens before any heartbeat, so the
    /// starting lease has to cover the gap to the first renewal with room for
    /// a slow start.
    /// </summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(40);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        logger.LogInformation("Import worker started.");

        try
        {
            while (!stopping.IsCancellationRequested)
            {
                health.RecordPoll();

                bool worked;

                try
                {
                    worked = await RunOnceAsync(stopping);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    // The loop survives anything one iteration can throw — a
                    // database blip that killed this worker would leave every
                    // upload owed with nothing to restart it.
                    logger.LogError(e, "Import worker iteration failed. Continuing.");
                    worked = false;
                }

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
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            health.RecordFatal(e);
            logger.LogCritical(e, "Import worker's loop exited with an unrecovered exception.");
            throw;
        }

        logger.LogInformation("Import worker stopped.");
    }

    /// <summary>
    /// One claim-and-import. <c>false</c> when the queue had nothing due, so
    /// the caller knows to sleep.
    ///
    /// <b>Public so a test can pump the worker exactly once.</b> Driving the
    /// hosted loop instead would make every assertion a race against a five
    /// second timer, and the properties worth pinning here — a job abandoned
    /// mid-import is picked up again, a lease lost mid-import stops the worker
    /// that lost it, a budget that is spent ends in Failed — are about what
    /// one pass does, not about how often passes happen.
    /// </summary>
    public async Task<bool> RunOnceAsync(CancellationToken stopping)
    {
        // A scope per job, not per process: the stores are scoped services.
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;

        var outbox = services.GetRequiredService<IImportOutbox>();
        var archives = services.GetRequiredService<IImportArchiveStore>();
        var pipeline = services.GetRequiredService<ExamPackageImportPipeline>();

        var leaseToken = Guid.NewGuid().ToString("n");

        var job = await outbox.ClaimAsync(leaseToken, Lease, stopping);
        if (job is null) return false;

        logger.LogInformation(
            "Importing {Definition} v{Version}, attempt {Attempt} of {Max}, from stage {Stage}.",
            job.DefinitionId.Value, job.VersionNumber, job.Attempts, ImportJob.MaxAttempts, job.Stage);

        using var beating = new CancellationTokenSource();
        var heartbeat = RenewAsync(outbox, job, leaseToken, beating.Token);

        /*
         * <b>The trace the upload started, continued in another process.</b>
         * Same mechanism and same reason as `MarkingWorker`: parsing
         * `job.TraceParent` makes this span a child of the request that
         * enqueued the job, so "the operator uploaded and no draft appeared"
         * is one trace rather than two nobody can join. A malformed or absent
         * value simply starts a new trace — telemetry must never fail the work
         * it describes.
         *
         * The tags are identifiers and counters. Never a finding message, a
         * passage, a key or a file name: a span leaves this machine.
         */
        var parent = default(ActivityContext);
        var hasParent = job.TraceParent is { Length: > 0 } tp
            && ActivityContext.TryParse(tp, null, out parent);

        using var span = hasParent
            ? Telemetry.Source.StartActivity("import.job", ActivityKind.Consumer, parent)
            : Telemetry.Source.StartActivity("import.job", ActivityKind.Consumer);

        span?.SetTag("vni.attempt", job.Attempts);

        var started = Stopwatch.GetTimestamp();
        var outcome = "failed";

        try
        {
            /*
             * `CancellationToken.None`, not `stopping` — F2.3, exactly as the
             * twin does it. Once a job is claimed, a deploy must not tear it
             * out of the middle of a paid parse and have the catch below read
             * that as a failure worth burning an attempt on. Claiming already
             * stopped; `HostOptions.ShutdownTimeout` bounds the grace.
             */
            outcome = await ImportAsync(
                outbox, archives, pipeline, job, leaseToken, span, CancellationToken.None);
        }
        catch (Exception e)
        {
            // The exception TYPE on the span, never its message: a provider or
            // driver message can carry request content, and a span is
            // exported. → F4.2
            span?.SetStatus(ActivityStatusCode.Error, e.GetType().Name);
            await GiveUpOrRetryAsync(outbox, archives, job, leaseToken, e);
        }
        finally
        {
            Telemetry.QueueJobs.Add(
                1,
                new KeyValuePair<string, object?>("vni.outcome", outcome),
                new KeyValuePair<string, object?>("vni.queue", "import"));

            Telemetry.QueueJobDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalSeconds,
                new KeyValuePair<string, object?>("vni.outcome", outcome));

            await beating.CancelAsync();
            await heartbeat;
        }

        return true;
    }

    /// <returns>The outcome tag, for the metric.</returns>
    private async Task<string> ImportAsync(
        IImportOutbox outbox, IImportArchiveStore archives, ExamPackageImportPipeline pipeline,
        ImportJob job, string leaseToken, Activity? span, CancellationToken ct)
    {
        await using var archive = await archives.OpenAsync(job.ArchiveKey, ct);

        if (archive is null)
        {
            /*
             * <b>Terminal, and it must be.</b> The upload is not coming back —
             * retrying twice more would spend two more claims to learn the
             * same thing, and leave the job looking busy meanwhile. The
             * operator's answer is "upload it again", which they can only act
             * on if they are told.
             */
            await FailAndForgetAsync(
                outbox, archives, job, leaseToken,
                "Không tìm thấy tệp gói đã tải lên trong kho lưu trữ. "
                + "The uploaded archive is no longer in storage, so this import cannot be run. "
                + "Upload the package again.");

            span?.SetStatus(ActivityStatusCode.Error, "ArchiveMissing");
            return "failed";
        }

        /*
         * <b>Spooled, because the pipeline needs to seek and this stream
         * cannot.</b> Inspection reads the ZIP central directory at the end of
         * the file; extraction then re-reads the whole archive. An
         * object-storage response body is forward-only, and handing it over
         * directly is a truncated read that presents as a corrupt package.
         * The same spool `UploadPackageEndpoint` does for a non-seekable form
         * part. → `IExamPackageArchiveInspector`
         */
        await using var seekable = new FileStream(
            Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            81_920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        await archive.CopyToAsync(seekable, ct);
        seekable.Position = 0;

        var stages = new StageRecorder(outbox, job.OperationId, leaseToken, logger);

        var attempt = await pipeline.ImportAsync(
            seekable, job.DefinitionId, job.VersionNumber, ct, stages);

        // Every stage report is a write; they are drained before the job is
        // settled so a Done never lands ahead of the stage that preceded it.
        await stages.DrainAsync();

        if (!attempt.IsAccepted || attempt.Draft is null)
        {
            /*
             * <b>Terminal, not transient.</b> A package that was extracted and
             * then refused — a bomb, a path escape, a schema failure, a rights
             * refusal — is permanently bad. Burning the remaining attempts on
             * it would pay up to three times to learn what the first attempt
             * already proved. This is precisely the judgement
             * <c>RetryAsync</c> and <c>FailAsync</c> were split apart to let
             * a caller make.
             *
             * The reason stored is the refusal's own code and message, which
             * this repository's own validators wrote — bounded, authored here,
             * and naming a path rather than quoting a passage or a key.
             */
            var reason = attempt.Findings.Count > 0
                ? $"{attempt.Findings[0].Code} at {attempt.Findings[0].Path}: {attempt.Findings[0].Message}"
                : "The package was refused, and the pipeline reported no finding to say why.";

            await FailAndForgetAsync(outbox, archives, job, leaseToken, reason);

            logger.LogWarning(
                "Import of {Definition} v{Version} was refused: {Code}.",
                job.DefinitionId.Value, job.VersionNumber,
                attempt.Findings.Count > 0 ? attempt.Findings[0].Code : "no finding");

            span?.SetStatus(ActivityStatusCode.Error, "PackageRefused");
            return "refused";
        }

        var draftId = attempt.Draft.Id;

        // Done and the draft id in one write, so a completed job can never be
        // seen carrying no draft.
        await AdvanceAsync(outbox, job.OperationId, leaseToken, ImportJobStage.Done, draftId);

        var completed = await outbox.CompleteAsync(job.OperationId, leaseToken, CancellationToken.None);

        if (!completed)
        {
            /*
             * <b>Somebody took this job over while we were inside it.</b> The
             * draft is written — the pipeline is idempotent on its own content
             * hash — but this worker no longer owns the job, so it must not
             * mark it done and, above all, must not delete the archive the new
             * owner is about to read.
             */
            logger.LogError(
                "Lost the lease on the import of {Definition} v{Version} before completing it. "
                + "Another worker owns it now; the stored archive is being left alone.",
                job.DefinitionId.Value, job.VersionNumber);

            span?.SetStatus(ActivityStatusCode.Error, "LeaseLost");
            return "lease-lost";
        }

        await ForgetArchiveAsync(archives, job);

        logger.LogInformation(
            "Imported {Definition} v{Version} into draft {Draft}.",
            job.DefinitionId.Value, job.VersionNumber, draftId);

        span?.SetStatus(ActivityStatusCode.Ok);
        return "completed";
    }

    /// <summary>
    /// The attempt budget, kept rather than promised.
    ///
    /// <b>Transient by default, terminal only where the caller knows.</b> An
    /// exception reaching here is a timeout, a 5xx, a driver blip or a bug —
    /// none of which the first attempt proves permanent. The permanent cases
    /// (a missing archive, a refused package) never reach this method; they are
    /// settled where they are recognised. So the rule is the one
    /// <see cref="ImportJob.MayRetry"/> states: retry while the budget allows,
    /// then fail in public with the reason stored rather than only logged.
    /// </summary>
    private async Task GiveUpOrRetryAsync(
        IImportOutbox outbox, IImportArchiveStore archives, ImportJob job, string leaseToken,
        Exception failure)
    {
        var reason = SafeReason(job.Stage, failure);

        if (!job.MayRetry)
        {
            logger.LogError(
                failure,
                "Import of {Definition} v{Version} failed {Attempts} times and is being given up "
                + "on. The job records why, so an operator sees a reason rather than a job that "
                + "looks stuck.",
                job.DefinitionId.Value, job.VersionNumber, job.Attempts);

            await FailAndForgetAsync(outbox, archives, job, leaseToken, reason);
            return;
        }

        var wait = BackoffFor(job.Attempts);

        logger.LogWarning(
            failure,
            "Import of {Definition} v{Version} failed on attempt {Attempt} of {Max}. Retrying in {Wait}.",
            job.DefinitionId.Value, job.VersionNumber, job.Attempts, ImportJob.MaxAttempts, wait);

        /*
         * <b>The archive is NOT deleted here.</b> A retry has to read the same
         * bytes; deleting on a transient failure would turn one timeout into a
         * permanently unimportable package, which is the failure this whole
         * queue exists to avoid.
         */
        await outbox.RetryAsync(
            job.OperationId, leaseToken, clock.UtcNow.Add(wait), reason, CancellationToken.None);
    }

    /// <summary>
    /// Gives up on the job permanently and removes the stored archive.
    ///
    /// <b>The archive is deleted only when the write actually landed.</b>
    /// <c>FailAsync</c> returning false means this worker's lease is gone and
    /// another worker owns the job — deleting the archive then would take the
    /// bytes out from under the process that is about to read them.
    /// </summary>
    private async Task FailAndForgetAsync(
        IImportOutbox outbox, IImportArchiveStore archives, ImportJob job, string leaseToken,
        string reason)
    {
        var settled = await outbox.FailAsync(
            job.OperationId, leaseToken, reason, CancellationToken.None);

        if (!settled)
        {
            logger.LogError(
                "Lost the lease on the import of {Definition} v{Version} before settling it. "
                + "Another worker owns it now; the stored archive is being left alone.",
                job.DefinitionId.Value, job.VersionNumber);
            return;
        }

        await ForgetArchiveAsync(archives, job);
    }

    /// <summary>
    /// <b>An uploaded exam package is third-party copyrighted material.</b>
    /// Keeping every upload forever is a rights exposure nobody chose, and the
    /// one moment it is certainly no longer needed is when the job settles. A
    /// failure to delete is logged and never fatal: a stray object is a
    /// hygiene problem, a lost or re-opened import is not.
    /// </summary>
    private async Task ForgetArchiveAsync(IImportArchiveStore archives, ImportJob job)
    {
        try
        {
            await archives.DeleteAsync(job.ArchiveKey, CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e, "Could not remove the stored archive for a settled import of {Definition} v{Version}.",
                job.DefinitionId.Value, job.VersionNumber);
        }
    }

    /// <summary>
    /// What is allowed to be stored on the job, and what is not.
    ///
    /// <b>The message of an arbitrary exception is not.</b> <c>LastError</c>
    /// reaches an operator through an admin API; a provider, driver or HTTP
    /// message can carry a response body, a connection string or the very
    /// paper being parsed. So only exceptions this repository authors are
    /// quoted, and everything else becomes a sentence naming the stage and the
    /// type. The full detail is logged, where it stays on the machine.
    /// </summary>
    private static string SafeReason(ImportJobStage stage, Exception failure) =>
        failure is ExamSourceParsingUnavailableException or ArgumentException
            ? $"{stage}: {failure.Message}"
            : $"Nhập gói thất bại ở bước {stage} ({failure.GetType().Name}). "
              + $"The import failed at stage {stage} with {failure.GetType().Name}. The full "
              + "detail is in the worker log and is deliberately not stored here, because a "
              + "provider or driver message can carry the paper it was reading.";

    /// <summary>
    /// Exponential, capped, jittered — the twin's curve. Jitter matters for
    /// the same reason: a provider outage fails every queued import at once,
    /// and without it they all come back together against something already
    /// struggling.
    /// </summary>
    private static TimeSpan BackoffFor(int attempts)
    {
        var seconds = Math.Min(300, Math.Pow(4, Math.Max(1, attempts)));
        var jittered = seconds * (0.75 + (Random.Shared.NextDouble() * 0.5));

        return TimeSpan.FromSeconds(jittered);
    }

    /// <summary>
    /// Records a stage, and tells a rewind apart from a stolen lease.
    ///
    /// <b><c>AdvanceAsync</c> returns false for two different reasons.</b> The
    /// lease is gone, or the requested stage would move the job backwards —
    /// and the store deliberately refuses the second, because a resumed worker
    /// re-entering a paid stage buys a Cambridge parse twice. A worker that
    /// read every false as "stop" would abandon every resumed job on its first
    /// stage report; one that read every false as "carry on" would keep working
    /// a job it no longer owns. <c>RenewAsync</c> answers which it was, because
    /// it is false only for the lease.
    /// </summary>
    private async Task<bool> AdvanceAsync(
        IImportOutbox outbox, string operationId, string leaseToken, ImportJobStage stage,
        Guid? draftId)
    {
        var advanced = await outbox.AdvanceAsync(
            operationId, leaseToken, stage, draftId, CancellationToken.None);

        if (advanced) return true;

        var stillOurs = await outbox.RenewAsync(
            operationId, leaseToken, Lease, CancellationToken.None);

        if (stillOurs)
        {
            // A refused rewind. Expected on a resumed job, which re-runs the
            // cheap early stages and reports them again.
            logger.LogDebug(
                "Stage {Stage} was not recorded because the job is already past it.", stage);
            return true;
        }

        logger.LogError(
            "Lost the lease while recording stage {Stage}: another worker has taken this import "
            + "over while this one is still running it.",
            stage);

        return false;
    }

    private async Task RenewAsync(
        IImportOutbox outbox, ImportJob job, string leaseToken, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(Heartbeat, stop);

                var kept = await outbox.RenewAsync(
                    job.OperationId, leaseToken, Lease, CancellationToken.None);

                if (kept)
                {
                    // The loop is inside one long RunOnceAsync and would
                    // otherwise look stale for the job's whole duration.
                    health.RecordPoll();
                    continue;
                }

                logger.LogError(
                    "Lost the lease on the import of {Definition} v{Version}: another worker has "
                    + "taken it over while this one is still running it. The parse is being "
                    + "performed twice.",
                    job.DefinitionId.Value, job.VersionNumber);

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

    /// <summary>
    /// Turns the pipeline's stage reports into <c>AdvanceAsync</c> writes.
    ///
    /// <b><see cref="IProgress{T}.Report"/> is synchronous and the write is
    /// not, so the writes are chained rather than fired and forgotten.</b>
    /// <c>Progress&lt;T&gt;</c> posts each callback to the thread pool with no
    /// ordering guarantee, and a stage arriving after the job has been
    /// completed — or two arriving out of order — is precisely the lost or
    /// rewound stage that makes a resumed worker re-buy a parse. Each report
    /// is appended to a single task chain, and <see cref="DrainAsync"/> is
    /// awaited before the job is settled.
    ///
    /// <b>Nothing here may throw into the pipeline.</b> A bookkeeping failure
    /// must not fail an import that has already been paid for; the pipeline
    /// guards the call as well, on the same reasoning from the other side.
    /// </summary>
    private sealed class StageRecorder(
        IImportOutbox outbox, string operationId, string leaseToken, ILogger logger)
        : IProgress<ImportJobStage>
    {
        private readonly Lock _gate = new();
        private Task _chain = Task.CompletedTask;

        public void Report(ImportJobStage stage)
        {
            lock (_gate)
            {
                _chain = _chain.ContinueWith(
                    _ => RecordAsync(stage),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default).Unwrap();
            }
        }

        /// <summary>Every write this recorder started, finished.</summary>
        public Task DrainAsync()
        {
            lock (_gate) return _chain;
        }

        private async Task RecordAsync(ImportJobStage stage)
        {
            try
            {
                await outbox.AdvanceAsync(
                    operationId, leaseToken, stage, null, CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    e, "Could not record that an import reached stage {Stage}.", stage);
            }
        }
    }
}
