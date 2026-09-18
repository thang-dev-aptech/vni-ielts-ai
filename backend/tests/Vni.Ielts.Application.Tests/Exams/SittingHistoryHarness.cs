using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Learning;
using Vni.Ielts.Application.Tests.Assessment;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Learning;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// One learner's sitting history, with every port counting what it is asked.
///
/// <b>Shared because the history is read from more than one door.</b>
/// `GET /api/v1/sessions` reads it through <see cref="ListMySittings"/>;
/// `/me/coaching` reads it through <see cref="GetCoaching"/>, which calls the
/// same handler and then asks the marking store again itself; and
/// `/me/activity` reads the session list directly with a different ceiling.
/// A cost measured at one door says nothing about the others, so all of them
/// are built over one set of counting ports here. → slice `W10`
///
/// <b>Counting two things, not one.</b> <c>Reads</c> is round trips — what an
/// N+1 inflates. <c>SessionsAsked</c> is how many sittings those round trips
/// named, which is what keeps a batch read honest: collapsing ten reads into
/// one is not a saving if the one read quietly asks about sittings that could
/// never have an overall band.
/// </summary>
internal sealed class SittingHistoryHarness
{
    public static readonly DateTimeOffset T0 = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private static readonly IWritingTaskWeighting OneToTwo =
        new WritingTaskWeightPolicy(new WritingTaskWeights(1m, 2m));

    public SittingHistoryHarness(UserId? learner = null)
    {
        Learner = learner ?? UserId.New();
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

        Coaching = new GetCoaching(
            new EmptyGoalStore(), History, Markings, new UnconfiguredAdvisor(),
            new EmptyAdviceCache());

        Activity = new GetLearnerActivity(
            new EmptyActivityLog(), Sessions, new UtcCalendar(), clock);
    }

    public UserId Learner { get; }
    public ExamVersion Version { get; }
    public CountingCatalogue Catalogue { get; }
    public CountingSessionRepository Sessions { get; }
    public CountingSectionResultStore Results { get; }
    public CountingMarkingStore Markings { get; }
    public CountingMarkingOutbox Outbox { get; }
    public GetSessionResults ReadResults { get; }
    public ListMySittings History { get; }
    public GetCoaching Coaching { get; }
    public GetLearnerActivity Activity { get; }

    public void ResetCounters()
    {
        Catalogue.Finds = 0;
        Sessions.Lists = 0;
        Results.Reads = 0;
        Markings.Reads = 0;
        Markings.SessionsAsked = 0;
        Outbox.Reads = 0;
        Outbox.SessionsAsked = 0;
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

    public Task<CoachingView> CoachingAsync() =>
        Coaching.HandleAsync(new GetCoachingQuery(Learner), default);

    public Task<ActivityView> ActivityAsync() =>
        Activity.HandleAsync(new GetLearnerActivityQuery(Learner, 30), default);

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

    // ── Fixtures ──────────────────────────────────────────────────────────

    public static SectionScore Score(ExamModule module, decimal band) =>
        new(module, 30, 40, BandScore.Create(band), []);

    public static SectionMarking WritingTask(int number, decimal band) =>
        new(ExamModule.Writing, "ielts-writing-synthetic-v1", [], BandScore.Create(band),
            null, [], [], number);

    public static MarkingJob Job(
        ExamSessionId sitting, ExamModule module, MarkingJobState state, string? code) =>
        new(MarkingJob.IdFor(sitting, module, "v1"), sitting, module, "v1", state, 0, T0,
            null, null, null, code, null, null);

    public static ExamVersion FourSkillVersion()
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

    // ── Learning ports, stubbed to nothing ────────────────────────────────
    //
    // The coaching view is measured here for its reads, not its advice: no
    // goal, no advisor, no cache. A stub that returned advice would put a
    // provider call inside a query-count test.

    private sealed class EmptyGoalStore : ILearnerGoalStore
    {
        public Task<LearnerGoal?> GetAsync(UserId userId, CancellationToken ct) =>
            Task.FromResult<LearnerGoal?>(null);

        public Task SaveAsync(LearnerGoal goal, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class UnconfiguredAdvisor : ICoachingAdvisor
    {
        public bool IsConfigured => false;

        public Task<CoachingAdviceResult> AdviseAsync(CoachingFacts facts, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyAdviceCache : ICoachingAdviceCache
    {
        public Task<CoachingAdvice?> GetAsync(string key, CancellationToken ct) =>
            Task.FromResult<CoachingAdvice?>(null);

        public Task SetAsync(string key, CoachingAdvice advice, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class EmptyActivityLog : ILearnerActivityLog
    {
        public Task RecordAsync(UserId userId, DateOnly day, ActivityKind kind, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ActivityDay>> ListAsync(
            UserId userId, DateOnly from, DateOnly to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActivityDay>>([]);
    }

    private sealed class UtcCalendar : ILearnerCalendar
    {
        public string TimeZoneId => "UTC";

        public DateOnly DayOf(DateTimeOffset instant) => DateOnly.FromDateTime(instant.UtcDateTime);
    }
}

// ── Counting ports ────────────────────────────────────────────────────────
//
// Session-scoped on purpose: the shared `FakeMarkingStore` answers every
// session with every marking it holds, which is harmless for a one-sitting
// test and would quietly hand ten sittings each other's Writing bands here.

internal sealed class CountingCatalogue(ExamVersion version) : IExamCatalogue
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

internal sealed class CountingSessionRepository : IExamSessionRepository
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

internal sealed class CountingSectionResultStore : ISectionResultStore
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

internal sealed class CountingMarkingStore : ISectionMarkingStore
{
    private readonly Dictionary<string, List<SectionMarking>> _markings = [];

    /// <summary>Round trips to the store.</summary>
    public int Reads { get; set; }

    /// <summary>Sittings those round trips named, batched or not.</summary>
    public int SessionsAsked { get; set; }

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
        SessionsAsked++;
        return Task.FromResult<IReadOnlyList<SectionMarking>>(
            _markings.TryGetValue(sessionId.Value, out var held) ? [.. held] : []);
    }

    public Task<IReadOnlyDictionary<ExamSessionId, IReadOnlyList<SectionMarking>>> ListManyAsync(
        IReadOnlyCollection<ExamSessionId> sessionIds, CancellationToken ct)
    {
        Reads++;
        SessionsAsked += sessionIds.Count;

        return Task.FromResult<IReadOnlyDictionary<ExamSessionId, IReadOnlyList<SectionMarking>>>(
            sessionIds
                .Where(id => _markings.ContainsKey(id.Value))
                .ToDictionary(
                    id => id,
                    IReadOnlyList<SectionMarking> (id) => [.. _markings[id.Value]]));
    }
}

internal sealed class CountingMarkingOutbox : IMarkingOutbox
{
    private readonly Dictionary<string, MarkingJob> _jobs = [];

    /// <summary>Round trips to the store.</summary>
    public int Reads { get; set; }

    /// <summary>Sittings those round trips named, batched or not.</summary>
    public int SessionsAsked { get; set; }

    public Task<bool> EnqueueAsync(MarkingJob job, CancellationToken ct)
    {
        if (_jobs.ContainsKey(job.OperationId)) return Task.FromResult(false);

        _jobs[job.OperationId] = job;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<MarkingJob>> ListAsync(ExamSessionId sessionId, CancellationToken ct)
    {
        Reads++;
        SessionsAsked++;
        return Task.FromResult<IReadOnlyList<MarkingJob>>(
            [.. _jobs.Values.Where(j => j.SessionId == sessionId)]);
    }

    public Task<IReadOnlyDictionary<ExamSessionId, IReadOnlyList<MarkingJob>>> ListManyAsync(
        IReadOnlyCollection<ExamSessionId> sessionIds, CancellationToken ct)
    {
        Reads++;
        SessionsAsked += sessionIds.Count;

        var wanted = sessionIds.ToHashSet();

        return Task.FromResult<IReadOnlyDictionary<ExamSessionId, IReadOnlyList<MarkingJob>>>(
            _jobs.Values
                .Where(j => wanted.Contains(j.SessionId))
                .GroupBy(j => j.SessionId)
                .ToDictionary(g => g.Key, IReadOnlyList<MarkingJob> (g) => [.. g]));
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
