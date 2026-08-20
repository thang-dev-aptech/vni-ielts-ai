using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

public sealed class BandScoreTests
{
    [Theory]
    [InlineData(0)] [InlineData(0.5)] [InlineData(6.5)] [InlineData(9)]
    public void Every_reportable_band_is_accepted(decimal value) =>
        Assert.True(BandScore.IsValid(value));

    [Theory]
    [InlineData(6.3)]   // what a model returns when a numeric range was ignored
    [InlineData(7.77)]
    [InlineData(0.25)]
    [InlineData(8.75)]
    public void A_value_off_the_half_step_grid_is_rejected(decimal value) =>
        Assert.False(BandScore.IsValid(value));

    [Theory]
    [InlineData(-0.5)] [InlineData(9.5)] [InlineData(10)] [InlineData(47)]
    public void An_out_of_scale_value_throws_rather_than_clamping(decimal value)
    {
        // Clamping 47 to 9 would turn a visible fault into a plausible-looking
        // wrong score, and nobody investigates a plausible score.
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => BandScore.Create(value));
        Assert.Contains("never clamp", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every worked example from docs/domain/band-scoring.md, verbatim.
    ///
    /// The `.75` row is the reason this function exists at all: rounding to
    /// the nearest 0.5 by any generic helper yields 6.5, and the correct
    /// answer is 7.0. If someone later "simplifies" this to Math.Round, that
    /// row goes red.
    /// </summary>
    [Theory]
    // L    R    W    S     mean     expected   rule
    [InlineData(6.5, 6.5, 5.0, 7.0, 6.5)]  // 6.25   -> .25 up to half band
    [InlineData(4.0, 3.5, 4.0, 4.0, 4.0)]  // 3.875  -> nearest half band
    [InlineData(6.5, 6.5, 5.5, 6.0, 6.0)]  // 6.125  -> nearest half band
    [InlineData(7.0, 8.0, 7.0, 8.0, 7.5)]  // 7.5    -> exact half band
    [InlineData(6.5, 7.0, 7.0, 7.5, 7.0)]  // 7.0    -> exact whole band
    [InlineData(7.5, 8.0, 8.0, 8.0, 8.0)]  // 7.875  -> nearest is 8.0
    [InlineData(6.0, 6.5, 7.0, 7.5, 7.0)]  // 6.75   -> .75 up to WHOLE band
    public void Overall_band_follows_the_official_asymmetric_rule(
        decimal listening, decimal reading, decimal writing, decimal speaking, decimal expected)
    {
        var overall = BandScore.Overall(
        [
            BandScore.Create(listening), BandScore.Create(reading),
            BandScore.Create(writing), BandScore.Create(speaking),
        ]);

        Assert.Equal(expected, overall.Value);
    }

    /// <summary>
    /// Pins the two rounding strategies that actually get this wrong, because
    /// the failure mode is easy to state imprecisely.
    ///
    /// <list type="bullet">
    /// <item><b>MidpointRounding.ToEven</b> — the DEFAULT for
    /// <c>Math.Round</c> in .NET — yields 6.0 for the .25 case. Correct is
    /// 6.5. This is the most dangerous of the three, because reaching for
    /// <c>Math.Round</c> without naming a mode looks entirely reasonable in
    /// review.</item>
    /// <item><b>Truncation</b> (<c>Math.Floor(mean * 2) / 2</c>) yields 6.5
    /// for the .75 case and 6.0 for the .25 case. Wrong on both.</item>
    /// </list>
    ///
    /// Round-half-up on the half-band grid — what <c>Overall</c> implements —
    /// is correct for both. Note that ToEven happens to get the .75 case right,
    /// so a test covering only .75 would pass against a broken implementation.
    /// Both rows are needed.
    /// </summary>
    [Theory]
    [InlineData(6.25, 6.5)]  // ToEven gives 6.0 here — the trap
    [InlineData(6.75, 7.0)]  // truncation gives 6.5 here — the other trap
    public void The_two_asymmetric_cases_defeat_the_obvious_implementations(
        decimal mean, decimal correct)
    {
        var onGrid = mean * 2m;

        var toEven = Math.Round(onGrid, MidpointRounding.ToEven) / 2m;
        var truncated = Math.Floor(onGrid) / 2m;
        var halfUp = Math.Round(onGrid, MidpointRounding.AwayFromZero) / 2m;

        // At least one obvious approach is wrong for each of these means.
        Assert.True(
            toEven != correct || truncated != correct,
            $"Neither naive strategy failed for mean {mean}, so this row proves nothing.");

        // The strategy Overall actually uses is right for both.
        Assert.Equal(correct, halfUp);
    }

    [Fact]
    public void Overall_agrees_with_round_half_up_on_both_asymmetric_cases()
    {
        // 6.0 + 6.5 + 7.0 + 7.5 = 27 / 4 = 6.75
        Assert.Equal(7.0m, BandScore.Overall(
        [
            BandScore.Create(6.0m), BandScore.Create(6.5m),
            BandScore.Create(7.0m), BandScore.Create(7.5m),
        ]).Value);

        // 6.5 + 6.5 + 5.0 + 7.0 = 25 / 4 = 6.25
        Assert.Equal(6.5m, BandScore.Overall(
        [
            BandScore.Create(6.5m), BandScore.Create(6.5m),
            BandScore.Create(5.0m), BandScore.Create(7.0m),
        ]).Value);
    }

    [Fact]
    public void A_single_skill_session_still_produces_an_overall_band()
    {
        // Single Skill has one SectionAttempt, so the mean is that one band.
        var overall = BandScore.Overall([BandScore.Create(6.5m)]);
        Assert.Equal(6.5m, overall.Value);
    }

    [Fact]
    public void An_empty_set_throws_rather_than_returning_zero()
    {
        // Returning 0.0 here would be a fabricated score — product law L3.
        Assert.Throws<ArgumentException>(() => BandScore.Overall([]));
    }

    [Fact]
    public void Formatting_always_carries_one_decimal()
    {
        Assert.Equal("7.0", BandScore.Create(7m).ToString());
        Assert.Equal("6.5", BandScore.Create(6.5m).ToString());
        Assert.Equal("0.0", BandScore.Create(0m).ToString());
    }
}
