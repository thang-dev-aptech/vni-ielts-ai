using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Explanations;

/// <summary>
/// Generates Reading/Listening explanations via a structured-output LLM call.
/// The port cannot express a band — only explanatory text about an answer.
/// → A-11, docs/ai/ai-architecture.md
/// </summary>
public interface IReadingListeningExplanationGenerator
{
    Task<ExplanationGenerationResult> GenerateAsync(
        ExplanationGenerationRequest request, CancellationToken ct);
}

public sealed record ExplanationGenerationRequest(
    ExamModule Module,
    string QuestionId,
    string QuestionPrompt,
    string ExpectedAnswer,
    string? LearnerAnswer,
    string? PassageOrTranscript,
    bool Personalized);

public sealed record ExplanationGenerationResult(
    bool IsSuccess,
    string? RawJson,
    ExplanationProviderMetadata? Metadata,
    string? RefusalCode);

public sealed record ExplanationProviderMetadata(
    string Provider,
    string Model,
    string PromptVersion,
    string RequestId);

/// <summary>Canonical explanations keyed by exam version and question.</summary>
public interface ICanonicalExplanationCache
{
    Task<StoredCanonicalExplanation?> FindAsync(
        ExamVersionId versionId, string questionId, CancellationToken ct);

    Task SaveAsync(StoredCanonicalExplanation entry, CancellationToken ct);
}

public sealed record StoredCanonicalExplanation(
    ExamVersionId VersionId,
    string QuestionId,
    ValidatedExplanation Explanation,
    ExplanationProviderMetadata Metadata);

/// <summary>Per-sitting personalized explanations, idempotent on operation id.</summary>
public interface IPersonalizedExplanationStore
{
    Task<PersonalizedExplanationJob?> FindByOperationAsync(
        string operationId, CancellationToken ct);

    Task<PersonalizedExplanationJob?> FindReadyAsync(
        ExamSessionId sessionId, string questionId, string answerHash, CancellationToken ct);

    Task<bool> TryInsertAsync(PersonalizedExplanationJob job, CancellationToken ct);

    Task<bool> UpdateAsync(PersonalizedExplanationJob job, CancellationToken ct);

    Task<IReadOnlyList<PersonalizedExplanationJob>> ListForSessionAsync(
        ExamSessionId sessionId, CancellationToken ct);
}

public enum ExplanationJobState
{
    Pending,
    Running,
    Ready,
    Failed,
}

public sealed record PersonalizedExplanationJob(
    string OperationId,
    ExamSessionId SessionId,
    ExamVersionId VersionId,
    string QuestionId,
    string AnswerHash,
    ExplanationJobState State,
    ValidatedExplanation? Content,
    ExplanationProviderMetadata? Metadata,
    int Attempts,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
