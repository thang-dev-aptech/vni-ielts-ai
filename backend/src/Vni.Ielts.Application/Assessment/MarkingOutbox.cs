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
    DateTimeOffset? CompletedAt,
    /// <summary>
    /// The W3C `traceparent` of the request that enqueued this job.
    ///
    /// <b>F4.2 — the queue is where a trace would otherwise end.</b> Marking
    /// happens in another process, minutes later; without carrying the
    /// context, the worker's span starts a brand-new trace and "why did this
    /// learner's Writing never come back" cannot be joined to the submit that
    /// asked for it. Storing it on the job is the only carrier available —
    /// there is no message broker here, the job row *is* the message.
    ///
    /// A string rather than an `ActivityContext`: this is the Application
    /// layer, the value is persisted, and W3C traceparent is a stable wire
    /// format that survives a database change. Null when nothing was tracing.
    /// </summary>
    string? TraceParent = null,
    /// <summary>
    /// The idempotency key of the last Failed→Pending reopen.
    ///
    /// Replay of the same key returns this job in whatever state it has
    /// reached, rather than opening a second run. A different key is only
    /// legal while the job is Failed.
    /// </summary>
    string? ReopenKey = null,
    DateTimeOffset? ReopenedAt = null,
    /// <summary>When the job last entered <see cref="MarkingJobState.Failed"/>. Newest-failed listing sorts on this.</summary>
    DateTimeOffset? FailedAt = null)
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

    /// <summary>
    /// Every job for many sittings, in one read.
    ///
    /// <b>The history list's read, and the reason it exists.</b> Whether a
    /// sitting's overall band is final depends on whether any of its markings
    /// is still owed, which is a question only this collection can answer — so
    /// `W1` made the history query ask it once per mock, and a learner with
    /// fifty mocks bought fifty round trips for one screen. The same list,
    /// asked for once. → slice `W10`
    ///
    /// Mirrors <see cref="ISectionMarkingStore.ListManyAsync"/> in every
    /// respect, deliberately: the two are always called together over the same
    /// set of ids, and a caller that had to remember which of them omits empty
    /// sittings would eventually remember wrong.
    /// </summary>
    Task<IReadOnlyDictionary<ExamSessionId, IReadOnlyList<MarkingJob>>> ListManyAsync(
        IReadOnlyCollection<ExamSessionId> sessionIds, CancellationToken ct);

    /// <summary>
    /// How much work is owed, and how long the oldest piece has been waiting.
    ///
    /// <b>F4.3 — depth alone cannot tell a busy queue from a stuck one.</b> A
    /// depth of fifty is ordinary when fifty sittings just ended and fifty
    /// markings are seconds old; it is an incident when the oldest is an hour
    /// old, because that is a learner still looking at a dash. The age is the
    /// signal that distinguishes them, so both are returned together and from
    /// one point in time — reading them separately can report a depth from
    /// before a drain and an age from after it.
    ///
    /// Counts only work that is <i>owed</i>: Pending and Retryable, plus jobs
    /// whose lease has expired (a worker died holding them). A job a live
    /// worker is inside is not backlog.
    /// </summary>
    Task<QueueBacklog> BacklogAsync(DateTimeOffset asOf, CancellationToken ct);

    /// <summary>
    /// Pages jobs across sittings, optionally filtered by state and module.
    /// Failed jobs are newest-first on the moment they died.
    /// </summary>
    Task<MarkingJobPage> QueryAsync(MarkingJobQuery query, CancellationToken ct) =>
        throw new NotSupportedException("This outbox does not provide cross-session job listing.");

    /// <summary>
    /// Moves a Failed job back to Pending so a worker will run it again.
    ///
    /// <b>The operation id does not change.</b> A rerun is a new evaluation of
    /// the same owed marking, which then supersedes the prior band; it is not
    /// a second queue entry. The idempotency key is what makes two operators
    /// hitting retry, or one retry retried, produce at most one such transition.
    /// </summary>
    Task<MarkingJobReopenResult> ReopenFailedAsync(
        string operationId, string idempotencyKey, DateTimeOffset now, CancellationToken ct) =>
        throw new NotSupportedException("This outbox does not provide a failed-job reopen.");
}

/// <param name="Depth">Jobs owed and not currently being worked on.</param>
/// <param name="OldestAge">
/// How long the oldest owed job has waited since it was created.
/// <see cref="TimeSpan.Zero"/> when nothing is owed — an empty queue has no
/// oldest item, and reporting null would make every dashboard handle a gap.
/// </param>
public readonly record struct QueueBacklog(long Depth, TimeSpan OldestAge)
{
    public static readonly QueueBacklog Empty = new(0, TimeSpan.Zero);
}

/// <summary>Filters the operator-facing marking-job list across all sittings.</summary>
public sealed record MarkingJobQuery(
    MarkingJobState? State = null,
    ExamModule? Module = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50)
{
    public const int MaxPageSize = 50;

    public void Validate()
    {
        if (From is { } from && To is { } to && from > to)
            throw new ArgumentException("The job-list start must not be after its end.");
        if (Page < 1) throw new ArgumentOutOfRangeException(nameof(Page));
        if (PageSize < 1 || PageSize > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(PageSize));
    }
}

public sealed record MarkingJobPage(
    IReadOnlyList<MarkingJob> Items,
    long TotalCount,
    int Page,
    int PageSize);

public enum MarkingJobReopenStatus
{
    /// <summary>This call performed the Failed→Pending transition.</summary>
    Reopened,

    /// <summary>The same idempotency key already drove a reopen of this job.</summary>
    Replayed,

    /// <summary>No job with this operation id exists.</summary>
    NotFound,

    /// <summary>The job exists but is not Failed, and the key is not a replay.</summary>
    Illegal,

    /// <summary>Another caller won the Failed→Pending race with a different key.</summary>
    Conflict,
}

public sealed record MarkingJobReopenResult(
    MarkingJobReopenStatus Status,
    MarkingJob? Job,
    MarkingJobState? CurrentState = null);

public enum EvaluationAttemptOutcome
{
    Succeeded,
    Rejected,
    ProviderError,
}

/// <summary>
/// One provider call, kept even when validation refuses the JSON before a
/// <c>SectionMarking</c> exists.
///
/// <b>Raw output lives here and nowhere else.</b> Application logs must not
/// carry it; the bound below is the only trim, and it is a size cap rather
/// than an editorial one.
/// </summary>
public sealed record EvaluationAttempt(
    string Id,
    string OperationId,
    ExamSessionId SessionId,
    ExamModule Module,
    int? TaskNumber,
    string? Provider,
    string? Model,
    string? RequestId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    EvaluationAttemptOutcome Outcome,
    string? ErrorCode,
    string? ErrorMessage,
    string? RawOutput,
    bool RawOutputTruncated,
    string? MarkingId,
    int? MarkingVersion)
{
    /// <summary>1 MiB — the persist ceiling, matching a provider response-size bound rather than a log-line trim.</summary>
    public const int MaxRawOutputChars = 1_048_576;

    public const int MaxErrorChars = 1_000;

    public static EvaluationAttempt Capture(
        string operationId,
        ExamSessionId sessionId,
        ExamModule module,
        int? taskNumber,
        string? provider,
        string? model,
        string? requestId,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        EvaluationAttemptOutcome outcome,
        string? errorCode,
        string? errorMessage,
        string? rawOutput) =>
        new(
            Guid.NewGuid().ToString("N"),
            operationId,
            sessionId,
            module,
            taskNumber,
            provider,
            model,
            requestId,
            startedAt,
            finishedAt,
            outcome,
            BoundError(errorCode),
            BoundError(errorMessage),
            BoundRaw(rawOutput, out var truncated),
            truncated,
            MarkingId: null,
            MarkingVersion: null);

    public static string? BoundRaw(string? raw, out bool truncated)
    {
        if (raw is null)
        {
            truncated = false;
            return null;
        }

        if (raw.Length <= MaxRawOutputChars)
        {
            truncated = false;
            return raw;
        }

        truncated = true;
        return raw[..MaxRawOutputChars];
    }

    public static string? BoundError(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Length > MaxErrorChars ? text[..MaxErrorChars] : text;
    }
}

public interface IEvaluationAttemptStore
{
    Task RecordAsync(EvaluationAttempt attempt, CancellationToken ct);

    Task AttachMarkingAsync(string attemptId, string markingId, int version, CancellationToken ct);

    /// <summary>
    /// Ties the newest unmarked attempt for this operation/task to the
    /// marking version the worker just stored. A rematch therefore points at
    /// the new current version, not the superseded one.
    /// </summary>
    Task AttachLatestUnmarkedAsync(
        string operationId,
        ExamModule module,
        int? taskNumber,
        string markingId,
        int version,
        CancellationToken ct);

    Task<IReadOnlyList<EvaluationAttempt>> ListByOperationAsync(
        string operationId, CancellationToken ct);
}

/// <summary>
/// Ambient correlation for one evaluator call.
///
/// The port takes primitives and a rubric, not a session. The runner opens
/// this before the call so the adapter can file the raw output against the
/// job that bought it — the same shape as <c>EvaluationUsageReport</c>.
/// </summary>
public static class EvaluationAttemptContext
{
    private static readonly AsyncLocal<Correlation?> Current = new();

    public sealed record Correlation(
        string OperationId,
        ExamSessionId SessionId,
        ExamModule Module,
        int? TaskNumber);

    public static IDisposable Open(Correlation correlation)
    {
        var previous = Current.Value;
        Current.Value = correlation;
        return new Pop(previous);
    }

    public static Correlation? Peek => Current.Value;

    private sealed class Pop(Correlation? previous) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(Current.Value, previous) is false)
                Current.Value = previous;
        }
    }
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
    /// <returns>
    /// Whether a job now exists for this section. <b>The caller needs this to
    /// know whether anything else owes the marking.</b> `MarkSection` only
    /// marks inline when nothing was enqueued; a void return made "no rubric,
    /// so no job" indistinguishable from "queued", and the caller had to
    /// attempt the evaluation anyway to be safe.
    /// </returns>
    public static async Task<bool> EnqueueAsync(
        ExamVersion version,
        ExamModule module,
        ExamSessionId sessionId,
        IMarkingOutbox outbox,
        IRubricSource rubrics,
        Vni.Ielts.Domain.Common.IClock clock,
        CancellationToken ct)
    {
        if (module is not (ExamModule.Writing or ExamModule.Speaking)) return false;
        if (version.Section(module) is null) return false;

        if (rubrics.For(module) is not { } rubric) return false;

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
                CompletedAt: null,
                // Captured from whatever is tracing the submit, right here at
                // the boundary — `Activity.Current` is meaningless by the time
                // a worker picks the job up. → F4.2
                TraceParent: System.Diagnostics.Activity.Current?.Id),
            ct);

        return true;
    }
}
