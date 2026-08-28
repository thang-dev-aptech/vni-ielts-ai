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

    /// <summary>
    /// The key one question's recording is filed under.
    ///
    /// <b>Derived, not generated, and that is the fix for two different
    /// orphans.</b> A random id meant every upload created a new object:
    ///
    ///   • a retry after a lost response — the client sends no idempotency key
    ///     on this route, deliberately, because demanding one made Speaking
    ///     unanswerable — wrote a <i>second</i> object and pointed the sheet at
    ///     it, stranding the first;
    ///   • re-recording an answer did the same thing, every time.
    ///
    /// Both left an unreferenced blob holding a learner's voice, which is
    /// personal data under PDPL and therefore not something to accumulate by
    /// accident. Deriving the key means the second upload <i>replaces</i> the
    /// first rather than joining it.
    ///
    /// <b>Hashed rather than concatenated.</b> A question id is content from an
    /// exam package; a GridFS filename built by joining strings would let one
    /// carrying the separator address another question's file. The hash has no
    /// structure to exploit and a fixed length.
    ///
    /// <b>Overwriting is safe here because writes stop before reads start.</b>
    /// Marking runs after the section is frozen, and the closure protocol
    /// refuses uploads from that moment (ADR-0015) — so there is no window in
    /// which a replacement lands under a reader. → `I2.1`, `I2.4`
    /// </summary>
    private static string KeyFor(ExamSessionId sessionId, string questionId)
    {
        var material = System.Text.Encoding.UTF8.GetBytes($"{sessionId.Value}\u0000{questionId}");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(material))[..32]
            .ToLowerInvariant();
    }

    public async Task<string> SaveAsync(
        ExamSessionId sessionId, string questionId, Stream content, string contentType,
        CancellationToken ct)
    {
        // Derived from the session and question, so a retry or a re-record
        // replaces rather than accumulates. It still carries no client input in
        // a form anything can address: the question id goes through a hash.
        var id = KeyFor(sessionId, questionId);

        var metadata = new BsonDocument
        {
            { "sessionId", sessionId.Value },
            { "questionId", questionId },
            { "contentType", contentType },
            { "uploadedAt", clock.UtcNow.UtcDateTime },
        };

        /*
         * <b>GridFS allows duplicate filenames, so the previous revisions have
         * to go explicitly.</b> Uploading under the same name adds a file
         * rather than replacing one — which would turn the derived key from a
         * fix into a slower version of the same leak.
         *
         * Read before the upload, deleted after it. That order means a crash in
         * between leaves the <i>old</i> object alongside the new one — visible
         * to the reconciliation sweep, and recoverable — rather than leaving
         * the question with no audio at all.
         */
        var previous = await _bucket
            .Find(Builders<GridFSFileInfo>.Filter.Eq(f => f.Filename, id), null, ct)
            .ToListAsync(ct);

        await _bucket.UploadFromStreamAsync(
            id, content, new GridFSUploadOptions { Metadata = metadata }, ct);

        foreach (var stale in previous)
        {
            try
            {
                await _bucket.DeleteAsync(stale.Id, ct);
            }
            catch (GridFSFileNotFoundException)
            {
                // Two uploads raced and the other one tidied first. Fine.
            }
        }

        return id;
    }

    public async Task<IReadOnlyList<StoredRecording>> ListOlderThanAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct)
    {
        /*
         * <b>Filtered on the metadata this store writes, not on GridFS's own
         * upload date.</b> The two agree today and would stop agreeing the
         * moment anything re-uploads a file — and the question this sweep asks
         * is "how long has this <i>answer</i> been here", which is what the
         * metadata records.
         */
        var older = Builders<GridFSFileInfo>.Filter.Lt(
            "metadata.uploadedAt", olderThan.UtcDateTime);

        var files = await _bucket
            .Find(older, new GridFSFindOptions { Limit = limit }, ct)
            .ToListAsync(ct);

        var found = new List<StoredRecording>();

        foreach (var file in files)
        {
            var metadata = file.Metadata;

            // A file with no metadata is one this store did not write, or one
            // written before the metadata existed. Skipped rather than deleted:
            // a sweep that removes what it cannot identify is a sweep nobody
            // should run.
            if (metadata is null) continue;
            if (!metadata.TryGetValue("sessionId", out var session)) continue;
            if (!metadata.TryGetValue("questionId", out var question)) continue;

            var uploaded = metadata.TryGetValue("uploadedAt", out var at) && at.IsValidDateTime
                ? at.ToUniversalTime()
                : file.UploadDateTime;

            found.Add(new StoredRecording(
                file.Filename,
                new ExamSessionId(session.AsString),
                question.AsString,
                new DateTimeOffset(uploaded, TimeSpan.Zero)));
        }

        return found;
    }

    public async Task DeleteAsync(string recordingId, CancellationToken ct)
    {
        /*
         * <b>Found by filename, because that is what `SaveAsync` returns.</b>
         * GridFS keys a file by its own `ObjectId` and stores what we pass as
         * the filename, so the id the rest of the application holds is not the
         * one the bucket deletes by. Looking it up first is one query on an
         * indexed field and it keeps the port's contract — "the id you were
         * given" — rather than leaking GridFS's id into every caller.
         */
        // Every revision under this name, not only the newest: a crash between
        // the upload and the tidy-up above can leave two, and a delete that
        // removed one of them would leave the other holding a learner's voice
        // with nothing referencing it.
        var files = await _bucket
            .Find(Builders<GridFSFileInfo>.Filter.Eq(f => f.Filename, recordingId), null, ct)
            .ToListAsync(ct);

        foreach (var file in files)
        {
            try
            {
                await _bucket.DeleteAsync(file.Id, ct);
            }
            catch (GridFSFileNotFoundException)
            {
                // Already gone. The caller is cleaning up after a failure and
                // has nothing to do about a second one.
            }
        }
    }
}
