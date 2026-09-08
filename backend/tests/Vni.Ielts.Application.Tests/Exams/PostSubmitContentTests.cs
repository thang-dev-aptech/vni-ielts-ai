using System.Reflection;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Tests.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// `S2a` — the left column of the Result/Review screen (`P-08`): the paper
/// as it was sat, served only once the sitting itself has left
/// <see cref="SessionStatus.InProgress"/>.
///
/// <b>The gate under test is on the whole sitting, not on each section.</b>
/// <see cref="Content_stays_empty_while_a_full_test_is_still_in_progress"/>
/// is the one that would have caught the obvious wrong shortcut — gating per
/// section instead of per sitting, which would have hand the Reading passage
/// to a Full Test candidate still answering Listening.
/// </summary>
public sealed class PostSubmitContentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Learner = UserId.New();

    private const string Essay =
        "The chart shows a steady rise in coffee consumption between 2010 and 2020.";

    // ── The exam under test: Reading then Writing ──────────────────────────

    private static ExamVersion ReadingThenWritingVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>
            {
                [ExamModule.Reading] =
                [
                    new BandBoundary(0, BandScore.Create(4.0m)),
                    new BandBoundary(1, BandScore.Create(5.5m)),
                    new BandBoundary(2, BandScore.Create(7.0m)),
                ],
            },
            AnswerMatchingRules.Default);

        var timing = new TimingProfile(
            new Dictionary<ExamModule, int> { [ExamModule.Reading] = 3600, [ExamModule.Writing] = 3600 },
            null, []);

        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Post-submit content", ExamVariant.Academic, scoring, timing,
            [
                new Section(ExamModule.Reading, 1,
                [
                    new SectionPart(1, "passage", "Passage 1", "Humans have recorded information for millennia.",
                        null, null, null, null, null, null, null,
                    [
                        new Question("r-1", 1, QuestionType.ShortAnswer, "What is it made of?", [], null,
                            new AnswerKey([new AcceptedAnswer("paper", null, null)], null)),
                        new Question("r-2", 2, QuestionType.ShortAnswer, "What is the frame made of?", [], null,
                            new AnswerKey([new AcceptedAnswer("wood", null, null)], null)),
                    ]),
                ]),
                new Section(ExamModule.Writing, 2,
                [
                    new SectionPart(1, "writing-task", "Task 1", "Describe the chart.", null, null, null,
                        1, null, null, 150,
                    [
                        new Question("w-task-1", 1, QuestionType.EssayTask, "Describe the chart.", [], null, null),
                    ]),
                ]),
            ]);

        version.Publish(T0.AddDays(-1));
        return version;
    }

    /// <summary>Every handler the scenarios below need, over one set of fake ports.</summary>
    private sealed class Harness
    {
        public Harness()
        {
            Version = ReadingThenWritingVersion();
            Catalogue = new FakeExamCatalogue(Version);
            Clock = new MovableClock(T0);

            Marker = new SectionMarkingRunner(
                new FakeRubricSource(), [], Markings, new FakeTranscriptSource(null));

            Start = new StartExamSession(Catalogue, Sessions, Clock);
            Save = new SaveAnswers(Catalogue, Sessions, Answers, Clock);
            Advance = new AdvanceSection(Catalogue, Sessions, Answers, Results, Marker, Outbox, Rubrics, Clock);
            Submit = new SubmitExamSession(
                Catalogue, Sessions, Answers, Results, Markings, Marker, Outbox, Rubrics, Explanations, Clock);
            ReadResults = new GetSessionResults(
                Catalogue, Sessions, Answers, Results, Markings, Marker, Outbox, Rubrics, Explanations, Clock);
        }

        public ExamVersion Version { get; }
        public FakeExamCatalogue Catalogue { get; }
        public FakeSessionRepository Sessions { get; } = new();
        public FakeAnswerSheetStore Answers { get; } = new();
        public FakeSectionResultStore Results { get; } = new();
        public FakeMarkingStore Markings { get; } = new();
        public FakeMarkingOutbox Outbox { get; } = new();
        public FakePersonalizedExplanationStore Explanations { get; } = new();
        public IRubricSource Rubrics { get; } = new FakeRubricSource();
        public MovableClock Clock { get; }
        public SectionMarkingRunner Marker { get; }

        public StartExamSession Start { get; }
        public SaveAnswers Save { get; }
        public AdvanceSection Advance { get; }
        public SubmitExamSession Submit { get; }
        public GetSessionResults ReadResults { get; }

        public Task<SessionView> StartFullAsync() =>
            Start.HandleAsync(
                new StartExamSessionCommand(Learner, Version.Id, SessionMode.Full, null), default);

        public Task<SessionView> StartSingleAsync(ExamModule module) =>
            Start.HandleAsync(
                new StartExamSessionCommand(Learner, Version.Id, SessionMode.Single, module), default);

        public Task SaveAsync(string sessionId, ExamModule module, params (string Id, string Value)[] a) =>
            Save.HandleAsync(
                new SaveAnswersCommand(
                    Learner, new ExamSessionId(sessionId), module,
                    a.ToDictionary(x => x.Id, x => (string?)x.Value)),
                default);

        public Task<SessionView> AdvanceAsync(string sessionId) =>
            Advance.HandleAsync(new AdvanceSectionCommand(Learner, new ExamSessionId(sessionId)), default);

        public Task<SessionResultsView> SubmitAsync(string sessionId) =>
            Submit.HandleAsync(new SubmitExamSessionCommand(Learner, new ExamSessionId(sessionId)), default);

        public Task<SessionResultsView> ResultsAsync(UserId userId, string sessionId) =>
            ReadResults.HandleAsync(
                new GetSessionResultsQuery(userId, new ExamSessionId(sessionId)), default);
    }

    // ── The gate: whole-sitting status, not per-section ────────────────────

    [Fact]
    public async Task Content_stays_empty_while_a_full_test_is_still_in_progress()
    {
        var h = new Harness();
        var session = await h.StartFullAsync();

        await h.SaveAsync(session.SessionId, ExamModule.Reading, ("r-1", "paper"), ("r-2", "wood"));

        // Closes and marks Reading, opens Writing. The sitting as a whole is
        // still running — this is exactly the Full Test candidate the gate
        // exists for.
        await h.AdvanceAsync(session.SessionId);

        var results = await h.ResultsAsync(Learner, session.SessionId);

        Assert.Equal("inprogress", results.Status);
        Assert.NotEmpty(results.Sections); // Reading is already scored...
        Assert.Empty(results.Content);     // ...but the paper itself is not shown yet.
    }

    // ── After submit: the paper each module actually carried ──────────────

    [Fact]
    public async Task Reading_content_carries_the_passage_and_questions_with_no_answer_key()
    {
        var h = new Harness();
        var session = await h.StartFullAsync();
        await h.SaveAsync(session.SessionId, ExamModule.Reading, ("r-1", "paper"), ("r-2", "wood"));
        await h.AdvanceAsync(session.SessionId);
        await h.SaveAsync(session.SessionId, ExamModule.Writing, ("w-task-1", Essay));
        await h.SubmitAsync(session.SessionId);

        var results = await h.ResultsAsync(Learner, session.SessionId);

        var reading = Assert.Single(results.Content, c => c.Module == "reading");
        var part = Assert.Single(reading.Parts);
        Assert.Equal("Humans have recorded information for millennia.", part.Body);
        Assert.Equal(2, part.Questions.Count);
        Assert.Empty(reading.Submissions); // Submissions is the Writing-only slot.

        // The left column must never be the answer key wearing a new name.
        var serialised = System.Text.Json.JsonSerializer.Serialize(part);
        Assert.DoesNotContain("paper", serialised, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wood", serialised, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Writing_content_carries_the_task_prompt_and_the_learners_own_essay_text()
    {
        var h = new Harness();
        var session = await h.StartFullAsync();
        await h.SaveAsync(session.SessionId, ExamModule.Reading, ("r-1", "paper"), ("r-2", "wood"));
        await h.AdvanceAsync(session.SessionId);
        await h.SaveAsync(session.SessionId, ExamModule.Writing, ("w-task-1", Essay));
        await h.SubmitAsync(session.SessionId);

        var results = await h.ResultsAsync(Learner, session.SessionId);

        var writing = Assert.Single(results.Content, c => c.Module == "writing");
        var part = Assert.Single(writing.Parts);
        Assert.Equal("Describe the chart.", part.Body);
        Assert.Equal(Essay, writing.Submissions["w-task-1"]);
    }

    // ── Ownership, same shape as every other sitting route ─────────────────

    [Fact]
    public async Task Another_learners_results_carry_no_content_because_the_session_is_not_found()
    {
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Reading);
        await h.SaveAsync(session.SessionId, ExamModule.Reading, ("r-1", "paper"), ("r-2", "wood"));
        await h.SubmitAsync(session.SessionId);

        await Assert.ThrowsAsync<SessionNotFoundException>(() =>
            h.ResultsAsync(UserId.New(), session.SessionId));
    }

    // ── Regression insurance: the pre-submit view cannot carry this shape ──

    [Fact]
    public void The_pre_submit_session_view_has_no_content_shaped_field()
    {
        // SessionView/CurrentSectionView are what `GET /sessions/{id}` returns
        // while a sitting is InProgress. This never touched them — Content
        // lives only on SessionResultsView — and this is the structural proof
        // a future merge cannot silently put it there.
        var names = typeof(CurrentSectionView).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Content", names);
        Assert.DoesNotContain(nameof(SectionContentView), names);
    }
}
