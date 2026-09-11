using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

/// <summary>
/// Quarantine storage for raw package uploads, in GridFS.
///
/// Same stopgap reasoning as <see cref="GridFsRecordingStore"/>: the real home
/// for this is object storage (MinIO locally, S3-shaped in production — Phase
/// 2 Plan 04), which streams without passing through the API process and can
/// carry a lifecycle policy. GridFS needs no new dependency, no bucket, and no
/// credentials, so the upload endpoint can exist now instead of after that
/// infrastructure decision.
///
/// <b>Never overwritten, never truncated.</b> This is the one artefact every
/// later validation stage reads — Plan 03's structural checks, and eventually
/// Plan 06's PDF extraction — so a client re-uploading under the same package
/// id must still get a distinct, independently retrievable blob. The GridFS
/// id this returns is the only handle to it.
/// </summary>
internal sealed class GridFsPackageUploadStore(IMongoDatabase database, IClock clock) : IPackageUploadStore
{
    private readonly GridFSBucket _bucket = new(database, new GridFSBucketOptions
    {
        BucketName = "package_uploads",
        // 1 MB chunks — packages can run to tens of megabytes; the default
        // 255 KB would turn one upload into hundreds of round trips.
        ChunkSizeBytes = 1024 * 1024,
    });

    public Task<string> SaveAsync(
        Stream content, string fileName, string contentType, CancellationToken ct) =>
        SaveAsync(content, fileName, contentType, null, ct);

    public async Task<string> SaveAsync(
        Stream content, string fileName, string contentType, string? uploadRef, CancellationToken ct)
    {
        var id = !string.IsNullOrWhiteSpace(uploadRef) ? uploadRef : Guid.NewGuid().ToString("n");

        var metadata = new BsonDocument
        {
            { "fileName", fileName },
            { "contentType", contentType },
            { "uploadedAt", clock.UtcNow.UtcDateTime },
        };

        await _bucket.UploadFromStreamAsync(
            DeterministicId(id), id, content, new GridFSUploadOptions { Metadata = metadata }, ct);

        return id;
    }

    public async Task<Stream> OpenAsync(string uploadRef, CancellationToken ct) =>
        await _bucket.OpenDownloadStreamAsync(DeterministicId(uploadRef), cancellationToken: ct);

    public async Task DeleteAsync(string uploadRef, CancellationToken ct)
    {
        try
        {
            await _bucket.DeleteAsync(DeterministicId(uploadRef), ct);
        }
        catch (GridFSFileNotFoundException)
        {
            // Compensation is idempotent.
        }
    }

    private static ObjectId DeterministicId(string uploadRef)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(uploadRef));
        return new ObjectId(hash.AsSpan(0, 12).ToArray());
    }
}
