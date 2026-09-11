using System.Net;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Importing;

namespace Vni.Ielts.Infrastructure.Storage;

internal sealed class S3ImportExamAssetStore(
    IAmazonS3 client,
    ObjectStorageOptions options,
    MongoContext context) : IImportExamAssetStore
{
    public async Task<StagedImportAsset> StageAsync(
        Guid draftId, string reference, Stream content,
        string contentType, long length, string sha256, CancellationToken ct)
    {
        if (!ImportAssetPaths.TryCanonicalize(reference, out var canonical, out _))
            throw new ArgumentException("Asset reference must be a canonical assets/ path.", nameof(reference));

        var stagingKey = ImportAssetPaths.StagingKey(draftId, canonical);
        var request = new PutObjectRequest
        {
            BucketName = options.ExamAssetsBucket,
            Key = StagingObjectKey(stagingKey),
            InputStream = content,
            ContentType = contentType,
            CannedACL = S3CannedACL.Private,
            AutoCloseStream = false,
            UseChunkEncoding = false,
        };
        request.Metadata["sha256"] = sha256;
        request.Metadata["length"] = length.ToString();
        await client.PutObjectAsync(request, ct);
        return new StagedImportAsset(canonical, stagingKey, contentType, length, sha256);
    }

    public async Task<ImportAssetAvailability> CheckFinalAsync(string reference, CancellationToken ct)
    {
        var key = FinalObjectKey(reference);
        if (key is null) return ImportAssetAvailability.Absent;
        return await HeadAsync(key, ct);
    }

    public async Task<ImportAssetPromotionResult> PromoteAsync(StagedImportAsset asset, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return new ImportAssetPromotionResult(ImportAssetPromotionStatus.Canceled, asset.Reference);

        if (string.IsNullOrEmpty(asset.StagingKey))
        {
            var provisioned = await CheckFinalAsync(asset.Reference, ct);
            return SameIdentity(provisioned, asset.Sha256, asset.Length)
                ? new ImportAssetPromotionResult(ImportAssetPromotionStatus.AlreadyPresent, asset.Reference)
                : new ImportAssetPromotionResult(ImportAssetPromotionStatus.Missing, asset.Reference);
        }

        var destination = FinalObjectKey(asset.Reference);
        if (destination is null)
            return new ImportAssetPromotionResult(ImportAssetPromotionStatus.Missing, asset.Reference);

        var destinationState = await HeadAsync(destination, ct);
        if (destinationState.Kind == ImportAssetAvailabilityKind.Present)
        {
            return SameIdentity(destinationState, asset.Sha256, asset.Length)
                ? new ImportAssetPromotionResult(ImportAssetPromotionStatus.AlreadyPresent, asset.Reference)
                : new ImportAssetPromotionResult(ImportAssetPromotionStatus.Conflict, asset.Reference);
        }

        var sourceKey = StagingObjectKey(asset.StagingKey);
        var staging = await HeadAsync(sourceKey, ct);
        if (staging.Kind != ImportAssetAvailabilityKind.Present)
            return new ImportAssetPromotionResult(ImportAssetPromotionStatus.Missing, asset.Reference);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (ct.IsCancellationRequested)
                return new ImportAssetPromotionResult(ImportAssetPromotionStatus.Canceled, asset.Reference);

            try
            {
                using var source = await client.GetObjectAsync(
                    new GetObjectRequest
                    {
                        BucketName = options.ExamAssetsBucket,
                        Key = sourceKey,
                    },
                    ct);
                var put = new PutObjectRequest
                {
                    BucketName = options.ExamAssetsBucket,
                    Key = destination,
                    InputStream = source.ResponseStream,
                    ContentType = asset.ContentType,
                    CannedACL = S3CannedACL.Private,
                    AutoCloseStream = false,
                    UseChunkEncoding = false,
                    IfNoneMatch = "*",
                };
                put.Headers.ContentLength = asset.Length;
                put.Metadata["sha256"] = asset.Sha256;
                put.Metadata["length"] = asset.Length.ToString();
                await client.PutObjectAsync(put, ct);
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new ImportAssetPromotionResult(ImportAssetPromotionStatus.Canceled, asset.Reference);
            }
            catch (AmazonS3Exception ex) when (IsPreconditionFailed(ex) || IsConditionalConflict(ex))
            {
                var current = await HeadAsync(destination, ct);
                if (SameIdentity(current, asset.Sha256, asset.Length))
                    return new ImportAssetPromotionResult(ImportAssetPromotionStatus.AlreadyPresent, asset.Reference);
                if (current.Kind == ImportAssetAvailabilityKind.Present)
                    return new ImportAssetPromotionResult(ImportAssetPromotionStatus.Conflict, asset.Reference);
                if (IsConditionalConflict(ex) && attempt == 0)
                    continue;
                return new ImportAssetPromotionResult(ImportAssetPromotionStatus.Missing, asset.Reference);
            }
        }

        if (ct.IsCancellationRequested)
            return new ImportAssetPromotionResult(ImportAssetPromotionStatus.Canceled, asset.Reference);

        var verified = await HeadAsync(destination, ct);
        if (!SameIdentity(verified, asset.Sha256, asset.Length))
            return new ImportAssetPromotionResult(ImportAssetPromotionStatus.Missing, asset.Reference);

        return new ImportAssetPromotionResult(ImportAssetPromotionStatus.PromotedThisAttempt, asset.Reference);
    }

    public async Task VerifyFinalAsync(IReadOnlyList<ImportAssetManifestEntry> assets, CancellationToken ct)
    {
        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();
            var found = await CheckFinalAsync(asset.Reference, ct);
            if (!SameIdentity(found, asset.Sha256, asset.Length))
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
                    var reason = Enum.TryParse<ImportAssetCleanupReason>(intent.Reason, out var parsed)
                        ? parsed
                        : ImportAssetCleanupReason.StagingAbandoned;
                    var deleteFinal = reason == ImportAssetCleanupReason.ApprovalCommitFailed;
                    var skipped = false;
                    foreach (var reference in intent.References)
                    {
                        await lease.EnsureCurrentAsync(leaseCt);
                        if (deleteFinal)
                        {
                            if (await MongoImportAssetCleanup.CatalogueReferencesAsync(catalogue, reference, leaseCt))
                            {
                                skipped = true;
                            }
                            else if (FinalObjectKey(reference) is { } finalKey)
                            {
                                await lease.EnsureCurrentAsync(leaseCt);
                                await DeleteQuietlyAsync(finalKey, leaseCt);
                            }
                        }

                        if (Guid.TryParse(intent.DraftId, out var draftId))
                        {
                            await lease.EnsureCurrentAsync(leaseCt);
                            await DeleteQuietlyAsync(StagingObjectKey(ImportAssetPaths.StagingKey(draftId, reference)), leaseCt);
                        }
                    }

                    await lease.EnsureCurrentAsync(leaseCt);
                    await MongoImportAssetCleanup.MarkAsync(
                        context, intent.Id, skipped ? "skipped" : "completed", leaseCt);
                }

                return 0;
            },
            ct);
    }

    private async Task<ImportAssetAvailability> HeadAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            var response = await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = options.ExamAssetsBucket,
                    Key = objectKey,
                },
                ct);

            var sha = Metadata(response, "sha256");
            var length = response.ContentLength;
            if (string.IsNullOrEmpty(sha))
                sha = await HashObjectAsync(objectKey, ct);

            var contentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? "application/octet-stream"
                : response.Headers.ContentType;
            return ImportAssetAvailability.Present(contentType, length, sha);
        }
        catch (AmazonS3Exception e) when (e.ErrorCode is "NoSuchKey" or "NotFound"
            || e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ImportAssetAvailability.Absent;
        }
    }

    private async Task<string> HashObjectAsync(string objectKey, CancellationToken ct)
    {
        using var response = await client.GetObjectAsync(
            new GetObjectRequest { BucketName = options.ExamAssetsBucket, Key = objectKey }, ct);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81_920];
        int read;
        while ((read = await response.ResponseStream.ReadAsync(buffer, ct)) > 0)
            hasher.AppendData(buffer.AsSpan(0, read));
        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }

    private async Task DeleteQuietlyAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await client.DeleteObjectAsync(options.ExamAssetsBucket, objectKey, ct);
        }
        catch (AmazonS3Exception)
        {
            // Best-effort cleanup; the intent row remains the recovery pointer.
        }
    }

    private string? FinalObjectKey(string reference)
    {
        if (!ImportAssetPaths.TryCanonicalize(reference, out var canonical, out _)) return null;
        var relative = canonical[ImportAssetPaths.Prefix.Length..];
        return ObjectStorageOptions.Under(options.ExamAssetsPrefix, relative);
    }

    private string StagingObjectKey(string stagingKey)
    {
        if (!stagingKey.StartsWith("imports/", StringComparison.Ordinal)
            || stagingKey.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("Import asset key must stay below imports/.", nameof(stagingKey));
        }

        return ObjectStorageOptions.Under(options.ExamAssetsPrefix, stagingKey);
    }

    private static bool IsPreconditionFailed(AmazonS3Exception ex) =>
        ex.StatusCode == HttpStatusCode.PreconditionFailed
        || string.Equals(ex.ErrorCode, "PreconditionFailed", StringComparison.OrdinalIgnoreCase);

    private static bool IsConditionalConflict(AmazonS3Exception ex) =>
        ex.StatusCode == HttpStatusCode.Conflict
        || string.Equals(ex.ErrorCode, "ConditionalRequestConflict", StringComparison.OrdinalIgnoreCase);

    private static bool SameIdentity(ImportAssetAvailability found, string sha256, long length) =>
        found.Kind == ImportAssetAvailabilityKind.Present
        && found.Length == length
        && string.Equals(found.Sha256, sha256, StringComparison.OrdinalIgnoreCase);

    private static string? Metadata(GetObjectMetadataResponse response, string name)
    {
        if (response.Metadata.Keys.Contains($"x-amz-meta-{name}"))
            return response.Metadata[$"x-amz-meta-{name}"];
        return response.Metadata.Keys.Contains(name) ? response.Metadata[name] : null;
    }
}
