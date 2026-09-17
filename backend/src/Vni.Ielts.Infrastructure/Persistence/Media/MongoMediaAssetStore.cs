using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Media;

namespace Vni.Ielts.Infrastructure.Persistence.Media;

[BsonIgnoreExtraElements]
internal sealed class MediaAssetDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("kind")]
    public string Kind { get; set; } = string.Empty;

    [BsonElement("fileName")]
    public string FileName { get; set; } = string.Empty;

    [BsonElement("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [BsonElement("bytes")]
    public long Bytes { get; set; }

    [BsonElement("durationMs")]
    [BsonIgnoreIfNull]
    public long? DurationMs { get; set; }

    [BsonElement("checksumSha256")]
    public string ChecksumSha256 { get; set; } = string.Empty;

    [BsonElement("uploadedByName")]
    public string UploadedByName { get; set; } = string.Empty;

    [BsonElement("uploadedAt")]
    public DateTime UploadedAt { get; set; }

    [BsonElement("retired")]
    public bool Retired { get; set; }
}

internal sealed class MongoMediaAssetStore(IMongoDatabase database) : IMediaAssetStore
{
    private readonly IMongoCollection<MediaAssetDocument> _collection =
        database.GetCollection<MediaAssetDocument>("media_assets");

    public async Task InsertAsync(MediaAsset asset, CancellationToken ct) =>
        await _collection.InsertOneAsync(ToDoc(asset), cancellationToken: ct);

    public async Task<MediaAsset?> FindAsync(string mediaId, CancellationToken ct)
    {
        var doc = await _collection
            .Find(Builders<MediaAssetDocument>.Filter.Eq(d => d.Id, mediaId))
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : FromDoc(doc);
    }

    public async Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken ct)
    {
        var docs = await _collection
            .Find(Builders<MediaAssetDocument>.Filter.Empty)
            .SortByDescending(d => d.UploadedAt)
            .ToListAsync(ct);

        return docs.Select(FromDoc).ToList();
    }

    public async Task SetRetiredAsync(string mediaId, bool retired, CancellationToken ct) =>
        await _collection.UpdateOneAsync(
            Builders<MediaAssetDocument>.Filter.Eq(d => d.Id, mediaId),
            Builders<MediaAssetDocument>.Update.Set(d => d.Retired, retired),
            cancellationToken: ct);

    public async Task DeleteAsync(string mediaId, CancellationToken ct) =>
        await _collection.DeleteOneAsync(
            Builders<MediaAssetDocument>.Filter.Eq(d => d.Id, mediaId), ct);

    internal static async Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken ct)
    {
        var collection = database.GetCollection<MediaAssetDocument>("media_assets");
        await collection.Indexes.CreateManyAsync(
        [
            // UploadedAt desc is the list's only sort. Uniqueness of the id is
            // the _id index's job; this index only makes the library render in
            // order without an in-memory sort.
            new CreateIndexModel<MediaAssetDocument>(
                Builders<MediaAssetDocument>.IndexKeys.Descending(d => d.UploadedAt)),
        ], ct);
    }

    private static MediaAssetDocument ToDoc(MediaAsset asset) => new()
    {
        Id = asset.MediaId,
        Kind = asset.Kind.ToString(),
        FileName = asset.FileName,
        ContentType = asset.ContentType,
        Bytes = asset.Bytes,
        DurationMs = asset.DurationMs,
        ChecksumSha256 = asset.ChecksumSha256,
        UploadedByName = asset.UploadedByName,
        UploadedAt = asset.UploadedAt.UtcDateTime,
        Retired = asset.Retired,
    };

    private static MediaAsset FromDoc(MediaAssetDocument doc) => new(
        doc.Id,
        Enum.Parse<MediaKind>(doc.Kind),
        doc.FileName,
        doc.ContentType,
        doc.Bytes,
        doc.DurationMs,
        doc.ChecksumSha256,
        doc.UploadedByName,
        new DateTimeOffset(doc.UploadedAt, TimeSpan.Zero),
        doc.Retired);
}
