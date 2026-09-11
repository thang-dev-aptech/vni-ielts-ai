using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Api.Endpoints;

internal static class PackageUploadCompensation
{
    public static async Task RunAsync(
        IPackageUploadIdempotencyStore idempotency,
        IPackageUploadStore uploads,
        IPackageUploadReconciliationStore reconciliations,
        string actorId,
        string? idempotencyKey,
        string? claimToken,
        string sha256,
        string packageId,
        string uploadRef,
        DateTimeOffset now)
    {
        if (idempotencyKey is not null && claimToken is not null)
        {
            try
            {
                using var releaseCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await idempotency.ReleaseClaimAsync(
                    actorId, idempotencyKey, claimToken, sha256, packageId, releaseCts.Token);
            }
            catch
            {
                // GridFS cleanup remains mandatory even when releasing the claim fails.
            }
        }

        try
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await uploads.DeleteAsync(uploadRef, cleanupCts.Token);
        }
        catch (Exception cleanupEx)
        {
            try
            {
                await reconciliations.RecordFailureAsync(
                    uploadRef,
                    packageId,
                    actorId,
                    cleanupEx.Message,
                    now,
                    CancellationToken.None);
            }
            catch
            {
                // Preserve the original upload transaction failure.
            }
        }
    }
}
