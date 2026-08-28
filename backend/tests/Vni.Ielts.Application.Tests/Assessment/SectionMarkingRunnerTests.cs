using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Tests.Assessment;

/// <summary>
/// The half of marking that is not arithmetic.
///
/// Reading and Listening are marked from the answer key and tested as such.
/// These tests are about the other two skills, where the band comes from a
/// judgement — and almost all of them are about <b>refusing</b>. That balance
/// is the point: a pipeline that produces a band when everything is present is
/// the easy half, and the half that has never been the source of a wrong score.
/// What goes wrong is a band appearing when something was missing.
/// </summary>
public sealed class SectionMarkingRunnerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly ExamSessionId Session = ExamSessionId.New();

    private const string Essay =
        "The chart shows a steady rise in coffee consumption between 2010 and 2020. "
        + "Tea, by contrast, fell sharply after 2015 and never recovered.";

    private static readonly Rubric WritingRubric = Rubric.Create(
        "ielts-writing-2023.1", ExamModule.Writing, CriterionKeys.Writing,
        "IELTS public band descriptors, May 2023");

    private static readonly Rubric SpeakingRubric = Rubric.Create(
        "ielts-speaking-2023.1", ExamModule.Speaking, CriterionKeys.Speaking,
        "IELTS public band descriptors, May 2023");

    /// <summary>A version with a two-task Writing section and a one-part Speaking section.</summary>
    private static ExamVersion Version(
        decimal? task1Weight = null, decimal? task2Weight = null)
    {
        var writing = new Section(ExamModule.Writing, 1,
        [
            Task(1, "Describe the chart in at least 150 words."),
            Task(2, "Discuss both views and give your own opinion."),
        ]);

        var speaking = new Section(ExamModule.Speaking, 2,
        [
            new SectionPart(1, "speaking", "Part 1", "Tell me about your home town.",
                null, null, null, null, 1, null, null,
                [new Question("s-part-1", 1, QuestionType.SpeakingResponse, "Part 1", [], null, null)]),
        ]);

        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default,
            task1Weight,
            task2Weight);

        var timing = new TimingProfile(
            new Dictionary<ExamModule, int> { [ExamModule.Writing] = 3600, [ExamModule.Speaking] = 900 },
            null, []);

        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Test", ExamVariant.Academic, scoring, timing,
            [writing, speaking]);

        version.Publish(T0.AddDays(-1));
        return version;
    }

    private static SectionPart Task(int number, string prompt) =>
        new(number, "writing-task", $"Task {number}", prompt, null, null, null, number, null, null, 150,
            [new Question($"w-task-{number}", number, QuestionType.EssayTask, prompt, [], null, null)]);

    private static List<ClaimedCriterion> FourClaims(decimal band = 6m) =>
    [
        new(CriterionKeys.TaskResponse, band, "Covers the task.", ["a steady rise in coffee consumption"]),
        new(CriterionKeys.CoherenceAndCohesion, band, "Sequenced clearly.", ["By contrast"]),
        new(CriterionKeys.LexicalResource, band, "Adequate range.", ["fell sharply"]),
        new(CriterionKeys.GrammaticalRangeAndAccuracy, band, "Mostly accurate.", ["never recovered"]),
    ];

    private static SectionMarkingRunner Runner(
        IRubricSource rubrics, FakeMarkingStore store,
        IEnumerable<ISectionEvaluator>? evaluators = null, string? transcript = null) =>
        new(rubrics, evaluators ?? [], store, new FakeTranscriptSource(transcript));

    private static FakeAnswerSheetStore WritingSheet(string? task1 = Essay, string? task2 = Essay) =>
        new(new Dictionary<ExamModule, Dictionary<string, string?>>
        {
            [ExamModule.Writing] = new() { ["w-task-1"] = task1, ["w-task-2"] = task2 },
        });

    /// <summary>
    /// A Speaking sheet holding what the server filed on upload: the question
    /// that was asked, and the key its audio was stored under.
    /// </summary>
    private static FakeAnswerSheetStore SpeakingSheet(string? recordingId = "rec-abc123") =>
        new(new Dictionary<ExamModule, Dictionary<string, string?>>
        {
            [ExamModule.Speaking] = new() { ["s-part-1"] = recordingId },
        });

    // ── The runner's scope ────────────────────────────────────────────────

    [Theory]
    [InlineData(ExamModule.Reading)]
    [InlineData(ExamModule.Listening)]
    public async Task It_declines_the_modules_whose_band_comes_from_the_answer_key(ExamModule module)
    {
        // `A-11` is enforced by shape here, not by discipline: there is no
        // branch in this runner through which Reading or Listening could reach
        // an evaluator, so no configuration mistake can route a band that must
        // come from the key through a model.
        var store = new FakeMarkingStore();

        var outcomes = await Runner(new FakeRubricSource(WritingRubric), store)
            .RunAsync(Version(), module, Session, WritingSheet(), default);

        Assert.Empty(outcomes);
        Assert.Empty(store.Saved);
    }

    // ── Every way a band can be absent ────────────────────────────────────

    [Fact]
    public async Task Without_a_rubric_it_reports_why_and_never_reaches_the_evaluator()
    {
        // A rubric records which criteria were used and where their descriptors
        // came from (`H-8a`, unresolved). Marking without one would produce a
        // band nobody could reproduce or defend — and would have sent a
        // learner's essay to a provider to get it.
        var evaluator = new StubEvaluator(ExamModule.Writing, _ => new(FourClaims(), null));
        var store = new FakeMarkingStore();

        var outcomes = await Runner(new FakeRubricSource(), store, [evaluator])
            .RunAsync(Version(), ExamModule.Writing, Session, WritingSheet(), default);

        Assert.All(outcomes, o => Assert.Equal(MarkingAvailability.AwaitingRubric, o.Availability));
        Assert.Empty(store.Saved);
        Assert.Empty(evaluator.Requests);
    }

    [Fact]
    public async Task Without_an_evaluator_it_reports_why_rather_than_producing_a_band()
    {
        // The expected state today. `B-2` — the Vietnam PDPL cross-border
        // position — is unresolved, so no real learner work may cross a border.
        var store = new FakeMarkingStore();

        var outcomes = await Runner(new FakeRubricSource(WritingRubric), store)
            .RunAsync(Version(), ExamModule.Writing, Session, WritingSheet(), default);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.Equal(MarkingAvailability.AwaitingEvaluator, o.Availability));
        Assert.All(outcomes, o => Assert.Null(o.Marking));
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task An_unanswered_task_is_reported_as_unanswered_not_as_a_zero()
    {
        // A blank task scored 0 and a blank task never marked look identical on
        // a results screen, and only one of them is true. Product law L3 wants
        // the dash; this is what keeps the dash distinguishable from a mark.
        var evaluator = new StubEvaluator(ExamModule.Writing, _ => new(FourClaims(), null));
        var store = new FakeMarkingStore();

        var outcomes = await Runner(new FakeRubricSource(WritingRubric), store, [evaluator])
            .RunAsync(Version(), ExamModule.Writing, Session, WritingSheet(task2: "  "), default);

        Assert.Equal(MarkingAvailability.Marked, outcomes[0].Availability);
        Assert.Equal(MarkingAvailability.NothingSubmitted, outcomes[1].Availability);

        // And the unanswered one never cost a provider call.
        Assert.Single(evaluator.Requests);
    }

    [Fact]
    public async Task Speaking_names_the_missing_transcript_rather_than_the_missing_evaluator()
    {
        // Two blockers, and they are not equally far from being solved. An
        // evaluator is a legal question with an answer coming; speech-to-text
        // has not been chosen at all, and pronunciation needs word-level
        // timings, which narrows the field. Reporting whichever was checked
        // first would hide the one that actually has to be decided.
        var evaluator = new StubEvaluator(ExamModule.Speaking, _ => new(
        [
            new(CriterionKeys.FluencyAndCoherence, 6m, "f", ["x"]),
            new(CriterionKeys.LexicalResource, 6m, "l", ["x"]),
            new(CriterionKeys.GrammaticalRangeAndAccuracy, 6m, "g", ["x"]),
            new(CriterionKeys.Pronunciation, 6m, "p", ["x"]),
        ], null));

        var store = new FakeMarkingStore();

        var outcomes = await Runner(
                new FakeRubricSource(SpeakingRubric), store, [evaluator], transcript: null)
            .RunAsync(Version(), ExamModule.Speaking, Session, SpeakingSheet(), default);

        var only = Assert.Single(outcomes);
        Assert.Equal(MarkingAvailability.AwaitingTranscript, only.Availability);
        Assert.Empty(evaluator.Requests);
    }

    [Fact]
    public async Task Speaking_with_nothing_recorded_is_unanswered_rather_than_awaiting_a_transcript()
    {
        // <b>The bug this test exists for.</b> Speaking could not report
        // "nothing submitted" at all: the runner went straight to the
        // transcript source, got null — which is all it can return until an
        // ASR is chosen — and reported AwaitingTranscript. So a learner who
        // said nothing and a platform with no speech-to-text produced the same
        // line on the results screen, and the one that was the learner's own
        // doing read as the platform's fault.
        //
        // An empty Speaking sheet means no recording was ever filed, and
        // that is a fact about the learner, not about the pipeline.
        var store = new FakeMarkingStore();

        var outcomes = await Runner(
                new FakeRubricSource(SpeakingRubric), store, [], transcript: "a transcript")
            .RunAsync(Version(), ExamModule.Speaking, Session, SpeakingSheet(null), default);

        var only = Assert.Single(outcomes);
        Assert.Equal(MarkingAvailability.NothingSubmitted, only.Availability);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task The_recordings_reach_the_transcript_source_under_the_ids_they_were_filed_under()
    {
        // <b>The other half of the same break.</b> The runner used to ask for a
        // transcript by an invented key — `speaking-{sectionOrder}` — which
        // appears in no exam package, on no answer sheet and in no recording's
        // metadata. Nothing failed: the port returns null today whatever it is
        // asked, so the chain looked exactly as it does when it works. It would
        // have stayed broken until the day an ASR was wired and every Speaking
        // section came back empty.
        var transcripts = new FakeTranscriptSource("I grew up in a small town near Huế.");
        var store = new FakeMarkingStore();
        var runner = new SectionMarkingRunner(
            new FakeRubricSource(SpeakingRubric), [], store, transcripts);

        await runner.RunAsync(Version(), ExamModule.Speaking, Session, SpeakingSheet(), default);

        var asked = Assert.Single(transcripts.Requests);
        var recording = Assert.Single(asked);

        // The question the exam version declares, and the key the upload
        // returned — not a synthetic id derived from the section's order.
        Assert.Equal("s-part-1", recording.QuestionId);
        Assert.Equal("rec-abc123", recording.RecordingId);
    }

    [Fact]
    public async Task A_task_that_is_already_marked_is_not_evaluated_a_second_time()
    {
        // The store refuses the duplicate insert, so a band could never move —
        // but it refuses it after the evaluation, which is the half that costs
        // money. A retried submit, or a results screen refreshed on a sitting
        // the server has just expired, would each buy a second opinion on the
        // same essay and discard it.
        var evaluator = new StubEvaluator(ExamModule.Writing, _ => new(FourClaims(), null));
        var store = new FakeMarkingStore();
        var runner = Runner(new FakeRubricSource(WritingRubric), store, [evaluator]);
        var sheet = WritingSheet();

        await runner.RunAsync(Version(), ExamModule.Writing, Session, sheet, default);
        Assert.Equal(2, evaluator.Requests.Count);

        var outcomes = await runner.RunAsync(Version(), ExamModule.Writing, Session, sheet, default);

        // Still two calls, and the second run reports the marks that exist
        // rather than pretending the section is unmarked.
        Assert.Equal(2, evaluator.Requests.Count);
        Assert.Equal(2, store.Saved.Count);
        Assert.All(outcomes, o => Assert.Equal(MarkingAvailability.Marked, o.Availability));
        Assert.All(outcomes, o => Assert.NotNull(o.Marking));
    }

    [Fact]
    public async Task A_response_that_fails_validation_is_refused_and_nothing_is_stored()
    {
        // Three criteria where the rubric names four. A quarter of the mark is
        // missing, so the band would be a mean over the wrong denominator —
        // exactly the plausible-looking wrong number that never gets
        // investigated. `CriterionMarking` refuses; the runner reports the
        // refusal as a state of its own rather than as "not marked yet",
        // because the fix is a prompt or a provider, not a wait. → `A-8`
        var evaluator = new StubEvaluator(ExamModule.Writing, _ => new(
            [.. FourClaims().Take(3)], null));

        var store = new FakeMarkingStore();

        var outcomes = await Runner(new FakeRubricSource(WritingRubric), store, [evaluator])
            .RunAsync(Version(), ExamModule.Writing, Session, WritingSheet(), default);

        Assert.All(outcomes, o => Assert.Equal(MarkingAvailability.Rejected, o.Availability));
        Assert.All(outcomes, o => Assert.NotNull(o.Detail));
        Assert.Empty(store.Saved);
    }

    // ── And the case where it works ───────────────────────────────────────

    [Fact]
    public async Task Writing_produces_one_marking_per_task_because_that_is_how_IELTS_marks_it()
    {
        // Not one marking for the section. Each task is assessed against all
        // four criteria and earns its own band; combining them needs a ratio
        // IELTS does not publish (`H-8b`), so the two bands stay two bands.
        var evaluator = new StubEvaluator(
            ExamModule.Writing,
            request => new(FourClaims(request.Prompt.Contains("chart") ? 6m : 7m), null));

        var store = new FakeMarkingStore();

        var outcomes = await Runner(new FakeRubricSource(WritingRubric), store, [evaluator])
            .RunAsync(Version(), ExamModule.Writing, Session, WritingSheet(), default);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.Equal(MarkingAvailability.Marked, o.Availability));

        Assert.Equal([1, 2], store.Saved.Select(m => m.TaskNumber));
        Assert.Equal(6.0m, store.Saved[0].Band.Value);
        Assert.Equal(7.0m, store.Saved[1].Band.Value);

        // The rubric version travels with every mark, which is what keeps a
        // band explicable after the descriptors change.
        Assert.All(store.Saved, m => Assert.Equal("ielts-writing-2023.1", m.RubricVersion));

        // Each task was marked against its own prompt, not against the section.
        Assert.Equal(2, evaluator.Requests.Count);
        Assert.Contains("chart", evaluator.Requests[0].Prompt);
        Assert.Contains("both views", evaluator.Requests[1].Prompt);
    }

    [Fact]
    public async Task The_band_is_recomputed_and_a_model_that_disagrees_is_flagged_not_obeyed()
    {
        // The model claims 8.0 over four criteria that average 6.0. The stored
        // band is 6.0 and the disagreement is recorded — a provider whose
        // arithmetic drifts is worth knowing about, and the way to find out is
        // not to discard the number it gave.
        var evaluator = new StubEvaluator(ExamModule.Writing, _ => new(FourClaims(6m), 8m));
        var store = new FakeMarkingStore();

        await Runner(new FakeRubricSource(WritingRubric), store, [evaluator])
            .RunAsync(Version(), ExamModule.Writing, Session, WritingSheet(task2: null), default);

        var marking = Assert.Single(store.Saved);
        Assert.Equal(6.0m, marking.Band.Value);
        Assert.Equal(8.0m, marking.ReportedBand!.Value.Value);
        Assert.Contains(MarkingFlag.ArithmeticMismatch, marking.Flags);
        Assert.True(marking.IsFlagged);
    }

    [Fact]
    public async Task Evidence_the_learner_never_wrote_is_flagged_rather_than_quietly_kept()
    {
        // A quotation is what makes a band defensible (`A-13c`). One the
        // learner did not write is worse than none, because it reads as
        // verifiable — and a reader who checks it finds the product citing
        // words nobody said.
        var claims = FourClaims();
        claims[0] = claims[0] with { Evidence = ["a phrase that appears nowhere in the essay"] };

        var evaluator = new StubEvaluator(ExamModule.Writing, _ => new(claims, null));
        var store = new FakeMarkingStore();

        await Runner(new FakeRubricSource(WritingRubric), store, [evaluator])
            .RunAsync(Version(), ExamModule.Writing, Session, WritingSheet(task2: null), default);

        var marking = Assert.Single(store.Saved);
        Assert.Contains(MarkingFlag.EvidenceNotGrounded, marking.Flags);
        Assert.Single(marking.UngroundedEvidence);
    }
}
