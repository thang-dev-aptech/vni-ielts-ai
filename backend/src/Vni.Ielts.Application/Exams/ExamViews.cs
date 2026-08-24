using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Exams;

/// <summary>
/// What a learner's client is allowed to see.
///
/// <b>These types exist to make one rule structural rather than careful.</b>
/// <see cref="Question"/> carries an <see cref="AnswerKey"/>; <see
/// cref="QuestionView"/> has no field it could travel in. A client that can
/// score locally makes the exam unscoreable, and the only reliable defence is
/// a shape the key cannot fit through — not a mapper someone remembers to
/// keep correct. → threat `T7`
///
/// The same reasoning removes <c>Transcript</c> from <see cref="PartView"/>:
/// a Listening transcript is the answer sheet in prose.
/// </summary>
public sealed record ExamCatalogueItem(
    string ExamVersionId,
    string Title,
    string Variant,
    IReadOnlyList<ModuleSummary> Modules);

public sealed record ModuleSummary(string Module, int QuestionCount, int DurationSeconds);

public sealed record QuestionView(
    string Id,
    int Order,
    string Type,
    string? Prompt,
    IReadOnlyList<QuestionOptionView> Options,
    int? MaxWords);

public sealed record QuestionOptionView(string Key, string Text);

public sealed record CueCardView(string Topic, IReadOnlyList<string> Bullets);

public sealed record PartView(
    int Order,
    string Kind,
    string? Title,
    string? Body,
    string? AudioKey,
    string? ImageKey,
    int? TaskNumber,
    int? PartNumber,
    CueCardView? CueCard,
    int? MinWords,
    IReadOnlyList<QuestionView> Questions);

/// <summary>
/// The open section, with the deadline the server derived.
///
/// <see cref="RemainingSeconds"/> is a convenience for a first render only.
/// The client must keep counting from <see cref="DeadlineAt"/> against the
/// server clock it already reconciles on every response — a number that ages
/// in transit is not a timer. → ADR-0007
/// </summary>
public sealed record SpeakingPartTimingView(int Part, int PrepSeconds, int ResponseSeconds);

public sealed record CurrentSectionView(
    string Module,
    DateTimeOffset StartedAt,
    DateTimeOffset DeadlineAt,
    int RemainingSeconds,
    IReadOnlyList<PartView> Parts,
    IReadOnlyDictionary<string, string?> Answers,
    /// <summary>
    /// Per-part preparation and response times, Speaking only and empty
    /// elsewhere.
    ///
    /// It travels with the section rather than being hard-coded in the client
    /// because it is configuration on the exam version — and because `H-1`
    /// (one session or three submissions) is still open, so the client must
    /// not be the place that decides what a Speaking part is.
    /// </summary>
    IReadOnlyList<SpeakingPartTimingView> SpeakingTiming,
    /// <summary>
    /// Listening only: extra time after the audio to copy answers over. Null
    /// when the version does not configure it.
    /// </summary>
    int? TransferSeconds);

public sealed record SessionView(
    string SessionId,
    string ExamVersionId,
    string ExamTitle,
    string Mode,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ServerNow,
    IReadOnlyList<string> CompletedModules,
    CurrentSectionView? Current);

public sealed record QuestionResultView(string QuestionId, string? Submitted, bool IsCorrect);

/// <summary>
/// A marked section.
///
/// <c>Band</c> is a <see cref="decimal"/> straight off <see cref="BandScore"/>,
/// which only admits whole and half bands — so a client cannot be handed a
/// 6.25 to round however it likes. Product law L3 lives on the other side:
/// a section with no result is absent from the list, never present with a zero.
/// </summary>
public sealed record SectionResultView(
    string Module,
    int RawScore,
    int MaxScore,
    decimal Band,
    IReadOnlyList<QuestionResultView> Questions);

public sealed record SessionResultsView(
    string SessionId,
    string ExamTitle,
    string Mode,
    string Status,
    DateTimeOffset? SubmittedAt,
    IReadOnlyList<SectionResultView> Sections,
    /// <summary>
    /// Null until every one of the four modules has a band. Absent is the
    /// honest state; the client draws it as `—`, never as a partial average.
    /// </summary>
    decimal? OverallBand);

/// <summary>
/// One row of a learner's own history.
///
/// <b>Deliberately not <see cref="SessionResultsView"/>.</b> That one carries
/// every question and every mark, which is right for the results screen and
/// wrong for a list — ten sittings of it is a payload measured in hundreds of
/// kilobytes to render a handful of numbers.
///
/// <b>Bands are nullable at every level and that is the honest shape.</b>
/// Reading and Listening are marked from the answer key the moment the section
/// closes; Writing and Speaking have no evaluation pipeline yet, so their band
/// is absent rather than zero. Product law L3 — the client draws `—`.
/// </summary>
public sealed record SittingSummaryView(
    string SessionId,
    string ExamVersionId,
    string ExamTitle,
    string Variant,
    string Mode,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,

    /// <summary>The section a learner would return to. Null unless in progress.</summary>
    string? CurrentModule,

    /// <summary>
    /// The server's deadline for that section, so the client can show how long
    /// is left without asking a second time. Display only — the server still
    /// decides what is late. → ADR-0007
    /// </summary>
    DateTimeOffset? DeadlineAt,

    IReadOnlyList<SittingSectionView> Sections,

    /// <summary>Null until every section of this sitting has a band.</summary>
    decimal? OverallBand);

public sealed record SittingSectionView(string Module, decimal? Band);

/// <summary>
/// The overall band of a sitting — or nothing, which is the usual answer.
/// </summary>
public static class SittingBand
{
    private static readonly IReadOnlyList<ExamModule> FourSkills =
    [
        ExamModule.Listening, ExamModule.Reading, ExamModule.Writing, ExamModule.Speaking,
    ];

    /// <summary>
    /// <b>All four skills or nothing.</b> An IELTS overall band is the mean of
    /// Listening, Reading, Writing and Speaking, so a single-skill sitting has
    /// no overall band at all. The first version of this returned one anyway:
    /// a Reading-only sitting reported <c>overall = 0.0</c> — the Reading band
    /// wearing a label that means something else, which on a dashboard reads
    /// as "your overall band is 0".
    ///
    /// A partial mean is worse. Two marked skills out of four averaged
    /// together is not a band, it is an arithmetic accident that looks like
    /// one, and it moves every time another section is marked. Writing and
    /// Speaking have no evaluation pipeline yet, so today this returns null for
    /// every sitting in the product — which is the honest answer, and the
    /// client draws `—`. → L3
    /// </summary>
    public static decimal? Overall(IReadOnlyList<SittingSectionView> sections)
    {
        if (sections.Count != FourSkills.Count) return null;

        var bands = new List<BandScore>(FourSkills.Count);

        foreach (var module in FourSkills)
        {
            var section = sections.FirstOrDefault(
                s => string.Equals(s.Module, module.ToString(), StringComparison.OrdinalIgnoreCase));
            if (section?.Band is not { } band) return null;

            bands.Add(BandScore.Create(band));
        }

        return BandScore.Overall(bands).Value;
    }
}

internal static class ExamViewMapping
{
    public static ExamCatalogueItem ToCatalogueItem(this ExamVersion version) =>
        new(
            version.Id.Value,
            version.Title,
            version.Variant.ToString().ToLowerInvariant(),
            [.. version.Sections
                .OrderBy(s => s.Order)
                .Select(s => new ModuleSummary(
                    s.Module.ToString().ToLowerInvariant(),
                    s.Questions.Count(),
                    (int)version.Timing.DurationFor(s.Module).TotalSeconds))]);

    public static QuestionView ToView(this Question question) =>
        new(
            question.Id,
            question.Order,
            question.Type.ToWire(),
            question.Prompt,
            [.. question.Options.Select(o => new QuestionOptionView(o.Key, o.Text))],
            question.MaxWords);

    public static PartView ToView(this SectionPart part) =>
        new(
            part.Order,
            part.Kind,
            part.Title,
            part.Body,
            part.AudioKey,
            part.ImageKey,
            part.TaskNumber,
            part.PartNumber,
            part.CueCard is { } card ? new CueCardView(card.Topic, card.Bullets) : null,
            part.MinWords,
            [.. part.Questions.OrderBy(q => q.Order).Select(q => q.ToView())]);

    public static SectionResultView ToView(this SectionScore score) =>
        new(
            score.Module.ToString().ToLowerInvariant(),
            score.RawScore,
            score.MaxScore,
            score.Band.Value,
            [.. score.Questions.Select(q => new QuestionResultView(q.QuestionId, q.Submitted, q.IsCorrect))]);

    /// <summary>
    /// `true-false-notgiven`, not `TrueFalseNotGiven`.
    ///
    /// The wire spelling matches the exam package schema, so one vocabulary
    /// covers authoring, storage and the client. A second spelling would mean
    /// a translation table nobody keeps in sync.
    /// </summary>
    public static string ToWire(this QuestionType type) => type switch
    {
        QuestionType.MultipleChoice => "multiple-choice",
        QuestionType.MultipleSelect => "multiple-select",
        QuestionType.TrueFalseNotGiven => "true-false-notgiven",
        QuestionType.YesNoNotGiven => "yes-no-notgiven",
        QuestionType.Matching => "matching",
        QuestionType.Completion => "completion",
        QuestionType.ShortAnswer => "short-answer",
        QuestionType.Labelling => "labelling",
        QuestionType.EssayTask => "essay-task",
        QuestionType.SpeakingResponse => "speaking-response",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unmapped question type."),
    };
}
