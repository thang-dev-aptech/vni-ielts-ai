namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// The cost of opening the history screen, as a function of how much history
/// there is — slice `W10`.
///
/// <b>Why this is a separate proposition from `W1`.</b> `W1` asked whether the
/// history and the results screen report the same number; they do, and that
/// closed. Making them agree cost a marking read and a job read for every mock
/// in the list, which is correct and unbounded at the same time. At the `50`
/// ceiling `W5` raised the list to, a learner whose history is all mocks of
/// different papers bought about two hundred round trips for one screen.
///
/// <b>A count asserted once proves nothing about growth.</b> Twenty-two reads
/// for ten sittings is either a constant or a slope, and a single measurement
/// cannot tell which. So every test here measures the same handler twice, at
/// two sizes, and compares — a number that is equal at five sittings and at
/// twenty is not growing with them.
/// </summary>
public sealed class SittingHistoryBatchReadTests
{
    /// <summary>
    /// <b>The `W10` defect, in one assertion.</b> Four times the history, the
    /// same number of round trips to the marking store and the outbox.
    /// </summary>
    [Fact]
    public async Task Marking_and_job_reads_do_not_grow_with_the_number_of_sittings()
    {
        var small = await MeasureAsync(mocks: 5);
        var large = await MeasureAsync(mocks: 20);

        Assert.Equal(small.MarkingReads, large.MarkingReads);
        Assert.Equal(small.OutboxReads, large.OutboxReads);

        // Pinned, not merely "equal": two measurements of zero would also be
        // equal, and a history that never reads a marking cannot report a band.
        Assert.Equal(1, large.MarkingReads);
        Assert.Equal(1, large.OutboxReads);
        Assert.Equal(20, large.MarkingSessionsAsked);
        Assert.Equal(20, large.OutboxSessionsAsked);
    }

    /// <summary>
    /// <b>The two gates `W10` left open, now closed — and measured the way the
    /// one before them was.</b>
    ///
    /// This assertion used to read <c>Assert.Equal(20, large.ResultReads)</c>:
    /// a characterisation of a score read per sitting and a catalogue lookup
    /// per paper, written down so the slice that fixed it would find this test
    /// red and have to state the new number rather than inherit a sentence in
    /// a queue file. The new number is one and one.
    ///
    /// <b>Both halves are pinned, not just their equality.</b> Two
    /// measurements of twenty would also be equal to each other; what is being
    /// asserted is that the round trips are a constant and that the constant
    /// is one, while the sittings and papers those round trips <i>name</i>
    /// still grow — a batch read that quietly stopped asking about nineteen of
    /// twenty sittings would pass the first assertion and fail the second.
    /// </summary>
    [Fact]
    public async Task Score_and_paper_reads_do_not_grow_with_the_number_of_sittings()
    {
        var small = await MeasureAsync(mocks: 5);
        var large = await MeasureAsync(mocks: 20);

        Assert.Equal(small.ResultReads, large.ResultReads);
        Assert.Equal(1, large.ResultReads);
        Assert.Equal(20, large.ResultSessionsAsked);

        // One list, one catalogue lookup — every mock here is the same paper,
        // so this one was already a constant. It is kept because the batch
        // read must not lose the dedupe that made it one.
        Assert.Equal(1, large.SessionLists);
        Assert.Equal(1, large.CatalogueFinds);
    }

    /// <summary>
    /// <b>The worst case the queue named and nothing measured: every sitting a
    /// different paper.</b>
    ///
    /// A history of one paper cannot tell a batched catalogue read from a
    /// deduplicated loop — both cost one lookup. Twenty papers can: the loop
    /// costs twenty and the batch costs one. This is the case behind the
    /// «xấu nhất ở trần 50» figure, and until now the only thing standing
    /// behind that figure was arithmetic done by hand.
    /// </summary>
    [Fact]
    public async Task A_history_of_twenty_different_papers_still_costs_one_catalogue_read()
    {
        var small = await MeasureAsync(mocks: 5, papersAllDifferent: true);
        var large = await MeasureAsync(mocks: 20, papersAllDifferent: true);

        Assert.Equal(small.CatalogueFinds, large.CatalogueFinds);
        Assert.Equal(1, large.CatalogueFinds);
        Assert.Equal(20, large.CatalogueVersionsAsked);
    }

    /// <summary>
    /// <b>The whole page, as one number.</b> Five round trips: the session
    /// list, the papers, the scores, the markings, the jobs. Nothing else, and
    /// nothing per sitting.
    ///
    /// The figures this replaces, for the record: twenty-two for ten sittings
    /// before `W10`, fourteen after it, and about two hundred and one at the
    /// fifty-sitting ceiling with every paper different — a hundred and three
    /// after `W10`. Five is what a screen costs now, at any size this endpoint
    /// can return.
    /// </summary>
    [Fact]
    public async Task Opening_the_history_screen_costs_five_round_trips_whatever_its_size()
    {
        var small = await MeasureAsync(mocks: 5, papersAllDifferent: true);
        var large = await MeasureAsync(mocks: 50, papersAllDifferent: true);

        Assert.Equal(5, small.Total);
        Assert.Equal(5, large.Total);
    }

    private static async Task<Measurement> MeasureAsync(int mocks, bool papersAllDifferent = false)
    {
        var h = new SittingHistoryHarness();
        for (var i = 0; i < mocks; i++)
            await h.ThreeSkillMockAsync(paper: papersAllDifferent ? h.NewPaper() : null);

        h.ResetCounters();
        var history = await h.HistoryAsync(limit: ListMySittingsLimit);

        Assert.Equal(mocks, history.Count);

        return new Measurement(
            h.Sessions.Lists,
            h.Catalogue.Finds,
            h.Catalogue.VersionsAsked,
            h.Results.Reads,
            h.Results.SessionsAsked,
            h.Markings.Reads,
            h.Markings.SessionsAsked,
            h.Outbox.Reads,
            h.Outbox.SessionsAsked);
    }

    private const int ListMySittingsLimit = 50;

    private readonly record struct Measurement(
        int SessionLists,
        int CatalogueFinds,
        int CatalogueVersionsAsked,
        int ResultReads,
        int ResultSessionsAsked,
        int MarkingReads,
        int MarkingSessionsAsked,
        int OutboxReads,
        int OutboxSessionsAsked)
    {
        /// <summary>Every round trip one history screen costs.</summary>
        public int Total =>
            SessionLists + CatalogueFinds + ResultReads + MarkingReads + OutboxReads;
    }
}
