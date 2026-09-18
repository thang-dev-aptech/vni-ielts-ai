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
    /// <b>What `W10` did not fix, pinned so nobody has to guess.</b>
    ///
    /// The score read is still one per sitting and the catalogue lookup one per
    /// distinct paper, so the total is still linear — just with a much smaller
    /// constant. Batching <c>ISectionResultStore</c> means changing a port
    /// declared in <c>Exams/Ports.cs</c>, outside the file scope this slice was
    /// given, and the catalogue the same. Recorded here as a measurement rather
    /// than as a sentence in a queue file: the slice that fixes it will find
    /// this test red and will have to say what the new number is.
    /// </summary>
    [Fact]
    public async Task Score_reads_are_still_one_per_sitting()
    {
        var small = await MeasureAsync(mocks: 5);
        var large = await MeasureAsync(mocks: 20);

        Assert.Equal(5, small.ResultReads);
        Assert.Equal(20, large.ResultReads);

        // One list, one catalogue lookup — every mock here is the same paper.
        Assert.Equal(1, large.SessionLists);
        Assert.Equal(1, large.CatalogueFinds);
    }

    private static async Task<Measurement> MeasureAsync(int mocks)
    {
        var h = new SittingHistoryHarness();
        for (var i = 0; i < mocks; i++) await h.ThreeSkillMockAsync();

        h.ResetCounters();
        var history = await h.HistoryAsync(limit: ListMySittingsLimit);

        Assert.Equal(mocks, history.Count);

        return new Measurement(
            h.Sessions.Lists,
            h.Catalogue.Finds,
            h.Results.Reads,
            h.Markings.Reads,
            h.Markings.SessionsAsked,
            h.Outbox.Reads,
            h.Outbox.SessionsAsked);
    }

    private const int ListMySittingsLimit = 50;

    private readonly record struct Measurement(
        int SessionLists,
        int CatalogueFinds,
        int ResultReads,
        int MarkingReads,
        int MarkingSessionsAsked,
        int OutboxReads,
        int OutboxSessionsAsked);
}
