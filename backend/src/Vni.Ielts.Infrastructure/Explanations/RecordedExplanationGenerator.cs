using System.Collections.Concurrent;
using System.Text.Json;
using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Infrastructure.Explanations;

/// <summary>
/// Recorded-response adapter for tests and installs without live credentials.
/// </summary>
public sealed class RecordedExplanationGenerator : IReadingListeningExplanationGenerator
{
    private readonly ConcurrentDictionary<string, int> _callCounts = new();
    private readonly Func<ExplanationGenerationRequest, string?> _responseFactory;

    public RecordedExplanationGenerator(Func<ExplanationGenerationRequest, string?>? responseFactory = null)
    {
        _responseFactory = responseFactory ?? DefaultResponse;
    }

    public int CallCount => _callCounts.Values.Sum();

    public int CallCountFor(string questionId) =>
        _callCounts.TryGetValue(questionId, out var count) ? count : 0;

    public Task<ExplanationGenerationResult> GenerateAsync(
        ExplanationGenerationRequest request, CancellationToken ct)
    {
        _callCounts.AddOrUpdate(request.QuestionId, 1, (_, c) => c + 1);

        if (ct.IsCancellationRequested)
            return Task.FromResult(new ExplanationGenerationResult(false, null, null, "EXPLANATION_CANCELLED"));

        var raw = _responseFactory(request);
        if (raw is null)
            return Task.FromResult(new ExplanationGenerationResult(false, null, null, "EXPLANATION_PROVIDER_FAILED"));

        return Task.FromResult(new ExplanationGenerationResult(
            true,
            raw,
            new ExplanationProviderMetadata("recorded", "fixture-1.0", CanonicalExplanationWorkflow.PromptVersion,
                $"rec-{request.QuestionId}"),
            null));
    }

    private static string? DefaultResponse(ExplanationGenerationRequest request)
    {
        var evidence = request.Module == ExamModule.Listening
            ? """[{"source":"transcript","quote":"sample transcript evidence","start":0,"end":12}]"""
            : """["sample passage evidence"]""";

        return $$"""
            {
              "correctAnswer": "{{request.ExpectedAnswer}}",
              "shortReason": "The answer follows from the text.",
              "evidence": {{evidence}},
              "commonMistake": "Choosing a distractor from the same paragraph."
            }
            """;
    }
}

/// <summary>Process-local canonical cache — one provider call per question per version.</summary>
public sealed class InMemoryCanonicalExplanationCache : ICanonicalExplanationCache
{
    private readonly ConcurrentDictionary<string, StoredCanonicalExplanation> _entries = new();

    private static string Key(ExamVersionId versionId, string questionId) =>
        $"{versionId.Value}:{questionId}";

    public Task<StoredCanonicalExplanation?> FindAsync(
        ExamVersionId versionId, string questionId, CancellationToken ct) =>
        Task.FromResult(_entries.TryGetValue(Key(versionId, questionId), out var entry) ? entry : null);

    public Task SaveAsync(StoredCanonicalExplanation entry, CancellationToken ct)
    {
        _entries[Key(entry.VersionId, entry.QuestionId)] = entry;
        return Task.CompletedTask;
    }
}

/// <summary>In-memory personalized store for tests and development.</summary>
public sealed class InMemoryPersonalizedExplanationStore : IPersonalizedExplanationStore
{
    private readonly ConcurrentDictionary<string, PersonalizedExplanationJob> _byOperation = new();
    private readonly ConcurrentDictionary<string, PersonalizedExplanationJob> _ready = new();

    public Task<PersonalizedExplanationJob?> FindByOperationAsync(
        string operationId, CancellationToken ct) =>
        Task.FromResult(_byOperation.TryGetValue(operationId, out var job) ? job : null);

    public Task<PersonalizedExplanationJob?> FindReadyAsync(
        ExamSessionId sessionId, string questionId, string answerHash, CancellationToken ct)
    {
        var key = ReadyKey(sessionId, questionId, answerHash);
        _ready.TryGetValue(key, out var job);
        return Task.FromResult(job);
    }

    public Task<bool> TryInsertAsync(PersonalizedExplanationJob job, CancellationToken ct)
    {
        if (!_byOperation.TryAdd(job.OperationId, job))
            return Task.FromResult(false);

        if (job.State == ExplanationJobState.Ready && job.Content is not null)
            _ready[ReadyKey(job.SessionId, job.QuestionId, job.AnswerHash)] = job;

        return Task.FromResult(true);
    }

    public Task<bool> UpdateAsync(PersonalizedExplanationJob job, CancellationToken ct)
    {
        _byOperation[job.OperationId] = job;
        if (job.State == ExplanationJobState.Ready && job.Content is not null)
            _ready[ReadyKey(job.SessionId, job.QuestionId, job.AnswerHash)] = job;

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<PersonalizedExplanationJob>> ListForSessionAsync(
        ExamSessionId sessionId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PersonalizedExplanationJob>>(
            _byOperation.Values.Where(j => j.SessionId == sessionId).ToList());

    private static string ReadyKey(ExamSessionId sessionId, string questionId, string answerHash) =>
        $"{sessionId.Value}:{questionId}:{answerHash}";
}

/// <summary>Simulates a provider that never returns — for timeout semantics tests.</summary>
public sealed class HangingExplanationGenerator : IReadingListeningExplanationGenerator
{
    public Task<ExplanationGenerationResult> GenerateAsync(
        ExplanationGenerationRequest request, CancellationToken ct) =>
        Task.FromResult(new ExplanationGenerationResult(false, null, null, "EXPLANATION_PROVIDER_TIMEOUT"));
}
