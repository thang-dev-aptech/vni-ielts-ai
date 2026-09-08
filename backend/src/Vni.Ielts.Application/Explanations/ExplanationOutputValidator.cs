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

    private static readonly char[] SurroundingQuotes = ['"', '\'', '“', '”', '‘', '’'];

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

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.ShortReason))
            return Refuse("EXPLANATION_SCHEMA_INVALID");

        // With a passage or transcript to quote from, an explanation without a
        // quote is not an explanation. Without one — a question whose part has
        // no body or transcript — there is nothing to quote, so an empty array
        // is the honest answer and refusing it would fail every such question.
        var sourceAvailable = HasSource(source);
        if (sourceAvailable && parsed.Evidence is not { Count: > 0 })
            return Refuse("EXPLANATION_SCHEMA_INVALID");

        if (string.IsNullOrWhiteSpace(parsed.CorrectAnswer))
            return Refuse("EXPLANATION_MISSING_ANSWER");

        if (!AnswerMatches(expectedAnswer, parsed.CorrectAnswer))
            return Refuse("EXPLANATION_ANSWER_MISMATCH");

        var evidence = new List<string>();
        foreach (var item in parsed.Evidence ?? [])
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
                Optional(parsed.CommonMistake),
                Optional(parsed.Translation)),
            null);
    }

    private static bool HasSource(EvidenceSourceContext source) =>
        !string.IsNullOrWhiteSpace(source.PassageBody) || !string.IsNullOrWhiteSpace(source.Transcript);

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    /// <summary>
    /// Whether the model's <c>correctAnswer</c> is the answer key's answer.
    ///
    /// The prompt says "copy it character for character", and models still
    /// wrap a word in quotes, end it with a full stop, or double a space. None
    /// of those is a different answer, so both sides are normalised the same
    /// way before the ordinal compare; a genuinely different word still fails.
    /// </summary>
    internal static bool AnswerMatches(string expected, string claimed)
    {
        var normExpected = Normalize(expected);
        var normClaimed = Normalize(claimed);
        return string.Equals(normExpected, normClaimed, StringComparison.Ordinal);
    }

    private static string Normalize(string value)
    {
        var text = value.Trim();

        // Peel surrounding quotes and a trailing full stop until stable, so
        // "Cartography." and "Cartography". both reduce to cartography.
        while (true)
        {
            var before = text;

            if (text.Length >= 2
                && Array.IndexOf(SurroundingQuotes, text[0]) >= 0
                && Array.IndexOf(SurroundingQuotes, text[^1]) >= 0)
                text = text[1..^1].Trim();

            if (text.EndsWith('.'))
                text = text[..^1].TrimEnd();

            if (text == before) break;
        }

        return string.Join(' ', text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static ExplanationValidationResult Refuse(string code) =>
        new(false, null, code);
}
