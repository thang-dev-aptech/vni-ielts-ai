using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Assessment;
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
