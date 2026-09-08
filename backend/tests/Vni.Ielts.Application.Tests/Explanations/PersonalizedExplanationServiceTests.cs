using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Tests.Explanations;

public sealed class PersonalizedExplanationServiceTests
{
  [Fact]
  public async Task Same_answer_hash_uses_cache_without_second_provider_call()
  {
    var generator = new CountingGenerator();
    var store = new InMemoryPersonalizedExplanationStore();
    var version = ReadingVersion();
    var session = SubmittedSession(version);
    var service = BuildService(generator, store, version, session);

    var first = await service.RequestAsync(
        new RequestPersonalizedExplanationCommand(
            session.UserId, session.Id, "q1", "op-1"),
        default);

    Assert.Equal("ready", first.State);
    Assert.Equal(1, generator.CallCount);

    var second = await service.RequestAsync(
        new RequestPersonalizedExplanationCommand(
            session.UserId, session.Id, "q1", "op-2"),
        default);

    Assert.Equal("ready", second.State);
    Assert.Equal(1, generator.CallCount);
  }

  [Fact]
  public async Task Translation_from_the_provider_reaches_the_view()
  {
    var generator = new CountingGenerator();
    var store = new InMemoryPersonalizedExplanationStore();
    var version = ReadingVersion();
    var session = SubmittedSession(version);
    var service = BuildService(generator, store, version, session);

    var view = await service.RequestAsync(
        new RequestPersonalizedExplanationCommand(session.UserId, session.Id, "q1", "op-1"),
        default);

    Assert.Equal("ready", view.State);
    Assert.Equal("Câu hỏi: chọn một.", view.Explanation!.Translation);
  }

  [Fact]
  public async Task The_request_carries_the_question_options_to_the_generator()
  {
    var generator = new CountingGenerator();
    var store = new InMemoryPersonalizedExplanationStore();
    var version = ReadingVersion(withOptions: true);
    var session = SubmittedSession(version);
    var service = BuildService(generator, store, version, session);

    await service.RequestAsync(
        new RequestPersonalizedExplanationCommand(session.UserId, session.Id, "q1", "op-1"),
        default);

    Assert.Equal("A. Alpha\nB. Beta", generator.LastRequest!.QuestionOptions);
  }

  /// <summary>
  /// Past the cap the service returns the failed job without another provider
  /// call, so the reason must say the cap was reached rather than invite a
  /// retry the server will never run.
  /// </summary>
  [Fact]
  public async Task After_the_attempt_cap_the_reason_says_so_and_no_provider_call_is_made()
  {
    var generator = new CountingGenerator { Fails = true };
    var store = new InMemoryPersonalizedExplanationStore();
    var version = ReadingVersion();
    var session = SubmittedSession(version);
    var service = BuildService(generator, store, version, session);

    PersonalizedExplanationView? first = null;
    for (var i = 0; i < PersonalizedExplanationService.MaxAttempts; i++)
    {
      var view = await service.RequestAsync(
          new RequestPersonalizedExplanationCommand(session.UserId, session.Id, "q1", "op-1"),
          default);
      Assert.Equal("failed", view.State);
      Assert.Equal(i + 1, view.Attempts);
      first ??= view;
    }

    Assert.Equal(PersonalizedExplanationService.MaxAttempts, generator.CallCount);

    var capped = await service.RequestAsync(
        new RequestPersonalizedExplanationCommand(session.UserId, session.Id, "q1", "op-1"),
        default);

    Assert.Equal("failed", capped.State);
    Assert.Equal(PersonalizedExplanationService.MaxAttempts, capped.Attempts);
    Assert.Equal(PersonalizedExplanationService.MaxAttempts, generator.CallCount);
    Assert.Equal("Đã thử 3 lần nhưng chưa tạo được giải thích cho câu này.", capped.Reason);

    // Under the cap the message still invites a retry.
    Assert.Contains("thử lại", first!.Reason);
  }

  [Fact]
  public async Task A_job_left_running_by_a_dead_process_is_run_again_after_the_stale_window()
  {
    var generator = new CountingGenerator();
    var store = new InMemoryPersonalizedExplanationStore();
    var version = ReadingVersion();
    var session = SubmittedSession(version);
    var clock = new FixedClock();
    var service = new PersonalizedExplanationService(
        new FixedCatalogue(version), new FixedSessions(session),
        new FixedAnswers(new Dictionary<string, string?> { ["q1"] = "A" }),
        store, generator, clock);

    var hash = ExplanationAnswerHash.Compute("A");
    var dead = new PersonalizedExplanationJob(
        "op-dead", session.Id, version.Id, "q1", hash,
        ExplanationJobState.Running, null, null, Attempts: 1, null,
        clock.UtcNow - TimeSpan.FromMinutes(1), null,
        StartedAt: clock.UtcNow - TimeSpan.FromMinutes(1));
    Assert.True(await store.TryInsertAsync(dead, default));

    // Within the window it is believed to be busy and left alone.
    var busy = await service.RequestAsync(
        new RequestPersonalizedExplanationCommand(session.UserId, session.Id, "q1", "op-dead"),
        default);
    Assert.Equal("running", busy.State);
    Assert.Equal(0, generator.CallCount);

    // Past it, it is dead, and the request runs it.
    clock.UtcNow += PersonalizedExplanationService.StaleRunningAfter + TimeSpan.FromSeconds(1);
    var revived = await service.RequestAsync(
        new RequestPersonalizedExplanationCommand(session.UserId, session.Id, "q1", "op-dead"),
        default);
    Assert.Equal("ready", revived.State);
    Assert.Equal(2, revived.Attempts);
    Assert.Equal(1, generator.CallCount);
  }

  [Fact]
  public async Task An_aborted_request_does_not_leave_the_job_running()
  {
    var generator = new CountingGenerator { HonoursCancellation = true };
    var store = new InMemoryPersonalizedExplanationStore();
    var version = ReadingVersion();
    var session = SubmittedSession(version);
    var service = BuildService(generator, store, version, session);

    using var aborted = new CancellationTokenSource();
    aborted.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RequestAsync(
        new RequestPersonalizedExplanationCommand(session.UserId, session.Id, "q1", "op-1"),
        aborted.Token));

    var job = await store.FindByOperationAsync("op-1", default);
    Assert.NotNull(job);
    Assert.Equal(ExplanationJobState.Failed, job!.State);
    Assert.Equal("EXPLANATION_REQUEST_ABORTED", job.Error);

    // And the next request simply tries again.
    var retried = await service.RequestAsync(
        new RequestPersonalizedExplanationCommand(session.UserId, session.Id, "q1", "op-1"),
        default);
    Assert.Equal("ready", retried.State);
  }

  [Fact]
  public void Statuses_pick_one_job_per_question_preferring_ready_then_newest()
  {
    var version = ReadingVersion();
    var session = SubmittedSession(version);
    var t0 = DateTimeOffset.UtcNow;
    PersonalizedExplanationJob Job(string op, ExplanationJobState state, int attempts, DateTimeOffset at) =>
        new(op, session.Id, version.Id, "q1", "hash-" + op, state, null, null, attempts, null, at, null, at);

    // Two failed jobs and one ready one for the same question: ready wins.
    var statuses = PersonalizedExplanationService.ProjectStatuses(version,
    [
      Job("op-1", ExplanationJobState.Failed, 3, t0),
      Job("op-2", ExplanationJobState.Ready, 1, t0 - TimeSpan.FromMinutes(5)),
      Job("op-3", ExplanationJobState.Failed, 1, t0 + TimeSpan.FromMinutes(1)),
    ]);
    var q1 = Assert.Single(statuses, s => s.QuestionId == "q1");
    Assert.Equal("ready", q1.State);

    // No ready job: the newest one is what the learner is waiting on.
    statuses = PersonalizedExplanationService.ProjectStatuses(version,
    [
      Job("op-1", ExplanationJobState.Failed, 3, t0),
      Job("op-3", ExplanationJobState.Running, 1, t0 + TimeSpan.FromMinutes(1)),
    ]);
    q1 = Assert.Single(statuses, s => s.QuestionId == "q1");
    Assert.Equal("running", q1.State);
  }

  private static PersonalizedExplanationService BuildService(
      CountingGenerator generator,
      InMemoryPersonalizedExplanationStore store,
      ExamVersion version,
      ExamSession session)
  {
    return new PersonalizedExplanationService(
        new FixedCatalogue(version),
        new FixedSessions(session),
        new FixedAnswers(new Dictionary<string, string?> { ["q1"] = "A" }),
        store,
        generator,
        new FixedClock());
  }

  private static ExamVersion ReadingVersion(bool withOptions = false)
  {
    IReadOnlyList<QuestionOption> options = withOptions
        ? [new QuestionOption("A", "Alpha"), new QuestionOption("B", "Beta")]
        : [];
    var question = new Question(
        "q1", 1, QuestionType.MultipleChoice, "Pick one", options, null,
        new AnswerKey([new AcceptedAnswer("B", null, null)], null));

    var part = new SectionPart(
        1, "passage", null, "Passage with sample passage evidence inside.", null, null, null,
        null, null, null, null, [question]);

    return ExamVersion.CreateDraft(
        ExamDefinitionId.New(), 1, "Sample", ExamVariant.Academic,
        new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
        new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
        [new Section(ExamModule.Reading, 1, [part])]);
  }

  private static ExamSession SubmittedSession(ExamVersion version)
  {
    version.Publish(DateTimeOffset.UtcNow);
    var session = ExamSession.Start(
        UserId.New(),
        version,
        SessionMode.Single,
        SessionTiming.OpenEnded,
        version.FirstModule(),
        DateTimeOffset.UtcNow);

    session.Submit(DateTimeOffset.UtcNow);
    return session;
  }

  private sealed class CountingGenerator : IReadingListeningExplanationGenerator
  {
    public int CallCount { get; private set; }
    public bool Fails { get; init; }
    public bool HonoursCancellation { get; init; }
    public ExplanationGenerationRequest? LastRequest { get; private set; }

    public Task<ExplanationGenerationResult> GenerateAsync(
        ExplanationGenerationRequest request, CancellationToken ct)
    {
      CallCount++;
      LastRequest = request;
      if (HonoursCancellation) ct.ThrowIfCancellationRequested();
      if (Fails)
        return Task.FromResult(new ExplanationGenerationResult(false, null, null, "EXPLANATION_PROVIDER_FAILED"));

      return Task.FromResult(new ExplanationGenerationResult(
          true,
          """
          {
            "correctAnswer": "B",
            "shortReason": "Because the text says so.",
            "evidence": ["sample passage evidence"],
            "translation": "Câu hỏi: chọn một."
          }
          """,
          new ExplanationProviderMetadata("test", "fixture", "v1", "req-1"),
          null));
    }
  }

  private sealed class FixedCatalogue(ExamVersion version) : IExamCatalogue
  {
    public Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExamVersion>>([version]);

    public Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExamVersion>>([version]);

    public Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct) =>
        Task.FromResult<ExamVersion?>(id == version.Id ? version : null);

    public Task UpsertAsync(ExamVersion v, CancellationToken ct) => Task.CompletedTask;
    public Task SetStatusAsync(ExamVersionId id, ExamVersionStatus status, CancellationToken ct) => Task.CompletedTask;
  }

  private sealed class FixedSessions(ExamSession session) : IExamSessionRepository
  {
    public Task<ExamSession?> FindAsync(ExamSessionId id, CancellationToken ct) =>
        Task.FromResult<ExamSession?>(id == session.Id ? session : null);

    public Task<ExamSession?> FindOpenForUserAsync(UserId userId, CancellationToken ct) =>
        Task.FromResult<ExamSession?>(null);

    public Task<IReadOnlyList<ExamSession>> ListForUserAsync(UserId userId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExamSession>>([]);

    public Task AddAsync(ExamSession s, CancellationToken ct) => Task.CompletedTask;

    public Task<bool> TrySaveAsync(ExamSession s, SessionState from, CancellationToken ct) =>
        Task.FromResult(true);
  }

  private sealed class FixedAnswers(IReadOnlyDictionary<string, string?> answers) : IAnswerSheetStore
  {
    public Task<IReadOnlyDictionary<string, string?>> LoadAsync(
        ExamSessionId sessionId, ExamModule module, CancellationToken ct) =>
        Task.FromResult(answers);

    public Task<AnswerSheet> ReadAsync(
        ExamSessionId sessionId, ExamModule module, CancellationToken ct) =>
        Task.FromResult(new AnswerSheet(answers, 1));

    public Task<PatchedSheet> PatchAsync(
        ExamSessionId sessionId, ExamModule module,
        IReadOnlyDictionary<string, string?> changes, DateTimeOffset at, CancellationToken ct,
        IReadOnlyDictionary<string, long>? sequences = null) =>
        throw new NotSupportedException();

    public Task SetAnswerAsync(
        ExamSessionId sessionId, ExamModule module, string questionId, string? value,
        DateTimeOffset at, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<AnswerSheet> CloseAsync(
        ExamSessionId sessionId, ExamModule module, DateTimeOffset at, CancellationToken ct) =>
        throw new NotSupportedException();
  }

  private sealed class InMemoryPersonalizedExplanationStore : IPersonalizedExplanationStore
  {
    private readonly Dictionary<string, PersonalizedExplanationJob> _jobs = new();

    public Task<PersonalizedExplanationJob?> FindByOperationAsync(
        string operationId, CancellationToken ct) =>
        Task.FromResult(_jobs.TryGetValue(operationId, out var job) ? job : null);

    public Task<PersonalizedExplanationJob?> FindReadyAsync(
        ExamSessionId sessionId, string questionId, string answerHash, CancellationToken ct) =>
        Task.FromResult(_jobs.Values.FirstOrDefault(j =>
            j.SessionId == sessionId
            && j.QuestionId == questionId
            && j.AnswerHash == answerHash
            && j.State == ExplanationJobState.Ready));

    public Task<bool> TryInsertAsync(PersonalizedExplanationJob job, CancellationToken ct)
    {
      return Task.FromResult(_jobs.TryAdd(job.OperationId, job));
    }

    public Task<bool> UpdateAsync(PersonalizedExplanationJob job, CancellationToken ct)
    {
      _jobs[job.OperationId] = job;
      return Task.FromResult(true);
    }

    public Task<IReadOnlyList<PersonalizedExplanationJob>> ListForSessionAsync(
        ExamSessionId sessionId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PersonalizedExplanationJob>>(
            _jobs.Values.Where(j => j.SessionId == sessionId).ToList());
  }

  private sealed class FixedClock : IClock
  {
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
  }
}
