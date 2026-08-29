using MongoDB.Bson.Serialization.Attributes;

namespace Vni.Ielts.Infrastructure.Persistence.Content;

/// <summary>
/// Persistence models for the content rights registry.
///
/// <b>The only types here allowed to carry driver attributes.</b>
/// <c>ContentSource</c> and everything it composes carry none — CLAUDE.md
/// rule 7, enforced by the architecture tests. → ADR-0004
///
/// <b>Environments are stored by name, never as an ordinal.</b> The driver
/// serialises a bare enum as its position, so inserting a member above
/// <c>LearnerProduction</c> later would silently reinterpret every stored
/// grant as a different one — which here means turning a fixture-only record
/// into a publication right with no error at any point.
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class ContentSourceDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Absent when nobody has established who the material belongs to.</summary>
    [BsonElement("owner")]
    [BsonIgnoreIfNull]
    public string? Owner { get; set; }

    /// <summary>Absent when no licence or permission has been recorded.</summary>
    [BsonElement("proof")]
    [BsonIgnoreIfNull]
    public RightsProofDocument? Proof { get; set; }

    /// <summary>Environment names — "Fixture", "InternalReview", "LearnerProduction".</summary>
    [BsonElement("allowedEnvironments")]
    public List<string> AllowedEnvironments { get; set; } = [];

    [BsonElement("expiresAt")]
    [BsonIgnoreIfNull]
    public DateTime? ExpiresAt { get; set; }

    [BsonElement("rootPath")]
    public string RootPath { get; set; } = string.Empty;

    [BsonElement("files")]
    public List<ContentFileDocument> Files { get; set; } = [];

    /// <summary>Exam versions built from this material. Indexed — the publish gate reads by it.</summary>
    [BsonElement("examVersionIds")]
    public List<string> ExamVersionIds { get; set; } = [];

    /// <summary>Exam definitions built from this material. Survives a content edit.</summary>
    [BsonElement("examDefinitionIds")]
    public List<string> ExamDefinitionIds { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class RightsProofDocument
{
    [BsonElement("reference")]
    public string Reference { get; set; } = string.Empty;

    [BsonElement("reviewer")]
    public string Reviewer { get; set; } = string.Empty;

    [BsonElement("reviewedAt")]
    public DateTime ReviewedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ContentFileDocument
{
    [BsonElement("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Absent when nobody has computed one.
    ///
    /// <b>Absent never means "unchanged".</b> The verification path reports it
    /// as <c>NotHashed</c> rather than folding it into a pass.
    /// </summary>
    [BsonElement("sha256")]
    [BsonIgnoreIfNull]
    public string? Sha256 { get; set; }

    [BsonElement("sizeBytes")]
    [BsonIgnoreIfNull]
    public long? SizeBytes { get; set; }
}
