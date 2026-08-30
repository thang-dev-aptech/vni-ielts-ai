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

  private static ExamVersion ReadingVersion()
  {
    var question = new Question(
        "q1", 1, QuestionType.MultipleChoice, "Pick one", [], null,
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

    public Task<ExplanationGenerationResult> GenerateAsync(
        ExplanationGenerationRequest request, CancellationToken ct)
    {
      CallCount++;
      return Task.FromResult(new ExplanationGenerationResult(
          true,
          """
          {
            "correctAnswer": "B",
            "shortReason": "Because the text says so.",
            "evidence": ["sample passage evidence"]
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
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
  }
}
