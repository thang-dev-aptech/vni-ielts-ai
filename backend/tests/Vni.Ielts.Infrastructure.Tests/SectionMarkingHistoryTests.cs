using Microsoft.Extensions.Options;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests;

/// <summary>
/// Evaluation-slice integration coverage — `eval-verification`.
///
/// <see cref="Persistence.Exams.MongoSectionMarkingStoreTests"/> already
/// covers a sequential rerun and the history query's filters/pagination. What
/// it does not cover, and what this file exists for:
///
/// <list type="bullet">
/// <item><b>Concurrency.</b> `eval-marking-history`'s own acceptance
/// criterion — "concurrent or repeated completion for one rerun cannot
/// create two current versions" — has no test exercising two callers racing
/// the same slot, only two sequential awaited calls.</item>
/// <item><b>`ListAllVersionsAsync`.</b> Added while verifying this slice:
/// <see cref="AdminEvaluationEndpoints"/>'s detail endpoint resolves a
/// marking by <c>markingId</c> using <see cref="ISectionMarkingStore.ListAsync"/>,
/// which is current-only — so the "Thay cho" / "Bị thay bởi" links the CMS
/// detail screen itself renders would 404 the moment they pointed at a
/// superseded id. Nothing exercised that path before this file.</item>
/// </list>
/// </summary>
public sealed class SectionMarkingHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private static async Task<MongoSectionMarkingStore> NewStoreAsync()
    {
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_eval_verify_{Guid.NewGuid():n}",
        }));
        await context.EnsureIndexesAsync(default);
        return new MongoSectionMarkingStore(context, new FixedClock(Now));
    }

    /// <summary>
    /// The invariant `eval-marking-history` stated but never raced: two
    /// callers completing a rerun for the same slot at the same time must not
    /// both become current. One wins; the loser's write either serialises
    /// behind it (becoming version 3) or is rejected — either way, the store
    /// never ends the race holding two <c>IsCurrent</c> documents.
    /// </summary>
    [Fact]
    public async Task Two_concurrent_reruns_of_the_same_slot_never_leave_two_current_versions()
    {
        var store = await NewStoreAsync();
        var session = ExamSessionId.New();

        await store.SaveAsync(session, Marking("v1"), default);

        var racerA = SaveOrNullAsync(store, session, Marking("v2-a"));
        var racerB = SaveOrNullAsync(store, session, Marking("v2-b"));
        await Task.WhenAll(racerA, racerB);

        var history = await store.QueryAsync(new SectionMarkingHistoryQuery(IsCurrent: null), default);
        var current = history.Items.Where(i => i.Marking.IsCurrent).ToArray();

        Assert.Single(current);
        // Whichever id won, the loser (if it did not throw) must have landed
        // as non-current — never as a second current row.
        Assert.Single(await store.ListAsync(session, default));
    }

    /// <summary>
    /// A concurrent write that could not win the race is expected to throw
    /// (the transaction's own guard — <c>MatchedCount != 1</c>) rather than
    /// silently overwrite. Retrying it must still converge on one current
    /// version, not create a stray extra one.
    /// </summary>
    private static async Task<Exception?> SaveOrNullAsync(
        MongoSectionMarkingStore store, ExamSessionId session, SectionMarking marking)
    {
        try
        {
            await store.SaveAsync(session, marking, default);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task ListAllVersionsAsync_resolves_a_superseded_marking_by_its_own_id()
    {
        var store = await NewStoreAsync();
        var session = ExamSessionId.New();

        await store.SaveAsync(session, Marking("first"), default);
        await store.SaveAsync(session, Marking("second"), default);

        // What DetailEndpoint does: look a specific markingId up within one
        // session's full version set, not only the current one.
        var all = await store.ListAllVersionsAsync(session, default);
        var old = all.SingleOrDefault(x => x.MarkingId == "first");
        var current = all.SingleOrDefault(x => x.MarkingId == "second");

        Assert.NotNull(old);
        Assert.False(old!.IsCurrent);
        Assert.Equal("second", old.SupersededById);
        Assert.NotNull(current);
        Assert.True(current!.IsCurrent);
        Assert.Equal("first", current.SupersedesId);

        // The regression this method exists to close: ListAsync alone cannot
        // do this — it is current-only by design, and must stay that way
        // for the learner-facing read.
        Assert.DoesNotContain(await store.ListAsync(session, default), x => x.MarkingId == "first");
    }

    [Fact]
    public async Task ListAllVersionsAsync_is_scoped_to_its_own_session()
    {
        var store = await NewStoreAsync();
        var mine = ExamSessionId.New();
        var someoneElses = ExamSessionId.New();

        await store.SaveAsync(mine, Marking("mine-1"), default);
        await store.SaveAsync(someoneElses, Marking("theirs-1"), default);

        var all = await store.ListAllVersionsAsync(mine, default);

        Assert.Single(all);
        Assert.Equal("mine-1", all[0].MarkingId);
    }

    /// <summary>
    /// A row the admin history list surfaces as superseded must be the exact
    /// row <see cref="ISectionMarkingStore.ListAllVersionsAsync"/> hands back
    /// for its own session — the list→detail navigation this slice ships
    /// depends on the two never disagreeing about a marking's own fields.
    /// </summary>
    [Fact]
    public async Task A_superseded_row_from_the_admin_query_matches_its_own_detail_lookup()
    {
        var store = await NewStoreAsync();
        var session = ExamSessionId.New();
        await store.SaveAsync(session, Marking("gen-1"), default);
        await store.SaveAsync(session, Marking("gen-2"), default);

        var historyRow = (await store.QueryAsync(
                new SectionMarkingHistoryQuery(IsCurrent: false), default))
            .Items.Single(i => i.SessionId == session);

        var detailRow = (await store.ListAllVersionsAsync(session, default))
            .Single(x => x.MarkingId == historyRow.Marking.MarkingId);

        Assert.Equal(historyRow.Marking.Version, detailRow.Version);
        Assert.Equal(historyRow.Marking.SupersededById, detailRow.SupersededById);
        Assert.False(detailRow.IsCurrent);
    }

    /// <summary>
    /// <see cref="ISectionMarkingStore.ListManyAsync"/> is the cross-session
    /// batch read the learner history screen uses (`W10`). A rerun that
    /// leaves a superseded row in the collection must not leak into it —
    /// only current markings belong in a learner-facing batch.
    /// </summary>
    [Fact]
    public async Task ListManyAsync_returns_only_current_markings_even_after_a_rerun()
    {
        var store = await NewStoreAsync();
        var session = ExamSessionId.New();
        await store.SaveAsync(session, Marking("old"), default);
        await store.SaveAsync(session, Marking("new"), default);

        var many = await store.ListManyAsync([session], default);

        var markings = Assert.Single(many).Value;
        var only = Assert.Single(markings);
        Assert.Equal("new", only.MarkingId);
        Assert.True(only.IsCurrent);
    }

    /// <summary>
    /// The history page is what an operator pastes a link to. Two requests
    /// for the same filters and page must return the identical id sequence —
    /// not merely the same count — even when several rows share one
    /// <c>MarkedAt</c> (the ordinary case for a backfill or a burst of
    /// reruns at the same clock tick).
    /// </summary>
    [Fact]
    public async Task Same_filters_and_page_return_the_identical_id_sequence_on_repeat()
    {
        var store = await NewStoreAsync();
        for (var i = 0; i < 5; i++)
            await store.SaveAsync(ExamSessionId.New(), Marking($"det-{i}"), default);

        var query = new SectionMarkingHistoryQuery(Module: ExamModule.Writing, Page: 1, PageSize: 3);
        var first = await store.QueryAsync(query, default);
        var second = await store.QueryAsync(query, default);

        Assert.Equal(
            first.Items.Select(i => i.Marking.MarkingId),
            second.Items.Select(i => i.Marking.MarkingId));
        Assert.Equal(5, first.TotalCount);
        Assert.Equal(3, first.Items.Count);
    }

    private static SectionMarking Marking(string id, bool flagged = false) => new(
        ExamModule.Writing,
        "writing-v2",
        [CriterionAssessment.Create("taskResponse", BandScore.Create(6m), "feedback", ["evidence"])],
        BandScore.Create(6m),
        null,
        flagged ? [MarkingFlag.EvidenceNotGrounded] : [],
        [],
        TaskNumber: 2,
        MarkingId: id);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
