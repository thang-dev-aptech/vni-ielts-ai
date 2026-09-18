using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// Paging a learner's history — the half of `W5` that was left owing.
///
/// <b>The defect, stated as a number.</b> `ListForUserAsync` took a limit and
/// nothing else, so the fifty `ListMySittings` clamps to was a <i>ceiling</i>
/// rather than a first page: a learner with sixty sittings could not reach the
/// ten oldest by any means the product offered. Not by asking for more — the
/// clamp refuses. Not by scrolling — "xem thêm" re-asked the same endpoint with
/// a bigger number, which is the same first page, longer. Their own data,
/// stored, listed nowhere.
///
/// <b>And the screen could not even be honest about it.</b> A full page and the
/// last page looked identical on the wire, so the only claim the client could
/// make truthfully was "the N most recent" — said equally to someone who had
/// seen everything and someone who had seen half. `NextCursor` is what makes
/// the difference sayable.
///
/// <b>What is checked here and what is not.</b> These run against the in-memory
/// repository, which filters through <see cref="SittingCursor.Precedes"/> — the
/// same method the ordering rule is written in — so what they prove is that the
/// handler walks pages, trims correctly and stops when it should. That the
/// MongoDB filter and sort agree with that rule is a different claim and is
/// pinned against a real database in
/// <c>Vni.Ielts.Integration.Tests.SittingHistoryCursorTests</c>.
/// </summary>
public sealed class SittingHistoryCursorTests
{
    /// <summary>
    /// <b>The headline: sixty sittings, every one of them reachable.</b>
    ///
    /// Sixty is chosen to sit past <see cref="ListMySittings.MaxLimit"/> — under
    /// the old behaviour this walk would have handed back the same fifty rows
    /// for ever, and the ten oldest would not appear at any page size.
    /// </summary>
    [Fact]
    public async Task A_learner_past_the_ceiling_reaches_their_oldest_sitting()
    {
        var h = new SittingHistoryHarness();

        for (var i = 0; i < 60; i++)
            await h.PracticeSittingAsync(SittingHistoryHarness.T0.AddMinutes(i));

        var walked = await WalkAsync(h, pageSize: 20);

        Assert.Equal(60, walked.Count);
        Assert.Equal(60, walked.Select(s => s.SessionId).Distinct().Count());

        // Newest first across page boundaries, not merely within a page.
        Assert.Equal(
            walked.Select(s => s.StartedAt).OrderByDescending(at => at),
            walked.Select(s => s.StartedAt));

        // The oldest sitting is the one the ceiling used to hide.
        Assert.Equal(SittingHistoryHarness.T0, walked[^1].StartedAt);
    }

    /// <summary>
    /// <b>The reason the cursor carries two keys.</b>
    ///
    /// Five sittings sharing one instant — which is what the store can actually
    /// represent, since a BSON date is milliseconds and a Full Test opens its
    /// sections in a burst. A cursor keyed on the timestamp alone has only two
    /// options at that boundary and both are wrong: skip every sitting sharing
    /// the instant, or return them all again and page for ever.
    /// </summary>
    [Fact]
    public async Task Sittings_sharing_one_instant_are_each_returned_exactly_once()
    {
        var h = new SittingHistoryHarness();

        // No timestamp given, so every one of them starts at T0.
        for (var i = 0; i < 5; i++) await h.PracticeSittingAsync();

        var walked = await WalkAsync(h, pageSize: 2);

        Assert.Equal(5, walked.Count);
        Assert.Equal(5, walked.Select(s => s.SessionId).Distinct().Count());
        Assert.All(walked, s => Assert.Equal(SittingHistoryHarness.T0, s.StartedAt));
    }

    /// <summary>
    /// <b>A full page that is also the whole history says so.</b>
    ///
    /// This is the case the old response shape could not express, and the one
    /// the screen's wording had to hedge around: ten rows asked for, ten rows
    /// returned, nothing left. "There may be more" was the only safe thing to
    /// say, and it was wrong.
    /// </summary>
    [Fact]
    public async Task The_last_page_is_marked_as_the_last_even_when_it_is_full()
    {
        var h = new SittingHistoryHarness();
        for (var i = 0; i < 10; i++)
            await h.PracticeSittingAsync(SittingHistoryHarness.T0.AddMinutes(i));

        var page = await h.PageAsync(limit: 10);

        Assert.Equal(10, page.Sittings.Count);
        Assert.Null(page.NextCursor);
    }

    /// <summary>
    /// And the other direction, which must never be got wrong in the optimistic
    /// way: one more sitting than the page holds, so there is a next page and
    /// the response has to offer one.
    /// </summary>
    [Fact]
    public async Task A_page_with_anything_after_it_carries_a_cursor()
    {
        var h = new SittingHistoryHarness();
        for (var i = 0; i < 11; i++)
            await h.PracticeSittingAsync(SittingHistoryHarness.T0.AddMinutes(i));

        var page = await h.PageAsync(limit: 10);

        Assert.Equal(10, page.Sittings.Count);
        Assert.NotNull(page.NextCursor);

        var next = await h.PageAsync(limit: 10, after: page.NextCursor);

        Assert.Single(next.Sittings);
        Assert.Null(next.NextCursor);
    }

    /// <summary>
    /// <b>No query without a bound, including the one that peeks ahead.</b>
    ///
    /// Learning whether another page exists costs one extra document, and that
    /// `+ 1` has to land inside the clamp rather than outside it. A client
    /// asking for a million must not reach the repository with a million and
    /// one.
    /// </summary>
    [Fact]
    public async Task The_peek_ahead_stays_inside_the_ceiling()
    {
        var h = new SittingHistoryHarness();
        for (var i = 0; i < 3; i++) await h.PracticeSittingAsync();

        await h.PageAsync(limit: 1_000_000);

        Assert.Equal(ListMySittings.MaxLimit + 1, h.Sessions.LargestLimitAsked);
    }

    /// <summary>
    /// The token survives the round trip it exists for, at the precision the
    /// store keeps. Anything else is refused rather than read as a fresh start.
    /// </summary>
    [Fact]
    public void A_cursor_round_trips_and_a_forged_one_does_not()
    {
        var original = new SittingCursor(
            new DateTimeOffset(2026, 9, 18, 9, 30, 15, 250, TimeSpan.Zero),
            new ExamSessionId("2f1c9d4e6b7a48f0a1b2c3d4e5f60718"));

        var decoded = SittingCursor.Decode(original.Encode());

        Assert.Equal(original, decoded);

        // A URL-safe alphabet with no padding, so it survives a query string
        // without being re-encoded into something else.
        Assert.DoesNotContain('+', original.Encode());
        Assert.DoesNotContain('/', original.Encode());
        Assert.DoesNotContain('=', original.Encode());

        Assert.Null(SittingCursor.Decode("not-a-cursor"));
        Assert.Null(SittingCursor.Decode(Convert.ToBase64String("nocolon"u8)));
        Assert.Null(SittingCursor.Decode(Convert.ToBase64String("abc:sitting"u8)));
        Assert.Null(SittingCursor.Decode(""));
        Assert.Null(SittingCursor.Decode(null));
    }

    /// <summary>
    /// Walks every page the way a client would: first page, then the cursor it
    /// was handed, until it is handed none.
    /// </summary>
    /// <remarks>
    /// <b>Bounded, and the bound is a failure rather than a break.</b> A cursor
    /// that does not advance loops for ever, and a test that hangs reports
    /// nothing useful — this is precisely the failure being guarded against, so
    /// it has to be visible as a red assertion with a count on it.
    /// </remarks>
    private static async Task<List<SittingSummaryView>> WalkAsync(
        SittingHistoryHarness h, int pageSize)
    {
        var all = new List<SittingSummaryView>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var page = await h.PageAsync(pageSize, cursor);
            all.AddRange(page.Sittings);
            cursor = page.NextCursor;

            Assert.True(
                ++pages <= 20,
                $"The cursor stopped advancing: {pages} pages and {all.Count} rows so far.");
        }
        while (cursor is not null);

        return all;
    }
}
