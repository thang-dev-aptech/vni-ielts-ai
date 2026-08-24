using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

/// <summary>
/// Speaking recordings, in GridFS.
///
/// <b>A deliberate stopgap, and the reason is worth stating.</b> Audio belongs
/// in object storage — MinIO is already in the local stack and S3 is the
/// production target — because that is what streams a file to a client without
/// it passing through the API process, and what a lifecycle policy can expire.
/// GridFS is here because it needs no new dependency, no bucket, and no
/// credentials, so Speaking can be built and reviewed now instead of after an
/// infrastructure decision nobody has made.
///
/// It is not a bad stopgap: GridFS chunks into the same database that already
/// holds the sitting, so a recording and the attempt it belongs to are written
/// under one connection and expire together. It is the wrong long-term home
/// because every byte read goes through the API process.
///
/// <b>PDPL applies to what lands here.</b> A learner's voice is personal data,
/// and sending it to a foreign ASR is a cross-border transfer requiring a CTIA
/// filing. Nothing in this class moves audio anywhere; that decision is `B-2`
/// and it is not made by a storage adapter. → `privacy-vietnam-pdpl.md`
/// </summary>
internal sealed class GridFsRecordingStore(IMongoDatabase database, IClock clock) : IRecordingStore
{
    private readonly GridFSBucket _bucket = new(database, new GridFSBucketOptions
    {
        BucketName = "speaking_recordings",
        // 1 MB chunks. A two-minute answer is a handful of them, and the
        // default 255 KB turns one answer into dozens of round trips.
        ChunkSizeBytes = 1024 * 1024,
    });

    public async Task<string> SaveAsync(
        ExamSessionId sessionId, string questionId, Stream content, string contentType,
        CancellationToken ct)
    {
        // Server-generated, and it carries no client input. The session and
        // question are metadata rather than path segments precisely so nothing
        // a caller sends can shape the key.
        var id = Guid.NewGuid().ToString("n");

        var metadata = new BsonDocument
        {
            { "sessionId", sessionId.Value },
            { "questionId", questionId },
            { "contentType", contentType },
            { "uploadedAt", clock.UtcNow.UtcDateTime },
        };

        await _bucket.UploadFromStreamAsync(
            id, content, new GridFSUploadOptions { Metadata = metadata }, ct);

        return id;
    }
}
