using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The marking outbox, against a real MongoDB.
///
/// <b>Every rule here is one an in-memory dictionary gets right for free and a
/// database does not.</b> A unique insert that a second caller loses, a claim
/// that exactly one of two workers wins, a lease that expires. A fake under a
/// lock produces a single winner however the code is ordered, so a suite built
/// on one would agree with the bug for as long as it existed — and the bug this
/// queue can have is two paid provider calls for one essay.
/// </summary>
public sealed class MarkingOutboxTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private IServiceScope Scope() => app.Services.CreateScope();

    private static IMarkingOutbox OutboxIn(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IMarkingOutbox>();

    private static readonly DateTimeOffset At = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);


    /// <summary>
    /// Empties the queue before each case.
    ///
    /// <b>The claim is global by design — a worker takes whatever is due — and
    /// these cases share one database.</b> `IClassFixture` gives this class its
    /// own, so nothing outside it is affected; what is left is one test's job
    /// being picked up by the next test's claim, which would make every
    /// assertion here about whichever case ran before it.
    ///
    /// Emptying is honest about that rather than pretending the claim is
    /// scoped. A helper that drained past foreign jobs would be modelling a
    /// queue this product does not have.
    /// </summary>
    private async Task PurgeAsync()
    {
        var db = app.Services.GetRequiredService<MongoDB.Driver.IMongoDatabase>();
        await db.DropCollectionAsync("marking_jobs");
    }

    private static MarkingJob Owed(ExamSessionId session, ExamModule module = ExamModule.Writing) =>
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

    [SkippableFact]
    public async Task Closing_the_same_section_twice_leaves_one_job()
    {
        /*
         * A retried submit, two tabs on "Tiếp theo", the expiry sweep meeting a
         * learner's own submit — every one re-closes the same section. Two jobs
         * would be two evaluations for one essay, and the second band would
         * silently replace a band the learner may already have seen.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        await PurgeAsync();

        using var scope = Scope();
        var outbox = OutboxIn(scope);
        var session = ExamSessionId.New();

        Assert.True(await outbox.EnqueueAsync(Owed(session), default));
        Assert.False(await outbox.EnqueueAsync(Owed(session), default));

        Assert.Single(await outbox.ListAsync(session, default));
    }

    [SkippableFact]
    public async Task Exactly_one_of_two_workers_claims_a_job()
    {
        // The property the whole queue rests on. Read-then-write would pass
        // single-threaded and buy two evaluations the first time two workers
        // polled together.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        await PurgeAsync();

        using var scope = Scope();
        var outbox = OutboxIn(scope);
        var session = ExamSessionId.New();

        await outbox.EnqueueAsync(Owed(session), default);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<MarkingJob?> WorkerAsync(string token)
        {
            await gate.Task;
            return await outbox.ClaimAsync(token, At, TimeSpan.FromMinutes(2), default);
        }

        var a = WorkerAsync("worker-a");
        var b = WorkerAsync("worker-b");
        gate.SetResult();

        var claims = await Task.WhenAll(a, b);

        Assert.Single(claims, c => c is not null);
    }

    [SkippableFact]
    public async Task A_job_whose_worker_died_is_taken_over_once_its_lease_expires()
    {
        /*
         * <b>This is the crash recovery the whole outbox exists for.</b> A
         * process that dies mid-marking leaves a job claimed and nobody
         * working on it. Without a lease that section is owed a band for ever,
         * silently — which is exactly the failure the outbox replaced, arriving
         * one layer further in.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        await PurgeAsync();

        using var scope = Scope();
        var outbox = OutboxIn(scope);
        var session = ExamSessionId.New();

        await outbox.EnqueueAsync(Owed(session), default);

        var died = await outbox.ClaimAsync("worker-that-died", At, TimeSpan.FromMinutes(2), default);
        Assert.NotNull(died);

        // Still inside the lease: nobody else may have it.
        Assert.Null(await outbox.ClaimAsync("worker-b", At.AddMinutes(1), TimeSpan.FromMinutes(2), default));

        // Past it: the work is owed again, and this time it is picked up.
        var recovered = await outbox.ClaimAsync("worker-b", At.AddMinutes(3), TimeSpan.FromMinutes(2), default);

        Assert.NotNull(recovered);
        Assert.Equal(2, recovered!.Attempts);
    }

    [SkippableFact]
    public async Task A_worker_that_lost_its_lease_cannot_complete_the_job()
    {
        /*
         * A handler that comes back from the dead must not overwrite the answer
         * of the worker that replaced it. Same rule, same reason, as the
         * idempotency guard's token: two owners of one operation would hand two
         * callers two different outcomes.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        await PurgeAsync();

        using var scope = Scope();
        var outbox = OutboxIn(scope);
        var session = ExamSessionId.New();

        await outbox.EnqueueAsync(Owed(session), default);
        await outbox.ClaimAsync("worker-that-died", At, TimeSpan.FromMinutes(2), default);
        await outbox.ClaimAsync("worker-b", At.AddMinutes(3), TimeSpan.FromMinutes(2), default);

        Assert.False(
            await outbox.CompleteAsync(
                MarkingJob.IdFor(session, ExamModule.Writing, "writing-v1"),
                "worker-that-died", At.AddMinutes(4), default));

        var job = (await outbox.ListAsync(session, default)).Single();
        Assert.NotEqual(MarkingJobState.Completed, job.State);
    }

    [SkippableFact]
    public async Task A_retry_is_not_claimable_until_its_backoff_has_elapsed()
    {
        // Without this the backoff is decoration: a worker polling every five
        // seconds would re-claim a failing job five seconds later, and a
        // provider outage becomes a tight loop against something already down.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        await PurgeAsync();

        using var scope = Scope();
        var outbox = OutboxIn(scope);
        var session = ExamSessionId.New();
        var id = MarkingJob.IdFor(session, ExamModule.Writing, "writing-v1");

        await outbox.EnqueueAsync(Owed(session), default);
        await outbox.ClaimAsync("worker-a", At, TimeSpan.FromMinutes(2), default);

        await outbox.RetryAsync(id, "worker-a", At.AddMinutes(10), "timeout", default);

        Assert.Null(await outbox.ClaimAsync("worker-b", At.AddMinutes(5), TimeSpan.FromMinutes(2), default));

        Assert.NotNull(await outbox.ClaimAsync("worker-b", At.AddMinutes(11), TimeSpan.FromMinutes(2), default));
    }

    [SkippableFact]
    public async Task A_failed_job_stays_failed_and_keeps_its_reason()
    {
        /*
         * <b>Dead-lettered into the database, not into a log.</b> A learner
         * looking at a dash is owed the difference between "no evaluator is
         * wired" and "we tried five times and stopped", and a results screen
         * cannot read a log line.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        await PurgeAsync();

        using var scope = Scope();
        var outbox = OutboxIn(scope);
        var session = ExamSessionId.New();
        var id = MarkingJob.IdFor(session, ExamModule.Writing, "writing-v1");

        await outbox.EnqueueAsync(Owed(session), default);
        await outbox.ClaimAsync("worker-a", At, TimeSpan.FromMinutes(2), default);
        await outbox.FailAsync(id, "worker-a", "the provider refused five times", default);

        var job = (await outbox.ListAsync(session, default)).Single();

        Assert.Equal(MarkingJobState.Failed, job.State);
        Assert.Contains("refused", job.LastError);

        // And it is not picked up again. Giving up has to mean giving up, or
        // the budget that bounds the bill means nothing.
        Assert.Null(await outbox.ClaimAsync("worker-b", At.AddHours(1), TimeSpan.FromMinutes(2), default));
    }

    [SkippableFact]
    public async Task A_completed_job_is_never_claimed_again()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        await PurgeAsync();

        using var scope = Scope();
        var outbox = OutboxIn(scope);
        var session = ExamSessionId.New();
        var id = MarkingJob.IdFor(session, ExamModule.Writing, "writing-v1");

        await outbox.EnqueueAsync(Owed(session), default);
        await outbox.ClaimAsync("worker-a", At, TimeSpan.FromMinutes(2), default);
        Assert.True(await outbox.CompleteAsync(id, "worker-a", At.AddMinutes(1), default));

        Assert.Null(await outbox.ClaimAsync("worker-b", At.AddHours(1), TimeSpan.FromMinutes(2), default));
    }

    [SkippableFact]
    public async Task A_new_rubric_version_is_a_new_job()
    {
        /*
         * The operation id carries the rubric version deliberately. Pinning it
         * to the sitting alone would make a corrected rubric silently
         * unmarkable — the enqueue would be refused as a duplicate, and the
         * learner would keep the band produced under the wrong standard.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        await PurgeAsync();

        using var scope = Scope();
        var outbox = OutboxIn(scope);
        var session = ExamSessionId.New();

        await outbox.EnqueueAsync(Owed(session), default);

        var corrected = Owed(session) with
        {
            OperationId = MarkingJob.IdFor(session, ExamModule.Writing, "writing-v2"),
            RubricVersion = "writing-v2",
        };

        Assert.True(await outbox.EnqueueAsync(corrected, default));
        Assert.Equal(2, (await outbox.ListAsync(session, default)).Count);
    }
}
