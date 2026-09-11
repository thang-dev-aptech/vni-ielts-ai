namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Test seam for retention txn mid-points. Production registers
/// <see cref="NoOpPackageRetentionHooks"/>; tests that construct the processor
/// pass a mutable implementation — never wired in production DI as mutable.
/// </summary>
public interface IPackageRetentionHooks
{
    /// <summary>
    /// Inside the upload-purge txn, immediately before the CAS claim
    /// (Id + Version + terminal + cutoff + UploadPurged=false).
    /// </summary>
    Task BeforeUploadClaimAsync(string packageId, CancellationToken ct);

    Task AfterUploadClaimedAsync(CancellationToken ct);

    Task AfterUploadDeletedAsync(CancellationToken ct);

    /// <summary>Inside the candidate-purge txn, immediately before the conditional delete.</summary>
    Task BeforeCandidateDeleteAsync(string documentId, CancellationToken ct);

    /// <summary>Inside the grouping-purge txn, immediately before the package CAS recheck.</summary>
    Task BeforeGroupingDeleteAsync(string packageId, CancellationToken ct);

    /// <summary>
    /// After the package CAS recheck matched, before the confirming CAS
    /// and proposal delete.
    /// </summary>
    Task AfterGroupingPackageRecheckAsync(string packageId, CancellationToken ct);

    /// <summary>Inside the extraction-purge txn, immediately before the conditional delete.</summary>
    Task BeforeExtractionDeleteAsync(string runId, CancellationToken ct);
}

public sealed class NoOpPackageRetentionHooks : IPackageRetentionHooks
{
    public Task BeforeUploadClaimAsync(string packageId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task AfterUploadClaimedAsync(CancellationToken ct) => Task.CompletedTask;

    public Task AfterUploadDeletedAsync(CancellationToken ct) => Task.CompletedTask;

    public Task BeforeCandidateDeleteAsync(string documentId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task BeforeGroupingDeleteAsync(string packageId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task AfterGroupingPackageRecheckAsync(string packageId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task BeforeExtractionDeleteAsync(string runId, CancellationToken ct) =>
        Task.CompletedTask;
}
