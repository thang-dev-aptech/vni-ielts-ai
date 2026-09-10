using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Exams;

public sealed record PackageClaim(string PackageId, string Owner, long Fence, DateTimeOffset LeaseUntil);

public interface IExamPackageRepository
{
    Task<ExamPackage?> FindAsync(string packageId, CancellationToken ct);
    Task SaveAsync(ExamPackage package, CancellationToken ct);
    Task ReplaceVersionAsync(ExamPackage package, int expectedVersion, CancellationToken ct);
    Task<ExamPackage?> TryClaimAsync(string packageId, string owner, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken ct) =>
        throw new NotSupportedException("This repository does not support atomic package claims.");
    Task<bool> RenewClaimAsync(PackageClaim claim, DateTimeOffset leaseUntil, CancellationToken ct) =>
        throw new NotSupportedException("This repository does not support package claim renewal.");
    Task<bool> ReleaseClaimAsync(PackageClaim claim, DateTimeOffset now, CancellationToken ct) =>
        throw new NotSupportedException("This repository does not support package claim release.");
    Task ReplaceClaimedAsync(ExamPackage package, int expectedVersion, PackageClaim claim, DateTimeOffset now, CancellationToken ct) =>
        throw new NotSupportedException("This repository does not support fenced package replacement.");
    Task ReplaceVersionWithClaimAsync(ExamPackage package, int expectedVersion, string claimOwner, CancellationToken ct);
    Task<ExamPackage?> FindByImportDraftIdAsync(string importDraftId, CancellationToken ct);
    Task<IReadOnlyList<ExamPackage>> ListByStatusAsync(PackageImportStatus status, CancellationToken ct);
    Task<IReadOnlyList<ExamPackage>> ListClaimableAsync(DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyList<ExamPackage>> ListAllAsync(CancellationToken ct);
    Task<IReadOnlyList<ExamPackage>> ListUnpurgedTerminalAsync(CancellationToken ct);

    /// <summary>
    /// Removes the package row. Callers that cascade-delete drafts must
    /// finish those deletes first — this does not touch <c>exam_versions</c>.
    /// </summary>
    Task DeleteAsync(string packageId, CancellationToken ct);
}

public interface IPackageUploadStore
{
    Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct);
    Task<string> SaveAsync(Stream content, string fileName, string contentType, string? uploadRef, CancellationToken ct);
    Task<Stream> OpenAsync(string uploadRef, CancellationToken ct);
    Task DeleteAsync(string uploadRef, CancellationToken ct);
}

public enum PackageUploadClaimState
{
    Pending,
    Completed,
}

public enum PackageUploadClaimStatus
{
    NewClaim,
    AlreadyCompleted,
    PendingWait,
    ConflictMismatch,
}

public sealed record PackageUploadClaimResult(
    PackageUploadClaimStatus Status,
    PackageUploadIdempotencyRecord Record);

public sealed record PackageUploadIdempotencyRecord(
    string ActorId,
    string IdempotencyKey,
    string Sha256,
    string PackageId,
    string? Status,
    int? StatusCode,
    DateTimeOffset CreatedAt,
    PackageUploadClaimState State = PackageUploadClaimState.Completed,
    DateTimeOffset? LeaseExpiresAt = null,
    string? ClaimToken = null);

public interface IPackageUploadIdempotencyStore
{
    Task<PackageUploadIdempotencyRecord?> FindAsync(string actorId, string idempotencyKey, CancellationToken ct);
    Task<PackageUploadClaimResult> ClaimOrGetAsync(
        string actorId,
        string idempotencyKey,
        string sha256,
        string candidatePackageId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken ct);
    Task MarkCompletedAsync(
        string actorId,
        string idempotencyKey,
        string claimToken,
        string sha256,
        string packageId,
        string status,
        int statusCode,
        DateTimeOffset now,
        CancellationToken ct);
    Task ReleaseClaimAsync(
        string actorId,
        string idempotencyKey,
        string claimToken,
        string sha256,
        string packageId,
        CancellationToken ct);
    Task SaveAsync(PackageUploadIdempotencyRecord record, CancellationToken ct);
}

public interface IPackageUploadTransaction
{
    Task CommitUploadAsync(
        ExamPackage package,
        string? actorId,
        string? idempotencyKey,
        string? claimToken,
        int statusCode,
        CancellationToken ct);
}

public interface IPackageUploadReconciliationStore
{
    Task RecordFailureAsync(
        string uploadRef,
        string packageId,
        string actorId,
        string reason,
        DateTimeOffset createdAt,
        CancellationToken ct);
}

public sealed class PackageConcurrencyException(string packageId, int expectedVersion)
    : InvalidOperationException(
        $"Package '{packageId}' is no longer at version {expectedVersion}.")
{
    public string PackageId { get; } = packageId;
    public int ExpectedVersion { get; } = expectedVersion;
}

/// <summary>
/// Preflight saw a deletable set, but the transactional re-check found a
/// different persisted set (status changed, row missing, or package version
/// moved). Callers must abort with conflict and leave every row untouched.
/// </summary>
public sealed class PackageCascadeConflictException(string packageId, string detail)
    : InvalidOperationException(detail)
{
    public string PackageId { get; } = packageId;
}

public interface IPackageImportTransaction
{
    Task<IReadOnlyList<ExamVersionId>> ImportAsync(
        ExamPackage package,
        int expectedVersion,
        IReadOnlyList<ExamVersion> versions,
        CancellationToken ct);

    Task<IReadOnlyList<ExamVersionId>> ImportClaimedAsync(
        ExamPackage package,
        int expectedVersion,
        PackageClaim claim,
        IReadOnlyList<ExamVersion> versions,
        AuditEntry audit,
        CancellationToken ct);
}

public interface IRawPackageParsingTransaction
{
    Task CommitAsync(
        ExamPackage package,
        int expectedVersion,
        PackageClaim claim,
        SourceDocumentGroupingProposal proposal,
        ParsedExamCandidate candidate,
        IReadOnlyList<PackageFinding> findings,
        CancellationToken ct);
}

/// <summary>
/// Deletes a package and every still-Draft exam version it created, with matching
/// audit rows, in one Mongo transaction — so a mid-cascade failure leaves neither
/// half-deleted drafts nor a package row without its versions.
/// </summary>
public interface IPackageCascadeDelete
{
    Task CascadeDeleteAsync(
        ExamPackage package,
        IReadOnlyList<ExamVersion> drafts,
        UserId actorId,
        string actorEmail,
        DateTimeOffset now,
        CancellationToken ct);
}

/// <summary>
/// Test seam around cascade deletes. Production registers a no-op; integration
/// tests override the registration in a test factory — never in the production
/// DI graph.
/// </summary>
public interface IPackageCascadeDeleteHooks
{
    /// <summary>Runs inside the txn immediately before the conditional Draft delete.</summary>
    Task BeforeDraftDeleteAsync(ExamVersionId versionId, CancellationToken ct);

    /// <summary>Runs inside the txn after a successful Draft delete + its audit insert.</summary>
    Task AfterDraftDeletedAsync(ExamVersionId versionId, CancellationToken ct);
}
