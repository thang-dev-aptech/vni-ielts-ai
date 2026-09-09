using System.Security.Cryptography;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Importing;

namespace Vni.Ielts.Infrastructure.Storage;

/// <summary>
/// Development / unconfigured object-storage path. ZIP media cannot be staged
/// here — that would silently discard bytes. Final-object checks reuse the
/// existing exam-asset reader so already-provisioned fixture audio still works.
/// </summary>
internal sealed class ExamAssetStoreBackedImportExamAssetStore(
    MongoContext context,
    IServiceProvider services) : IImportExamAssetStore
{
    public Task<StagedImportAsset> StageAsync(
        Guid draftId, string reference, Stream content,
        string contentType, long length, string sha256, CancellationToken ct) =>
        throw new ImportExamAssetsUnavailableException();

    public async Task<ImportAssetAvailability> CheckFinalAsync(string reference, CancellationToken ct)
    {
        var assets = services.GetService(typeof(IExamAssetStore)) as IExamAssetStore;
        if (assets is null) return ImportAssetAvailability.Absent;

        var opened = await assets.OpenAsync(reference, ct);
        if (opened is null) return ImportAssetAvailability.Absent;

        await using var stream = opened.Content;
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81_920];
        long length = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            hasher.AppendData(buffer.AsSpan(0, read));
            length += read;
        }

        return ImportAssetAvailability.Present(
            opened.ContentType, length, Convert.ToHexStringLower(hasher.GetHashAndReset()));
    }

    public async Task<ImportAssetPromotionResult> PromoteAsync(StagedImportAsset asset, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(asset.StagingKey))
            return new ImportAssetPromotionResult(ImportAssetPromotionStatus.Missing, asset.Reference);

        var existing = await CheckFinalAsync(asset.Reference, ct);
        if (existing.Kind == ImportAssetAvailabilityKind.Present
            && existing.Length == asset.Length
            && string.Equals(existing.Sha256, asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return new ImportAssetPromotionResult(ImportAssetPromotionStatus.AlreadyPresent, asset.Reference);
        }

        return new ImportAssetPromotionResult(ImportAssetPromotionStatus.Missing, asset.Reference);
    }

    public async Task VerifyFinalAsync(IReadOnlyList<ImportAssetManifestEntry> assets, CancellationToken ct)
    {
        foreach (var asset in assets)
        {
            var found = await CheckFinalAsync(asset.Reference, ct);
            if (found.Kind != ImportAssetAvailabilityKind.Present
                || found.Length != asset.Length
                || !string.Equals(found.Sha256, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(ImportAssetFindingCodes.Missing);
            }
        }
    }

    public Task RecordCleanupIntentAsync(
        Guid draftId, IReadOnlyList<string> promotedReferences,
        ImportAssetCleanupReason reason, CancellationToken ct) =>
        MongoImportAssetCleanup.RecordAsync(context, draftId, promotedReferences, reason, ct);

    public async Task ProcessPendingCleanupAsync(IExamCatalogue catalogue, CancellationToken ct)
    {
        await MongoImportAssetCleanup.WithCoordinationLeaseAsync(
            context,
            async (lease, leaseCt) =>
            {
                foreach (var intent in await MongoImportAssetCleanup.PendingAsync(context, leaseCt))
                {
                    var referenced = false;
                    foreach (var reference in intent.References)
                    {
                        await lease.EnsureCurrentAsync(leaseCt);
                        if (await MongoImportAssetCleanup.CatalogueReferencesAsync(catalogue, reference, leaseCt))
                            referenced = true;
                    }

                    await lease.EnsureCurrentAsync(leaseCt);
                    await MongoImportAssetCleanup.MarkAsync(
                        context, intent.Id, referenced ? "skipped" : "completed", leaseCt);
                }

                return 0;
            },
            ct);
    }
}
