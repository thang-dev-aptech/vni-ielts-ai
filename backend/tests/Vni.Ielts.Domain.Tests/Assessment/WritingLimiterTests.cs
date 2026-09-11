using Vni.Ielts.Domain.Assessment;

namespace Vni.Ielts.Domain.Tests.Assessment;

public sealed class WritingLimiterTests
{
    private static List<ClaimedCriterion> Claims(decimal band = 7m) =>
    [
        new(CriterionKeys.TaskAchievement, band, "Covers the graphic.", ["the chart shows"]),
        new(CriterionKeys.CoherenceAndCohesion, band, "Sequenced.", ["the chart shows"]),
        new(CriterionKeys.LexicalResource, band, "Range.", ["the chart shows"]),
        new(CriterionKeys.GrammaticalRangeAndAccuracy, band, "Control.", ["the chart shows"]),
    ];

    private static WritingLimiterInput None => new(
        false, false, false, false, false, false, false, false, 1, false);

    [Fact]
    public void Each_limiter_caps_only_the_criteria_it_names()
    {
        var unrelated = WritingLimiters.Apply(Claims(), None with { WhollyUnrelated = true });
        Assert.Equal(1m, Band(unrelated, CriterionKeys.TaskAchievement));
        Assert.Equal(7m, Band(unrelated, CriterionKeys.LexicalResource));

        var offTopic = WritingLimiters.Apply(Claims(), None with { EntirelyOffTopic = true });
        Assert.Equal(2m, Band(offTopic, CriterionKeys.CoherenceAndCohesion));
        Assert.Equal(7m, Band(offTopic, CriterionKeys.TaskAchievement));

        var gra = WritingLimiters.Apply(Claims(), None with { SimpleSentencesPredominate = true });
        Assert.Equal(4m, Band(gra, CriterionKeys.GrammaticalRangeAndAccuracy));
        Assert.Equal(7m, Band(gra, CriterionKeys.TaskAchievement));
    }

    [Fact]
    public void A_rationale_that_mentions_off_topic_does_not_cap_when_the_flag_is_unset()
    {
        var claimed = Claims();
        claimed[0] = claimed[0] with
        {
            Feedback = "The essay is entirely off-topic and unrelated to the prompt.",
        };

        var result = WritingLimiters.Apply(claimed, None);

        Assert.All(result.Criteria, c => Assert.Equal(7m, c.Band));
        Assert.Empty(result.Advisories);
    }

    [Fact]
    public void Gt_tone_limiter_does_not_fire_on_academic_task_1()
    {
        var result = WritingLimiters.Apply(
            Claims(),
            None with { GtBulletsOrTone = true, TaskNumber = 1, GeneralTraining = false });

        Assert.Equal(7m, Band(result, CriterionKeys.TaskAchievement));
        Assert.Empty(result.Advisories);
    }

    [Fact]
    public void Task_2_paragraphing_does_not_fire_on_task_1()
    {
        var result = WritingLimiters.Apply(
            Claims(),
            None with { Task2ParagraphingInadequate = true, TaskNumber = 1 });

        Assert.Equal(7m, Band(result, CriterionKeys.CoherenceAndCohesion));
    }

    [Fact]
    public void Academic_no_data_does_not_fire_on_general_training()
    {
        var result = WritingLimiters.Apply(
            Claims(),
            None with { AcademicTask1NoData = true, TaskNumber = 1, GeneralTraining = true });

        Assert.Equal(7m, Band(result, CriterionKeys.TaskAchievement));
    }

    [Fact]
    public void The_stricter_cap_wins_when_two_limiters_hit_the_same_criterion()
    {
        var result = WritingLimiters.Apply(
            Claims(),
            None with { WhollyUnrelated = true, GtBulletsOrTone = true, TaskNumber = 1, GeneralTraining = true });

        Assert.Equal(1m, Band(result, CriterionKeys.TaskAchievement));
        Assert.Contains("wholly-unrelated", result.Advisories);
        Assert.Contains("gt-bullets-or-tone", result.Advisories);
    }

    private static decimal Band(WritingLimiterResult result, string criterion) =>
        result.Criteria.Single(c => c.Criterion == criterion).Band;
}
