namespace Vni.Ielts.Domain.Assessment;

/// <summary>
/// Layer 3 limiter capping — applied by code after the model returns.
///
/// A judged limiter is a boolean the model set, never English parsed out of
/// the rationale. A rationale that mentions "off-topic" with the flag unset
/// caps nothing. → writing-marking-rubric-v2 §4
///
/// Memorised-script → 0 is deliberately absent (`W-Q5`).
/// </summary>
public sealed record WritingLimiterInput(
    bool WhollyUnrelated,
    bool EntirelyOffTopic,
    bool FormatNotProse,
    bool InsufficientSentenceControl,
    bool SimpleSentencesPredominate,
    bool GtBulletsOrTone,
    bool Task2ParagraphingInadequate,
    bool AcademicTask1NoData,
    int? TaskNumber,
    bool GeneralTraining);

public sealed record WritingLimiterResult(
    IReadOnlyList<ClaimedCriterion> Criteria,
    IReadOnlyList<string> Advisories);

public static class WritingLimiters
{
    public static WritingLimiterResult Apply(
        IReadOnlyList<ClaimedCriterion> claimed,
        WritingLimiterInput input)
    {
        var caps = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var advisories = new List<string>();

        void Cap(string criterion, decimal to)
        {
            if (!caps.TryGetValue(criterion, out var current) || to < current)
                caps[criterion] = to;
        }

        void CapTask(decimal to)
        {
            Cap(CriterionKeys.TaskAchievement, to);
            Cap(CriterionKeys.TaskResponse, to);
        }

        if (input.WhollyUnrelated)
        {
            CapTask(1m);
            advisories.Add("wholly-unrelated");
        }

        if (input.EntirelyOffTopic)
        {
            Cap(CriterionKeys.CoherenceAndCohesion, 2m);
            advisories.Add("entirely-off-topic");
        }

        if (input.FormatNotProse)
        {
            CapTask(4m);
            advisories.Add("format-not-prose");
        }

        if (input.InsufficientSentenceControl)
        {
            Cap(CriterionKeys.GrammaticalRangeAndAccuracy, 3m);
            advisories.Add("insufficient-sentence-control");
        }

        if (input.SimpleSentencesPredominate)
        {
            Cap(CriterionKeys.GrammaticalRangeAndAccuracy, 4m);
            advisories.Add("simple-sentences-predominate");
        }

        if (input.GtBulletsOrTone && input.TaskNumber == 1 && input.GeneralTraining)
        {
            CapTask(4m);
            advisories.Add("gt-bullets-or-tone");
        }

        if (input.Task2ParagraphingInadequate && input.TaskNumber == 2)
        {
            Cap(CriterionKeys.CoherenceAndCohesion, 5m);
            advisories.Add("t2-paragraphing");
        }

        if (input.AcademicTask1NoData && input.TaskNumber == 1 && !input.GeneralTraining)
        {
            CapTask(5m);
            advisories.Add("ac-t1-no-data");
        }

        var capped = claimed
            .Select(c => caps.TryGetValue(c.Criterion, out var to) && c.Band > to
                ? c with { Band = to }
                : c)
            .ToList();

        return new WritingLimiterResult(capped, advisories);
    }
}
