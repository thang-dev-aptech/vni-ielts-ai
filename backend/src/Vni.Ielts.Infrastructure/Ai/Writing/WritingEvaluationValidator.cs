using System.Text.Json;
using System.Text.Json.Serialization;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Assessment;

namespace Vni.Ielts.Infrastructure.Ai.Writing;

/// <summary>
/// Parses and schema-validates provider JSON before it reaches <see cref="CriterionMarking"/>.
/// </summary>
public static class WritingEvaluationValidator
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static void EnsureSchemaValid(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = WritingEvaluationSchema.Instance.Evaluate(doc.RootElement);

        if (!result.IsValid)
        {
            var detail = string.Join(
                "; ",
                result.Errors?.Select(e => e.Value) ?? ["schema validation failed"]);

            throw new MarkingRejectedException($"Writing evaluation failed schema validation: {detail}");
        }
    }

    public static ClaimedEvaluation ToClaimedEvaluation(string json)
    {
        EnsureSchemaValid(json);

        var dto = JsonSerializer.Deserialize<WritingEvaluationDto>(json, Json)
            ?? throw new MarkingRejectedException("Writing evaluation JSON did not deserialize.");

        var criteria = new List<ClaimedCriterion>(4)
        {
            Map(CriterionKeys.TaskResponse, dto.Criteria.TaskResponse),
            Map(CriterionKeys.CoherenceAndCohesion, dto.Criteria.CoherenceAndCohesion),
            Map(CriterionKeys.LexicalResource, dto.Criteria.LexicalResource),
            Map(CriterionKeys.GrammaticalRangeAndAccuracy, dto.Criteria.GrammaticalRangeAndAccuracy),
        };

        return new ClaimedEvaluation(criteria, dto.SectionBand);
    }

    private static ClaimedCriterion Map(string key, CriterionDto c) =>
        new(key, c.Band, c.Feedback, c.Evidence);

    private sealed class WritingEvaluationDto
    {
        public required CriteriaBlockDto Criteria { get; init; }
        public required decimal SectionBand { get; init; }
        public required string Summary { get; init; }
    }

    private sealed class CriteriaBlockDto
    {
        public required CriterionDto TaskResponse { get; init; }
        public required CriterionDto CoherenceAndCohesion { get; init; }
        public required CriterionDto LexicalResource { get; init; }
        public required CriterionDto GrammaticalRangeAndAccuracy { get; init; }
    }

    private sealed class CriterionDto
    {
        public required decimal Band { get; init; }
        public required string Feedback { get; init; }
        public required List<string> Evidence { get; init; }
    }
}
