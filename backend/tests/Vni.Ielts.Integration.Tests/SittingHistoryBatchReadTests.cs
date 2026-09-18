using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The batch reads behind the history screen, against a real MongoDB —
/// slice `W10`.
///
/// <b>An in-memory fake cannot fail the way these can.</b> A dictionary keyed
/// by session id groups correctly by construction; a `$in` over a string field
/// has to name the right field, carry the right keys, and group a flat result
/// set back by the sitting each document belongs to. Get any of that wrong and
/// the history screen hands one learner's Writing band to another sitting —
/// quietly, with the right number of round trips, and with every unit test
/// green.
///
/// So the property asserted here is not "the batch read returns something". It
/// is that the batch read returns <i>exactly</i> what the per-sitting read it
/// replaced returns, sitting by sitting, out of the same database.
/// </summary>
public sealed class SittingHistoryBatchReadTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private IServiceScope Scope() => app.Services.CreateScope();

    private static readonly DateTimeOffset At = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Batched_markings_match_the_per_sitting_read_for_every_sitting()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = scope.ServiceProvider.GetRequiredService<ISectionMarkingStore>();

        var one = ExamSessionId.New();
        var two = ExamSessionId.New();
        var unmarked = ExamSessionId.New();

        await store.SaveAsync(one, WritingTask(1, 6m), default);
        await store.SaveAsync(one, WritingTask(2, 7m), default);
        await store.SaveAsync(two, WritingTask(1, 5.5m), default);

        var batched = await store.ListManyAsync([one, two, unmarked], default);

        Assert.Equal(
            (await store.ListAsync(one, default)).Select(Key).OrderBy(k => k),
            batched[one].Select(Key).OrderBy(k => k));

        Assert.Equal(
            (await store.ListAsync(two, default)).Select(Key).OrderBy(k => k),
            batched[two].Select(Key).OrderBy(k => k));

        // Absent, not empty. A sitting nobody has marked has no entry, which
        // is the one shape both stores agreed to return.
        Assert.False(batched.ContainsKey(unmarked));

        // And the read is scoped: sitting two's single task must not have
        // arrived under sitting one's key.
        Assert.Equal(2, batched[one].Count);
        Assert.Single(batched[two]);
    }

    [SkippableFact]
    public async Task Batched_jobs_match_the_per_sitting_read_for_every_sitting()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var outbox = scope.ServiceProvider.GetRequiredService<IMarkingOutbox>();

        var one = ExamSessionId.New();
        var two = ExamSessionId.New();
        var idle = ExamSessionId.New();

        await outbox.EnqueueAsync(Owed(one, ExamModule.Writing), default);
        await outbox.EnqueueAsync(Owed(one, ExamModule.Speaking), default);
        await outbox.EnqueueAsync(Owed(two, ExamModule.Writing), default);

        var batched = await outbox.ListManyAsync([one, two, idle], default);

        Assert.Equal(
            (await outbox.ListAsync(one, default)).Select(j => j.OperationId).OrderBy(id => id),
            batched[one].Select(j => j.OperationId).OrderBy(id => id));

        Assert.Equal(
            (await outbox.ListAsync(two, default)).Select(j => j.OperationId).OrderBy(id => id),
            batched[two].Select(j => j.OperationId).OrderBy(id => id));

        Assert.False(batched.ContainsKey(idle));
    }

    /// <summary>
    /// <b>The empty request is the common one, and it must not reach the
    /// database.</b> Most learners' histories are single-skill practice, which
    /// `SittingBand.Applies` excludes entirely — so the id list is empty and
    /// the saving `W1` paid for would be given back by one `{$in: []}` round
    /// trip per screen.
    /// </summary>
    [SkippableFact]
    public async Task An_empty_request_is_answered_without_a_query()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();

        Assert.Empty(await scope.ServiceProvider
            .GetRequiredService<ISectionMarkingStore>().ListManyAsync([], default));

        Assert.Empty(await scope.ServiceProvider
            .GetRequiredService<IMarkingOutbox>().ListManyAsync([], default));
    }

    /// <summary>The key the store itself uses: one marking per module and task.</summary>
    private static string Key(SectionMarking m) => $"{m.Module}:{m.TaskNumber}:{m.Band.Value}";

    private static SectionMarking WritingTask(int number, decimal band) =>
        new(ExamModule.Writing, "ielts-writing-synthetic-v1", [], BandScore.Create(band),
            null, [], [], number);

    private static MarkingJob Owed(ExamSessionId session, ExamModule module) =>
        new(
            MarkingJob.IdFor(session, module, "writing-v1"),
            session,
            module,
            "writing-v1",
            MarkingJobState.Pending,
            Attempts: 0,
            CreatedAt: At,
            NextAttemptAt: At,
            LeaseUntil: null,
            LeaseToken: null,
            LastError: null,
            CompletedAt: null);
}

/// <summary>
/// Paging a learner's history against a real MongoDB — the other half of `W5`.
///
/// <b>The in-memory repository cannot fail the way this can.</b> It filters
/// through <see cref="SittingCursor.Precedes"/> — the rule itself — so it
/// agrees with the rule by construction. MongoDB does not: the rule has to be
/// re-expressed as a filter (<c>$lt</c> on the timestamp, <i>or</i> equal on the
/// timestamp and <c>$lt</c> on the id) and as a matching sort, and a filter that
/// disagrees with its own ordering by one row drops sittings on the page
/// boundary only, for the learners who happen to have two in the same
/// millisecond, with every unit test green.
///
/// So the properties asserted here are the ones a fake cannot hold up: that the
/// pages tile the history exactly, and that they do it when every sitting shares
/// an instant.
/// </summary>
public sealed class SittingHistoryCursorTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Paging_reaches_every_sitting_exactly_once_past_the_ceiling()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

        var learner = UserId.New();
        var written = new List<ExamSessionId>();

        // Sixty: past the fifty `ListMySittings` clamps to, so under the old
        // limit-only read the ten oldest were unreachable by any page size.
        for (var i = 0; i < 60; i++)
        {
            var id = ExamSessionId.New();
            written.Add(id);
            await sessions.AddAsync(Sitting(id, learner, T0.AddMinutes(i)), default);
        }

        var walked = await WalkAsync(sessions, learner, pageSize: 20);

        Assert.Equal(60, walked.Count);
        Assert.Equal(written.Select(id => id.Value).OrderBy(v => v), walked.OrderBy(v => v));
    }

    /// <summary>
    /// <b>Every sitting in the same millisecond — the case the second cursor key
    /// exists for.</b>
    ///
    /// Against a real store this also proves the sort is total. Without the id
    /// in the sort, MongoDB is free to return these twenty in a different order
    /// on two reads of the same data, and a cursor over an unstable order
    /// resumes after a row that is no longer there.
    /// </summary>
    [SkippableFact]
    public async Task Paging_is_exact_when_every_sitting_shares_an_instant()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

        var learner = UserId.New();
        var written = new List<ExamSessionId>();

        for (var i = 0; i < 20; i++)
        {
            var id = ExamSessionId.New();
            written.Add(id);
            await sessions.AddAsync(Sitting(id, learner, T0), default);
        }

        var walked = await WalkAsync(sessions, learner, pageSize: 3);

        Assert.Equal(20, walked.Count);
        Assert.Equal(written.Select(id => id.Value).OrderBy(v => v), walked.OrderBy(v => v));
    }

    /// <summary>
    /// A cursor names a position in one learner's ordering and grants nothing:
    /// handed another learner's cursor, the read still returns only its own
    /// user's sittings.
    /// </summary>
    [SkippableFact]
    public async Task A_cursor_does_not_reach_across_learners()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = app.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

        var mine = UserId.New();
        var theirs = UserId.New();

        await sessions.AddAsync(Sitting(ExamSessionId.New(), mine, T0), default);

        var theirSitting = ExamSessionId.New();
        await sessions.AddAsync(Sitting(theirSitting, theirs, T0.AddHours(1)), default);

        var page = await sessions.ListForUserAsync(
            mine, 10, default, new SittingCursor(T0.AddHours(1), theirSitting));

        Assert.All(page, s => Assert.Equal(mine, s.UserId));
    }

    /// <summary>
    /// The batched score read returns, sitting by sitting, what the per-sitting
    /// read it replaced returns — the same property the marking and job batches
    /// are held to, applied to the gate `W10` left open.
    /// </summary>
    [SkippableFact]
    public async Task Batched_scores_match_the_per_sitting_read_for_every_sitting()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = app.Services.CreateScope();
        var results = scope.ServiceProvider.GetRequiredService<ISectionResultStore>();

        var one = ExamSessionId.New();
        var two = ExamSessionId.New();
        var unmarked = ExamSessionId.New();

        await results.SaveAsync(one, Score(ExamModule.Reading, 7m), default);
        await results.SaveAsync(one, Score(ExamModule.Listening, 6.5m), default);
        await results.SaveAsync(two, Score(ExamModule.Reading, 5m), default);

        var batched = await results.ListManyAsync([one, two, unmarked], default);

        Assert.Equal(
            (await results.ListAsync(one, default)).Select(Key).OrderBy(k => k),
            batched[one].Select(Key).OrderBy(k => k));

        Assert.Equal(
            (await results.ListAsync(two, default)).Select(Key).OrderBy(k => k),
            batched[two].Select(Key).OrderBy(k => k));

        Assert.False(batched.ContainsKey(unmarked));
        Assert.Equal(2, batched[one].Count);
        Assert.Single(batched[two]);
    }

    /// <summary>
    /// Both of this wave's gates answer an empty request without a round trip,
    /// for the reason spelled out on the marking store's: most histories are
    /// practice, and one <c>{$in: []}</c> per screen gives back the saving.
    /// </summary>
    [SkippableFact]
    public async Task An_empty_batch_request_is_answered_without_a_query()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = app.Services.CreateScope();

        Assert.Empty(await scope.ServiceProvider
            .GetRequiredService<ISectionResultStore>().ListManyAsync([], default));

        Assert.Empty(await scope.ServiceProvider
            .GetRequiredService<IExamCatalogue>().FindManyAsync([], default));
    }

    private static async Task<List<string>> WalkAsync(
        IExamSessionRepository sessions, UserId learner, int pageSize)
    {
        var seen = new List<string>();
        SittingCursor? cursor = null;
        var pages = 0;

        while (true)
        {
            var page = await sessions.ListForUserAsync(learner, pageSize + 1, default, cursor);
            var rows = page.Take(pageSize).ToList();

            seen.AddRange(rows.Select(s => s.Id.Value));

            if (page.Count <= pageSize || rows.Count == 0) return seen;

            cursor = SittingCursor.Of(rows[^1]);

            Assert.True(
                ++pages <= 40,
                $"The cursor stopped advancing: {pages} pages and {seen.Count} rows so far.");
        }
    }

    private static ExamSession Sitting(ExamSessionId id, UserId owner, DateTimeOffset startedAt) =>
        ExamSession.Rehydrate(
            id, owner, new ExamVersionId("paging-fixture"), SessionMode.Single,
            SessionStatus.Submitted, startedAt, startedAt.AddMinutes(60),
            [SectionAttempt.Rehydrate(ExamModule.Reading, startedAt, null, startedAt.AddMinutes(60))],
            SessionTiming.OpenEnded);

    private static SectionScore Score(ExamModule module, decimal band) =>
        new(module, 30, 40, BandScore.Create(band), []);

    private static string Key(SectionScore s) => $"{s.Module}:{s.Band?.Value}";
}
