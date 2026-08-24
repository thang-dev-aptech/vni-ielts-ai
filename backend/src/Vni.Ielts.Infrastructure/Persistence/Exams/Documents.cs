using MongoDB.Bson.Serialization.Attributes;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

/// <summary>
/// Persistence models for exam content and sittings.
///
/// <b>The only types allowed to carry driver attributes.</b> The domain
/// entities they map to carry none — CLAUDE.md rule 7, enforced by the
/// architecture tests. → ADR-0004
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class ExamVersionDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("definitionId")]
    public string DefinitionId { get; set; } = string.Empty;

    [BsonElement("versionNumber")]
    public int VersionNumber { get; set; }

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("variant")]
    public string Variant { get; set; } = "Academic";

    [BsonElement("status")]
    public string Status { get; set; } = "Draft";

    [BsonElement("publishedAt")]
    [BsonIgnoreIfNull]
    public DateTime? PublishedAt { get; set; }

    [BsonElement("timing")]
    public TimingDocument Timing { get; set; } = new();

    [BsonElement("scoring")]
    public ScoringDocument Scoring { get; set; } = new();

    [BsonElement("sections")]
    public List<SectionDocument> Sections { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class TimingDocument
{
    /// <summary>
    /// A list of pairs, not a dictionary keyed by module.
    ///
    /// BSON field names cannot contain a dot and cannot begin with a dollar,
    /// so any map whose keys come from content is a latent write failure. The
    /// keys here are enum names and would be safe — but the same shape is used
    /// for answer sheets, whose keys come from an authored package, and one
    /// rule for both is cheaper than remembering which is which.
    /// </summary>
    [BsonElement("sections")]
    public List<ModuleSecondsDocument> Sections { get; set; } = [];

    [BsonElement("listeningTransferSeconds")]
    [BsonIgnoreIfNull]
    public int? ListeningTransferSeconds { get; set; }

    [BsonElement("speakingParts")]
    public List<SpeakingPartDocument> SpeakingParts { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class ModuleSecondsDocument
{
    [BsonElement("module")]
    public string Module { get; set; } = string.Empty;

    [BsonElement("seconds")]
    public int Seconds { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class SpeakingPartDocument
{
    [BsonElement("part")]
    public int Part { get; set; }

    [BsonElement("prepSeconds")]
    public int PrepSeconds { get; set; }

    [BsonElement("responseSeconds")]
    public int ResponseSeconds { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ScoringDocument
{
    [BsonElement("rawToBand")]
    public List<ModuleBandTableDocument> RawToBand { get; set; } = [];

    [BsonElement("matching")]
    public MatchingRulesDocument Matching { get; set; } = new();

    [BsonElement("writingTask1Weight")]
    public decimal? WritingTask1Weight { get; set; }

    [BsonElement("writingTask2Weight")]
    public decimal? WritingTask2Weight { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ModuleBandTableDocument
{
    [BsonElement("module")]
    public string Module { get; set; } = string.Empty;

    [BsonElement("boundaries")]
    public List<BandBoundaryDocument> Boundaries { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class BandBoundaryDocument
{
    [BsonElement("minRaw")]
    public int MinRaw { get; set; }

    /// <summary>
    /// Stored as a decimal, not a double. A band is a value on a half-step
    /// grid and binary floating point cannot hold 6.5 exactly; a round trip
    /// through <c>double</c> is how a boundary silently becomes 6.499999.
    /// </summary>
    [BsonElement("band")]
    [BsonRepresentation(MongoDB.Bson.BsonType.Decimal128)]
    public decimal Band { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class MatchingRulesDocument
{
    [BsonElement("caseSensitive")]
    public bool CaseSensitive { get; set; }

    [BsonElement("trimWhitespace")]
    public bool TrimWhitespace { get; set; } = true;

    [BsonElement("collapseInnerWhitespace")]
    public bool CollapseInnerWhitespace { get; set; } = true;

    [BsonElement("allowSpellingVariants")]
    public bool AllowSpellingVariants { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class SectionDocument
{
    [BsonElement("module")]
    public string Module { get; set; } = string.Empty;

    [BsonElement("order")]
    public int Order { get; set; }

    [BsonElement("parts")]
    public List<PartDocument> Parts { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class PartDocument
{
    [BsonElement("order")]
    public int Order { get; set; }

    [BsonElement("kind")]
    public string Kind { get; set; } = string.Empty;

    [BsonElement("title")]
    [BsonIgnoreIfNull]
    public string? Title { get; set; }

    [BsonElement("body")]
    [BsonIgnoreIfNull]
    public string? Body { get; set; }

    [BsonElement("audioKey")]
    [BsonIgnoreIfNull]
    public string? AudioKey { get; set; }

    [BsonElement("imageKey")]
    [BsonIgnoreIfNull]
    public string? ImageKey { get; set; }

    /// <summary>Never leaves the server: a transcript is the answer sheet in prose.</summary>
    [BsonElement("transcript")]
    [BsonIgnoreIfNull]
    public string? Transcript { get; set; }

    [BsonElement("taskNumber")]
    [BsonIgnoreIfNull]
    public int? TaskNumber { get; set; }

    [BsonElement("partNumber")]
    [BsonIgnoreIfNull]
    public int? PartNumber { get; set; }

    [BsonElement("cueCard")]
    [BsonIgnoreIfNull]
    public CueCardDocument? CueCard { get; set; }

    [BsonElement("minWords")]
    [BsonIgnoreIfNull]
    public int? MinWords { get; set; }

    [BsonElement("questions")]
    public List<QuestionDocument> Questions { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class CueCardDocument
{
    [BsonElement("topic")]
    public string Topic { get; set; } = string.Empty;

    [BsonElement("bullets")]
    public List<string> Bullets { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class QuestionDocument
{
    [BsonElement("id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("order")]
    public int Order { get; set; }

    [BsonElement("type")]
    public string Type { get; set; } = string.Empty;

    [BsonElement("prompt")]
    [BsonIgnoreIfNull]
    public string? Prompt { get; set; }

    [BsonElement("options")]
    public List<OptionDocument> Options { get; set; } = [];

    [BsonElement("maxWords")]
    [BsonIgnoreIfNull]
    public int? MaxWords { get; set; }

    /// <summary>Stays here. It has no route to a client — see <c>QuestionView</c>.</summary>
    [BsonElement("answerKey")]
    [BsonIgnoreIfNull]
    public AnswerKeyDocument? AnswerKey { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class OptionDocument
{
    [BsonElement("key")]
    public string Key { get; set; } = string.Empty;

    [BsonElement("text")]
    public string Text { get; set; } = string.Empty;
}

[BsonIgnoreExtraElements]
internal sealed class AnswerKeyDocument
{
    [BsonElement("accepted")]
    public List<AcceptedAnswerDocument> Accepted { get; set; } = [];

    [BsonElement("overrides")]
    [BsonIgnoreIfNull]
    public MatchingRulesDocument? Overrides { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class AcceptedAnswerDocument
{
    [BsonElement("single")]
    [BsonIgnoreIfNull]
    public string? Single { get; set; }

    [BsonElement("all")]
    [BsonIgnoreIfNull]
    public List<string>? All { get; set; }

    [BsonElement("pairLeft")]
    [BsonIgnoreIfNull]
    public string? PairLeft { get; set; }

    [BsonElement("pairRight")]
    [BsonIgnoreIfNull]
    public string? PairRight { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ExamSessionDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("examVersionId")]
    public string ExamVersionId { get; set; } = string.Empty;

    [BsonElement("mode")]
    public string Mode { get; set; } = "Single";

    [BsonElement("status")]
    public string Status { get; set; } = "InProgress";

    [BsonElement("startedAt")]
    public DateTime StartedAt { get; set; }

    [BsonElement("submittedAt")]
    [BsonIgnoreIfNull]
    public DateTime? SubmittedAt { get; set; }

    [BsonElement("attempts")]
    public List<AttemptDocument> Attempts { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class AttemptDocument
{
    [BsonElement("module")]
    public string Module { get; set; } = string.Empty;

    [BsonElement("startedAt")]
    public DateTime StartedAt { get; set; }

    [BsonElement("deadlineAt")]
    public DateTime DeadlineAt { get; set; }

    [BsonElement("submittedAt")]
    [BsonIgnoreIfNull]
    public DateTime? SubmittedAt { get; set; }
}

/// <summary>
/// One sheet per section attempt, addressed by <c>{sessionId}:{module}</c>.
///
/// A deterministic id makes the autosave an upsert with no read first, which
/// is what keeps it cheap enough to run on a timer from a phone.
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class AnswerSheetDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [BsonElement("module")]
    public string Module { get; set; } = string.Empty;

    [BsonElement("answers")]
    public List<AnswerDocument> Answers { get; set; } = [];

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class AnswerDocument
{
    [BsonElement("questionId")]
    public string QuestionId { get; set; } = string.Empty;

    [BsonElement("value")]
    [BsonIgnoreIfNull]
    public string? Value { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class SectionResultDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [BsonElement("module")]
    public string Module { get; set; } = string.Empty;

    [BsonElement("rawScore")]
    public int RawScore { get; set; }

    [BsonElement("maxScore")]
    public int MaxScore { get; set; }

    [BsonElement("band")]
    [BsonRepresentation(MongoDB.Bson.BsonType.Decimal128)]
    public decimal Band { get; set; }

    [BsonElement("questions")]
    public List<QuestionResultDocument> Questions { get; set; } = [];

    [BsonElement("scoredAt")]
    public DateTime ScoredAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class QuestionResultDocument
{
    [BsonElement("questionId")]
    public string QuestionId { get; set; } = string.Empty;

    [BsonElement("submitted")]
    [BsonIgnoreIfNull]
    public string? Submitted { get; set; }

    [BsonElement("isCorrect")]
    public bool IsCorrect { get; set; }
}
