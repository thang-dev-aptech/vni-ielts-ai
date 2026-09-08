using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Exams;

public readonly record struct ExamDefinitionId(string Value)
{
    public override string ToString() => Value;
    public static ExamDefinitionId New() => new(Guid.NewGuid().ToString("n"));
}

public readonly record struct ExamVersionId(string Value)
{
    public override string ToString() => Value;
    public static ExamVersionId New() => new(Guid.NewGuid().ToString("n"));
}

public enum ExamModule { Reading, Listening, Writing, Speaking }

public enum ExamVariant { Academic, General }

/// <summary>
/// `InReview` and `Approved` implement `P-20`'s review lifecycle — author
/// submits, a <b>different</b> person reviews, admin publishes:
/// <c>Draft → InReview → Approved → Published → Unpublished</c>.
///
/// <b>No separate "returned" status.</b> The blueprint's own transition
/// table (`docs/product/mvp-blueprint.md` § 06) sends a returned version
/// straight back to <c>Draft</c> — the reviewer's reason travels in the
/// audit log, not in a fifth persisted state. A broader CMS redesign
/// proposes a distinct `Returned` state (`docs/ux/cms-content-operations.md`
/// § 3.1), but that document is marked `PROPOSED` with open questions of its
/// own; `P-20` is the decision this enum implements. → `[QUYẾT ĐỊNH kỹ thuật]`
/// </summary>
public enum ExamVersionStatus { Draft, InReview, Approved, Published, Unpublished }

public enum QuestionType
{
    MultipleChoice, MultipleSelect, TrueFalseNotGiven, YesNoNotGiven,
    Matching, Completion, ShortAnswer, Labelling, EssayTask, SpeakingResponse,
}

public static class QuestionTypeRules
{
    /// <summary>
    /// Which types are scored against an answer key, with no model involved.
    ///
    /// This split is requirement <c>A-11</c> made structural: Reading and
    /// Listening bands come from the answer key, deterministically, and an AI
    /// explanation can never modify one. It is also what lets Reading and
    /// Listening work with no AI provider configured at all.
    /// </summary>
    public static bool IsAutoScored(this QuestionType type) => type is not (
        QuestionType.EssayTask or QuestionType.SpeakingResponse);
}

/// <summary>
/// The content. Immutable once published.
///
/// Editing a published version is not possible — it produces a new version.
/// Sessions and results reference the exact version they used, so correcting a
/// conversion table cannot silently rewrite historical scores. That failure is
/// invisible until someone disputes a band, which is the worst time to find it.
/// </summary>
public sealed class ExamVersion
{
    private ExamVersion(
        ExamVersionId id, ExamDefinitionId definitionId, int versionNumber,
        string title, ExamVariant variant, ExamVersionStatus status,
        DateTimeOffset? publishedAt, ScoringProfile scoring, TimingProfile timing,
        IReadOnlyList<Section> sections, IReadOnlyList<ExamModule> moduleSequence,
        ListeningPlaybackProfile? listeningPlayback, string? description, UserId? authorId)
    {
        Id = id; DefinitionId = definitionId; VersionNumber = versionNumber;
        Title = title; Description = description;
        Variant = variant; Status = status; PublishedAt = publishedAt;
        Scoring = scoring; Timing = timing; Sections = sections;
        ModuleSequence = moduleSequence; AuthorId = authorId;
        ListeningPlayback = listeningPlayback ?? ListeningPlaybackProfile.Conservative;
    }

    public ExamVersionId Id { get; }
    public ExamDefinitionId DefinitionId { get; }
    public int VersionNumber { get; }
    public string Title { get; }

    /// <summary>
    /// What this paper is, in the learner's words. Null when the package did
    /// not supply one.
    ///
    /// <b>Carried on the version rather than the definition, because it
    /// describes the content and the content is what is frozen.</b> A published
    /// version is immutable; if the description belonged to the definition it
    /// could be edited after a sitting, and a result would then reference a
    /// paper described differently from the one that was actually sat.
    ///
    /// <b>Null is a supported state and must render as nothing.</b> Not "Chưa
    /// có mô tả", not an em dash — a card with no description simply has one
    /// less line. An invented placeholder is a sentence the academic team did
    /// not write appearing under a paper they did. → `G-11`
    /// </summary>
    public string? Description { get; }

    public ExamVariant Variant { get; }
    public ExamVersionStatus Status { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>
    /// The account that drafted this version, when known. `P-20` needs this
    /// to enforce reviewer ≠ author <b>on the server</b> — not by disabling a
    /// button in the CMS.
    ///
    /// <b>Null is a supported, common state.</b> The only production path
    /// that creates an <see cref="ExamVersion"/> today is
    /// <c>ExamPackageReader</c>, and it has no caller identity to attribute —
    /// threading one through the import/CLI pipeline is out of this slice's
    /// reach (`S6` territory). <see cref="Approve"/> cannot enforce its rule
    /// against an author nobody recorded, and it does not invent one; it
    /// simply lets the approval through. → `[OPEN QUESTION]`, S7 report
    /// </summary>
    public UserId? AuthorId { get; }
    public ScoringProfile Scoring { get; }
    public TimingProfile Timing { get; }
    public ListeningPlaybackProfile ListeningPlayback { get; }
    public IReadOnlyList<Section> Sections { get; }

    /// <summary>
    /// The order a Full Test advances through this version's modules.
    /// Resolved from <c>sequenceProfile</c> at import, or from
    /// <see cref="SequenceProfile.CanonicalOrder"/> when absent. → `E-12`
    /// </summary>
    public IReadOnlyList<ExamModule> ModuleSequence { get; }

    public bool IsSittable => Status == ExamVersionStatus.Published;

    public static ExamVersion CreateDraft(
        ExamDefinitionId definitionId, int versionNumber, string title, ExamVariant variant,
        ScoringProfile scoring, TimingProfile timing, IReadOnlyList<Section> sections,
        ListeningPlaybackProfile? listeningPlayback = null,
        IReadOnlyList<ExamModule>? declaredSequence = null,
        string? description = null,
        UserId? authorId = null)
    {
        var present = sections.Select(s => s.Module).ToHashSet();
        var sequence = SequenceProfile.Resolve(declaredSequence, present);
        return new(ExamVersionId.New(), definitionId, versionNumber, title, variant,
            ExamVersionStatus.Draft, null, scoring, timing, sections, sequence,
            listeningPlayback, description, authorId);
    }

    public static ExamVersion Rehydrate(
        ExamVersionId id, ExamDefinitionId definitionId, int versionNumber, string title,
        ExamVariant variant, ExamVersionStatus status, DateTimeOffset? publishedAt,
        ScoringProfile scoring, TimingProfile timing, IReadOnlyList<Section> sections,
        ListeningPlaybackProfile? listeningPlayback = null,
        IReadOnlyList<ExamModule>? moduleSequence = null,
        string? description = null,
        UserId? authorId = null)
    {
        var present = sections.Select(s => s.Module).ToHashSet();
        var sequence = moduleSequence ?? SequenceProfile.Resolve(null, present);
        return new(id, definitionId, versionNumber, title, variant, status, publishedAt,
            scoring, timing, sections, sequence, listeningPlayback, description, authorId);
    }

    public void Publish(DateTimeOffset now)
    {
        if (Status == ExamVersionStatus.Published)
            throw new InvalidOperationException("This version is already published.");
        Status = ExamVersionStatus.Published;
        PublishedAt = now;
    }

    /// <summary>
    /// Blocks new sessions. Does <b>not</b> end sessions already running —
    /// terminating a timed exam mid-attempt is a scoring incident, not an
    /// administrative action. → `M-15`
    /// </summary>
    public void Unpublish() => Status = ExamVersionStatus.Unpublished;

    /// <summary>
    /// The author sends a draft to review. First step of `P-20`'s
    /// <c>Draft → InReview → Approved → Published</c>.
    ///
    /// <b>Does not check for blocking errors or warnings.</b> That is the
    /// package validator's job, at import time — `P-19` explicitly allows a
    /// version with open warnings to enter review; only a blocking error
    /// stops content from reaching <see cref="ExamVersionStatus.Draft"/> at
    /// all, which means it stops it here too, by never having produced a
    /// draft to submit.
    /// </summary>
    public void SubmitForReview()
    {
        if (Status != ExamVersionStatus.Draft)
            throw new InvalidOperationException(
                $"Chỉ bản nháp mới nộp duyệt được. Version này đang ở trạng thái {Status}.");
        Status = ExamVersionStatus.InReview;
    }

    /// <summary>
    /// A different person signs off the content. `P-20`'s one non-negotiable
    /// rule — "bắt buộc khác người soạn, cưỡng chế ở server, không phải ẩn
    /// nút" — is enforced <b>here</b>, so it holds even for a caller who
    /// somehow reaches this method with every permission there is.
    ///
    /// <b>A no-op guard when <see cref="AuthorId"/> is null.</b> See that
    /// property's doc comment — most versions today carry no author, and
    /// refusing every one of them until the import pipeline threads an actor
    /// id through would stop content from shipping for a reason no operator
    /// on this screen can fix. That gap is tracked, not hidden: `[OPEN
    /// QUESTION]`, S7 report.
    /// </summary>
    public void Approve(UserId reviewerId)
    {
        if (Status != ExamVersionStatus.InReview)
            throw new InvalidOperationException(
                $"Chỉ version đang chờ duyệt mới duyệt được. Version này đang ở trạng thái {Status}.");
        if (AuthorId is { } author && author == reviewerId)
            throw new ReviewerIsAuthorException();
        Status = ExamVersionStatus.Approved;
    }

    /// <summary>
    /// The reviewer sends a draft back. `P-20`'s transition table: "Trả về
    /// kèm lý do" — a reason is not optional, the same way a Library return
    /// is not (`cms-content-operations.md` § 3.2, `Đ8`).
    ///
    /// <b>Goes straight to <see cref="ExamVersionStatus.Draft"/>, not to a
    /// separate "returned" state.</b> See <see cref="ExamVersionStatus"/>'s
    /// doc comment for why this enum has no fifth member for it. The reason
    /// travels into the audit log's detail dictionary, which is where every
    /// other "why" on this admin surface already lives — there is no
    /// `ExamReviewNote` type, and none is added by this slice.
    /// </summary>
    public void ReturnToDraft(string reason)
    {
        if (Status != ExamVersionStatus.InReview)
            throw new InvalidOperationException(
                $"Chỉ version đang chờ duyệt mới trả về được. Version này đang ở trạng thái {Status}.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Trả về đề cần kèm lý do.", nameof(reason));
        Status = ExamVersionStatus.Draft;
    }

    public Section? Section(ExamModule module) =>
        Sections.FirstOrDefault(s => s.Module == module);

    /// <summary>
    /// The settled Full Test fallback order (`E-12`). Prefer
    /// <see cref="ModuleSequence"/> for a specific version.
    /// </summary>
    public static IReadOnlyList<ExamModule> FullTestOrder => SequenceProfile.CanonicalOrder;

    /// <summary>The next module in a Full Test, or null when the session is complete.</summary>
    public ExamModule? NextModuleAfter(ExamModule current)
    {
        for (var i = 0; i < ModuleSequence.Count; i++)
        {
            if (ModuleSequence[i] != current) continue;
            return i + 1 < ModuleSequence.Count ? ModuleSequence[i + 1] : null;
        }

        return null;
    }

    public ExamModule FirstModule() =>
        ModuleSequence.FirstOrDefault(m => Section(m) is not null,
            Sections.Count > 0 ? Sections[0].Module
                : throw new InvalidOperationException("An exam version has no sections."));
}

/// <summary>
/// <see cref="ExamVersion.Approve"/> refused because the reviewer and the
/// author are the same account — `P-20`'s one non-negotiable rule.
///
/// <b>A distinct type, not a bare <see cref="InvalidOperationException"/>.</b>
/// The API layer has to tell this apart from "this version is not awaiting
/// review right now": one is a 403 that no retry or wait fixes, the other is
/// a 409 that a status change resolves.
/// </summary>
public sealed class ReviewerIsAuthorException() : InvalidOperationException(
    "Người duyệt không thể là người soạn. Cần một người khác duyệt version này.");

public sealed record Section(ExamModule Module, int Order, IReadOnlyList<SectionPart> Parts)
{
    public IEnumerable<Question> Questions => Parts.SelectMany(p => p.Questions);

    /// <summary>
    /// The highest raw score obtainable — marks, not question objects.
    ///
    /// <b>The band table is equated against the answer sheet.</b> A real
    /// Listening paper has forty numbered lines; "Choose TWO letters" fills
    /// two of them and is worth two. Counting objects made this 36 for a
    /// forty-mark section, so the table's own top four bands were unreachable
    /// and every score below them was read off the wrong row.
    ///
    /// The name kept its old meaning for two years and would have kept it
    /// through this change too, which is why it changed: `AutoScoredMarks`
    /// sounded like a count of questions and is now a sum of marks.
    /// </summary>
    public int AutoScoredMarks => Questions.Where(q => q.Type.IsAutoScored()).Sum(q => q.Marks);
}

public sealed record SectionPart(
    int Order,
    string Kind,
    string? Title,
    string? Body,
    string? AudioKey,
    string? ImageKey,
    string? Transcript,
    int? TaskNumber,
    int? PartNumber,
    CueCard? CueCard,
    int? MinWords,
    IReadOnlyList<Question> Questions,
    PartTiming? Timing = null);

/// <summary>Optional per-part timing override carried by package v2.</summary>
public sealed record PartTiming(int DurationSeconds, int? PrepSeconds = null, int? ResponseSeconds = null);

public sealed record CueCard(string Topic, IReadOnlyList<string> Bullets);

public sealed record QuestionOption(string Key, string Text);

/// <summary>
/// The shared frame a run of questions is answered inside — a heading bank, a
/// table, a diagram, a map, a summary paragraph.
///
/// <b>It is not decoration.</b> A matching question without its bank is a
/// prompt with nothing to choose from, and a map labelling without its map is a
/// prompt about a room nobody can see. On the first real package authored
/// against this schema, 55 of 76 auto-scored questions carry one — so a
/// renderer that ignores groups does not render a plainer exam, it renders an
/// unanswerable one.
///
/// <b>Repeated on every member rather than held once on the part.</b> A
/// question then stays self-describing wherever it travels — a review screen, a
/// report, a single-question fixture — and the renderer groups by
/// <see cref="Id"/> instead of by position, which is what makes the order of
/// the questions array the only thing that has to be right.
/// </summary>
public sealed record QuestionGroup(
    string Id,
    string? Title,
    string? Instruction,
    string? Image,
    string? Text,
    bool EachLetterOnce);

/// <summary>
/// One answer-sheet position. A question may occupy more than one position
/// (for example, a "Choose TWO" prompt) while retaining one shared prompt.
/// </summary>
public sealed record ResponseSlot(string Id, int Number, AnswerKey? AnswerKey);

public sealed record QuestionExplanation(
    string? CorrectAnswer,
    string ShortReason,
    IReadOnlyList<string> Evidence,
    string? CommonMistake = null);

public sealed record Question(
    string Id,
    int Order,
    QuestionType Type,
    string? Prompt,
    IReadOnlyList<QuestionOption> Options,
    int? MaxWords,
    AnswerKey? AnswerKey,
    QuestionGroup? Group = null,
    /// <summary>
    /// Raw marks this question carries. One unless the paper says otherwise.
    ///
    /// <b>"Choose TWO letters" is two marks.</b> It occupies two numbered
    /// lines on a real answer sheet, and the band table is equated against
    /// numbered lines — not against however many objects the author happened
    /// to model them as. Counting objects instead scored a 40-mark Listening
    /// section out of 36 and made the top of its own table unreachable.
    ///
    /// <b>How a multi-mark question is part-marked is not decided here.</b>
    /// The answer key is a set that must match, so today it is all or nothing;
    /// a half-mark rule needs an answer-key shape that does not exist and a
    /// decision nobody has made. → `[OPEN QUESTION]`
    /// </summary>
    int Marks = 1,
    IReadOnlyList<ResponseSlot>? Slots = null,
    QuestionExplanation? Explanation = null);

/// <summary>
/// The accepted answers. <b>Never sent to a client before scoring</b> — a
/// client that can score locally makes the exam unscoreable. → threat `T7`
/// </summary>
public sealed record AnswerKey(
    IReadOnlyList<AcceptedAnswer> Accepted,
    AnswerMatchingRules? Overrides);

/// <summary>
/// One acceptable response. Exactly one of the three shapes is populated:
/// a single value, a set that must all be present, or a matching pair.
/// </summary>
public sealed record AcceptedAnswer(
    string? Single,
    IReadOnlyList<string>? All,
    (string Left, string Right)? Pair);

public sealed record TimingProfile(
    IReadOnlyDictionary<ExamModule, int> SectionDurationSeconds,
    int? ListeningTransferSeconds,
    IReadOnlyList<SpeakingPartTiming> SpeakingParts)
{
    /// <summary>
    /// How long a learner gets, deadline included.
    ///
    /// <b>Listening's transfer time is part of the section, not an extra
    /// courtesy.</b> A real Listening paper is thirty minutes of audio
    /// <i>plus</i> ten minutes to copy answers onto the answer sheet, and the
    /// ten minutes are scored time — they are when most candidates fix
    /// spelling and fill the blanks they left. Leaving them out of the deadline
    /// is not a rounding difference: on Exam 1 the four parts run 24 minutes 33
    /// seconds against a 30-minute section, so a learner playing straight
    /// through had about five and a half minutes to transfer forty answers
    /// while the paper's own <c>transferNote</c> promised ten. The clock ran
    /// out before the exam said it would, and the answers not yet typed were
    /// simply lost.
    ///
    /// <b>Only Listening.</b> The other three write straight onto the sheet, so
    /// there is nothing to transfer and no second phase to add.
    /// </summary>
    public TimeSpan DurationFor(ExamModule module) =>
        SectionDurationSeconds.TryGetValue(module, out var seconds)
            ? TimeSpan.FromSeconds(seconds + TransferAllowanceFor(module))
            : module == ExamModule.Speaking && SpeakingParts.Count > 0
                ? TimeSpan.FromSeconds(SpeakingParts.Sum(p => p.PrepSeconds + p.ResponseSeconds))
                : throw new InvalidOperationException($"No timing configured for {module}.");

    /// <summary>
    /// Zero unless this is Listening and the version declares an allowance.
    ///
    /// A version that declares none gets none — an exam whose author left the
    /// field out is not an invitation to add ten minutes on their behalf, and
    /// the length of a transfer window is a property of the paper. → `G-11`
    /// </summary>
    private int TransferAllowanceFor(ExamModule module) =>
        module == ExamModule.Listening ? ListeningTransferSeconds ?? 0 : 0;
}

public sealed record SpeakingPartTiming(int Part, int PrepSeconds, int ResponseSeconds);

/// <summary>
/// Versioned Listening playback rules. Practice and mock are intentionally
/// separate: a client must not infer exam conditions from its route or ship a
/// permanent one-pass default. Older packages resolve fail-closed to one pass
/// with no seek for both run kinds.
/// </summary>
public sealed record ListeningPlaybackProfile(
    AudioPlaybackRule Practice,
    AudioPlaybackRule Mock)
{
    public static readonly ListeningPlaybackProfile Conservative = new(
        new AudioPlaybackRule(true, false),
        new AudioPlaybackRule(true, false));
}

public sealed record AudioPlaybackRule(bool PlayOnce, bool AllowSeek);

public sealed record AnswerMatchingRules(
    bool CaseSensitive = false,
    bool TrimWhitespace = true,
    bool CollapseInnerWhitespace = true,
    bool AllowSpellingVariants = false,
    bool NumericEquivalence = true)
{
    public static readonly AnswerMatchingRules Default = new();
}

public enum MultiMarkPartialCredit { AllOrNothing }

public sealed record PartialCreditPolicy(MultiMarkPartialCredit MultiMark);

/// <summary>
/// Raw score to band, plus how answers are compared. <b>Configuration, not
/// code</b> — boundaries are equated per test version, so the same raw score
/// can mean different bands across versions. Attaching this to the version is
/// what makes a historical score reproducible. → `G-3`, `H-4`
/// </summary>
public sealed record ScoringProfile(
    IReadOnlyDictionary<ExamModule, IReadOnlyList<BandBoundary>> RawToBand,
    AnswerMatchingRules Matching,
    decimal? WritingTask1Weight = null,
    decimal? WritingTask2Weight = null,
    PartialCreditPolicy? PartialCredit = null,
    /// <summary>
    /// Where this version's raw-to-band tables came from, and therefore
    /// whether a Reading or Listening band may reach a learner. `P-11`: a
    /// band is shown only when the table is <see
    /// cref="BandTableProvenanceStatus.Equated"/>.
    ///
    /// <b>Absent means not-equated, and that is deliberate — the conservative
    /// reading, never the flattering one.</b> `H-4` (where VNI's tables came
    /// from) is still open, so a version that says nothing about its table's
    /// origin gets treated exactly like one that admits the table is
    /// invented. No default fills the gap. → `G-11`
    /// </summary>
    BandTableProvenance? Provenance = null)
{
    /// <summary>
    /// The relative weight of Writing Task 1 and Task 2, or a refusal.
    ///
    /// <b>Nullable on purpose, and this used to default to 1 and 2.</b>
    /// `docs/domain/band-scoring.md` records the two-to-one split as
    /// `[ASSUMPTION]` carrying `[NEEDS VALIDATION]`: Task 2 is known to weigh
    /// more, but IELTS does not publish the exact ratio the way it publishes
    /// the overall-band rule. A default turned that open question into a
    /// silent answer — every exam version without an explicit weighting got
    /// marked on a guess, and nothing anywhere said so.
    ///
    /// So it refuses, in the same way <see cref="BandFor"/> refuses a raw
    /// score its table does not cover. A Writing band that combines two tasks
    /// needs the ratio; an exam version that does not carry one is incomplete
    /// content, not an invitation to pick a number. → `G-11`
    /// </summary>
    public (decimal Task1, decimal Task2) RequireWritingTaskWeights()
    {
        if (WritingTask1Weight is { } t1 && WritingTask2Weight is { } t2)
        {
            if (t1 <= 0m || t2 <= 0m)
                throw new InvalidOperationException(
                    "Writing task weights must be positive. Refusing to combine two task bands "
                    + $"on a weighting of {t1}:{t2}.");

            return (t1, t2);
        }

        throw new InvalidOperationException(
            "This exam version declares no Writing task weighting, and there is no default to "
            + "fall back on — the official Task 1 : Task 2 ratio is not published, so a default "
            + "would be a guess applied silently. Set scoring.criterionWeights.writing on the "
            + "exam version. Refusing to invent a weighting.");
    }

    /// <summary>
    /// The band for a raw score. Boundaries are sorted descending and the
    /// first whose threshold is met wins.
    /// </summary>
    public BandScore BandFor(ExamModule module, int rawScore)
    {
        if (!RawToBand.TryGetValue(module, out var table) || table.Count == 0)
            throw new InvalidOperationException(
                $"No raw-to-band table configured for {module}. Refusing to invent a band.");

        foreach (var boundary in table.OrderByDescending(b => b.MinRaw))
        {
            if (rawScore >= boundary.MinRaw)
                return boundary.Band;
        }

        // Below EVERY declared threshold — the table does not cover this score.
        //
        // An earlier version returned band 0 here. That was the precise failure
        // band-scoring.md warns about: a learner scoring 14 out of 40 against a
        // table whose lowest entry is 15 was shown 0.0, which is a real,
        // reportable band and therefore indistinguishable from a genuine zero.
        // Nobody would have investigated it.
        //
        // A missing band is a broken exam version, not a bad performance. The
        // real defence is CoversRange rejecting the table at import; this throw
        // is the backstop for a version that reached storage some other way.
        throw new InvalidOperationException(
            $"The {module} raw-to-band table has no entry at or below a raw score of {rawScore}. "
            + "Refusing to invent a band — this exam version's scoring profile is incomplete.");
    }

    /// <summary>
    /// Whether the table covers the whole raw range without a gap.
    ///
    /// An incomplete table does not fail loudly — it silently produces a wrong
    /// band for every score inside the gap. Checked in code rather than by the
    /// JSON Schema, which can validate shape but not coverage.
    /// </summary>
    /// <summary>
    /// Whether every raw score from 0 to <paramref name="maxRaw"/> resolves to a band.
    ///
    /// <para>
    /// <b>The gap that matters is at the BOTTOM, not the top.</b> An earlier
    /// version of this method checked whether the highest threshold reached
    /// <c>maxRaw</c>, which is the wrong end: <see cref="BandFor"/> walks
    /// downwards and takes the first threshold at or below the score, so a
    /// score above the top threshold resolves fine. What does not resolve is a
    /// score <i>below the lowest</i> threshold — and a table pasted from a
    /// source that only listed bands 5 and up leaves every score under 15
    /// uncovered.
    /// </para>
    ///
    /// <para>
    /// That is not a hypothetical. It produced band 0.0 for a learner who
    /// answered 14 questions correctly.
    /// </para>
    /// </summary>
    public bool CoversRange(ExamModule module, int maxRaw, out int firstUncovered)
    {
        firstUncovered = -1;

        if (!RawToBand.TryGetValue(module, out var table) || table.Count == 0)
        {
            firstUncovered = 0;
            return false;
        }

        // Every score from 0 upward must find a threshold at or below it, so
        // the table must start at 0. Anything less leaves a silent hole.
        var lowest = table.Min(b => b.MinRaw);
        if (lowest > 0)
        {
            firstUncovered = 0;
            return false;
        }

        // A threshold above the maximum achievable score is not a gap, but it
        // is a sign the table was written for a different question count — a
        // 40-row table on a 15-question section means someone reused the wrong
        // profile, and the bands will be wrong across the whole range.
        var highest = table.Max(b => b.MinRaw);
        if (highest > maxRaw)
        {
            firstUncovered = maxRaw + 1;
            return false;
        }

        return true;
    }
}

public sealed record BandBoundary(int MinRaw, BandScore Band);

/// <summary>
/// Where a version's raw-to-band conversion tables came from.
///
/// `H-4` — whether VNI's tables are licensed, internally equated, or
/// approximated — is open, and this enumeration is what lets a version say
/// which of those is true of its own tables rather than leaving every reader
/// to assume the flattering answer. → `contracts/schemas/exam.schema.json`
/// `bandTableProvenance`
/// </summary>
public enum BandTableProvenanceStatus
{
    /// <summary>Invented for this repository. Never learner-facing.</summary>
    Synthetic,

    /// <summary>A real table, but generic — not equated to this paper.</summary>
    Provisional,

    /// <summary>
    /// Equated to this test version. The only status that may feed a band
    /// trend, and the only one under which `P-11` lets a Reading or Listening
    /// band reach a learner.
    /// </summary>
    Equated,
}

/// <summary>
/// <see cref="Source"/> is required by the package schema when
/// <see cref="Status"/> is <see cref="BandTableProvenanceStatus.Equated"/> —
/// an equated table without a stated source is an unfalsifiable claim, and
/// it is the claim that unlocks a learner-facing band. Not re-validated here:
/// the schema is the gate at import time, and this record carries whatever
/// passed it.
/// </summary>
public sealed record BandTableProvenance(
    BandTableProvenanceStatus Status, string? Source = null, string? Note = null);
