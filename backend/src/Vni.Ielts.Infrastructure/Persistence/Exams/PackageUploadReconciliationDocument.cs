using MongoDB.Bson.Serialization.Attributes;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

[BsonIgnoreExtraElements]
internal sealed class PackageUploadReconciliationDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("uploadRef")]
    public string UploadRef { get; set; } = string.Empty;

    [BsonElement("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [BsonElement("actorId")]
    public string ActorId { get; set; } = string.Empty;

    [BsonElement("reason")]
    public string Reason { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}
