using System.Text;
using System.Text.Json;
using Json.Schema;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Ai.Extraction;

/// <summary>
/// The trust boundary between a provider's bytes and a review candidate.
///
/// ── Order of the checks, and why it is this order ─────────────────────────
///
/// <b>Size, then JSON, then schema, then relationships.</b> Each step is
/// cheaper than the one after it and each assumes the previous provider-side
/// enforcement failed. A response that is refused for its size is never parsed;
/// one refused by the schema is never mapped. The expensive relational checks —
/// referential integrity, option/answer-key coherence, the candidate's own
/// invariants — run last, on a shape already known to be the right shape.
///
/// <b>Both adapters come through here.</b> GPT and Gemini differ in wire
/// format and in nothing else that matters: the same JSON string reaches the
/// same validator, so a claim that one provider "supports structured output
/// better" cannot become a reason to trust its answer more.
/// → <c>docs/ai/output-contracts.md</c>
/// </summary>
public static class ExamExtractionValidator
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Validates a provider response and maps it onto a review candidate, or
    /// throws <see cref="ExamExtractionRejectedException"/> with a stable code.
    /// </summary>
    public static ParsedExamCandidate ToCandidate(
        string json,
        string candidateId,
        string packageId,
        ExamExtractionSourceIndex sources,
        int maxResponseBytes)
    {
        EnsureWithinSizeLimit(json, maxResponseBytes);
        return ExamExtractionMapper.ToCandidate(Deserialize(json), candidateId, packageId, sources);
    }

    /// <summary>
    /// Refuses a response larger than the configured cap.
    ///
    /// <para>
    /// <b>Measured in UTF-8 bytes, not characters.</b> The cap exists to bound
    /// memory and parse time, and both are paid per byte; a response of
    /// Vietnamese or Greek text costs two to three bytes per character, so a
    /// character count would let through several times the intended size.
    /// </para>
    /// </summary>
    public static void EnsureWithinSizeLimit(string json, int maxResponseBytes)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (maxResponseBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));

        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > maxResponseBytes)
        {
            /*
             * The refusal names neither the limit nor the actual size. An error
             * message is the thing most likely to be logged verbatim and
             * forwarded, and a specific limit value tells whoever reads it
             * exactly how much fits under the gate.
             */
            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.ResponseTooLarge,
                "The extraction response exceeds the configured response size limit.");
        }
    }

    public static void EnsureSchemaValid(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.MalformedJson,
                $"The extraction response is not valid JSON: {e.Message}");
        }

        using (document)
        {
            var result = ExamExtractionSchema.Instance.Evaluate(document.RootElement);

            if (result.IsValid) return;

            var detail = string.Join(
                "; ",
                Failures(result)
                    .Distinct(StringComparer.Ordinal)
                    .Take(8)
                    .DefaultIfEmpty("the response does not match the extraction contract"));

            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.SchemaInvalid,
                $"The extraction response failed schema validation: {detail}");
        }
    }

    /// <summary>
    /// Where the response failed, as a location and a keyword —
    /// <c>/modules/0: additionalProperties</c>.
    ///
    /// <para>
    /// <b>The offending value is deliberately not included.</b> A rejected
    /// response is the one most likely to contain an injected string, and a
    /// validation message is read by a person and copied into a ticket; a
    /// keyword name says what to fix without carrying the payload along with
    /// it.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Failures(EvaluationResults results)
    {
        if (results.Errors is { Count: > 0 } errors)
        {
            foreach (var error in errors)
                yield return $"{results.InstanceLocation}: {error.Key}";
        }

        foreach (var nested in results.Details ?? [])
        {
            foreach (var failure in Failures(nested)) yield return failure;
        }
    }

    private static ExamExtractionDto Deserialize(string json)
    {
        EnsureSchemaValid(json);

        try
        {
            return JsonSerializer.Deserialize<ExamExtractionDto>(json, Json)
                ?? throw new ExamExtractionRejectedException(
                    ExamExtractionRejection.SchemaInvalid,
                    "The extraction response deserialized to nothing.");
        }
        catch (JsonException e)
        {
            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.SchemaInvalid,
                $"The extraction response could not be read into the contract: {e.Message}");
        }
    }
}
