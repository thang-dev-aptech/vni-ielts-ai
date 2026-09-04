using Vni.Ielts.Application.Learning;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Learning;

namespace Vni.Ielts.Application.Tests.Learning;

/// <summary>
/// The arithmetic a learner is shown with a number in it: which skill is
/// weakest, and how many days in a row they have turned up. Neither comes
/// from a model, so both are pinned here.
/// </summary>
public sealed class LearningTests
{
    // ── Goal gap ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(6.5, null, "none")]
    [InlineData(6.5, 6.5, "met")]
    [InlineData(6.5, 7.0, "met")]
    [InlineData(6.5, 6.0, "close")]
    [InlineData(6.5, 5.5, "behind")]
    [InlineData(6.5, 4.0, "behind")]
    public void A_skill_is_none_met_close_or_behind(double target, double? current, string expected) =>
        Assert.Equal(expected, GoalGap.StateOf((decimal)target, (decimal?)current));

    /// <summary>The owner's example: 6.5 target, R 4.0 · L 6.0 · W 6.5 · S 6.0 → Reading first.</summary>
    [Fact]
    public void Focus_puts_the_largest_gap_first_and_leaves_out_skills_at_target()
    {
        var focus = GoalGap.Focus(6.5m, new Dictionary<ExamModule, decimal?>
        {
            [ExamModule.Reading] = 4.0m,
            [ExamModule.Listening] = 6.0m,
            [ExamModule.Writing] = 6.5m,
            [ExamModule.Speaking] = 6.0m,
        });

        Assert.Equal(["reading", "listening", "speaking"], focus);
    }

    [Fact]
    public void Focus_ignores_skills_with_no_band()
    {
        var focus = GoalGap.Focus(7.0m, new Dictionary<ExamModule, decimal?>
        {
            [ExamModule.Reading] = null,
            [ExamModule.Listening] = 5.0m,
            [ExamModule.Writing] = null,
            [ExamModule.Speaking] = null,
        });

        Assert.Equal(["listening"], focus);
    }

    [Theory]
    [InlineData(4.0, true)]
    [InlineData(6.5, true)]
    [InlineData(9.0, true)]
    [InlineData(6.3, false)]
    [InlineData(3.5, false)]
    [InlineData(9.5, false)]
    public void A_target_is_a_half_band_between_4_and_9(double band, bool valid) =>
        Assert.Equal(valid, LearnerGoal.IsValidTarget((decimal)band));

    // ── Streak ─────────────────────────────────────────────────────────────

    private static readonly DateOnly Today = new(2026, 9, 4);

    private static HashSet<DateOnly> Days(params int[] daysAgo) =>
        daysAgo.Select(d => Today.AddDays(-d)).ToHashSet();

    [Fact]
    public void Three_days_in_a_row_ending_today_is_a_streak_of_three_with_a_flame()
    {
        var (current, longest, activeToday) = StreakCalculator.Compute(Days(0, 1, 2), Today);

        Assert.Equal(3, current);
        Assert.Equal(3, longest);
        Assert.True(activeToday);
        Assert.True(current >= StreakCalculator.FlameThreshold);
    }

    /// <summary>Breakfast rule: a day that has not ended does not end the streak.</summary>
    [Fact]
    public void A_streak_that_ran_through_yesterday_still_stands_this_morning()
    {
        var (current, _, activeToday) = StreakCalculator.Compute(Days(1, 2, 3, 4), Today);

        Assert.Equal(4, current);
        Assert.False(activeToday);
    }

    [Fact]
    public void A_missed_day_before_yesterday_ends_the_streak()
    {
        var (current, longest, _) = StreakCalculator.Compute(Days(2, 3, 4, 5, 6), Today);

        Assert.Equal(0, current);
        Assert.Equal(5, longest);
    }

    [Fact]
    public void Longest_is_the_longest_run_anywhere_in_the_window()
    {
        var (current, longest, _) = StreakCalculator.Compute(Days(0, 10, 11, 12, 13, 30), Today);

        Assert.Equal(1, current);
        Assert.Equal(4, longest);
    }

    [Fact]
    public void No_activity_is_zero_everywhere()
    {
        var (current, longest, activeToday) = StreakCalculator.Compute(new HashSet<DateOnly>(), Today);
        Assert.Equal((0, 0, false), (current, longest, activeToday));
    }

    // ── Advice validation ──────────────────────────────────────────────────

    [Fact]
    public void Well_formed_advice_is_accepted_and_whitespace_is_folded()
    {
        var parsed = CoachingAdviceValidator.Parse(
            """{"summary":"Reading  đang\n thấp nhất.","tips":[{"module":"Reading","text":"Đọc 1 passage mỗi ngày."}]}""");

        Assert.NotNull(parsed);
        Assert.Equal("Reading đang thấp nhất.", parsed.Value.Summary);
        var tip = Assert.Single(parsed.Value.Tips);
        Assert.Equal("reading", tip.Module);
    }

    [Theory]
    [InlineData("""{"summary":"Xem https://example.com","tips":[]}""")]
    [InlineData("""{"summary":"<b>ok</b>","tips":[]}""")]
    [InlineData("""{"summary":"ok","tips":[{"module":"grammar","text":"x"}]}""")]
    [InlineData("""{"tips":[]}""")]
    [InlineData("""[]""")]
    public void Links_markup_unknown_skills_and_missing_summary_are_refused(string json) =>
        Assert.Null(CoachingAdviceValidator.Parse(json));

    [Fact]
    public void More_than_five_tips_is_refused()
    {
        var tips = string.Join(",", Enumerable.Range(0, 6).Select(_ => """{"module":"reading","text":"x"}"""));
        Assert.Null(CoachingAdviceValidator.Parse($$"""{"summary":"ok","tips":[{{tips}}]}"""));
    }

    [Fact]
    public void The_cache_key_depends_on_the_standing_not_on_the_learner()
    {
        var a = GetCoaching.CacheKey(new CoachingFacts(6.5m, [new("reading", 4.0m, 2.5m), new("listening", 6.0m, 0.5m)]));
        var b = GetCoaching.CacheKey(new CoachingFacts(6.5m, [new("reading", 4.0m, 2.5m), new("listening", 6.0m, 0.5m)]));
        var c = GetCoaching.CacheKey(new CoachingFacts(7.0m, [new("reading", 4.0m, 3.0m), new("listening", 6.0m, 1.0m)]));

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
