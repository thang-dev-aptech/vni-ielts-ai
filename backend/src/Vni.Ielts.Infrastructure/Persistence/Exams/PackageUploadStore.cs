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

    public async Task<string> SaveAsync(
        Stream content, string fileName, string contentType, CancellationToken ct)
    {
        // Server-generated, and it carries no client input — the declared file
        // name is metadata, not a path segment. → `zip-ingestion-security.md` A4
        var id = Guid.NewGuid().ToString("n");

        var metadata = new BsonDocument
        {
            { "fileName", fileName },
            { "contentType", contentType },
            { "uploadedAt", clock.UtcNow.UtcDateTime },
        };

        await _bucket.UploadFromStreamAsync(
            id, content, new GridFSUploadOptions { Metadata = metadata }, ct);

        return id;
    }

    // By name, not by ObjectId: SaveAsync above passes the server-generated id
    // as the GridFS *filename*, not as `_id` (there is no overload of
    // UploadFromStreamAsync that pins a caller-supplied `_id`) — the ObjectId
    // GridFS actually assigns is discarded. Opening by name is therefore the
    // correct counterpart, not a shortcut.
    public async Task<Stream> OpenAsync(string uploadRef, CancellationToken ct) =>
        await _bucket.OpenDownloadStreamByNameAsync(uploadRef, cancellationToken: ct);

    public async Task DeleteAsync(string uploadRef, CancellationToken ct)
    {
        // GridFS deletes by `_id`, not by name — the same asymmetry as
        // OpenAsync above. One lookup to find the id this filename resolves
        // to, then delete that.
        var filter = Builders<GridFSFileInfo>.Filter.Eq(f => f.Filename, uploadRef);
        using var cursor = await _bucket.FindAsync(filter, cancellationToken: ct);
        var file = await cursor.FirstOrDefaultAsync(ct);
        if (file is null) return;

        await _bucket.DeleteAsync(file.Id, ct);
    }
}
