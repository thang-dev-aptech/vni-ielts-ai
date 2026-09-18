using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Tests.Assessment;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// One sitting, two screens, one number — slice `W1`.
///
/// <b>The defect these exist for.</b> `Q-01` (owner, 2026-09-18) made a
/// three-skill mock show its overall band, and it was applied to the results
/// screen only. The history list kept the superseded rule — all four skills or
/// nothing — so a learner who finished a Reading + Listening + Writing mock
/// read a band on `/exam/{id}/results` and a dash for the same sitting on
/// `/students/progress`. Two screens of one product answering "what did I get"
/// differently is worse than either answer alone: a reader has no way to tell
/// which one is lying.
///
/// <b>Both sides go through the real handlers, against the same stores.</b>
/// Asserting the projection twice would prove the projection agrees with
/// itself; the divergence was never in the projection, it was in what the
/// history query bothered to load.
/// </summary>
public sealed class SittingHistoryBandTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Learner = UserId.New();

    /// <summary>
    /// The `W1` defect itself: a three-skill mock, both screens asked, one
    /// number expected.
    /// </summary>
    [Fact]
    public async Task History_and_results_report_the_same_overall_band_for_a_three_skill_mock()
    {
        var h = new Harness();
        var sitting = await h.ThreeSkillMockAsync();

        var results = await h.ResultsAsync(sitting);
        var history = Assert.Single(await h.HistoryAsync());

        // Guards the assertion below from passing on two nulls — which is
        // exactly how the two screens "agreed" before `Q-01` reached either.
        Assert.NotNull(results.OverallBand);

        Assert.Equal(results.OverallBand, history.OverallBand);
    }

    /// <summary>
    /// `L3` still holds on the history list. A mean that would move when the
    /// running job lands is refused on both screens, not just on one.
    /// </summary>
    [Fact]
    public async Task A_skill_still_being_marked_withholds_the_band_on_both_screens()
    {
        var h = new Harness();
        var sitting = await h.ThreeSkillMockAsync(writingStillRunning: true);

        var results = await h.ResultsAsync(sitting);
        var history = Assert.Single(await h.HistoryAsync());

        Assert.Null(results.OverallBand);
        Assert.Null(history.OverallBand);
    }

    /// <summary>
    /// <b>The N+1 this slice had to widen, pinned to a number.</b>
    ///
    /// Knowing whether anything is still owed takes the marking store and the
    /// job outbox, and neither port can be asked about more than one sitting at
    /// a time — so the history query pays two extra reads for every sitting
    /// that could have an overall band, and none for the ones that could not.
    /// `SittingBand.Applies` is the one place that decides which is which, so
    /// the saving cannot drift away from the rule.
    ///
    /// Five mocks and five practice sittings of one exam therefore cost:
    /// 1 list + 1 catalogue lookup + 10 score reads + 5 marking reads + 5 job
    /// reads = 22. Before this slice it was 12.
    ///
    /// <b>This does not make the query bounded.</b> It is still linear in the
    /// number of sittings returned, which is why the queue carries the batch
    /// read as debt beside `W5`'s cursor.
    /// </summary>
    [Fact]
    public async Task A_practice_sitting_costs_no_marking_or_job_read()
    {
        var h = new Harness();

        for (var i = 0; i < 5; i++) await h.ThreeSkillMockAsync();
        for (var i = 0; i < 5; i++) await h.PracticeSittingAsync();

        h.ResetCounters();
        var history = await h.HistoryAsync(limit: 20);

        Assert.Equal(10, history.Count);
        Assert.Equal(1, h.Sessions.Lists);
        Assert.Equal(1, h.Catalogue.Finds);
        Assert.Equal(10, h.Results.Reads);
        Assert.Equal(5, h.Markings.Reads);
        Assert.Equal(5, h.Outbox.Reads);
    }

    // ── Harness ───────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public Harness()
        {
            Version = FourSkillVersion();
            Catalogue = new CountingCatalogue(Version);
            var clock = new MovableClock(T0);

            var rubrics = new FakeRubricSource();
            var marker = new SectionMarkingRunner(
                rubrics, [], new FakeMarkingStore(), new FakeTranscriptSource(null));

            Results = new CountingSectionResultStore();
            Markings = new CountingMarkingStore();
            Outbox = new CountingMarkingOutbox();
            Sessions = new CountingSessionRepository();

            ReadResults = new GetSessionResults(
                Catalogue, Sessions, new FakeAnswerSheetStore(), Results, Markings, marker,
                Outbox, rubrics, new FakePersonalizedExplanationStore(), clock, OneToTwo);

            History = new ListMySittings(
                Catalogue, Sessions, Results, Markings, Outbox, OneToTwo);
        }

        private static readonly IWritingTaskWeighting OneToTwo =
            new WritingTaskWeightPolicy(new WritingTaskWeights(1m, 2m));

        public ExamVersion Version { get; }
        public CountingCatalogue Catalogue { get; }
        public CountingSessionRepository Sessions { get; }
        public CountingSectionResultStore Results { get; }
        public CountingMarkingStore Markings { get; }
        public CountingMarkingOutbox Outbox { get; }
        public GetSessionResults ReadResults { get; }
        public ListMySittings History { get; }

        public void ResetCounters()
        {
            Catalogue.Finds = 0;
            Sessions.Lists = 0;
            Results.Reads = 0;
            Markings.Reads = 0;
            Outbox.Reads = 0;
        }

        /// <summary>
        /// Reading and Listening marked from the key, Writing marked by the
        /// evaluator, Speaking permanently blocked on the ASR decision
        /// (`P-02`) — the shape of every completed mock in this build.
        /// </summary>
        public async Task<ExamSessionId> ThreeSkillMockAsync(bool writingStillRunning = false)
        {
            var id = ExamSessionId.New();

            await Sessions.AddAsync(Mock(id), default);
            await Results.SaveAsync(id, Score(ExamModule.Reading, 7m), default);
            await Results.SaveAsync(id, Score(ExamModule.Listening, 6.5m), default);

            await Outbox.EnqueueAsync(
                Job(id, ExamModule.Speaking, MarkingJobState.Pending,
                    nameof(MarkingAvailability.AwaitingVoiceProvider)), default);

            if (writingStillRunning)
            {
                await Outbox.EnqueueAsync(
                    Job(id, ExamModule.Writing, MarkingJobState.Running, null), default);
                return id;
            }

            await Markings.SaveAsync(id, WritingTask(1, 6m), default);
            await Markings.SaveAsync(id, WritingTask(2, 7m), default);
            await Outbox.EnqueueAsync(
                Job(id, ExamModule.Writing, MarkingJobState.Completed, null), default);

            return id;
        }

        /// <summary>Single-skill Reading practice — never has an overall band.</summary>
        public async Task<ExamSessionId> PracticeSittingAsync()
        {
            var id = ExamSessionId.New();

            await Sessions.AddAsync(
                ExamSession.Rehydrate(
                    id, Learner, Version.Id, SessionMode.Single, SessionStatus.Submitted,
                    T0, T0.AddMinutes(60),
                    [SectionAttempt.Rehydrate(ExamModule.Reading, T0, null, T0.AddMinutes(60))],
                    SessionTiming.OpenEnded),
                default);

            await Results.SaveAsync(id, Score(ExamModule.Reading, 6m), default);
            return id;
        }

        public Task<SessionResultsView> ResultsAsync(ExamSessionId id) =>
            ReadResults.HandleAsync(new GetSessionResultsQuery(Learner, id), default);

        public Task<IReadOnlyList<SittingSummaryView>> HistoryAsync(int limit = 10) =>
            History.HandleAsync(new ListMySittingsQuery(Learner, limit), default);

        private ExamSession Mock(ExamSessionId id) =>
            ExamSession.Rehydrate(
                id, Learner, Version.Id, SessionMode.Full, SessionStatus.Submitted,
                T0, T0.AddHours(3),
                [
                    SectionAttempt.Rehydrate(ExamModule.Reading, T0, null, T0),
                    SectionAttempt.Rehydrate(ExamModule.Listening, T0, null, T0),
                    SectionAttempt.Rehydrate(ExamModule.Writing, T0, null, T0),
                    SectionAttempt.Rehydrate(ExamModule.Speaking, T0, null, T0),
                ],
                SessionTiming.Deadline);
    }

    // ── Fixtures ──────────────────────────────────────────────────────────

    private static SectionScore Score(ExamModule module, decimal band) =>
        new(module, 30, 40, BandScore.Create(band), []);

    private static SectionMarking WritingTask(int number, decimal band) =>
        new(ExamModule.Writing, "ielts-writing-synthetic-v1", [], BandScore.Create(band),
            null, [], [], number);

    private static MarkingJob Job(
        ExamSessionId sitting, ExamModule module, MarkingJobState state, string? code) =>
        new(MarkingJob.IdFor(sitting, module, "v1"), sitting, module, "v1", state, 0, T0,
            null, null, null, code, null, null);

    private static ExamVersion FourSkillVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default, 1m, 2m);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);

        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Four skills", ExamVariant.Academic, scoring, timing,
            [
                new Section(ExamModule.Reading, 1,
                [
                    new SectionPart(
                        1, "passage", "Passage 1", "Body.", null, null, null, null, null, null, null,
                        [new Question("r-1", 1, QuestionType.Completion, "Q", [], null, null)]),
                ]),
                new Section(ExamModule.Listening, 1,
                [
                    new SectionPart(
                        1, "listening", "Part 1", null, "audio/l1.mp3", null, null, null, 1, null, null,
                        [new Question("l-1", 1, QuestionType.Completion, "Q", [], null, null)]),
                ]),
                new Section(ExamModule.Writing, 1,
                [
                    new SectionPart(
                        1, "writing", "Task 1", "Describe.", null, null, null, 1, null, null, 150,
                        [new Question("w-1", 1, QuestionType.EssayTask, "Describe.", [], null, null)]),
                    new SectionPart(
                        2, "writing", "Task 2", "Discuss.", null, null, null, 2, null, null, 250,
                        [new Question("w-2", 2, QuestionType.EssayTask, "Discuss.", [], null, null)]),
                ]),
                new Section(ExamModule.Speaking, 1,
                [
                    new SectionPart(
                        1, "speaking-part", "Part 1", null, null, null, null, null, 1, null, null,
                        [new Question("sp-1", 1, QuestionType.SpeakingResponse, "Talk.", [], null, null)]),
                ]),
            ]);

        version.Publish(T0.AddDays(-1));
        return version;
    }

    // ── Counting ports ────────────────────────────────────────────────────
    //
    // Session-scoped on purpose: the shared `FakeMarkingStore` answers every
    // session with every marking it holds, which is harmless for a one-sitting
    // test and would quietly hand ten sittings each other's Writing bands here.

    private sealed class CountingCatalogue(ExamVersion version) : IExamCatalogue
    {
        public int Finds { get; set; }

        public Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct)
        {
            Finds++;
            return Task.FromResult<ExamVersion?>(id == version.Id ? version : null);
        }

        public Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([version]);

        public Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([version]);

        public Task UpsertAsync(ExamVersion updated, CancellationToken ct) => Task.CompletedTask;

        public Task SetStatusAsync(ExamVersionId id, ExamVersionStatus status, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class CountingSessionRepository : IExamSessionRepository
    {
        private readonly Dictionary<string, ExamSession> _sessions = [];

        public int Lists { get; set; }

        public Task<ExamSession?> FindAsync(ExamSessionId id, CancellationToken ct) =>
            Task.FromResult(_sessions.TryGetValue(id.Value, out var held) ? held : null);

        public Task<ExamSession?> FindOpenForUserAsync(UserId userId, CancellationToken ct) =>
            Task.FromResult<ExamSession?>(null);

        public Task<IReadOnlyList<ExamSession>> ListForUserAsync(
            UserId userId, int limit, CancellationToken ct)
        {
            Lists++;
            return Task.FromResult<IReadOnlyList<ExamSession>>(
            [
                .. _sessions.Values
                    .Where(s => s.UserId == userId)
                    .OrderByDescending(s => s.StartedAt)
                    .Take(limit),
            ]);
        }

        public Task AddAsync(ExamSession session, CancellationToken ct)
        {
            _sessions[session.Id.Value] = session;
            return Task.CompletedTask;
        }

        public Task<bool> TrySaveAsync(ExamSession session, SessionState from, CancellationToken ct)
        {
            _sessions[session.Id.Value] = session;
            return Task.FromResult(true);
        }
    }

    private sealed class CountingSectionResultStore : ISectionResultStore
    {
        private readonly Dictionary<string, List<SectionScore>> _scores = [];

        public int Reads { get; set; }

        public Task SaveAsync(ExamSessionId sessionId, SectionScore score, CancellationToken ct)
        {
            var held = _scores.TryGetValue(sessionId.Value, out var list) ? list : _scores[sessionId.Value] = [];
            if (!held.Any(s => s.Module == score.Module)) held.Add(score);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SectionScore>> ListAsync(ExamSessionId sessionId, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<SectionScore>>(
                _scores.TryGetValue(sessionId.Value, out var held) ? [.. held] : []);
        }
    }

    private sealed class CountingMarkingStore : ISectionMarkingStore
    {
        private readonly Dictionary<string, List<SectionMarking>> _markings = [];

        public int Reads { get; set; }

        public Task SaveAsync(ExamSessionId sessionId, SectionMarking marking, CancellationToken ct)
        {
            var held = _markings.TryGetValue(sessionId.Value, out var list)
                ? list : _markings[sessionId.Value] = [];
            held.Add(marking);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SectionMarking>> ListAsync(
            ExamSessionId sessionId, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<SectionMarking>>(
                _markings.TryGetValue(sessionId.Value, out var held) ? [.. held] : []);
        }
    }

    private sealed class CountingMarkingOutbox : IMarkingOutbox
    {
        private readonly Dictionary<string, MarkingJob> _jobs = [];

        public int Reads { get; set; }

        public Task<bool> EnqueueAsync(MarkingJob job, CancellationToken ct)
        {
            if (_jobs.ContainsKey(job.OperationId)) return Task.FromResult(false);

            _jobs[job.OperationId] = job;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<MarkingJob>> ListAsync(ExamSessionId sessionId, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<MarkingJob>>(
                [.. _jobs.Values.Where(j => j.SessionId == sessionId)]);
        }

        // Nothing in a history or results read claims, renews or finishes a
        // job. A stub that pretended to would let a test certify a worker
        // behaviour this file knows nothing about.
        public Task<MarkingJob?> ClaimAsync(
            string leaseToken, DateTimeOffset now, TimeSpan lease, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> RenewAsync(
            string operationId, string leaseToken, DateTimeOffset until, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> CompleteAsync(
            string operationId, string leaseToken, DateTimeOffset at, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> RetryAsync(
            string operationId, string leaseToken, DateTimeOffset nextAttemptAt, string error,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> FailAsync(
            string operationId, string leaseToken, string error, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<QueueBacklog> BacklogAsync(DateTimeOffset asOf, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
