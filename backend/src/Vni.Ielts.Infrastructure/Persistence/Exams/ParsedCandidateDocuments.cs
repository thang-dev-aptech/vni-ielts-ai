using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

[BsonIgnoreExtraElements]
internal sealed class ParsedExamCandidateDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("candidateId")]
    public string CandidateId { get; set; } = string.Empty;

    [BsonElement("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [BsonElement("title")]
    [BsonIgnoreIfNull]
    public string? Title { get; set; }

    [BsonElement("classification")]
    public string Classification { get; set; } = string.Empty;

    [BsonElement("confidence")]
    [BsonRepresentation(BsonType.Decimal128)]
    [BsonIgnoreIfNull]
    public decimal? Confidence { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("version")]
    public int Version { get; set; }

    [BsonElement("sources")]
    public List<ParsedSourceDocument> Sources { get; set; } = [];

    [BsonElement("modules")]
    public List<ParsedModuleDocument> Modules { get; set; } = [];

    [BsonElement("corrections")]
    public List<CandidateCorrectionDocument> Corrections { get; set; } = [];

    [BsonElement("confirmedBy")]
    [BsonIgnoreIfNull]
    public string? ConfirmedBy { get; set; }

    [BsonElement("confirmedAt")]
    [BsonIgnoreIfNull]
    public DateTime? ConfirmedAt { get; set; }

    [BsonElement("rejectedBy")]
    [BsonIgnoreIfNull]
    public string? RejectedBy { get; set; }

    [BsonElement("rejectedAt")]
    [BsonIgnoreIfNull]
    public DateTime? RejectedAt { get; set; }

    [BsonElement("draftExamVersionId")]
    [BsonIgnoreIfNull]
    public string? DraftExamVersionId { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ParsedSourceDocument
{
    [BsonElement("fileName")]
    public string FileName { get; set; } = string.Empty;

    [BsonElement("page")]
    [BsonIgnoreIfNull]
    public int? Page { get; set; }

    [BsonElement("section")]
    [BsonIgnoreIfNull]
    public string? Section { get; set; }

    [BsonElement("reference")]
    [BsonIgnoreIfNull]
    public string? Reference { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ParsedModuleDocument
{
    [BsonElement("module")]
    [BsonIgnoreIfNull]
    public string? Module { get; set; }

    [BsonElement("classification")]
    public string Classification { get; set; } = string.Empty;

    [BsonElement("confidence")]
    [BsonRepresentation(BsonType.Decimal128)]
    [BsonIgnoreIfNull]
    public decimal? Confidence { get; set; }

    [BsonElement("parts")]
    public List<ParsedPartDocument> Parts { get; set; } = [];

    [BsonElement("provenance")]
    public ParsedSourceDocument Provenance { get; set; } = new();
}

[BsonIgnoreExtraElements]
internal sealed class ParsedPartDocument
{
    [BsonElement("id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("order")]
    public int Order { get; set; }

    [BsonElement("title")]
    [BsonIgnoreIfNull]
    public string? Title { get; set; }

    [BsonElement("body")]
    [BsonIgnoreIfNull]
    public string? Body { get; set; }

    [BsonElement("questions")]
    public List<ParsedQuestionDocument> Questions { get; set; } = [];

    [BsonElement("provenance")]
    public ParsedSourceDocument Provenance { get; set; } = new();
}

[BsonIgnoreExtraElements]
internal sealed class ParsedQuestionDocument
{
    [BsonElement("id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("order")]
    public int Order { get; set; }

    [BsonElement("type")]
    [BsonIgnoreIfNull]
    public string? Type { get; set; }

    [BsonElement("prompt")]
    [BsonIgnoreIfNull]
    public string? Prompt { get; set; }

    [BsonElement("options")]
    public List<ParsedOptionDocument> Options { get; set; } = [];

    [BsonElement("answerKey")]
    [BsonIgnoreIfNull]
    public ParsedAnswerKeyDocument? AnswerKey { get; set; }

    [BsonElement("provenance")]
    public ParsedSourceDocument Provenance { get; set; } = new();
}

[BsonIgnoreExtraElements]
internal sealed class ParsedOptionDocument
{
    [BsonElement("key")]
    public string Key { get; set; } = string.Empty;

    [BsonElement("text")]
    public string Text { get; set; } = string.Empty;
}

[BsonIgnoreExtraElements]
internal sealed class ParsedAnswerKeyDocument
{
    [BsonElement("accepted")]
    public List<string> Accepted { get; set; } = [];

    [BsonElement("matchingRule")]
    [BsonIgnoreIfNull]
    public string? MatchingRule { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class CandidateCorrectionDocument
{
    [BsonElement("id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("reviewerId")]
    public string ReviewerId { get; set; } = string.Empty;

    [BsonElement("field")]
    public string Field { get; set; } = string.Empty;

    [BsonElement("targetId")]
    public string TargetId { get; set; } = string.Empty;

    [BsonElement("previousValue")]
    [BsonIgnoreIfNull]
    public string? PreviousValue { get; set; }

    [BsonElement("newValue")]
    [BsonIgnoreIfNull]
    public string? NewValue { get; set; }

    [BsonElement("at")]
    public DateTime At { get; set; }
}
