using System.Text.Json;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Explanations;

/// <summary>
/// Server-side validation for Reading/Listening explanation output.
/// Rejects band injection, answer-key tampering, and malformed payloads.
/// </summary>
public static class ExplanationOutputValidator
{
    private static readonly HashSet<string> ForbiddenTopLevelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "band", "score", "sectionBand", "criteria", "rawScore", "isCorrect",
    };

    public static ExplanationValidationResult Validate(
        string rawJson,
        string expectedAnswer,
        ExamModule module,
        EvidenceSourceContext source)
    {
        if (ContainsForbiddenKeys(rawJson))
            return Refuse("EXPLANATION_FORBIDDEN_FIELD");

        ExplanationProviderOutput? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ExplanationProviderOutput>(rawJson);
        }
        catch (JsonException)
        {
            return Refuse("EXPLANATION_MALFORMED_JSON");
        }

        if (parsed is null
            || string.IsNullOrWhiteSpace(parsed.ShortReason)
            || parsed.Evidence is not { Count: > 0 })
            return Refuse("EXPLANATION_SCHEMA_INVALID");

        if (string.IsNullOrWhiteSpace(parsed.CorrectAnswer))
            return Refuse("EXPLANATION_MISSING_ANSWER");

        if (!AnswerMatches(expectedAnswer, parsed.CorrectAnswer))
            return Refuse("EXPLANATION_ANSWER_MISMATCH");

        var evidence = new List<string>();
        foreach (var item in parsed.Evidence)
        {
            var check = EvidenceSafetyValidator.ValidateItem(item, module, source);
            if (!check.IsValid)
                return Refuse(check.RefusalCode ?? "EXPLANATION_EVIDENCE_INVALID");

            evidence.Add(check.NormalizedQuote!);
        }

        return new ExplanationValidationResult(
            true,
            new ValidatedExplanation(
                parsed.CorrectAnswer.Trim(),
                parsed.ShortReason.Trim(),
                evidence,
                string.IsNullOrWhiteSpace(parsed.CommonMistake) ? null : parsed.CommonMistake.Trim()),
            null);
    }

    private static bool ContainsForbiddenKeys(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return true;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (ForbiddenTopLevelKeys.Contains(prop.Name))
                    return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool AnswerMatches(string expected, string claimed)
    {
        var normExpected = Normalize(expected);
        var normClaimed = Normalize(claimed);
        return string.Equals(normExpected, normClaimed, StringComparison.Ordinal);
    }

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant();

    private static ExplanationValidationResult Refuse(string code) =>
        new(false, null, code);
}
