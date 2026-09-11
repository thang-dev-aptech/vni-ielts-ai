using System.Security.Cryptography;
using System.Text;
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

public sealed record ImportJob(
    string OperationId,
    ExamDefinitionId DefinitionId,
    int VersionNumber,
    string SourceSha256,
    /// <summary>Where the uploaded archive was put, so another process can read it.</summary>
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
    string? Traceparent)
{
    public const int MaxAttempts = 3;

    public bool MayRetry => Attempts < MaxAttempts;

    /// <summary>
    /// <c>{definitionId}:{version}:{sourceHash}</c>. The hash is in it so a
    /// corrected package for the same version is a different job, and a
    /// retried upload of identical bytes is the same one. A unique index on
    /// this field is what makes the enqueue idempotent in the database rather
    /// than in a caller's memory.
    /// </summary>
    public static string OperationIdFor(
        ExamDefinitionId definitionId, int versionNumber, string sourceSha256) =>
        $"{definitionId.Value}:{versionNumber}:{sourceSha256.ToLowerInvariant()}";

    public static ImportJob New(
        ExamDefinitionId definitionId, int versionNumber, string sourceSha256,
        string archiveKey, DateTimeOffset now, string? traceparent = null) =>
        new(OperationIdFor(definitionId, versionNumber, sourceSha256),
            definitionId, versionNumber, sourceSha256, archiveKey, null,
            ImportJobStage.Extracting, ImportJobState.Pending, 0, now,
            now, null, null, null, null, traceparent);
}

public interface IImportOutbox
{
    /// <summary>Idempotent on the operation id. False means the job was already there.</summary>
    Task<bool> EnqueueAsync(ImportJob job, CancellationToken ct);

    /// <summary>Takes one due job atomically, setting a lease. Null when none is due.</summary>
    Task<ImportJob?> ClaimAsync(string leaseToken, TimeSpan lease, CancellationToken ct);

    Task RenewAsync(string operationId, string leaseToken, TimeSpan lease, CancellationToken ct);

    Task AdvanceAsync(
        string operationId, string leaseToken, ImportJobStage stage, Guid? draftId,
        CancellationToken ct);

    Task CompleteAsync(string operationId, string leaseToken, CancellationToken ct);

    /// <summary>Releases the lease and schedules a retry, or fails the job when out of attempts.</summary>
    Task FailAsync(string operationId, string leaseToken, string error, CancellationToken ct);

    Task<ImportJob?> FindAsync(string operationId, CancellationToken ct);
}
