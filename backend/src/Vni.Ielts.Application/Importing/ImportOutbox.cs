using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// How far an import got, in the order the money is spent.
///
/// <b>Recorded rather than inferred.</b> A worker resuming a job needs to know
/// whether a paid parse already happened; reading that off the draft would
/// guess, and guessing wrong buys a second parse of a Cambridge paper.
/// </summary>
public enum ImportJobStage
{
    Extracting = 0,
    Parsing = 1,
    Transcribing = 2,
    Keying = 3,
    Checking = 4,
    Explaining = 5,
    Done = 6,
}

/// <summary>Mirrors <see cref="Vni.Ielts.Application.Assessment.MarkingJobState"/> deliberately.</summary>
public enum ImportJobState { Pending, Running, Retryable, Failed, Completed }

/// <param name="OperationId">
/// Stable, and derived rather than generated. → <see cref="OperationIdFor"/>.
/// </param>
/// <param name="ArchiveKey">Where the uploaded archive was put, so another process can read it.</param>
/// <param name="TraceParent">
/// The W3C <c>traceparent</c> of the request that enqueued this job, carried
/// the same way and for the same reason as <see
/// cref="Vni.Ielts.Application.Assessment.MarkingJob.TraceParent"/>: a parse
/// happens in another process, minutes later, and without this the worker's
/// span starts a brand-new trace nobody can join back to the upload.
/// </param>
public sealed record ImportJob(
    string OperationId,
    ExamDefinitionId DefinitionId,
    int VersionNumber,
    string SourceSha256,
    /// <summary>
    /// The prompt version the parser will run under, or
    /// <see cref="NoParserConfigured"/> when no AI parser is wired. →
    /// <see cref="OperationIdFor"/>.
    /// </summary>
    string ParsePromptVersion,
    string ArchiveKey,
    Guid? DraftId,
    ImportJobStage Stage,
    ImportJobState State,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? LeaseUntil,
    string? LeaseToken,
    string? LastError,
    DateTimeOffset? CompletedAt,
    string? TraceParent)
{
    public const int MaxAttempts = 3;

    /// <summary>
    /// Stands in for the parse prompt version when no AI parser is configured
    /// (<c>UnconfiguredExamSourceParser</c> — the structured-package route
    /// never calls a parser at all, so there is nothing to read a version
    /// from). A stable placeholder rather than an empty string: an id
    /// component that is sometimes absent would make "no parser configured"
    /// and "a version left blank by mistake" collide.
    /// </summary>
    public const string NoParserConfigured = "no-parser-configured";

    public bool MayRetry => Attempts < MaxAttempts;

    /// <summary>
    /// <c>{definitionId}:{version}:{sourceHash}:{parsePromptVersion}</c>.
    ///
    /// The hash is in it so a corrected package for the same version is a
    /// different job, and a retried upload of identical bytes is the same
    /// one.
    ///
    /// <b>The prompt version is in it for the same reason
    /// <c>MarkingJob.OperationId</c> carries a rubric version.</b> A parse
    /// under an improved prompt is a genuinely different piece of work, not a
    /// retry of the old one — and pinning the id to the definition, version
    /// and bytes alone would make re-uploading identical bytes after a prompt
    /// change silently collide with the job already keyed to the old prompt,
    /// with no way to force a re-parse. This repository has already lived
    /// that failure once, for a rubric version rather than a parser prompt.
    ///
    /// A unique index on this field is what makes the enqueue idempotent in
    /// the database rather than in a caller's memory.
    /// </summary>
    public static string OperationIdFor(
        ExamDefinitionId definitionId, int versionNumber, string sourceSha256,
        string parsePromptVersion) =>
        $"{definitionId.Value}:{versionNumber}:{sourceSha256.ToLowerInvariant()}:{parsePromptVersion}";

    public static ImportJob New(
        ExamDefinitionId definitionId, int versionNumber, string sourceSha256,
        string parsePromptVersion, string archiveKey, DateTimeOffset now,
        string? traceParent = null) =>
        new(OperationIdFor(definitionId, versionNumber, sourceSha256, parsePromptVersion),
            definitionId, versionNumber, sourceSha256, parsePromptVersion, archiveKey, null,
            ImportJobStage.Extracting, ImportJobState.Pending, 0, now,
            now, null, null, null, null, traceParent);
}

public interface IImportOutbox
{
    /// <summary>Idempotent on the operation id. False means the job was already there.</summary>
    Task<bool> EnqueueAsync(ImportJob job, CancellationToken ct);

    /// <summary>Takes one due job atomically, setting a lease. Null when none is due.</summary>
    Task<ImportJob?> ClaimAsync(string leaseToken, TimeSpan lease, CancellationToken ct);

    /// <summary>
    /// <b>False means "your lease is gone, stop."</b> A lease bounds a worker's
    /// death, not its duration — the worker is expected to keep renewing while
    /// it is still inside the job. If the renewal comes late enough that
    /// another worker already reclaimed the job, a Cambridge parse is running
    /// twice against the same draft and two processes think they own one
    /// write. Filtering the update on the caller's own lease token, and
    /// telling the caller whether it matched, is the only place that
    /// double-run can be caught — a worker that ignores the bool has no other
    /// way to find out it lost the job.
    /// </summary>
    Task<bool> RenewAsync(string operationId, string leaseToken, TimeSpan lease, CancellationToken ct);

    /// <summary>
    /// Records how far a job got. False for the same reason as
    /// <see cref="RenewAsync"/>: an advance from a worker that no longer holds
    /// the lease must not overwrite the stage a live worker is progressing —
    /// that stage is what stops a resumed job from paying for a parse twice.
    /// </summary>
    Task<bool> AdvanceAsync(
        string operationId, string leaseToken, ImportJobStage stage, Guid? draftId,
        CancellationToken ct);

    /// <summary>Marks the job done. False when this caller's lease no longer owns it.</summary>
    Task<bool> CompleteAsync(string operationId, string leaseToken, CancellationToken ct);

    /// <summary>
    /// Schedules another attempt, and records why this one did not work.
    ///
    /// <b>Separate from <see cref="FailAsync"/> because "did not work this
    /// time" and "will not work no matter how many times" are different
    /// judgements, and only the caller can make them.</b> A timeout, a 5xx or
    /// a rate limit from the parser might not recur; a package that parses
    /// cleanly and then fails schema or rights validation is permanently bad,
    /// and this outbox burning the remaining attempts on it — up to three
    /// Cambridge parses — would be paying three times to learn what the first
    /// parse already proved. This outbox only records the caller's
    /// judgement, it does not make it.
    ///
    /// False when this caller's lease no longer owns it.
    /// </summary>
    Task<bool> RetryAsync(
        string operationId, string leaseToken, DateTimeOffset nextAttemptAt, string error,
        CancellationToken ct);

    /// <summary>
    /// Gives up, permanently and visibly. Used both when a caller judges a
    /// failure unrecoverable outright and when <see cref="ImportJob.MayRetry"/>
    /// says the attempt budget is spent — either way the draft is stranded
    /// until a person looks at it, and a dash with no reason is worse than
    /// one that says why. False when this caller's lease no longer owns it.
    /// </summary>
    Task<bool> FailAsync(string operationId, string leaseToken, string error, CancellationToken ct);

    Task<ImportJob?> FindAsync(string operationId, CancellationToken ct);
}
