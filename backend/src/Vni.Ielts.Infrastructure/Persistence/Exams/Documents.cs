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

    /// <summary>
    /// Nullable, and stored rather than derived. Every version written before
    /// 2026-09-03 has no such field, and BSON deserialises a missing element to
    /// null — so old documents read back as "no description", which is exactly
    /// what they are. No migration is needed and none should be written.
    /// </summary>
    [BsonElement("description")]
    public string? Description { get; set; }

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

    [BsonElement("listeningPlayback")]
    [BsonIgnoreIfNull]
    public ListeningPlaybackDocument? ListeningPlayback { get; set; }

    [BsonElement("sections")]
    public List<SectionDocument> Sections { get; set; } = [];

    /// <summary>
    /// Resolved sitting order for this version. Absent on legacy documents —
    /// rehydration falls back to <see cref="SequenceProfile.CanonicalOrder"/>.
    /// </summary>
    [BsonElement("moduleSequence")]
    [BsonIgnoreIfNull]
    public List<string>? ModuleSequence { get; set; }

    /// <summary>
    /// A fingerprint of everything a sitting is scored against.
    ///
    /// <b>Written 2026-08-28, because the entity said "immutable once
    /// published" and nothing enforced it.</b>
    ///
    /// `UpsertAsync` was a `ReplaceOneAsync` with `IsUpsert`, so any caller
    /// could rewrite a published version's content wholesale — and one did, on
    /// every restart: the development seeder loads the fixtures and publishes
    /// them under a deterministic id. Editing a fixture and restarting the API
    /// therefore changed the exam <i>underneath</i> every sitting that was
    /// running it. The learner's screen kept the old passage; the marker used
    /// the new answer key.
    ///
    /// That failure is invisible until somebody disputes a band, which is the
    /// worst moment to discover it.
    ///
    /// <b>Content only — not status.</b> Publishing and unpublishing change
    /// what a version <i>is for</i>, not what it <i>says</i>, and both must
    /// keep working on a published version. So the hash covers the sections,
    /// the scoring profile, the timing, the title, the variant and the version
    /// number, and deliberately not `status` or `publishedAt`.
    ///
    /// Absent on documents written before this field existed. Those are treated
    /// as unfingerprinted and may be replaced once, which is what lets an
    /// existing deployment adopt the check without a migration.
    /// </summary>
    [BsonElement("contentHash")]
    [BsonIgnoreIfNull]
    public string? ContentHash { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ListeningPlaybackDocument
{
    [BsonElement("practice")]
    public AudioPlaybackRuleDocument Practice { get; set; } = new();

    [BsonElement("mock")]
    public AudioPlaybackRuleDocument Mock { get; set; } = new();
}

[BsonIgnoreExtraElements]
internal sealed class AudioPlaybackRuleDocument
{
    [BsonElement("playOnce")]
    public bool PlayOnce { get; set; } = true;

    [BsonElement("allowSeek")]
    public bool AllowSeek { get; set; }
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

    [BsonElement("timing")]
    [BsonIgnoreIfNull]
    public PartTimingDocument? Timing { get; set; }

    [BsonElement("questions")]
    public List<QuestionDocument> Questions { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class PartTimingDocument
{
    [BsonElement("durationSeconds")]
    public int DurationSeconds { get; set; }

    [BsonElement("prepSeconds")]
    [BsonIgnoreIfNull]
    public int? PrepSeconds { get; set; }

    [BsonElement("responseSeconds")]
    [BsonIgnoreIfNull]
    public int? ResponseSeconds { get; set; }
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

    /// <summary>
    /// Raw marks. Defaults to 1 so rows written before this field existed read
    /// back with the value they were scored under, rather than as zero-mark
    /// questions that quietly stop counting.
    /// </summary>
    [BsonElement("marks")]
    [BsonDefaultValue(1)]
    public int Marks { get; set; } = 1;

    /// <summary>
    /// The shared frame — heading bank, table, map, summary paragraph.
    /// Unlike the answer key this one <b>does</b> travel to the client: it is
    /// what the question cannot be answered without.
    /// </summary>
    [BsonElement("group")]
    [BsonIgnoreIfNull]
    public QuestionGroupDocument? Group { get; set; }

    /// <summary>Stays here. It has no route to a client — see <c>QuestionView</c>.</summary>
    [BsonElement("answerKey")]
    [BsonIgnoreIfNull]
    public AnswerKeyDocument? AnswerKey { get; set; }

    [BsonElement("slots")]
    public List<ResponseSlotDocument> Slots { get; set; } = [];

    [BsonElement("explanation")]
    [BsonIgnoreIfNull]
    public QuestionExplanationDocument? Explanation { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ResponseSlotDocument
{
    [BsonElement("id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("number")]
    public int Number { get; set; }

    [BsonElement("answerKey")]
    [BsonIgnoreIfNull]
    public AnswerKeyDocument? AnswerKey { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class QuestionExplanationDocument
{
    [BsonElement("correctAnswer")]
    [BsonIgnoreIfNull]
    public string? CorrectAnswer { get; set; }

    [BsonElement("shortReason")]
    public string ShortReason { get; set; } = string.Empty;

    [BsonElement("evidence")]
    public List<string> Evidence { get; set; } = [];

    [BsonElement("commonMistake")]
    [BsonIgnoreIfNull]
    public string? CommonMistake { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class QuestionGroupDocument
{
    [BsonElement("id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("title")]
    [BsonIgnoreIfNull]
    public string? Title { get; set; }

    [BsonElement("instruction")]
    [BsonIgnoreIfNull]
    public string? Instruction { get; set; }

    [BsonElement("image")]
    [BsonIgnoreIfNull]
    public string? Image { get; set; }

    [BsonElement("text")]
    [BsonIgnoreIfNull]
    public string? Text { get; set; }

    [BsonElement("eachLetterOnce")]
    public bool EachLetterOnce { get; set; }
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

    [BsonElement("practiceUnitId")]
    [BsonIgnoreIfNull]
    public string? PracticeUnitId { get; set; }

    [BsonElement("partIds")]
    public List<string> PartIds { get; set; } = [];

    [BsonElement("mode")]
    public string Mode { get; set; } = "Single";

    /// <summary>
    /// <c>Deadline</c> or <c>OpenEnded</c>.
    ///
    /// <b>Defaults to <c>Deadline</c> so sittings written before this field
    /// existed read as the exam they were.</b> Every one of them was timed —
    /// there was no other kind — so the default is not a guess about them, it
    /// is what those documents mean.
    /// </summary>
    [BsonElement("timing")]
    [BsonDefaultValue("Deadline")]
    public string Timing { get; set; } = "Deadline";

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

    [BsonElement("partId")]
    [BsonIgnoreIfNull]
    public string? PartId { get; set; }

    [BsonElement("startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>Absent for an open-ended attempt, which has no deadline.</summary>
    [BsonElement("deadlineAt")]
    [BsonIgnoreIfNull]
    public DateTime? DeadlineAt { get; set; }

    [BsonElement("submittedAt")]
    [BsonIgnoreIfNull]
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Seconds counted over the intervals that have already closed.</summary>
    [BsonElement("accumulatedSeconds")]
    [BsonDefaultValue(0)]
    public int AccumulatedSeconds { get; set; }

    /// <summary>
    /// The server clock at the last resume; absent while paused.
    ///
    /// A deadlined attempt carries it too, so elapsed time is reportable for
    /// thi thử as well — that one simply never pauses.
    /// </summary>
    [BsonElement("runningSince")]
    [BsonIgnoreIfNull]
    public DateTime? RunningSince { get; set; }

    /// <summary>The learner's own goal. Display only — no rule reads it.</summary>
    [BsonElement("targetSeconds")]
    [BsonIgnoreIfNull]
    public int? TargetSeconds { get; set; }
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

    /// <summary>
    /// The sheet as it was written before 27/08/2026 — an array of
    /// <c>{questionId, value}</c>.
    ///
    /// <b>Read, never written, and never cleared.</b> A field-level update
    /// cannot address an array element that may not exist yet, which is the
    /// whole reason <see cref="Entries"/> replaced it. Rewriting these
    /// documents would need a migration and a window in which a sitting in
    /// progress cannot save; leaving the array in place and reading it *under*
    /// the map costs two lines and has neither.
    ///
    /// So an old sheet keeps its answers as a base layer and every write from
    /// now on lands in the map on top. Nothing is lost, nothing is converted,
    /// and the code that reads it can be deleted the day no document has the
    /// field.
    /// </summary>
    [BsonElement("answers")]
    public List<AnswerDocument> Answers { get; set; } = [];

    /// <summary>
    /// Question id → answer, as a sub-document.
    ///
    /// <b>A map rather than an array so one question can be written without
    /// naming the others.</b> An array element is addressable only by index or
    /// by a positional match, and neither can create the element it did not
    /// find — so writing one answer meant reading the sheet, appending, and
    /// putting the whole thing back, which is a lost update wearing an
    /// autosave's clothes. Two tabs, and the loser's answers were gone.
    ///
    /// With a map, <c>$set: { "entries.r-7": "true" }</c> is one statement
    /// whether or not <c>r-7</c> was there, and two writers touching different
    /// questions do not collide at all. Only two writers touching the *same*
    /// question resolve at all, and they resolve to the later one — which is
    /// what the learner typed last, and therefore what they meant.
    ///
    /// <b>Question ids must be legal Mongo keys.</b> A <c>.</c> would be read
    /// as a path separator and a leading <c>$</c> as an operator, so the write
    /// would land somewhere else entirely. Validated at the handler against the
    /// exam's own question ids, and refused again here.
    /// </summary>
    [BsonElement("entries")]
    public Dictionary<string, string?> Entries { get; set; } = [];

    /// <summary>
    /// When this sheet was frozen, or null while the section is still open.
    ///
    /// <b>This is the join between the sitting and the sheet, and there was
    /// none before 27/08/2026.</b> They are different collections, so the
    /// transition compare-and-swap on the session document could not stop a
    /// patch that had already passed its section check from landing after the
    /// section closed and after marking had read the sheet. The learner was
    /// told the answer was saved and the result did not contain it.
    ///
    /// Every write filters on this being absent, so the freeze is what refuses
    /// a late patch — one atomic statement, not a read followed by a check
    /// followed by a write, which is three statements a competing writer fits
    /// between.
    ///
    /// <b>Null on every document written before this field existed, which is
    /// the correct reading.</b> Those sheets were never frozen, so they are
    /// open — and every sitting they belong to has long since ended, so nothing
    /// is going to write to them anyway.
    /// </summary>
    /// <summary>
    /// Question id → the ordering token of the write that produced its value.
    ///
    /// <b>Mongo's arrival order is not the learner's edit order.</b> Two writes
    /// for one question can be reordered by a retry, a proxy, a stalled
    /// request or a second tab, and the stored value then becomes whichever the
    /// server applied last — the older answer as often as the newer one. The
    /// learner watches their correction revert and there is nothing on screen
    /// that says why.
    ///
    /// The revision cannot answer this: one number for the whole sheet says
    /// whether a caller was behind, not which of two edits to one question came
    /// second.
    ///
    /// Absent for every question written before this field existed, which reads
    /// as -1 and therefore accepts the next write — the correct behaviour for a
    /// sheet nobody has ordered yet.
    /// </summary>
    [BsonElement("seqs")]
    public Dictionary<string, long> Sequences { get; set; } = [];

    [BsonElement("closedAt")]
    [BsonIgnoreIfNull]
    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// The revision this sheet held at the moment it was frozen.
    ///
    /// <b>Recorded so that "marking read the frozen content" is checkable
    /// rather than merely argued.</b> No patch can land after the freeze, so
    /// the revision cannot move — and an invariant nothing verifies is an
    /// invariant that decays quietly. A reader that finds a different number
    /// has found a hole in the protocol and should say so loudly.
    /// </summary>
    [BsonElement("closedRevision")]
    [BsonIgnoreIfNull]
    public int? ClosedRevision { get; set; }

    /// <summary>
    /// Which version of this sheet a writer must have been holding.
    ///
    /// <b>Defaults to zero so documents written before this field existed read
    /// as revision zero.</b> That is the same value a first write expects, so
    /// an old sheet's next autosave succeeds once and then the counter is live
    /// — no migration, and no window where an existing sitting cannot save.
    ///
    /// It is deliberately not <c>UpdatedAt</c>. A timestamp says when a
    /// document was written, not which state its writer derived it from, and
    /// under clock skew or a retry it can run backwards.
    /// </summary>
    [BsonElement("revision")]
    [BsonDefaultValue(0)]
    public int Revision { get; set; }

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
    [BsonIgnoreIfNull]
    public decimal? Band { get; set; }

    [BsonElement("questions")]
    public List<QuestionResultDocument> Questions { get; set; } = [];

    [BsonElement("scoredAt")]
    public DateTime ScoredAt { get; set; }
}

/// <summary>
/// A marked Writing task or Speaking test.
///
/// <b>A separate collection from <c>section_results</c>, because the two record
/// different kinds of fact.</b> A section result is arithmetic over an answer
/// key — a raw score, a maximum, and which questions were right. This is a
/// judgement: four criterion bands, the learner's own words quoted as the basis
/// for each, the rubric version it was produced under, and any flag raised
/// while validating the model's claim. Folding them into one collection would
/// leave half the fields null on every row and make "which rows have a band
/// anyone can defend" a question about null-checks.
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class SectionMarkingDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [BsonElement("module")]
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// Which Writing task, or absent for Speaking.
    ///
    /// Writing is two markings because IELTS assesses each task against all
    /// four criteria; Speaking is one band for the whole test. → `H-8b`
    /// </summary>
    [BsonElement("taskNumber")]
    [BsonIgnoreIfNull]
    public int? TaskNumber { get; set; }

    /// <summary>
    /// The rubric this was produced under.
    ///
    /// <b>Stored, not looked up.</b> A rubric can change; a band already shown
    /// to a learner cannot. Recording the version on the row is what keeps an
    /// old evaluation explicable after the descriptors move.
    /// </summary>
    [BsonElement("rubricVersion")]
    public string RubricVersion { get; set; } = string.Empty;

    [BsonElement("criteria")]
    public List<CriterionAssessmentDocument> Criteria { get; set; } = [];

    /// <summary>The band, recomputed from the criteria — never the model's own number.</summary>
    [BsonElement("band")]
    [BsonRepresentation(MongoDB.Bson.BsonType.Decimal128)]
    public decimal Band { get; set; }

    /// <summary>
    /// What the model claimed the section band was, when it claimed one.
    ///
    /// Kept although it is never used as the band: a stored disagreement is
    /// evidence about the provider, and discarding it makes the
    /// <c>ArithmeticMismatch</c> flag a fact with no supporting detail.
    /// </summary>
    [BsonElement("reportedBand")]
    [BsonRepresentation(MongoDB.Bson.BsonType.Decimal128)]
    [BsonIgnoreIfNull]
    public decimal? ReportedBand { get; set; }

    [BsonElement("flags")]
    public List<string> Flags { get; set; } = [];

    [BsonElement("ungroundedEvidence")]
    public List<string> UngroundedEvidence { get; set; } = [];

    [BsonElement("markedAt")]
    public DateTime MarkedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class CriterionAssessmentDocument
{
    [BsonElement("criterion")]
    public string Criterion { get; set; } = string.Empty;

    [BsonElement("band")]
    [BsonRepresentation(MongoDB.Bson.BsonType.Decimal128)]
    public decimal Band { get; set; }

    [BsonElement("feedback")]
    public string Feedback { get; set; } = string.Empty;

    /// <summary>Verbatim spans from the learner's submission. → `A-13c`</summary>
    [BsonElement("evidence")]
    public List<string> Evidence { get; set; } = [];
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

    [BsonElement("correctAnswer")]
    [BsonIgnoreIfNull]
    public string? CorrectAnswer { get; set; }

    [BsonElement("slots")]
    [BsonIgnoreIfNull]
    public List<SlotResultDocument>? Slots { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class SlotResultDocument
{
    [BsonElement("slotId")]
    public string SlotId { get; set; } = string.Empty;

    [BsonElement("number")]
    public int Number { get; set; }

    [BsonElement("submitted")]
    [BsonIgnoreIfNull]
    public string? Submitted { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("correctAnswer")]
    [BsonIgnoreIfNull]
    public string? CorrectAnswer { get; set; }
}
