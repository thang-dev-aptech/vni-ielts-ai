using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Learning;
using Vni.Ielts.Application.Tests.Exams;

namespace Vni.Ielts.Application.Tests.Learning;

/// <summary>
/// The second and third doors into a learner's sitting history — slice `W10`.
///
/// <b>`ListMySittings` is not the only caller, and fixing only it would be
/// half a fix.</b> <c>Learning/Handlers.cs</c> holds two more readers of the
/// same history, each with its own ceiling, and the queue named the file
/// without naming which of the two costs anything:
///
/// <list type="bullet">
/// <item><see cref="GetCoaching"/> — calls <see cref="ListMySittings"/> at its
/// full `50` ceiling and then <i>asks the marking store again, once per
/// sitting</i>, for the per-task Writing detail. That second loop reads exactly
/// the markings the first one already read and discarded, so `/me/coaching`
/// paid the N+1 twice over.</item>
/// <item><see cref="GetLearnerActivity"/> — <c>ListForUserAsync(userId, 500)</c>,
/// a ceiling ten times the history screen's. It reads two timestamps off each
/// sitting and nothing else, so it is one list read whatever the size. The
/// slice brief expected an N+1 here; there is none, and this file is where
/// that is established rather than asserted.</item>
/// </list>
///
/// <b>The early exit in the coaching loop has never fired.</b> It stops once
/// both Writing and Speaking have per-task detail, and Speaking cannot be
/// marked in this build — `P-02` defers the ASR decision, so
/// <c>detail[Speaking]</c> is never populated and the loop always runs to the
/// end of the list. A bound that depends on a deferred product decision is not
/// a bound.
/// </summary>
public sealed class LearnerHistoryReadCostTests
{
    /// <summary>
    /// Four times the history, the same number of reads — the coaching door,
    /// counting both its own loop and the one inside `ListMySittings`.
    /// </summary>
    [Fact]
    public async Task Coaching_marking_reads_do_not_grow_with_the_number_of_sittings()
    {
        var small = await MeasureCoachingAsync(mocks: 5);
        var large = await MeasureCoachingAsync(mocks: 20);

        Assert.Equal(small.MarkingReads, large.MarkingReads);
        Assert.Equal(small.OutboxReads, large.OutboxReads);

        // Two, and the two are named: one inside `ListMySittings` for the
        // overall band, one here for the per-task Writing detail. They stay
        // separate because the summary view does not carry markings, and
        // making it carry them would change a response shape → `W7`.
        Assert.Equal(2, large.MarkingReads);
        Assert.Equal(1, large.OutboxReads);

        // The whole profile, so the commit's before/after numbers are a
        // measurement rather than an argument. Twenty mocks of one paper:
        // one session list, one catalogue lookup, twenty score reads, and the
        // three above. Before this slice the last three were 20 + 20 + 20.
        Assert.Equal(
            new Reads(SessionLists: 1, CatalogueFinds: 1, ResultReads: 20, MarkingReads: 2, OutboxReads: 1),
            large);
    }

    /// <summary>
    /// The coaching view still reports what it reported before the reads were
    /// batched. A cheaper query that answers differently is not an
    /// optimisation.
    /// </summary>
    [Fact]
    public async Task Coaching_still_reports_the_writing_detail_of_the_latest_marked_sitting()
    {
        var h = new SittingHistoryHarness();
        for (var i = 0; i < 3; i++) await h.ThreeSkillMockAsync();

        var view = await h.CoachingAsync();

        // <b>The separator is the API's now, and no longer the machine's.</b>
        // This assertion used to read `$"Task 1 {6m:0.0} · Task 2 {7m:0.0}"` —
        // reproducing the handler's own ambient-culture formatting so that it
        // could not go red on a laptop whose locale differed. That made the two
        // agree about a defect: the handler answered "Task 1 6,0" on a
        // Vietnamese host and this line expected "Task 1 6,0" there too. The
        // handler now pins `InvariantCulture`, so the expectation is a literal.
        // → `CultureIndependentResponseTests`, `scripts/check-culture.mjs`
        var writing = Assert.Single(view.Skills, s => s.Module == "writing");
        Assert.Equal("Task 1 6.0 · Task 2 7.0", writing.Detail);

        // Speaking is never marked in this build, so it carries no detail —
        // which is why the loop's early exit above can never fire.
        var speaking = Assert.Single(view.Skills, s => s.Module == "speaking");
        Assert.Null(speaking.Detail);
    }

    /// <summary>
    /// <b>The activity heatmap, characterised rather than fixed.</b>
    ///
    /// <c>Learning/Handlers.cs:296</c> reads five hundred sittings in one call
    /// and touches no other store. This test was green the day it was written
    /// and is here to keep it that way: the handler is one `StartedAt` and one
    /// `SubmittedAt` away from being given a per-sitting lookup by someone who
    /// has not noticed the ceiling it runs at.
    /// </summary>
    [Fact]
    public async Task The_activity_heatmap_costs_one_list_read_and_nothing_per_sitting()
    {
        var small = await MeasureActivityAsync(mocks: 5);
        var large = await MeasureActivityAsync(mocks: 20);

        Assert.Equal(small, large);
        Assert.Equal(new Reads(SessionLists: 1, CatalogueFinds: 0, ResultReads: 0, MarkingReads: 0, OutboxReads: 0), large);
    }

    private static async Task<Reads> MeasureCoachingAsync(int mocks)
    {
        var h = new SittingHistoryHarness();
        for (var i = 0; i < mocks; i++) await h.ThreeSkillMockAsync();

        h.ResetCounters();
        await h.CoachingAsync();

        return Snapshot(h);
    }

    private static async Task<Reads> MeasureActivityAsync(int mocks)
    {
        var h = new SittingHistoryHarness();
        for (var i = 0; i < mocks; i++) await h.ThreeSkillMockAsync();

        h.ResetCounters();
        await h.ActivityAsync();

        return Snapshot(h);
    }

    private static Reads Snapshot(SittingHistoryHarness h) =>
        new(h.Sessions.Lists, h.Catalogue.Finds, h.Results.Reads, h.Markings.Reads, h.Outbox.Reads);

    private readonly record struct Reads(
        int SessionLists, int CatalogueFinds, int ResultReads, int MarkingReads, int OutboxReads);
}
