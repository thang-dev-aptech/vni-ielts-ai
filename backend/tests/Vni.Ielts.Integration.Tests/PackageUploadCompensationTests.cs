using Vni.Ielts.Api.Endpoints;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Integration.Tests;

public sealed class PackageUploadCompensationTests
{
    [Fact]
    public async Task Claim_release_failure_does_not_prevent_GridFS_delete()
    {
        var idempotency = new ThrowingIdempotencyStore();
        var uploads = new TrackingUploadStore();
        var reconciliations = new TrackingReconciliationStore();

        await PackageUploadCompensation.RunAsync(
            idempotency,
            uploads,
            reconciliations,
            "actor-1",
            "key-1",
            "claim-1",
            "sha-1",
            "package-1",
            "pkg-upload:package-1",
            DateTimeOffset.Parse("2026-09-10T00:00:00Z"));

        Assert.Equal("pkg-upload:package-1", uploads.DeletedUploadRef);
        Assert.Null(reconciliations.UploadRef);
    }

    [Fact]
    public async Task GridFS_delete_failure_records_the_upload_reference_as_reconciliation_pointer()
    {
        var idempotency = new ThrowingIdempotencyStore();
        var uploads = new TrackingUploadStore { DeleteFailure = new IOException("gridfs unavailable") };
        var reconciliations = new TrackingReconciliationStore();

        await PackageUploadCompensation.RunAsync(
            idempotency,
            uploads,
            reconciliations,
            "actor-1",
            "key-1",
            "claim-1",
            "sha-1",
            "package-1",
            "pkg-upload:package-1",
            DateTimeOffset.Parse("2026-09-10T00:00:00Z"));

        Assert.Equal("pkg-upload:package-1", reconciliations.UploadRef);
        Assert.Equal("package-1", reconciliations.PackageId);
        Assert.Equal("gridfs unavailable", reconciliations.Reason);
    }

    private sealed class ThrowingIdempotencyStore : IPackageUploadIdempotencyStore
    {
        public Task ReleaseClaimAsync(string actorId, string idempotencyKey, string claimToken, string sha256, string packageId, CancellationToken ct) =>
            throw new InvalidOperationException("claim release failed");

        public Task<PackageUploadIdempotencyRecord?> FindAsync(string actorId, string idempotencyKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<PackageUploadClaimResult> ClaimOrGetAsync(string actorId, string idempotencyKey, string sha256, string candidatePackageId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkCompletedAsync(string actorId, string idempotencyKey, string claimToken, string sha256, string packageId, string status, int statusCode, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveAsync(PackageUploadIdempotencyRecord record, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class TrackingUploadStore : IPackageUploadStore
    {
        public Exception? DeleteFailure { get; init; }
        public string? DeletedUploadRef { get; private set; }

        public Task DeleteAsync(string uploadRef, CancellationToken ct)
        {
            DeletedUploadRef = uploadRef;
            return DeleteFailure is null ? Task.CompletedTask : Task.FromException(DeleteFailure);
        }

        public Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> SaveAsync(Stream content, string fileName, string contentType, string? uploadRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenAsync(string uploadRef, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class TrackingReconciliationStore : IPackageUploadReconciliationStore
    {
        public string? UploadRef { get; private set; }
        public string? PackageId { get; private set; }
        public string? Reason { get; private set; }

        public Task RecordFailureAsync(string uploadRef, string packageId, string actorId, string reason, DateTimeOffset createdAt, CancellationToken ct)
        {
            UploadRef = uploadRef;
            PackageId = packageId;
            Reason = reason;
            return Task.CompletedTask;
        }
    }
}
