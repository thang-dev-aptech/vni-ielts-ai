using MongoDB.Bson.Serialization.Attributes;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

[BsonIgnoreExtraElements]
internal sealed class SourceDocumentGroupingProposalDocument
{
    [BsonId]
    public string PackageId { get; set; } = string.Empty;

    [BsonElement("needsReview")]
    public bool NeedsReview { get; set; }

    [BsonElement("version")]
    public int Version { get; set; }

    [BsonElement("groups")]
    public List<SourceDocumentGroupDocument> Groups { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class SourceDocumentGroupDocument
{
    [BsonElement("id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("directoryPath")]
    [BsonIgnoreIfNull]
    public string? DirectoryPath { get; set; }

    [BsonElement("entryPaths")]
    public List<string> EntryPaths { get; set; } = [];
}
