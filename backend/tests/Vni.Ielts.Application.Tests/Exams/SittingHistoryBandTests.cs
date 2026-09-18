namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// One sitting, two screens, one number — slice `W1`.
///
/// <b>The defect these exist for.</b> `Q-01` (owner, 2026-09-18) made a
/// three-skill mock show its overall band, and it was applied to the results
/// screen only. The history list kept the superseded rule — all four skills or
/// nothing — so a learner who finished a Reading + Listening + Writing mock
/// read a band on `/exam/{id}/results` and a dash for the same sitting on
/// `/students/progress`. Two screens of one product answering "what did I get"
/// differently is worse than either answer alone: a reader has no way to tell
/// which one is lying.
///
/// <b>Both sides go through the real handlers, against the same stores.</b>
/// Asserting the projection twice would prove the projection agrees with
/// itself; the divergence was never in the projection, it was in what the
/// history query bothered to load.
///
/// The harness and its counting ports moved to
/// <see cref="SittingHistoryHarness"/> in slice `W10`, unchanged, so the
/// second and third doors into the same history could be measured against the
/// same ports. No assertion below moved with them.
/// </summary>
public sealed class SittingHistoryBandTests
{
    /// <summary>
    /// The `W1` defect itself: a three-skill mock, both screens asked, one
    /// number expected.
    /// </summary>
    [Fact]
    public async Task History_and_results_report_the_same_overall_band_for_a_three_skill_mock()
    {
        var h = new SittingHistoryHarness();
        var sitting = await h.ThreeSkillMockAsync();

        var results = await h.ResultsAsync(sitting);
        var history = Assert.Single(await h.HistoryAsync());

        // Guards the assertion below from passing on two nulls — which is
        // exactly how the two screens "agreed" before `Q-01` reached either.
        Assert.NotNull(results.OverallBand);

        Assert.Equal(results.OverallBand, history.OverallBand);
    }

    /// <summary>
    /// `L3` still holds on the history list. A mean that would move when the
    /// running job lands is refused on both screens, not just on one.
    /// </summary>
    [Fact]
    public async Task A_skill_still_being_marked_withholds_the_band_on_both_screens()
    {
        var h = new SittingHistoryHarness();
        var sitting = await h.ThreeSkillMockAsync(writingStillRunning: true);

        var results = await h.ResultsAsync(sitting);
        var history = Assert.Single(await h.HistoryAsync());

        Assert.Null(results.OverallBand);
        Assert.Null(history.OverallBand);
    }

    /// <summary>
    /// <b>A practice sitting is never named in a marking or job read.</b>
    ///
    /// Knowing whether anything is still owed takes the marking store and the
    /// job outbox, and `SittingBand.Applies` is the one place that decides
    /// which sittings could have an overall band at all — so the saving cannot
    /// drift away from the rule.
    ///
    /// <b>Re-measured in `W10`, and the assertion got tighter rather than
    /// looser.</b> `W1` paid one marking read and one job read <i>per</i> mock:
    /// five mocks and five practice sittings cost 1 list + 1 catalogue lookup +
    /// 10 score reads + 5 marking reads + 5 job reads = 22. The batch read
    /// collapses the last two into one round trip each, so the same history
    /// costs 14 — and the count this test defends is no longer "five reads" but
    /// "one read, naming exactly the five sittings that could produce a band".
    /// <see cref="CountingMarkingStore.SessionsAsked"/> is what makes the
    /// second half of that sentence checkable: a batch read that asked about
    /// all ten would show the same <c>Reads</c> and a different
    /// <c>SessionsAsked</c>.
    ///
    /// The score read stays one per sitting — <c>ISectionResultStore</c> has no
    /// batch read and adding one would reach outside `W10`'s file scope. That
    /// is pinned below rather than left unsaid, so the slice that fixes it has
    /// to come back here and re-measure.
    /// </summary>
    [Fact]
    public async Task A_practice_sitting_costs_no_marking_or_job_read()
    {
        var h = new SittingHistoryHarness();

        for (var i = 0; i < 5; i++) await h.ThreeSkillMockAsync();
        for (var i = 0; i < 5; i++) await h.PracticeSittingAsync();

        h.ResetCounters();
        var history = await h.HistoryAsync(limit: 20);

        Assert.Equal(10, history.Count);
        Assert.Equal(1, h.Sessions.Lists);
        Assert.Equal(1, h.Catalogue.Finds);

        // One, and it was ten. The score read was the last per-sitting read on
        // this screen; batching it is what turned the total from a slope into
        // a constant. Tightened rather than relaxed — the sittings it names
        // still grow, only the round trips do not.
        Assert.Equal(1, h.Results.Reads);
        Assert.Equal(10, h.Results.SessionsAsked);

        Assert.Equal(1, h.Markings.Reads);
        Assert.Equal(1, h.Outbox.Reads);
        Assert.Equal(5, h.Markings.SessionsAsked);
        Assert.Equal(5, h.Outbox.SessionsAsked);
    }

    /// <summary>
    /// <b>A history with no mock in it asks the marking store nothing at
    /// all.</b>
    ///
    /// The batch read has to keep the saving `W1` paid for, and the way to
    /// lose it is to send one round trip with an empty id list because the
    /// call site no longer loops. One read of nothing is still a read.
    /// </summary>
    [Fact]
    public async Task A_history_of_practice_alone_costs_no_marking_or_job_read()
    {
        var h = new SittingHistoryHarness();

        for (var i = 0; i < 5; i++) await h.PracticeSittingAsync();

        h.ResetCounters();
        var history = await h.HistoryAsync(limit: 20);

        Assert.Equal(5, history.Count);
        Assert.Equal(0, h.Markings.Reads);
        Assert.Equal(0, h.Outbox.Reads);
    }
}
