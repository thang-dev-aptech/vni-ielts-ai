using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Explanations;

/// <summary>
/// A validated explanation artifact. No band, no score — A-11 enforced by shape.
/// </summary>
public sealed record ValidatedExplanation(
    string CorrectAnswer,
    string ShortReason,
    IReadOnlyList<string> Evidence,
    string? CommonMistake);

/// <summary>Wire shape the provider must return. No band field exists.</summary>
public sealed class ExplanationProviderOutput
{
    [JsonPropertyName("correctAnswer")]
    public string? CorrectAnswer { get; init; }

    [JsonPropertyName("shortReason")]
    public string? ShortReason { get; init; }

    [JsonPropertyName("evidence")]
    public List<JsonElement>? Evidence { get; init; }

    [JsonPropertyName("commonMistake")]
    public string? CommonMistake { get; init; }
}

public sealed record ExplanationValidationResult(
    bool IsValid,
    ValidatedExplanation? Explanation,
    string? RefusalCode);

public static class ExplanationAnswerHash
{
    public static string Compute(string? answer)
    {
        var normalized = (answer ?? string.Empty).Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public static class PersonalizedExplanationOperation
{
    public static string IdFor(ExamSessionId sessionId, string questionId, string answerHash) =>
        $"{sessionId.Value}:{questionId}:{answerHash}";
}
