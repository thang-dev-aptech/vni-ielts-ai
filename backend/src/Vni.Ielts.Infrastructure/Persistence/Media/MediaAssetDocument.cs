using MongoDB.Bson.Serialization.Attributes;

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
    public int? DurationMs { get; set; }

    [BsonElement("checksum")]
    public string Checksum { get; set; } = string.Empty;

    [BsonElement("storageKey")]
    public string StorageKey { get; set; } = string.Empty;

    [BsonElement("uploadedBy")]
    public string UploadedBy { get; set; } = string.Empty;

    [BsonElement("uploadedAt")]
    public DateTime UploadedAt { get; set; }

    [BsonElement("retired")]
    public bool Retired { get; set; }
}
