using System.Text;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Tests.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// One sitting, start to results, across all four skills.
///
/// <b>These tests exist because the lifecycle had no application-level tests
/// at all.</b> The domain had them — the timer, the advance order, the two
/// modes — and the marking runner had them, and between the two was the layer
/// that decides <i>when</i> marking runs. That layer had three ways to close a
/// section and only two of them marked anything.
///
/// So the shape here is deliberately end-to-end within Application: real
/// handlers, real domain aggregate, fake ports. A test that stubbed the
/// handlers would have agreed with the bug.
/// </summary>
public sealed class ExamLifecycleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Learner = UserId.New();

    private const string Essay =
        "The chart shows a steady rise in coffee consumption between 2010 and 2020.";

    // ── The exam under test ───────────────────────────────────────────────

    /// <summary>
    /// A four-skill version: two auto-scored marks each for Reading and
    /// Listening, two Writing tasks, and a two-part Speaking test.
    /// </summary>
    private static ExamVersion FourSkillVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>
            {
                [ExamModule.Reading] = Table(),
                [ExamModule.Listening] = Table(),
            },
            AnswerMatchingRules.Default);

        var timing = new TimingProfile(
            new Dictionary<ExamModule, int>
            {
                [ExamModule.Reading] = 3600,
                [ExamModule.Listening] = 1800,
                [ExamModule.Writing] = 3600,
                [ExamModule.Speaking] = 900,
            },
            null,
            []);

        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Lifecycle Test", ExamVariant.Academic, scoring, timing,
            [
                KeyedSection(ExamModule.Reading, 1, "r"),
                KeyedSection(ExamModule.Listening, 2, "l"),
                WritingSection(),
                SpeakingSection(),
            ],
            new ListeningPlaybackProfile(
                new AudioPlaybackRule(false, true),
                new AudioPlaybackRule(true, false)));

        version.Publish(T0.AddDays(-1));
        return version;
    }

    /// <summary>
    /// Starts at raw 0, because <c>CoversRange</c> exists to catch a table
    /// that does not — a hole at the bottom silently produced band 0.0 for a
    /// real score.
    /// </summary>
    private static IReadOnlyList<BandBoundary> Table() =>
    [
        new BandBoundary(0, BandScore.Create(4.0m)),
        new BandBoundary(1, BandScore.Create(5.5m)),
        new BandBoundary(2, BandScore.Create(7.0m)),
    ];

    private static Section KeyedSection(ExamModule module, int order, string prefix) =>
        new(module, order,
        [
            new SectionPart(1, "passage", "P", "body", null, null, null, null, null, null, null,
            [
                Keyed($"{prefix}-1", 1, "paper"),
                Keyed($"{prefix}-2", 2, "wood"),
            ]),
        ]);

    private static Question Keyed(string id, int order, string answer) =>
        new(id, order, QuestionType.ShortAnswer, "q", [], null,
            new AnswerKey([new AcceptedAnswer(answer, null, null)], null));

    private static Section WritingSection() =>
        new(ExamModule.Writing, 3,
        [
            WritingTask(1, "Describe the chart in at least 150 words."),
            WritingTask(2, "Discuss both views and give your own opinion."),
        ]);

    private static SectionPart WritingTask(int number, string prompt) =>
        new(number, "writing-task", $"Task {number}", prompt, null, null, null, number, null, null, 150,
            [new Question($"w-task-{number}", number, QuestionType.EssayTask, prompt, [], null, null)]);

    private static Section SpeakingSection() =>
        new(ExamModule.Speaking, 4,
        [
            SpeakingPart(1, "Tell me about your home town."),
            SpeakingPart(2, "Describe a journey you remember well."),
        ]);

    private static SectionPart SpeakingPart(int number, string prompt) =>
        new(number, "speaking", $"Part {number}", prompt, null, null, null, null, number, null, null,
            [new Question($"s-part-{number}", number, QuestionType.SpeakingResponse, prompt, [], null, null)]);

    // ── The wiring under test ─────────────────────────────────────────────

    /// <summary>
    /// Every handler in the sitting lifecycle over one set of fake ports.
    ///
    /// <b>Built as a whole rather than per test.</b> Several of these failures
    /// were only visible across two handlers — an "advance" that marked and an
    /// expiry that did not — so a harness that let a test wire only the
    /// handler it names would have been able to miss them.
    /// </summary>
    private sealed class Harness
    {
        public Harness(
            IEnumerable<ISectionEvaluator>? evaluators = null,
            IRubricSource? rubrics = null,
            string? transcript = null)
        {
            Version = FourSkillVersion();
            Catalogue = new FakeExamCatalogue(Version);
            Clock = new MovableClock(T0);

            Rubrics = rubrics ?? new FakeRubricSource();

            Marker = new SectionMarkingRunner(
                Rubrics,
                evaluators ?? [],
                Markings,
                new FakeTranscriptSource(transcript));

            Start = new StartExamSession(Catalogue, Sessions, Clock);
            Get = new GetExamSession(Catalogue, Sessions, Answers, Results, Marker, Clock);
            Save = new SaveAnswers(Catalogue, Sessions, Answers, Clock);
            Record = new SubmitSpeakingRecording(Catalogue, Sessions, Answers, Recordings, Clock);
            Advance = new AdvanceSection(
                Catalogue, Sessions, Answers, Results, Marker, Outbox, Rubrics, Clock);
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
        public FakeRecordingStore Recordings { get; } = new();
        public FakeMarkingOutbox Outbox { get; } = new();
        public FakePersonalizedExplanationStore Explanations { get; } = new();
        public IRubricSource Rubrics { get; }
        public MovableClock Clock { get; }
        public SectionMarkingRunner Marker { get; }

        public StartExamSession Start { get; }
        public GetExamSession Get { get; }
        public SaveAnswers Save { get; }
        public SubmitSpeakingRecording Record { get; }
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

        public Task<string> UploadAsync(string sessionId, string questionId) =>
            Record.HandleAsync(
                new SubmitSpeakingRecordingCommand(
                    Learner, new ExamSessionId(sessionId), questionId,
                    new MemoryStream(Encoding.UTF8.GetBytes("fake-opus-bytes")), "audio/ogg"),
                default);

        public Task<SessionView> AdvanceAsync(string sessionId) =>
            Advance.HandleAsync(new AdvanceSectionCommand(Learner, new ExamSessionId(sessionId)), default);

        public Task<SessionResultsView> SubmitAsync(string sessionId) =>
            Submit.HandleAsync(
                new SubmitExamSessionCommand(Learner, new ExamSessionId(sessionId)), default);

        public Task<SessionResultsView> ResultsAsync(string sessionId) =>
            ReadResults.HandleAsync(
                new GetSessionResultsQuery(Learner, new ExamSessionId(sessionId)), default);

        public ExamSession Session(string id) =>
            Sessions.FindAsync(new ExamSessionId(id), default).Result!;
    }

    // ── Full Test versus Single Skill ─────────────────────────────────────

    [Fact]
    public async Task Server_resolves_listening_playback_from_run_kind_and_version_profile()
    {
        var h = new Harness();
        var practice = await h.Start.HandleAsync(
            new StartExamSessionCommand(
                Learner, h.Version.Id, SessionMode.Single, ExamModule.Listening,
                SessionTiming.OpenEnded),
            default);

        Assert.Equal(new AudioPlaybackPolicyView(false, true), practice.Current!.AudioPlayback);

        var mock = await h.StartFullAsync();
        mock = await h.AdvanceAsync(mock.SessionId);

        Assert.Equal("listening", mock.Current!.Module);
        Assert.Equal(new AudioPlaybackPolicyView(true, false), mock.Current.AudioPlayback);
    }

    [Fact]
    public async Task A_full_test_walks_all_four_skills_in_one_sitting_and_marks_each_as_it_closes()
    {
        // CLAUDE.md rule 10 and `E-11`…`E-13`. The domain proves the order;
        // this proves the pipeline around it — that each section is marked at
        // the moment it closes rather than all of them at the end, so a learner
        // who stops halfway keeps what they earned.
        var h = new Harness();
        var session = await h.StartFullAsync();
        var id = session.SessionId;

        Assert.Equal("reading", session.Current!.Module);

        await h.SaveAsync(id, ExamModule.Reading, ("r-1", "paper"), ("r-2", "wood"));
        var afterReading = await h.AdvanceAsync(id);
        Assert.Equal("listening", afterReading.Current!.Module);

        // Marked already, not held until submission.
        var scored = await h.Results.ListAsync(new ExamSessionId(id), default);
        Assert.Equal(7.0m, Assert.Single(scored).Band!.Value.Value);

        await h.SaveAsync(id, ExamModule.Listening, ("l-1", "paper"), ("l-2", "brick"));
        Assert.Equal("writing", (await h.AdvanceAsync(id)).Current!.Module);

        await h.SaveAsync(id, ExamModule.Writing, ("w-task-1", Essay), ("w-task-2", Essay));
        Assert.Equal("speaking", (await h.AdvanceAsync(id)).Current!.Module);

        await h.UploadAsync(id, "s-part-1");
        await h.UploadAsync(id, "s-part-2");

        var results = await h.SubmitAsync(id);

        // One sitting, four attempts — not four sittings.
        Assert.Equal(4, h.Session(id).Attempts.Count);
        Assert.Equal("submitted", results.Status);

        // Reading 2/2 and Listening 1/2, both from the answer key with no
        // evaluator configured anywhere. → `A-11`
        Assert.Equal(["listening", "reading"], results.Sections.Select(s => s.Module).Order());
        Assert.Equal(7.0m, results.Sections.Single(s => s.Module == "reading").Band);
        Assert.Equal(5.5m, results.Sections.Single(s => s.Module == "listening").Band);

        // Writing and Speaking have no band, and the sitting therefore has no
        // overall band. Two of four averaged is not a band. → product law L3
        Assert.Empty(results.Markings);
        Assert.Null(results.OverallBand);
    }

    [Fact]
    public async Task A_single_skill_sitting_never_advances_and_never_gains_a_second_section()
    {
        // Its call to action is "làm đề mới", which is a different operation
        // with a different entitlement effect. → CLAUDE.md rule 10
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Writing);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.AdvanceAsync(session.SessionId));

        var stored = h.Session(session.SessionId);
        Assert.Single(stored.Attempts);
        Assert.Equal(ExamModule.Writing, stored.Current!.Module);
        Assert.Equal(SessionStatus.InProgress, stored.Status);
    }

    [Theory]
    [InlineData(ExamModule.Reading)]
    [InlineData(ExamModule.Listening)]
    [InlineData(ExamModule.Writing)]
    [InlineData(ExamModule.Speaking)]
    public async Task Any_of_the_four_skills_can_be_sat_on_its_own(ExamModule module)
    {
        // Each skill is a product surface in its own right, and a single-skill
        // sitting of one is not a Full Test with three sections missing.
        var h = new Harness();
        var session = await h.StartSingleAsync(module);

        Assert.Equal(module.ToString().ToLowerInvariant(), session.Current!.Module);
        Assert.Equal("single", session.Mode);
        Assert.Single(h.Session(session.SessionId).Attempts);
    }

    // ── Where marking runs ────────────────────────────────────────────────

    [Fact]
    public async Task A_sitting_that_runs_out_of_time_keeps_the_band_it_earned_before_the_deadline()
    {
        // <b>The gap this file was written for.</b> Scoring ran from "advance"
        // and from "submit". An expiry is neither — so a learner who ran out of
        // time ended with a full answer sheet, no result, and a results screen
        // that agreed there was nothing to show. Storage lost nothing; the
        // band was lost anyway, silently, for the life of the sitting.
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Reading);
        var id = session.SessionId;

        await h.SaveAsync(id, ExamModule.Reading, ("r-1", "paper"), ("r-2", "wood"));

        // One second past the hour the version allows.
        h.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        var results = await h.ResultsAsync(id);

        Assert.Equal("expired", results.Status);
        Assert.Equal(7.0m, Assert.Single(results.Sections).Band);
    }

    [Fact]
    public async Task Submitting_late_is_refused_and_still_marks_what_was_saved_in_time()
    {
        // The refusal and the marking are not in tension. The learner is told
        // no because they asked for more time; they are still paid for the
        // work they did inside it. Losing both is what made "what you saved
        // before the deadline is kept" a half-truth.
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Reading);
        var id = session.SessionId;

        await h.SaveAsync(id, ExamModule.Reading, ("r-1", "paper"), ("r-2", "wood"));
        h.Clock.Advance(TimeSpan.FromHours(2));

        await Assert.ThrowsAsync<SessionExpiredException>(() => h.SubmitAsync(id));

        Assert.Equal(SessionStatus.Expired, h.Session(id).Status);
        var scored = await h.Results.ListAsync(new ExamSessionId(id), default);
        Assert.Equal(7.0m, Assert.Single(scored).Band!.Value.Value);
    }

    [Fact]
    public async Task Reading_the_sitting_after_its_deadline_closes_and_marks_it_too()
    {
        // A learner returning to a tab they left open is the common way an
        // expiry is noticed. It has to reach the same place "submit" does, or
        // the same sitting is marked or not depending on which screen the
        // learner happened to open.
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Listening);
        var id = session.SessionId;

        await h.SaveAsync(id, ExamModule.Listening, ("l-1", "paper"), ("l-2", "wood"));
        h.Clock.Advance(TimeSpan.FromHours(1));

        var view = await h.Get.HandleAsync(
            new GetExamSessionQuery(Learner, new ExamSessionId(id)), default);

        Assert.Equal("expired", view.Status);
        Assert.Null(view.Current);

        var scored = await h.Results.ListAsync(new ExamSessionId(id), default);
        Assert.Equal(7.0m, Assert.Single(scored).Band!.Value.Value);
    }

    [Fact]
    public async Task An_expired_sitting_read_twice_is_not_marked_twice()
    {
        // Marking Writing costs a provider call. The expiry saves the sitting
        // before it marks, so the second reader finds it already closed and
        // does nothing — which is what stops a refreshed results screen buying
        // a second opinion on the same essay.
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Reading);
        var id = session.SessionId;

        await h.SaveAsync(id, ExamModule.Reading, ("r-1", "paper"), ("r-2", "wood"));
        h.Clock.Advance(TimeSpan.FromHours(2));

        await h.ResultsAsync(id);
        var writesAfterFirst = h.Results.Writes;

        await h.ResultsAsync(id);
        await h.ResultsAsync(id);

        Assert.Equal(writesAfterFirst, h.Results.Writes);
    }

    [Fact]
    public async Task Abandoning_a_full_test_halfway_keeps_every_section_already_closed()
    {
        // Submitting from the middle of a Full Test is giving up, not an error.
        // The sections behind the learner were marked as they closed and stay
        // marked; the ones ahead were never opened and have no result to show.
        var h = new Harness();
        var session = await h.StartFullAsync();
        var id = session.SessionId;

        await h.SaveAsync(id, ExamModule.Reading, ("r-1", "paper"), ("r-2", "wood"));
        await h.AdvanceAsync(id);

        await h.SaveAsync(id, ExamModule.Listening, ("l-1", "paper"), ("l-2", "wood"));
        var results = await h.SubmitAsync(id);

        Assert.Equal(2, results.Sections.Count);
        Assert.Equal(2, h.Session(id).Attempts.Count);
        Assert.Null(results.OverallBand);
    }

    // ── Writes that name the wrong section ────────────────────────────────

    [Fact]
    public async Task An_autosave_for_a_section_the_sitting_has_left_says_so_rather_than_claiming_expiry()
    {
        // <b>A real gap, and the wrong answer was worse than no answer.</b> The
        // refusal used to be SessionExpiredException, whose client contract is
        // "the sitting is over, show the results screen". A late autosave
        // racing an "advance" is routine on a flaky connection — and it would
        // have torn down an exam that was running perfectly well.
        var h = new Harness();
        var session = await h.StartFullAsync();
        var id = session.SessionId;

        await h.AdvanceAsync(id);   // now on Listening

        var refusal = await Assert.ThrowsAsync<SectionNotOpenException>(
            () => h.SaveAsync(id, ExamModule.Reading, ("r-1", "paper")));

        Assert.Equal(ExamModule.Reading, refusal.Requested);
        Assert.Equal(ExamModule.Listening, refusal.Open);

        // And the sitting is untouched — still running, still on Listening.
        Assert.Equal(SessionStatus.InProgress, h.Session(id).Status);
    }

    [Fact]
    public async Task A_speaking_answer_cannot_be_written_as_text()
    {
        // Speaking's sheet is the server's index of what was uploaded. A client
        // write there is at best a no-op and at worst a way to point marking
        // at a recording id the caller chose.
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Speaking);

        await Assert.ThrowsAsync<SpeakingIsNotWrittenException>(
            () => h.SaveAsync(session.SessionId, ExamModule.Speaking, ("s-part-1", "borrowed-id")));
    }

    // ── The Speaking chain ────────────────────────────────────────────────

    [Fact]
    public async Task An_uploaded_recording_is_filed_where_marking_looks_for_it()
    {
        // <b>The chain was broken end to end.</b> The endpoint stored the audio
        // and handed the id to the client, and nothing on the server connected
        // the two — so the marking runner had nothing to find, and Speaking
        // would have reported "awaiting transcript" even for a learner who had
        // said nothing. Storing audio is not submitting an answer.
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Speaking);
        var id = session.SessionId;

        var first = await h.UploadAsync(id, "s-part-1");
        var second = await h.UploadAsync(id, "s-part-2");

        // Filed under the session and the question the server checked, not
        // under anything the caller named for itself.
        Assert.Equal(2, h.Recordings.Saved.Count);
        Assert.All(h.Recordings.Saved, r => Assert.Equal(id, r.SessionId.Value));
        Assert.Equal(["s-part-1", "s-part-2"], h.Recordings.Saved.Select(r => r.QuestionId));

        // And the sheet — which is what marking reads — holds both ids.
        var sheet = await h.Answers.LoadAsync(new ExamSessionId(id), ExamModule.Speaking, default);
        Assert.Equal(first, sheet["s-part-1"]);
        Assert.Equal(second, sheet["s-part-2"]);
    }

    [Fact]
    public async Task A_second_recording_for_one_part_replaces_it_without_disturbing_the_others()
    {
        // A learner re-recording Part 1 must not lose Part 2. The sheet is
        // written one entry at a time for exactly this reason — a whole-sheet
        // replace here would drop whichever upload landed first.
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Speaking);
        var id = session.SessionId;

        await h.UploadAsync(id, "s-part-1");
        await h.UploadAsync(id, "s-part-2");
        var retake = await h.UploadAsync(id, "s-part-1");

        var sheet = await h.Answers.LoadAsync(new ExamSessionId(id), ExamModule.Speaking, default);
        Assert.Equal(retake, sheet["s-part-1"]);
        Assert.Equal("rec-2", sheet["s-part-2"]);
    }

    [Fact]
    public async Task A_recording_for_a_section_the_learner_is_not_in_is_refused()
    {
        // <b>ADR-0007 had a hole in the one module whose answer does not
        // travel through the autosave.</b> The upload checked only that the
        // sitting was in progress, so a Full Test candidate could file their
        // Speaking answers while still sitting Reading — three sections of
        // thinking time on a test that is meant to be spontaneous.
        var h = new Harness();
        var session = await h.StartFullAsync();   // opens on Reading

        var refusal = await Assert.ThrowsAsync<SectionNotOpenException>(
            () => h.UploadAsync(session.SessionId, "s-part-1"));

        Assert.Equal(ExamModule.Reading, refusal.Open);
        Assert.Empty(h.Recordings.Saved);
    }

    [Fact]
    public async Task A_recording_uploaded_after_the_speaking_deadline_is_refused()
    {
        // The same hole from the other side: nothing compared the upload
        // against the section's deadline, so the Speaking answer — the only
        // thing Speaking submits — could arrive at any time after the timer had
        // run out. An autosave one second late is refused; this was not.
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Speaking);

        h.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<SessionExpiredException>(
            () => h.UploadAsync(session.SessionId, "s-part-1"));

        Assert.Empty(h.Recordings.Saved);
    }

    [Fact]
    public async Task A_recording_for_a_question_this_exam_does_not_have_is_refused()
    {
        // The question id becomes a key on the answer sheet and then the thing
        // marking looks up. An unchecked one writes a row nobody reads, and
        // hides a recording from the section it belonged to.
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Speaking);

        await Assert.ThrowsAsync<ArgumentException>(
            () => h.UploadAsync(session.SessionId, "s-part-99"));

        Assert.Empty(h.Recordings.Saved);
    }

    [Fact]
    public async Task A_recording_cannot_be_uploaded_to_a_sitting_that_has_finished()
    {
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Speaking);
        var id = session.SessionId;

        await h.UploadAsync(id, "s-part-1");
        await h.SubmitAsync(id);

        await Assert.ThrowsAsync<SessionNotInProgressException>(() => h.UploadAsync(id, "s-part-2"));
        Assert.Single(h.Recordings.Saved);
    }

    [Fact]
    public async Task Another_learners_sitting_is_not_found_rather_than_forbidden()
    {
        // A 403 confirms the id exists, which turns the id space into an oracle
        // for enumerating other learners' sittings. Checked on the recording
        // upload because it is the newest way in, and the one most likely to
        // have been written without the rule.
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Speaking);

        var intruder = new SubmitSpeakingRecordingCommand(
            UserId.New(), new ExamSessionId(session.SessionId), "s-part-1",
            new MemoryStream([1, 2, 3]), "audio/ogg");

        await Assert.ThrowsAsync<SessionNotFoundException>(
            () => h.Record.HandleAsync(intruder, default));
    }

    [Fact]
    public async Task Another_learners_results_are_not_found_rather_than_forbidden()
    {
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Reading);
        await h.SaveAsync(session.SessionId, ExamModule.Reading, ("r-1", "paper"));
        await h.SubmitAsync(session.SessionId);

        await Assert.ThrowsAsync<SessionNotFoundException>(() =>
            h.ReadResults.HandleAsync(
                new GetSessionResultsQuery(UserId.New(), new ExamSessionId(session.SessionId)),
                default));
    }

    // ── What marking reports for the two judged skills ────────────────────

    [Fact]
    public async Task Speaking_with_a_recording_awaits_a_transcript_and_without_one_is_unanswered()
    {
        // Two dashes on the results screen with two different causes, and the
        // whole pipeline used to report the first for both. One is the
        // platform's missing ASR; the other is the learner's silence.
        // Two sittings, and therefore two harnesses: the fake answer sheet is
        // keyed by module alone, so one learner's recording would otherwise be
        // visible to the other and this test would prove the opposite of what
        // it claims.
        static Harness WithSpeakingRubric() => new(rubrics: new FakeRubricSource(
            Domain.Assessment.Rubric.Create(
                "ielts-speaking-2023.1", ExamModule.Speaking,
                Domain.Assessment.CriterionKeys.Speaking, "IELTS descriptors, May 2023")));

        var silence = WithSpeakingRubric();
        var speech = WithSpeakingRubric();

        var silent = await silence.StartSingleAsync(ExamModule.Speaking);
        var spoke = await speech.StartSingleAsync(ExamModule.Speaking);

        await speech.UploadAsync(spoke.SessionId, "s-part-1");

        var forSilence = await silence.Marker.RunAsync(
            silence.Version, ExamModule.Speaking, new ExamSessionId(silent.SessionId),
            silence.Answers, default);

        var forSpeech = await speech.Marker.RunAsync(
            speech.Version, ExamModule.Speaking, new ExamSessionId(spoke.SessionId),
            speech.Answers, default);

        Assert.Equal(MarkingAvailability.NothingSubmitted, Assert.Single(forSilence).Availability);
        Assert.Equal(MarkingAvailability.AwaitingVoiceProvider, Assert.Single(forSpeech).Availability);
    }

    [Fact]
    public async Task No_evaluator_anywhere_still_produces_reading_and_listening_bands()
    {
        // `A-11` made operational: two of four skills work fully with no AI
        // provider configured, which is the state the product is in and will
        // stay in until `B-2` is answered.
        var h = new Harness();
        var session = await h.StartFullAsync();
        var id = session.SessionId;

        await h.SaveAsync(id, ExamModule.Reading, ("r-1", "paper"), ("r-2", "wood"));
        await h.AdvanceAsync(id);
        await h.SaveAsync(id, ExamModule.Listening, ("l-1", "paper"), ("l-2", "wood"));

        var results = await h.SubmitAsync(id);

        Assert.Equal(2, results.Sections.Count);
        Assert.All(results.Sections, s => Assert.Equal(7.0m, s.Band));
        Assert.Empty(results.Markings);
    }

    // ── Transitions, when two of them arrive at once ──────────────────────

    /// <summary>
    /// The second "Tiếp theo" does not close the section a second time.
    ///
    /// <b>Both tabs marked the paper, and only the guard on the write was
    /// there to notice.</b> The order used to be mark-then-save, so by the time
    /// anything checked whether this caller was still allowed to advance, the
    /// evaluation had already been bought — twice, for one section, with the
    /// second result dropped by an insert-if-absent that had no way to refund
    /// it. Reversing the order is what makes the guard mean anything.
    /// </summary>
    [Fact]
    public async Task A_second_advance_against_a_section_already_left_does_not_mark_it_again()
    {
        var h = new Harness();
        var session = await h.StartFullAsync();

        await h.SaveAsync(session.SessionId, ExamModule.Reading, ("r-1", "paper"));

        var first = await h.AdvanceAsync(session.SessionId);
        Assert.Equal("listening", first.Current!.Module);

        var marksAfterFirst = h.Results.Writes;

        // A second tab, still holding the sitting as it was, presses the same
        // button. It is not an error — both learners pressed "Tiếp theo" — so
        // it is answered with where the sitting actually is.
        h.Sessions.MovedOn(
            new ExamSessionId(session.SessionId),
            new SessionState(SessionStatus.InProgress, ExamModule.Reading, true));

        var second = await h.AdvanceAsync(session.SessionId);

        Assert.Equal("listening", second.Current!.Module);
        Assert.Equal(marksAfterFirst, h.Results.Writes);
    }

    /// <summary>
    /// A submit that loses to an advance lands on its second attempt.
    ///
    /// <b>The first version discarded the answer to "did it land" entirely.</b>
    /// It re-read and returned the results view whatever had happened, so a
    /// learner pressing "Nộp bài" while their other tab pressed "Tiếp theo"
    /// submitted nothing and was handed a 200 and a results screen for a sitting
    /// that was still running. Losing to an advance is ordinary, and the state
    /// it loses to is one the submit can be made from — so it is made from
    /// there.
    /// </summary>
    [Fact]
    public async Task A_submit_that_loses_to_an_advance_lands_on_its_second_attempt()
    {
        var h = new Harness();
        var session = await h.StartFullAsync();
        var id = new ExamSessionId(session.SessionId);

        await h.SaveAsync(session.SessionId, ExamModule.Reading, ("r-1", "paper"));

        // The other tab presses "Tiếp theo" in the instant between this
        // handler's read and its write.
        h.Sessions.Interfere = () =>
        {
            var theirs = h.Sessions.FindAsync(id, default).GetAwaiter().GetResult()!;
            var from = SessionState.Of(theirs);
            theirs.AdvanceToNextSection(h.Version, h.Clock.UtcNow);
            h.Sessions.TrySaveAsync(theirs, from, default).GetAwaiter().GetResult();
        };

        var results = await h.Submit.HandleAsync(
            new SubmitExamSessionCommand(Learner, id), default);

        // The paper was handed in — from Listening, where the other tab left it.
        Assert.Equal("submitted", results.Status);

        var stored = await h.Sessions.FindAsync(id, default);
        Assert.Equal(SessionStatus.Submitted, stored!.Status);
    }

    /// <summary>
    /// A submit that keeps losing refuses, rather than reporting results for a
    /// sitting it never submitted.
    ///
    /// <b>The 200 was worse than useless.</b> The learner was shown a results
    /// screen while their exam was still running — and the idempotency guard
    /// then stored that 200 as the answer for their key, so every retry replayed
    /// "here are your results, status inprogress" and the paper could not be
    /// handed in at all. A refusal is not stored, so the retry actually runs.
    /// </summary>
    [Fact]
    public async Task A_submit_that_keeps_losing_the_race_refuses_rather_than_reporting_results()
    {
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Reading);
        var id = new ExamSessionId(session.SessionId);

        await h.SaveAsync(session.SessionId, ExamModule.Reading, ("r-1", "paper"));

        // A guard state no attempt can ever match: every retry loses.
        h.Sessions.MovedOn(id, new SessionState(SessionStatus.Expired, ExamModule.Writing, true));

        await Assert.ThrowsAsync<SessionMovedOnException>(() =>
            h.Submit.HandleAsync(new SubmitExamSessionCommand(Learner, id), default));

        // And nothing was marked on the way through.
        Assert.Empty(await h.Results.ListAsync(id, default));
    }

    /// <summary>
    /// A section closed without being scored is scored on the way to the
    /// results.
    ///
    /// <b>This is the price of closing before marking, paid.</b> The transition
    /// is written first so only one caller marks; the cost is a window where a
    /// section is closed and unmarked if the process dies in between. Without
    /// this there is no second chance — the Reading band is simply absent, for
    /// ever, and nothing says why.
    /// </summary>
    [Fact]
    public async Task A_closed_section_with_no_score_is_scored_when_the_results_are_read()
    {
        var h = new Harness();
        var session = await h.StartSingleAsync(ExamModule.Reading);
        var id = new ExamSessionId(session.SessionId);

        await h.SaveAsync(session.SessionId, ExamModule.Reading, ("r-1", "paper"), ("r-2", "wood"));

        // Close the sitting the way a crashed marking run would leave it:
        // transition written, nothing scored.
        var stored = (await h.Sessions.FindAsync(id, default))!;
        stored.Submit(h.Clock.UtcNow);
        Assert.True(await h.Sessions.TrySaveAsync(
            stored, new SessionState(SessionStatus.InProgress, ExamModule.Reading, true), default));

        Assert.Empty(await h.Results.ListAsync(id, default));

        var results = await h.ReadResults.HandleAsync(
            new GetSessionResultsQuery(Learner, id), default);

        var reading = Assert.Single(results.Sections, s => s.Module == "reading");
        Assert.Equal(2, reading.RawScore);
    }

    /// <summary>
    /// <b>A Speaking upload that finishes after its section closed is refused,
    /// and leaves nothing behind.</b>
    ///
    /// An upload is two writes: the audio goes to storage, then its id is filed
    /// into the answer sheet. The checks the handler makes happen before the
    /// first one — and twelve megabytes over a phone network is not a short
    /// window, so a submit or an expiry can close Speaking inside it.
    ///
    /// Before the closure protocol the second write simply landed, and a
    /// recording existed, was indexed against a question, and was never marked
    /// because marking had already read the sheet. For a spoken answer that
    /// object is often the learner's only copy of it.
    ///
    /// Two properties, and the second is the one that is easy to forget: the
    /// upload is <b>refused</b>, and the bytes it had already written are
    /// <b>removed</b>. Leaving them is an object nothing references, holding a
    /// learner's voice, which is personal data under PDPL.
    /// → ADR-0015, <c>docs/security/privacy-vietnam-pdpl.md</c>
    /// </summary>
    [Fact]
    public async Task A_recording_filed_after_its_section_closed_is_refused_and_cleaned_up()
    {
        var app = new Harness();
        var sitting = await app.StartSingleAsync(ExamModule.Speaking);

        // The section closes — the learner pressed Nộp bài in another tab, or
        // the deadline swept — freezing the sheet.
        await app.SubmitAsync(sitting.SessionId);

        await Assert.ThrowsAsync<SessionNotInProgressException>(
            () => app.UploadAsync(sitting.SessionId, "s-part-1"));

        // Nothing was written, because the handler refused before storage.
        Assert.Empty(app.Recordings.Saved);
    }

    /// <summary>
    /// The same, for the window the status check cannot see: the sheet is
    /// frozen while the sitting is still, briefly, in progress.
    ///
    /// This is the state ADR-0015 accepts as the cost of freezing before the
    /// transition — a crash between the two leaves it, and so does the instant
    /// between them under any concurrency. An upload arriving then has passed
    /// every check the handler makes and is refused by the store.
    /// </summary>
    [Fact]
    public async Task A_recording_meeting_a_frozen_sheet_is_refused_and_its_bytes_removed()
    {
        var app = new Harness();
        var sitting = await app.StartSingleAsync(ExamModule.Speaking);

        // Freeze the sheet without transitioning the sitting, which is exactly
        // the window between the two writes in every closing path.
        await app.Answers.CloseAsync(
            new ExamSessionId(sitting.SessionId), ExamModule.Speaking, app.Clock.UtcNow, default);

        await Assert.ThrowsAsync<SectionSheetClosedException>(
            () => app.UploadAsync(sitting.SessionId, "s-part-1"));

        /*
         * <b>The bytes were written and then removed.</b> Asserting only that
         * the upload failed would pass over an orphan — the failure mode this
         * test exists for is precisely the one where the refusal is correct and
         * the storage is not.
         */
        Assert.Single(app.Recordings.Deleted);
        Assert.Empty(app.Recordings.Saved);

        // And the sheet did not take it.
        var sheet = await app.Answers.LoadAsync(
            new ExamSessionId(sitting.SessionId), ExamModule.Speaking, default);

        Assert.False(sheet.ContainsKey("s-part-1"));
    }


    /// <summary>
    /// <b>Closing a section records that a band is owed, before it tries to
    /// produce one.</b>
    ///
    /// The transition is written first so that only one caller marks — otherwise
    /// two tabs on "Nộp bài" buy two evaluations. The cost of that order is a
    /// window: a process that dies between the transition and the marking
    /// leaves the section closed and unmarked. For Reading and Listening that
    /// is survivable, because the score is recomputed from the answer key. For
    /// Writing and Speaking it was <b>permanent</b> — a re-entered submit
    /// short-circuits on a sitting that is no longer in progress, and the
    /// catch-up pass skips them deliberately.
    ///
    /// The band was gone for the life of the sitting, and nothing said so.
    /// </summary>
    [Fact]
    public async Task Closing_a_writing_section_records_that_a_marking_is_owed()
    {
        var h = new Harness(rubrics: new FakeRubricSource(
            Domain.Assessment.Rubric.Create(
                "ielts-writing-2023.1", ExamModule.Writing,
                Domain.Assessment.CriterionKeys.Writing, "IELTS descriptors, May 2023")));

        var sitting = await h.StartSingleAsync(ExamModule.Writing);
        await h.SubmitAsync(sitting.SessionId);

        var owed = h.Outbox.Jobs.Values.Single();

        Assert.Equal(ExamModule.Writing, owed.Module);
        Assert.Equal(MarkingJobState.Pending, owed.State);
        Assert.Equal("ielts-writing-2023.1", owed.RubricVersion);
    }

    /// <summary>
    /// And re-closing it does not owe a second one.
    ///
    /// A retried submit, two tabs on "Tiếp theo", the expiry sweep meeting a
    /// learner's own submit — every one re-closes the same section. Two jobs
    /// would be two evaluations for one essay, and the second band would
    /// quietly replace one the learner may already have seen.
    /// </summary>
    [Fact]
    public async Task Re_closing_a_section_does_not_owe_a_second_marking()
    {
        var h = new Harness(rubrics: new FakeRubricSource(
            Domain.Assessment.Rubric.Create(
                "ielts-writing-2023.1", ExamModule.Writing,
                Domain.Assessment.CriterionKeys.Writing, "IELTS descriptors, May 2023")));

        var sitting = await h.StartSingleAsync(ExamModule.Writing);

        await h.SubmitAsync(sitting.SessionId);
        await h.SubmitAsync(sitting.SessionId);

        Assert.Single(h.Outbox.Jobs);
    }

    /// <summary>
    /// No rubric means no job, and that is not a silent skip.
    ///
    /// A rubric records which criteria were used and where their descriptors
    /// came from (`H-8a`). A job enqueued without one would be a promise to
    /// mark against a standard nobody has stated — and the results screen would
    /// then report "waiting" for something that is not coming. Reporting
    /// `AwaitingRubric` is the honest answer and a different one.
    /// </summary>
    [Fact]
    public async Task A_module_with_no_rubric_owes_nothing()
    {
        var h = new Harness();

        var sitting = await h.StartSingleAsync(ExamModule.Writing);
        await h.SubmitAsync(sitting.SessionId);

        Assert.Empty(h.Outbox.Jobs);
    }

    /// <summary>
    /// Reading owes nothing either, and for the opposite reason.
    ///
    /// Its band comes from the answer key (`A-11`), so a queue entry for it
    /// would be a job for arithmetic that is recomputed on demand anyway.
    /// </summary>
    [Fact]
    public async Task A_deterministic_module_owes_nothing()
    {
        var h = new Harness(rubrics: new FakeRubricSource(
            Domain.Assessment.Rubric.Create(
                "ielts-writing-2023.1", ExamModule.Writing,
                Domain.Assessment.CriterionKeys.Writing, "IELTS descriptors, May 2023")));

        var sitting = await h.StartSingleAsync(ExamModule.Reading);
        await h.SubmitAsync(sitting.SessionId);

        Assert.Empty(h.Outbox.Jobs);
    }

    /// <summary>
    /// The results screen says which module is waiting, and why.
    ///
    /// <b>One sentence for four situations is a lie.</b> "No model is wired" is
    /// true when nothing is wired, and wrong when the essay is queued, wrong
    /// when a recording has no transcript, and wrong when the platform tried
    /// five times and stopped. The learner's next move is different in each.
    /// </summary>
    [Fact]
    public async Task The_results_view_reports_what_each_owed_marking_is_waiting_on()
    {
        var h = new Harness(rubrics: new FakeRubricSource(
            Domain.Assessment.Rubric.Create(
                "ielts-writing-2023.1", ExamModule.Writing,
                Domain.Assessment.CriterionKeys.Writing, "IELTS descriptors, May 2023")));

        var sitting = await h.StartSingleAsync(ExamModule.Writing);
        await h.SubmitAsync(sitting.SessionId);

        var results = await h.ResultsAsync(sitting.SessionId);

        var status = Assert.Single(results.MarkingStatuses);

        Assert.Equal("writing", status.Module);
        Assert.Equal("pending", status.State);
    }

}
