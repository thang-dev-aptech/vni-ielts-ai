using Vni.Ielts.Domain.Assessment;
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

/// <summary>
/// The shared frame a run of questions is answered inside.
///
/// <b>It crosses to the client, unlike the answer key, and the difference is
/// the point.</b> The key is what a candidate must not see; the group is what
/// they cannot answer without — the bank of headings to choose from, the map
/// whose rooms are being labelled, the paragraph whose gaps are being filled.
/// The rubric line travels verbatim for the same reason: it is a scoring rule
/// stated to the candidate, and a paraphrase marks people wrong for obeying it.
/// </summary>
public sealed record QuestionGroupView(
    string Id,
    string? Title,
    string? Instruction,
    string? ImageKey,
    string? Text,
    bool EachLetterOnce);

public sealed record QuestionView(
    string Id,
    int Order,
    string Type,
    string? Prompt,
    IReadOnlyList<QuestionOptionView> Options,
    int? MaxWords,
    QuestionGroupView? Group);

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
    /// <summary>
    /// Null for luyện đề, which has no deadline at all.
    ///
    /// <b>Null rather than a far-future date.</b> A sentinel would make every
    /// countdown in the client work by accident and every rule about lateness
    /// silently true, which is the worst way for a distinction this important
    /// to be expressed.
    /// </summary>
    DateTimeOffset? DeadlineAt,
    /// <summary>Null where there is no deadline. Zero means the time is up.</summary>
    int? RemainingSeconds,
    /// <summary>
    /// How long this section has been worked on, by the <b>server's</b> clock.
    ///
    /// The practice clock counts up, and it counts up here rather than in the
    /// browser. A client that accumulated its own total would be a client that
    /// could be told to accumulate less. → ADR-0007
    /// </summary>
    int ElapsedSeconds,
    /// <summary>Whether the stopwatch is running. False while paused.</summary>
    bool Running,
    /// <summary>
    /// The learner's own goal in seconds, if they set one.
    ///
    /// <b>Display only, and the server enforces nothing with it.</b> The moment
    /// any rule reads it, it has become a deadline the learner chose — and
    /// luyện đề has become the exam. → `E-22`
    /// </summary>
    int? TargetSeconds,
    IReadOnlyList<PartView> Parts,
    IReadOnlyDictionary<string, string?> Answers,
    /// <summary>
    /// The version of the answer sheet these answers were read at.
    ///
    /// <b>Without it, the first autosave after any page load is a blind
    /// write.</b> The client had no revision to send, so the server filled in
    /// "whatever is current" on its behalf — and a compare-and-swap that
    /// supplies its own expectation compares nothing. A second tab moving the
    /// sheet on between the load and the save would have its work overwritten
    /// by a draft composed before it existed, which is the exact failure the
    /// revision was added to stop.
    ///
    /// It travels with the section, so an <c>advance</c> hands the next
    /// section's revision over in the same response that opens it.
    /// </summary>
    int AnswerRevision,
    /// <summary>
    /// Per-question ordering tokens, as the sheet currently holds them.
    ///
    /// <b>A page restoring unsent work from disk needs these to tell what is
    /// still unsent.</b> The client journals every keystroke before it goes on
    /// the wire, so a tab that crashes mid-section comes back with entries that
    /// may or may not have landed. Comparing each entry's token against the
    /// stored one is what distinguishes "the server never saw this" from "this
    /// already landed" — and restoring the second kind would put an old answer
    /// back on screen over a newer one.
    ///
    /// The revision cannot answer it: one number for the whole sheet says
    /// whether the page is behind, not which questions it is behind on.
    /// → `patchJournal.ts`, `IAnswerSheetStore.PatchAsync`
    /// </summary>
    IReadOnlyDictionary<string, long> AnswerSequences,
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
    /// Writing and Speaking, which are marked rather than scored.
    ///
    /// <b>A separate list from <c>Sections</c>, because they are not the same
    /// kind of result.</b> A section result answers "how many did they get
    /// right"; a marking answers "on what basis". Flattening them would force
    /// one shape to carry both and leave the client deciding which half of the
    /// fields to believe. Writing appears twice — once per task — and Speaking
    /// once, which is how IELTS marks them.
    /// </summary>
    IReadOnlyList<SectionMarkingView> Markings,
    /// <summary>
    /// Why a module has no band yet, for the modules that have none.
    ///
    /// <b>A dash needs a reason, and one sentence for every reason is a lie.</b>
    /// Product law `L3` says an unmarked skill shows `—` rather than a zero or
    /// an average, which is right — and it is where the trail goes cold. A
    /// learner, or the person answering their support ticket, is looking at a
    /// dash with no way to tell whether the essay never arrived, whether
    /// nothing has been wired up, whether a model answered and was refused, or
    /// whether the platform tried five times and stopped. Those are four
    /// situations with four different fixes, and the client was showing one
    /// message for all of them.
    ///
    /// Empty when everything that can be marked has been. → `I3.6`
    /// </summary>
    IReadOnlyList<MarkingStatusView> MarkingStatuses,
    /// <summary>
    /// Null until every one of the four modules has a band. Absent is the
    /// honest state; the client draws it as `—`, never as a partial average.
    /// </summary>
    decimal? OverallBand);

/// <summary>
/// One module's marking, and where it has got to.
/// </summary>
/// <param name="State">
/// `pending` · `running` · `retryable` · `failed` · `completed`.
///
/// <b>The worker's own state, not a translation of it.</b> A client that knows
/// the difference between "waiting" and "given up" can say so; one given a
/// single boolean cannot, and will invent a sentence that is wrong a quarter of
/// the time.
/// </param>
/// <param name="Reason">
/// A safe sentence for the learner, or null while nothing has gone wrong.
///
/// <b>Safe means it says what is true without saying what is private.</b> A
/// provider's raw error can carry a prompt fragment, a request id, or the
/// learner's own words back at them; none of that belongs on a results screen.
/// The mapping is deliberately coarse.
/// </param>
public sealed record MarkingStatusView(
    string Module,
    string State,
    int Attempts,
    string? Reason);

/// <summary>
/// One marked Writing task, or one marked Speaking test.
///
/// <b>Every band here arrives with the evidence for it.</b> `A-13c` requires a
/// band to carry a quoted basis, and the quote is the whole difference between
/// a mark a learner can learn from and a number they can only accept.
/// </summary>
public sealed record SectionMarkingView(
    string Module,
    int? TaskNumber,
    /// <summary>The rubric this was produced under, so an old band stays explicable.</summary>
    string RubricVersion,
    decimal Band,
    IReadOnlyList<CriterionAssessmentView> Criteria,
    /// <summary>
    /// Non-empty when validation found something worth a human's attention —
    /// the model's own band disagreeing with the recomputation, or a quotation
    /// that does not occur in the learner's submission. The band is still
    /// reportable; flagged means "usable, and worth a look".
    /// </summary>
    IReadOnlyList<string> Flags);

public sealed record CriterionAssessmentView(
    string Criterion,
    decimal Band,
    string Feedback,
    /// <summary>Verbatim spans from the learner's own writing or speech.</summary>
    IReadOnlyList<string> Evidence);

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
            question.MaxWords,
            question.Group is { } g
                ? new QuestionGroupView(g.Id, g.Title, g.Instruction, g.Image, g.Text, g.EachLetterOnce)
                : null);

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

    public static SectionMarkingView ToView(this SectionMarking marking) =>
        new(
            marking.Module.ToString().ToLowerInvariant(),
            marking.TaskNumber,
            marking.RubricVersion,
            marking.Band.Value,
            [
                .. marking.Criteria.Select(c => new CriterionAssessmentView(
                    c.Criterion, c.Band.Value, c.Feedback, c.Evidence)),
            ],
            [.. marking.Flags.Select(f => f.ToString())]);

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
