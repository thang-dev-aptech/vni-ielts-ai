using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Tests.Assessment;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// How much history one request may return — slice `W5`.
///
/// <b>`/students/progress` cut the list at ten and said nothing.</b> The
/// blueprint (§ 03) asks for the full history; the screen sliced the first ten
/// rows off an already-truncated response and drew them as if that were all
/// there was. Raising the server's ceiling is half the fix — the other half is
/// the screen admitting how many it is showing, which lives in
/// `apps/web/src/__tests__/progress-history.test.tsx`.
///
/// <b>What is deliberately not here: the cursor.</b> It was missing entirely
/// when this file was written, which made the ceiling below a wall — and it
/// arrived on 18/09/2026, which makes the ceiling a page size. Paging is
/// measured next door in <see cref="SittingHistoryCursorTests"/>; what stays
/// here is the other property, that no caller can talk the repository into an
/// unbounded read however it spells the parameter.
/// </summary>
public sealed class SittingHistoryLimitTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Learner = UserId.New();

    /// <summary>
    /// The number `W5` is measured against. Thirty sittings, one request, all
    /// thirty back — under the old ceiling of twenty this returned twenty and
    /// the ten oldest were unreachable by any means.
    /// </summary>
    [Fact]
    public async Task Thirty_sittings_all_come_back_in_one_request()
    {
        var h = await HarnessWith(30);

        var history = await h.HandleAsync(new ListMySittingsQuery(Learner, 30), default);

        Assert.Equal(30, history.Count);
    }

    /// <summary>
    /// <b>No query without a bound.</b> The clamp is on the handler, not on
    /// the endpoint's parameter parsing, so it holds for every caller — a
    /// client that asks for a million gets the ceiling, and the repository is
    /// never handed a million.
    /// </summary>
    [Fact]
    public async Task A_caller_asking_for_more_than_the_ceiling_gets_the_ceiling()
    {
        var h = await HarnessWith(ListMySittings.MaxLimit + 20);

        var history = await h.HandleAsync(new ListMySittingsQuery(Learner, 1_000_000), default);

        Assert.Equal(ListMySittings.MaxLimit, history.Count);
    }

    /// <summary>
    /// And the floor, for the same reason in the other direction: a zero or a
    /// negative is a bad parameter, not an instruction to return nothing or to
    /// make the repository interpret a negative take.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_caller_asking_for_nothing_gets_one_row_rather_than_an_odd_query(int asked)
    {
        var h = await HarnessWith(3);

        var history = await h.HandleAsync(new ListMySittingsQuery(Learner, asked), default);

        Assert.Single(history);
    }

    /// <summary>
    /// The newest first, because a truncated list is only honest if the rows
    /// it keeps are the ones the screen claims to be showing — "the N most
    /// recent".
    /// </summary>
    [Fact]
    public async Task The_rows_kept_are_the_most_recent_ones()
    {
        var h = await HarnessWith(12);

        var history = await h.HandleAsync(new ListMySittingsQuery(Learner, 5), default);

        Assert.Equal(5, history.Count);
        Assert.Equal(
            history.Select(s => s.StartedAt).OrderByDescending(at => at),
            history.Select(s => s.StartedAt));
        Assert.Equal(T0.AddMinutes(11), history[0].StartedAt);
    }

    // ── Harness ───────────────────────────────────────────────────────────

    private static async Task<ListMySittings> HarnessWith(int sittings)
    {
        var version = ReadingOnlyVersion();
        var sessions = new FakeSessionRepository();
        var results = new FakeSectionResultStore();

        for (var i = 0; i < sittings; i++)
        {
            var id = ExamSessionId.New();
            var startedAt = T0.AddMinutes(i);

            await sessions.AddAsync(
                ExamSession.Rehydrate(
                    id, Learner, version.Id, SessionMode.Single, SessionStatus.Submitted,
                    startedAt, startedAt.AddMinutes(60),
                    [SectionAttempt.Rehydrate(ExamModule.Reading, startedAt, null, startedAt)],
                    SessionTiming.OpenEnded),
                default);

            await results.SaveAsync(
                id, new SectionScore(ExamModule.Reading, 30, 40, BandScore.Create(6m), []), default);
        }

        // Single-skill practice, so `SittingBand.Applies` is false for every
        // row and the marking store and outbox are never reached. Nothing here
        // is about bands; mixing mocks in would only make the fixture slower.
        return new ListMySittings(
            new FakeExamCatalogue(version), sessions, results,
            new FakeMarkingStore(), new FakeMarkingOutbox());
    }

    private static ExamVersion ReadingOnlyVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default, 1m, 2m);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);

        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Reading only", ExamVariant.Academic, scoring, timing,
            [
                new Section(ExamModule.Reading, 1,
                [
                    new SectionPart(
                        1, "passage", "Passage 1", "Body.", null, null, null, null, null, null, null,
                        [new Question("r-1", 1, QuestionType.Completion, "Q", [], null, null)]),
                ]),
            ]);

        version.Publish(T0.AddDays(-1));
        return version;
    }
}
