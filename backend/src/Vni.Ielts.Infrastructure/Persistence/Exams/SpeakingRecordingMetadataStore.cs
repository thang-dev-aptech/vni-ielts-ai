using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

[BsonIgnoreExtraElements]
internal sealed class SpeakingRecordingDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("recordingId")]
    public string RecordingId { get; set; } = string.Empty;

    [BsonElement("objectKey")]
    public string ObjectKey { get; set; } = string.Empty;

    [BsonElement("ownerId")]
    public string OwnerId { get; set; } = string.Empty;

    [BsonElement("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [BsonElement("questionId")]
    public string QuestionId { get; set; } = string.Empty;

    [BsonElement("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [BsonElement("expectedSizeBytes")]
    [BsonIgnoreIfNull]
    public long? ExpectedSizeBytes { get; set; }

    [BsonElement("expectedChecksumSha256")]
    [BsonIgnoreIfNull]
    public string? ExpectedChecksumSha256 { get; set; }

    [BsonElement("actualSizeBytes")]
    [BsonIgnoreIfNull]
    public long? ActualSizeBytes { get; set; }

    [BsonElement("actualChecksumSha256")]
    [BsonIgnoreIfNull]
    public string? ActualChecksumSha256 { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("retentionExpiresAt")]
    [BsonIgnoreIfNull]
    public DateTime? RetentionExpiresAt { get; set; }

    [BsonElement("linkedAt")]
    [BsonIgnoreIfNull]
    public DateTime? LinkedAt { get; set; }
}

internal sealed class MongoSpeakingRecordingMetadataStore(IMongoDatabase database)
    : ISpeakingRecordingMetadataStore
{
    private readonly IMongoCollection<SpeakingRecordingDocument> _collection =
        database.GetCollection<SpeakingRecordingDocument>("speaking_recording_uploads");

    public async Task InsertAsync(SpeakingRecordingMetadata metadata, CancellationToken ct) =>
        await _collection.InsertOneAsync(ToDoc(metadata), cancellationToken: ct);

    public async Task<SpeakingRecordingMetadata?> FindAsync(string uploadId, CancellationToken ct)
    {
        var doc = await _collection
            .Find(Builders<SpeakingRecordingDocument>.Filter.Eq(d => d.Id, uploadId))
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : FromDoc(doc);
    }

    public async Task MarkAbandonedForQuestionAsync(
        ExamSessionId sessionId, string questionId, CancellationToken ct) =>
        await _collection.UpdateManyAsync(
            Builders<SpeakingRecordingDocument>.Filter.And(
                Builders<SpeakingRecordingDocument>.Filter.Eq(d => d.SessionId, sessionId.Value),
                Builders<SpeakingRecordingDocument>.Filter.Eq(d => d.QuestionId, questionId),
                Builders<SpeakingRecordingDocument>.Filter.Eq(
                    d => d.Status, nameof(SpeakingRecordingStatus.PendingUpload))),
            Builders<SpeakingRecordingDocument>.Update.Set(
                d => d.Status, nameof(SpeakingRecordingStatus.Abandoned)),
            cancellationToken: ct);

    /// <summary>
    /// <b>Every status write is conditional, and the match count is the
    /// answer.</b> A stale-abort pass and a second complete both read a
    /// pending row; the read-then-write between handlers is unguarded, so the
    /// guard lives where the write happens: an update whose filter no longer
    /// matches is the signal that the row changed under the caller, and the
    /// caller treats false as the 404 it factually is.
    /// </summary>
    public async Task<bool> UpdateAfterUploadAsync(
        string uploadId, long sizeBytes, string checksumSha256, CancellationToken ct)
    {
        var result = await _collection.UpdateOneAsync(
            Builders<SpeakingRecordingDocument>.Filter.And(
                Builders<SpeakingRecordingDocument>.Filter.Eq(d => d.Id, uploadId),
                Builders<SpeakingRecordingDocument>.Filter.Eq(
                    d => d.Status, nameof(SpeakingRecordingStatus.PendingUpload))),
            Builders<SpeakingRecordingDocument>.Update
                .Set(d => d.ActualSizeBytes, sizeBytes)
                .Set(d => d.ActualChecksumSha256, checksumSha256)
                .Set(d => d.Status, nameof(SpeakingRecordingStatus.Uploaded)),
            cancellationToken: ct);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> MarkLinkedAsync(string uploadId, DateTimeOffset at, CancellationToken ct)
    {
        var result = await _collection.UpdateOneAsync(
            Builders<SpeakingRecordingDocument>.Filter.And(
                Builders<SpeakingRecordingDocument>.Filter.Eq(d => d.Id, uploadId),
                Builders<SpeakingRecordingDocument>.Filter.Eq(
                    d => d.Status, nameof(SpeakingRecordingStatus.Uploaded))),
            Builders<SpeakingRecordingDocument>.Update
                .Set(d => d.Status, nameof(SpeakingRecordingStatus.Linked))
                .Set(d => d.LinkedAt, at.UtcDateTime),
            cancellationToken: ct);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> MarkAbandonedAsync(string uploadId, CancellationToken ct)
    {
        // Abandon from Pending or Uploaded - never from Linked (that would
        // retire a filed answer) and not from Abandoned itself.
        var result = await _collection.UpdateOneAsync(
            Builders<SpeakingRecordingDocument>.Filter.And(
                Builders<SpeakingRecordingDocument>.Filter.Eq(d => d.Id, uploadId),
                Builders<SpeakingRecordingDocument>.Filter.In(
                    d => d.Status,
                    [
                        nameof(SpeakingRecordingStatus.PendingUpload),
                        nameof(SpeakingRecordingStatus.Uploaded),
                    ])),
            Builders<SpeakingRecordingDocument>.Update.Set(
                d => d.Status, nameof(SpeakingRecordingStatus.Abandoned)),
            cancellationToken: ct);

        return result.ModifiedCount == 1;
    }

    public async Task<IReadOnlyList<SpeakingRecordingMetadata>> ListPendingOlderThanAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct)
    {
        var docs = await _collection
            .Find(Builders<SpeakingRecordingDocument>.Filter.And(
                Builders<SpeakingRecordingDocument>.Filter.Eq(
                    d => d.Status, nameof(SpeakingRecordingStatus.PendingUpload)),
                Builders<SpeakingRecordingDocument>.Filter.Lt(
                    d => d.CreatedAt, olderThan.UtcDateTime)))
            .SortBy(d => d.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);

        return docs.ConvertAll(FromDoc);
    }

    public async Task<IReadOnlyList<SpeakingRecordingMetadata>> ListBySessionAsync(
        ExamSessionId sessionId, CancellationToken ct)
    {
        var docs = await _collection
            .Find(Builders<SpeakingRecordingDocument>.Filter.Eq(d => d.SessionId, sessionId.Value))
            .ToListAsync(ct);

        return docs.ConvertAll(FromDoc);
    }

    public async Task<IReadOnlyList<SpeakingRecordingMetadata>> ListByOwnerAsync(
        UserId ownerId, CancellationToken ct)
    {
        var docs = await _collection
            .Find(Builders<SpeakingRecordingDocument>.Filter.Eq(d => d.OwnerId, ownerId.Value))
            .ToListAsync(ct);

        return docs.ConvertAll(FromDoc);
    }

    public async Task DeleteAsync(string uploadId, CancellationToken ct) =>
        await _collection.DeleteOneAsync(
            Builders<SpeakingRecordingDocument>.Filter.Eq(d => d.Id, uploadId),
            cancellationToken: ct);

    public async Task<IReadOnlyList<SpeakingRecordingMetadata>> ListOlderThanAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct)
    {
        var docs = await _collection
            .Find(Builders<SpeakingRecordingDocument>.Filter.Lt(
                d => d.CreatedAt, olderThan.UtcDateTime))
            .SortBy(d => d.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);

        return docs.ConvertAll(FromDoc);
    }

    public async Task<IReadOnlyList<SpeakingRecordingMetadata>> ListOlderThanSweepableAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct,
        StoredRecording? after)
    {
        // Linked and Uploaded both sweep - Uploaded is the crash window the
        // sweep either recovers or removes. Cursor in the query, before the
        // limit; the tiebreak on RecordingId makes the order total, so a
        // cursor cannot skip a row two rows share a CreatedAt with.
        var filter = Builders<SpeakingRecordingDocument>.Filter.And(
            Builders<SpeakingRecordingDocument>.Filter.In(
                d => d.Status,
                [
                    nameof(SpeakingRecordingStatus.Linked),
                    nameof(SpeakingRecordingStatus.Uploaded),
                ]),
            Builders<SpeakingRecordingDocument>.Filter.Lt(d => d.CreatedAt, olderThan.UtcDateTime));

        if (after is { } cursor)
        {
            filter = Builders<SpeakingRecordingDocument>.Filter.And(
                filter,
                Builders<SpeakingRecordingDocument>.Filter.Or(
                    Builders<SpeakingRecordingDocument>.Filter.Gt(
                        d => d.CreatedAt, cursor.UploadedAt.UtcDateTime),
                    Builders<SpeakingRecordingDocument>.Filter.And(
                        Builders<SpeakingRecordingDocument>.Filter.Eq(
                            d => d.CreatedAt, cursor.UploadedAt.UtcDateTime),
                        Builders<SpeakingRecordingDocument>.Filter.Gt(
                            d => d.RecordingId, cursor.RecordingId))));
        }

        var docs = await _collection
            .Find(filter)
            .SortBy(d => d.CreatedAt).ThenBy(d => d.RecordingId)
            .Limit(limit)
            .ToListAsync(ct);

        return docs.ConvertAll(FromDoc);
    }

    internal static async Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken ct)
    {
        var collection = database.GetCollection<SpeakingRecordingDocument>("speaking_recording_uploads");
        await collection.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<SpeakingRecordingDocument>(
                Builders<SpeakingRecordingDocument>.IndexKeys
                    .Ascending(d => d.SessionId)
                    .Ascending(d => d.QuestionId)),
            new CreateIndexModel<SpeakingRecordingDocument>(
                Builders<SpeakingRecordingDocument>.IndexKeys.Ascending(d => d.CreatedAt)),
            new CreateIndexModel<SpeakingRecordingDocument>(
                Builders<SpeakingRecordingDocument>.IndexKeys.Ascending(d => d.RecordingId)),
        ], ct);
    }

    private static SpeakingRecordingDocument ToDoc(SpeakingRecordingMetadata metadata) => new()
    {
        Id = metadata.UploadId,
        RecordingId = metadata.RecordingId,
        ObjectKey = metadata.ObjectKey,
        OwnerId = metadata.OwnerId.Value,
        SessionId = metadata.SessionId.Value,
        QuestionId = metadata.QuestionId,
        ContentType = metadata.ContentType,
        ExpectedSizeBytes = metadata.ExpectedSizeBytes,
        ExpectedChecksumSha256 = metadata.ExpectedChecksumSha256,
        ActualSizeBytes = metadata.ActualSizeBytes,
        ActualChecksumSha256 = metadata.ActualChecksumSha256,
        Status = metadata.Status.ToString(),
        CreatedAt = metadata.CreatedAt.UtcDateTime,
        RetentionExpiresAt = metadata.RetentionExpiresAt?.UtcDateTime,
        LinkedAt = metadata.LinkedAt?.UtcDateTime,
    };

    private static SpeakingRecordingMetadata FromDoc(SpeakingRecordingDocument doc) => new(
        doc.Id,
        doc.RecordingId,
        doc.ObjectKey,
        new UserId(doc.OwnerId),
        new ExamSessionId(doc.SessionId),
        doc.QuestionId,
        doc.ContentType,
        doc.ExpectedSizeBytes,
        doc.ExpectedChecksumSha256,
        doc.ActualSizeBytes,
        doc.ActualChecksumSha256,
        Enum.Parse<SpeakingRecordingStatus>(doc.Status),
        new DateTimeOffset(doc.CreatedAt, TimeSpan.Zero),
        doc.RetentionExpiresAt is { } retention
            ? new DateTimeOffset(retention, TimeSpan.Zero)
            : null,
        doc.LinkedAt is { } linked ? new DateTimeOffset(linked, TimeSpan.Zero) : null);
}
