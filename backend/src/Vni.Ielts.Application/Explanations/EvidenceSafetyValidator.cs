using System.Text.Json;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Explanations;

public sealed record EvidenceSourceContext(
    string? PassageBody,
    string? Transcript);

public sealed record EvidenceItemValidation(bool IsValid, string? NormalizedQuote, string? RefusalCode);

/// <summary>
/// FS5.5 — evidence must be a real span in the passage or transcript.
/// </summary>
public static class EvidenceSafetyValidator
{
    public static EvidenceItemValidation ValidateItem(
        JsonElement item, ExamModule module, EvidenceSourceContext source)
    {
        return item.ValueKind switch
        {
            JsonValueKind.String => ValidateQuote(item.GetString()!, module, source),
            JsonValueKind.Object => ValidateReference(item, module, source),
            _ => Invalid("EXPLANATION_EVIDENCE_INVALID"),
        };
    }

    private static EvidenceItemValidation ValidateReference(
        JsonElement obj, ExamModule module, EvidenceSourceContext source)
    {
        if (!obj.TryGetProperty("quote", out var quoteEl) || quoteEl.ValueKind != JsonValueKind.String)
            return Invalid("EXPLANATION_EVIDENCE_INVALID");

        var quote = quoteEl.GetString()!;
        if (string.IsNullOrWhiteSpace(quote))
            return Invalid("EXPLANATION_EVIDENCE_EMPTY");

        if (!obj.TryGetProperty("source", out var sourceEl) || sourceEl.ValueKind != JsonValueKind.String)
            return Invalid("EXPLANATION_EVIDENCE_INVALID");

        var evidenceSource = sourceEl.GetString()!;
        return evidenceSource switch
        {
            "passage" when module == ExamModule.Reading =>
                ValidateQuote(quote, module, source with { Transcript = null }),

            "transcript" when module == ExamModule.Listening =>
                ValidateListeningQuote(quote, obj, source),

            "prompt" => new EvidenceItemValidation(true, quote.Trim(), null),

            _ => Invalid("EXPLANATION_EVIDENCE_SOURCE_INVALID"),
        };
    }

    private static EvidenceItemValidation ValidateListeningQuote(
        string quote, JsonElement obj, EvidenceSourceContext source)
    {
        if (string.IsNullOrWhiteSpace(source.Transcript))
            return Invalid("EXPLANATION_TRANSCRIPT_UNAVAILABLE");

        if (obj.TryGetProperty("start", out _) || obj.TryGetProperty("end", out _))
        {
            if (!obj.TryGetProperty("start", out var startEl)
                || !obj.TryGetProperty("end", out var endEl)
                || startEl.ValueKind != JsonValueKind.Number
                || endEl.ValueKind != JsonValueKind.Number)
                return Invalid("EXPLANATION_EVIDENCE_TIMESTAMP_INVALID");

            var start = startEl.GetInt32();
            var end = endEl.GetInt32();
            if (start < 0 || end <= start)
                return Invalid("EXPLANATION_EVIDENCE_TIMESTAMP_INVALID");
        }

        return ValidateSubstring(source.Transcript, quote);
    }

    private static EvidenceItemValidation ValidateQuote(
        string quote, ExamModule module, EvidenceSourceContext source)
    {
        if (string.IsNullOrWhiteSpace(quote))
            return Invalid("EXPLANATION_EVIDENCE_EMPTY");

        var haystack = module == ExamModule.Reading ? source.PassageBody : source.Transcript;
        if (string.IsNullOrWhiteSpace(haystack))
            return Invalid(module == ExamModule.Reading
                ? "EXPLANATION_PASSAGE_UNAVAILABLE"
                : "EXPLANATION_TRANSCRIPT_UNAVAILABLE");

        return ValidateSubstring(haystack, quote);
    }

    private static EvidenceItemValidation ValidateSubstring(string haystack, string quote)
    {
        if (ContainsNormalized(haystack, quote))
            return new EvidenceItemValidation(true, quote.Trim(), null);

        return Invalid("EXPLANATION_EVIDENCE_NOT_FOUND");
    }

    internal static bool ContainsNormalized(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle)) return false;

        var normalizedHay = CollapseWhitespace(haystack);
        var normalizedNeedle = CollapseWhitespace(needle);
        return normalizedHay.Contains(normalizedNeedle, StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static EvidenceItemValidation Invalid(string code) =>
        new(false, null, code);
}
