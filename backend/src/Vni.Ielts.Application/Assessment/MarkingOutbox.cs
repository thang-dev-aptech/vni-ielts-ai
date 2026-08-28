using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Assessment;

/// <summary>
/// Where a marking job lives between the section closing and the band existing.
///
/// <b>Written 2026-08-28, to close a hole the code already described in prose
/// and did nothing about.</b>
///
/// A section closes, and then it is marked. Those are two writes, and the
/// transition has to come first — otherwise two callers arriving together, two
/// tabs on "Tiếp theo" or a retry from a phone that changed network, both mark
/// the section and the evaluation is bought twice. So the order is right, and
/// the cost of it is a window: a process that dies between the transition and
/// the marking leaves a section closed and unmarked.
///
/// For Reading and Listening that window is survivable, because a deterministic
/// score can be recomputed from the answer key on the next results read. For
/// Writing and Speaking it was <b>permanent</b>: a re-entered submit
/// short-circuits on a sitting that is no longer in progress, and the catch-up
/// pass skips the non-deterministic modules on purpose — re-running an
/// evaluation on every visit to a results screen would be an unbounded paid
/// retry the moment a provider is wired.
///
/// The band was gone for the life of the sitting, and nothing anywhere said so.
///
/// <b>An outbox makes the intent durable instead of the attempt.</b> Closing a
/// section records that this section <i>needs</i> marking; a worker turns that
/// into a band, and can crash, be restarted, or be deployed over without
/// losing the fact that the work is owed.
/// </summary>
public enum MarkingJobState
{
    /// <summary>Owed, and nobody is working on it.</summary>
    Pending,

    /// <summary>Claimed by a worker, whose lease is being renewed.</summary>
    Running,

    /// <summary>
    /// Failed in a way that might not fail again — a timeout, a 5xx, a
    /// rate limit. Waiting for its backoff to elapse.
    /// </summary>
    Retryable,

    /// <summary>
    /// Out of attempts, or refused in a way no retry can fix.
    ///
    /// <b>A terminal state that is stored rather than logged.</b> A learner
    /// looking at a dash is owed a reason, and "we tried nine times and gave
    /// up" is a different reason from "no evaluator is wired". Dead-lettering
    /// into a log leaves the results screen unable to tell them apart.
    /// </summary>
    Failed,

    /// <summary>Marked. The band is in the marking store.</summary>
    Completed,
}

/// <summary>
/// One section's marking, owed.
/// </summary>
/// <param name="OperationId">
/// Stable, and derived rather than generated.
///
/// <b>`{session}:{module}:{rubricVersion}` — and the rubric version is in it
/// deliberately.</b> Re-closing the same section must not enqueue a second job,
/// so the id cannot be random; but a rubric changing is a genuinely different
/// judgement, and pinning the id to the session alone would silently refuse to
/// re-mark under a corrected rubric. A unique index on this field is what makes
/// the enqueue idempotent, in the database rather than in a caller's memory.
///
/// It is also what a paid provider is given as its own idempotency key, so a
/// retry after a lost response does not buy a second evaluation. → `I3.5`
/// </param>
/// <param name="Attempts">
/// How many times this has been claimed. The backoff is computed from it, and
/// exhausting the budget is what moves the job to <see cref="MarkingJobState.Failed"/>.
/// </param>
/// <param name="LeaseUntil">
/// When another worker may take this job over. Renewed while a worker is inside
/// it, so the lease bounds <i>death</i> rather than duration — the same lesson
/// as the idempotency guard's heartbeat, learned in the same week.
/// </param>
public sealed record MarkingJob(
    string OperationId,
    ExamSessionId SessionId,
    ExamModule Module,
    string RubricVersion,
    MarkingJobState State,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? LeaseUntil,
    string? LeaseToken,
    string? LastError,
    DateTimeOffset? CompletedAt)
{
    /// <summary>The id a re-close of this section would produce. → <see cref="OperationId"/>.</summary>
    public static string IdFor(ExamSessionId sessionId, ExamModule module, string rubricVersion) =>
        $"{sessionId.Value}:{module.ToString().ToLowerInvariant()}:{rubricVersion}";
}

public interface IMarkingOutbox
{
    /// <summary>
    /// Records that a section is owed a marking. Idempotent on the operation id.
    ///
    /// <b>Enqueued at closure, in the same call that freezes the answer sheet
    /// and moves the sitting.</b> Not in a transaction with them — the three
    /// live in three collections and a transaction over all of them would put
    /// every section transition through a distributed commit for a job that is
    /// safe to enqueue twice. The unique index is what makes twice harmless,
    /// and the ordering is what makes "enqueued but never closed" impossible:
    /// the enqueue comes after the freeze, so a job exists only for a section
    /// whose answers can no longer change.
    ///
    /// @returns whether this call created the job. False means it was already
    /// there, which is the ordinary answer for a retried submit.
    /// </summary>
    Task<bool> EnqueueAsync(MarkingJob job, CancellationToken ct);

    /// <summary>
    /// Takes one job that is due, atomically.
    ///
    /// <b>One statement, and it has to be.</b> Finding a due job and then
    /// marking it running is two, and two workers fit between them — which
    /// means two paid evaluations for one essay. The filter names the states
    /// that may be claimed and the lease that must have expired; whichever
    /// worker's update matches owns it.
    ///
    /// @returns null when nothing is due, which is the normal state.
    /// </summary>
    Task<MarkingJob?> ClaimAsync(
        string leaseToken, DateTimeOffset now, TimeSpan lease, CancellationToken ct);

    /// <summary>
    /// Pushes the lease forward while a worker is inside the job.
    ///
    /// @returns false when this worker no longer owns it — a takeover happened,
    /// which means the job is running twice and somebody needs to know.
    /// </summary>
    Task<bool> RenewAsync(
        string operationId, string leaseToken, DateTimeOffset until, CancellationToken ct);

    /// <summary>Marks the job done. Filtered on the lease token, so a resurrected worker cannot.</summary>
    Task<bool> CompleteAsync(
        string operationId, string leaseToken, DateTimeOffset at, CancellationToken ct);

    /// <summary>Schedules another attempt, and records why this one did not work.</summary>
    Task<bool> RetryAsync(
        string operationId, string leaseToken, DateTimeOffset nextAttemptAt, string error,
        CancellationToken ct);

    /// <summary>Gives up, permanently and visibly. → <see cref="MarkingJobState.Failed"/>.</summary>
    Task<bool> FailAsync(
        string operationId, string leaseToken, string error, CancellationToken ct);

    /// <summary>Every job for one sitting, so a results screen can say what is owed.</summary>
    Task<IReadOnlyList<MarkingJob>> ListAsync(ExamSessionId sessionId, CancellationToken ct);
}

/// <summary>
/// Turns "this section closed" into "this marking is owed".
///
/// <b>A separate type rather than a method on the runner, because the two run
/// at different times and one of them must not depend on the other.</b>
/// Enqueuing happens inside the request that closes the section and has to be
/// cheap and total; running happens in a worker minutes later and may take as
/// long as a provider takes.
/// </summary>
public static class MarkingWork
{
    /// <summary>
    /// Records what this section owes, if it owes anything.
    ///
    /// <b>Only the modules whose band is a judgement.</b> Reading and Listening
    /// come from the answer key (`A-11`) and are recomputed on demand, so a job
    /// for them would be a queue entry for arithmetic.
    ///
    /// <b>No rubric means no job, and that is not a silent skip.</b> A rubric
    /// records which criteria were used and where their descriptors came from;
    /// a job enqueued without one would be a promise to mark against a standard
    /// nobody has stated. The results screen reports `AwaitingRubric`, which is
    /// the honest answer and a different one from "we tried and failed".
    /// </summary>
    public static async Task EnqueueAsync(
        ExamVersion version,
        ExamModule module,
        ExamSessionId sessionId,
        IMarkingOutbox outbox,
        IRubricSource rubrics,
        Vni.Ielts.Domain.Common.IClock clock,
        CancellationToken ct)
    {
        if (module is not (ExamModule.Writing or ExamModule.Speaking)) return;
        if (version.Section(module) is null) return;

        if (rubrics.For(module) is not { } rubric) return;

        var now = clock.UtcNow;

        await outbox.EnqueueAsync(
            new MarkingJob(
                MarkingJob.IdFor(sessionId, module, rubric.Version),
                sessionId,
                module,
                rubric.Version,
                MarkingJobState.Pending,
                Attempts: 0,
                CreatedAt: now,
                NextAttemptAt: now,
                LeaseUntil: null,
                LeaseToken: null,
                LastError: null,
                CompletedAt: null),
            ct);
    }
}
