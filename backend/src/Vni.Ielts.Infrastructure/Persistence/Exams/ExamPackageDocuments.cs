using MongoDB.Bson.Serialization.Attributes;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

[BsonIgnoreExtraElements]
internal sealed class ExamPackageDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("sourceKind")]
    public string SourceKind { get; set; } = string.Empty;

    [BsonElement("uploadedBy")]
    public string UploadedBy { get; set; } = string.Empty;

    [BsonElement("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [BsonElement("fileName")]
    public string FileName { get; set; } = string.Empty;

    [BsonElement("uploadRef")]
    public string UploadRef { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("findings")]
    public List<PackageFindingDocument> Findings { get; set; } = [];

    [BsonElement("entries")]
    public List<ExamPackageEntryDocument> Entries { get; set; } = [];

    [BsonElement("createdVersionIds")]
    public List<string> CreatedVersionIds { get; set; } = [];

    [BsonElement("version")]
    public int Version { get; set; }

    [BsonElement("uploadPurged")]
    public bool UploadPurged { get; set; }

    [BsonElement("uploadPurgeState")]
    [BsonIgnoreIfNull]
    public string? UploadPurgeState { get; set; }

    [BsonElement("uploadPurgeClaimId")]
    [BsonIgnoreIfNull]
    public string? UploadPurgeClaimId { get; set; }

    [BsonElement("uploadPurgeClaimedAt")]
    [BsonIgnoreIfNull]
    public DateTime? UploadPurgeClaimedAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("importDraftId")]
    [BsonIgnoreIfNull]
    public string? ImportDraftId { get; set; }

    [BsonElement("failureCode")]
    [BsonIgnoreIfNull]
    public string? FailureCode { get; set; }

    [BsonElement("failureDetail")]
    [BsonIgnoreIfNull]
    public string? FailureDetail { get; set; }

    [BsonElement("claimOwner")]
    [BsonIgnoreIfNull]
    public string? ClaimOwner { get; set; }

    [BsonElement("claimFence")]
    public long ClaimFence { get; set; }

    [BsonElement("claimedAt")]
    [BsonIgnoreIfNull]
    public DateTime? ClaimedAt { get; set; }

    [BsonElement("leaseUntil")]
    [BsonIgnoreIfNull]
    public DateTime? LeaseUntil { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class PackageFindingDocument
{
    [BsonElement("stage")]
    public string Stage { get; set; } = string.Empty;

    [BsonElement("code")]
    public string Code { get; set; } = string.Empty;

    [BsonElement("pointer")]
    [BsonIgnoreIfNull]
    public string? Pointer { get; set; }

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;
}

[BsonIgnoreExtraElements]
internal sealed class ExamPackageEntryDocument
{
    [BsonElement("proposedDefinitionId")]
    public string ProposedDefinitionId { get; set; } = string.Empty;

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("module")]
    public string Module { get; set; } = string.Empty;

    [BsonElement("questionCount")]
    public int QuestionCount { get; set; }
}
