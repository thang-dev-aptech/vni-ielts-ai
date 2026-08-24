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

public enum ExamVersionStatus { Draft, Published, Unpublished }

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
        IReadOnlyList<Section> sections)
    {
        Id = id; DefinitionId = definitionId; VersionNumber = versionNumber;
        Title = title; Variant = variant; Status = status; PublishedAt = publishedAt;
        Scoring = scoring; Timing = timing; Sections = sections;
    }

    public ExamVersionId Id { get; }
    public ExamDefinitionId DefinitionId { get; }
    public int VersionNumber { get; }
    public string Title { get; }
    public ExamVariant Variant { get; }
    public ExamVersionStatus Status { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public ScoringProfile Scoring { get; }
    public TimingProfile Timing { get; }
    public IReadOnlyList<Section> Sections { get; }

    public bool IsSittable => Status == ExamVersionStatus.Published;

    public static ExamVersion CreateDraft(
        ExamDefinitionId definitionId, int versionNumber, string title, ExamVariant variant,
        ScoringProfile scoring, TimingProfile timing, IReadOnlyList<Section> sections) =>
        new(ExamVersionId.New(), definitionId, versionNumber, title, variant,
            ExamVersionStatus.Draft, null, scoring, timing, sections);

    public static ExamVersion Rehydrate(
        ExamVersionId id, ExamDefinitionId definitionId, int versionNumber, string title,
        ExamVariant variant, ExamVersionStatus status, DateTimeOffset? publishedAt,
        ScoringProfile scoring, TimingProfile timing, IReadOnlyList<Section> sections) =>
        new(id, definitionId, versionNumber, title, variant, status, publishedAt,
            scoring, timing, sections);

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

    public Section? Section(ExamModule module) =>
        Sections.FirstOrDefault(s => s.Module == module);

    /// <summary>
    /// The order a Full Test advances through: Reading → Listening → Writing →
    /// Speaking. A VNI product decision (`E-12`), deliberately not the official
    /// IELTS order. The client never chooses; a client-supplied "next" would be
    /// a way to skip Writing.
    /// </summary>
    public static readonly IReadOnlyList<ExamModule> FullTestOrder =
        [ExamModule.Reading, ExamModule.Listening, ExamModule.Writing, ExamModule.Speaking];

    /// <summary>The next module in a Full Test, or null when the session is complete.</summary>
    public ExamModule? NextModuleAfter(ExamModule current)
    {
        var present = FullTestOrder.Where(m => Section(m) is not null).ToList();
        var index = present.IndexOf(current);
        return index >= 0 && index + 1 < present.Count ? present[index + 1] : null;
    }

    public ExamModule FirstModule() =>
        FullTestOrder.FirstOrDefault(m => Section(m) is not null,
            Sections.Count > 0 ? Sections[0].Module
                : throw new InvalidOperationException("An exam version has no sections."));
}

public sealed record Section(ExamModule Module, int Order, IReadOnlyList<SectionPart> Parts)
{
    public IEnumerable<Question> Questions => Parts.SelectMany(p => p.Questions);
    public int AutoScoredCount => Questions.Count(q => q.Type.IsAutoScored());
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
    IReadOnlyList<Question> Questions);

public sealed record CueCard(string Topic, IReadOnlyList<string> Bullets);

public sealed record QuestionOption(string Key, string Text);

public sealed record Question(
    string Id,
    int Order,
    QuestionType Type,
    string? Prompt,
    IReadOnlyList<QuestionOption> Options,
    int? MaxWords,
    AnswerKey? AnswerKey);

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
    public TimeSpan DurationFor(ExamModule module) =>
        SectionDurationSeconds.TryGetValue(module, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : module == ExamModule.Speaking && SpeakingParts.Count > 0
                ? TimeSpan.FromSeconds(SpeakingParts.Sum(p => p.PrepSeconds + p.ResponseSeconds))
                : throw new InvalidOperationException($"No timing configured for {module}.");
}

public sealed record SpeakingPartTiming(int Part, int PrepSeconds, int ResponseSeconds);

public sealed record AnswerMatchingRules(
    bool CaseSensitive = false,
    bool TrimWhitespace = true,
    bool CollapseInnerWhitespace = true,
    bool AllowSpellingVariants = false,
    bool NumericEquivalence = true)
{
    public static readonly AnswerMatchingRules Default = new();
}

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
    decimal? WritingTask2Weight = null)
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
