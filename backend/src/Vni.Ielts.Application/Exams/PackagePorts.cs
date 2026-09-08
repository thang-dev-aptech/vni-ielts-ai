using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Exams;

public interface IExamPackageRepository
{
    Task<ExamPackage?> FindAsync(string packageId, CancellationToken ct);
    Task SaveAsync(ExamPackage package, CancellationToken ct);
    Task<IReadOnlyList<ExamPackage>> ListByStatusAsync(PackageImportStatus status, CancellationToken ct);
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
    Task<Stream> OpenAsync(string uploadRef, CancellationToken ct);
    Task DeleteAsync(string uploadRef, CancellationToken ct);
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
