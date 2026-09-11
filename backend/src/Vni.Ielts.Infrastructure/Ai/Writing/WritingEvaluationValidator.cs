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

    public static ClaimedEvaluation ToClaimedEvaluation(string json, bool wholeBandCriteria = false)
    {
        EnsureSchemaValid(json);

        var dto = JsonSerializer.Deserialize<WritingEvaluationDto>(json, Json)
            ?? throw new MarkingRejectedException("Writing evaluation JSON did not deserialize.");

        var criteria = new List<ClaimedCriterion>(4);

        if (dto.Criteria.TaskAchievement is { } ta)
            criteria.Add(Map(CriterionKeys.TaskAchievement, ta, wholeBandCriteria));
        if (dto.Criteria.TaskResponse is { } tr)
            criteria.Add(Map(CriterionKeys.TaskResponse, tr, wholeBandCriteria));

        criteria.Add(Map(CriterionKeys.CoherenceAndCohesion, dto.Criteria.CoherenceAndCohesion, wholeBandCriteria));
        criteria.Add(Map(CriterionKeys.LexicalResource, dto.Criteria.LexicalResource, wholeBandCriteria));
        criteria.Add(Map(CriterionKeys.GrammaticalRangeAndAccuracy, dto.Criteria.GrammaticalRangeAndAccuracy, wholeBandCriteria));

        if (criteria.Count != 4)
        {
            throw new MarkingRejectedException(
                "Writing evaluation must carry exactly one of taskAchievement or taskResponse, plus CC, LR and GRA.");
        }

        return new ClaimedEvaluation(criteria, dto.SectionBand);
    }

    public static WritingLimiterInput LimitersFrom(
        string json, int? taskNumber, bool generalTraining, bool formatNotProse, bool insufficientSentenceControl)
    {
        var dto = JsonSerializer.Deserialize<WritingEvaluationDto>(json, Json);
        var flags = dto?.Limiters;

        return new WritingLimiterInput(
            flags?.WhollyUnrelated ?? false,
            flags?.EntirelyOffTopic ?? false,
            formatNotProse,
            insufficientSentenceControl,
            flags?.SimpleSentencesPredominate ?? false,
            flags?.GtBulletsOrTone ?? false,
            flags?.Task2ParagraphingInadequate ?? false,
            flags?.AcademicTask1NoData ?? false,
            taskNumber,
            generalTraining);
    }

    private static ClaimedCriterion Map(string key, CriterionDto c, bool wholeBandCriteria)
    {
        if (wholeBandCriteria && c.Band != decimal.Truncate(c.Band))
        {
            throw new MarkingRejectedException(
                $"Criterion '{key}' was awarded {c.Band}, but this rubric only publishes whole-band descriptors.");
        }

        return new ClaimedCriterion(key, c.Band, c.Feedback, c.Evidence);
    }

    private sealed class WritingEvaluationDto
    {
        public required CriteriaBlockDto Criteria { get; init; }
        public required decimal SectionBand { get; init; }
        public required string Summary { get; init; }
        public LimitersDto? Limiters { get; init; }
    }

    private sealed class CriteriaBlockDto
    {
        public CriterionDto? TaskAchievement { get; init; }
        public CriterionDto? TaskResponse { get; init; }
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

    private sealed class LimitersDto
    {
        public bool WhollyUnrelated { get; init; }
        public bool EntirelyOffTopic { get; init; }
        public bool SimpleSentencesPredominate { get; init; }
        public bool GtBulletsOrTone { get; init; }
        public bool Task2ParagraphingInadequate { get; init; }
        public bool AcademicTask1NoData { get; init; }
    }
}
