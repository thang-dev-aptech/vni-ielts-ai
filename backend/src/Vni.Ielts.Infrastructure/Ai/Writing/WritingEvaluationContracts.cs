namespace Vni.Ielts.Infrastructure.Ai.Writing;

public sealed record WritingEvaluationRequest(
    string TaskPrompt,
    string LearnerSubmission,
    int WordCount,
    int? MinWords,
    string RubricVersion,
    string DescriptorSource,
    string DescriptorText,
    string PromptVersion,
    string IdempotencyKey,
    int Attempt);

public sealed record WritingEvaluationResponse(
    string Json,
    string Provider,
    string Model,
    string RequestId,
    long InputTokens,
    long OutputTokens);

public sealed class TransientWritingEvaluationException(string message) : Exception(message);

public interface IWritingEvaluationClient
{
    string Provider { get; }

    Task<WritingEvaluationResponse> EvaluateAsync(
        WritingEvaluationRequest request, CancellationToken ct);
}

public interface IWritingEvaluationCostMetric
{
    void Record(string provider, string model, long inputTokens, long outputTokens);
}
