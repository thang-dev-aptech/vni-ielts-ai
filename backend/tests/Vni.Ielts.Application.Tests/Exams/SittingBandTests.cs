using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// What the overview screen is allowed to call an overall band.
///
/// Every case here came from looking at real rows returned by
/// `GET /api/v1/sessions` against the development database. The first
/// implementation reported <c>overall = 0.0</c> for a Reading-only sitting,
/// and it looked entirely plausible in JSON.
/// </summary>
public sealed class SittingBandTests
{
    private static SittingSectionView S(string module, decimal? band) => new(module, band);

    [Fact]
    public void A_single_skill_sitting_has_no_overall_band()
    {
        // The bug this file exists for. Reading 6.5 is a Reading band; calling
        // it an overall band claims the learner sat four skills.
        Assert.Null(SittingBand.Overall([S("Reading", 6.5m)]));
    }

    [Fact]
    public void A_reading_only_sitting_scoring_zero_reports_nothing_rather_than_zero()
    {
        // Band 0 is a real, reportable band — a learner who answered nothing
        // earns it. That makes it indistinguishable from a bug, which is
        // exactly why it must not appear under the wrong label.
        Assert.Null(SittingBand.Overall([S("Reading", 0m)]));
    }

    [Fact]
    public void Four_skills_with_two_still_unmarked_report_nothing()
    {
        // Today's normal case: Reading and Listening are marked from the answer
        // key the moment the section closes, Writing and Speaking have no
        // evaluation pipeline. A mean of the two that happen to be marked would
        // move every time another one lands.
        Assert.Null(SittingBand.Overall([
            S("Listening", 7m), S("Reading", 6.5m), S("Writing", null), S("Speaking", null),
        ]));
    }

    [Fact]
    public void Four_marked_skills_produce_the_official_rounded_mean()
    {
        // 6.5 + 6.5 + 5.0 + 7.0 = 25 / 4 = 6.25, which rounds UP to 6.5.
        var overall = SittingBand.Overall([
            S("Listening", 6.5m), S("Reading", 6.5m), S("Writing", 5m), S("Speaking", 7m),
        ]);

        Assert.Equal(6.5m, overall);
    }

    [Fact]
    public void Section_order_does_not_change_the_answer()
    {
        var forwards = SittingBand.Overall([
            S("Listening", 7m), S("Reading", 7m), S("Writing", 7m), S("Speaking", 6m),
        ]);

        var backwards = SittingBand.Overall([
            S("Speaking", 6m), S("Writing", 7m), S("Reading", 7m), S("Listening", 7m),
        ]);

        Assert.Equal(forwards, backwards);
        Assert.Equal(7m, forwards);   // 6.75 rounds up to the whole band
    }

    [Fact]
    public void Four_sections_that_are_not_the_four_skills_report_nothing()
    {
        // Guards the count check from standing in for the identity check: four
        // sections is not the same fact as "the four skills".
        Assert.Null(SittingBand.Overall([
            S("Reading", 7m), S("Reading", 7m), S("Reading", 7m), S("Reading", 7m),
        ]));
    }
}
