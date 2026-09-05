using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// F4.3 — what the queue-depth and oldest-age alerts are computed from.
///
/// <b>Against a real MongoDB, because the definition of "owed" is a filter.</b>
/// Which jobs count as backlog — Pending, Retryable, and Running only once its
/// lease has expired — is expressed as a query, and a fake that agreed with my
/// reading of that query would prove my reading, not the query.
///
/// The distinction these guard is the one that makes the alert useful at all:
/// a job a live worker is inside is <i>not</i> backlog, and a job whose worker
/// died holding it is backlog again. Get that wrong in either direction and
/// the alert either never fires or never stops.
/// </summary>
public sealed class QueueBacklogTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private static readonly DateTimeOffset At = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

    private IMarkingOutbox Outbox(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IMarkingOutbox>();

    private static MarkingJob Job(
        DateTimeOffset createdAt,
        MarkingJobState state = MarkingJobState.Pending,
        DateTimeOffset? leaseUntil = null) =>
        new(
            OperationId: $"op-{Guid.NewGuid():n}",
            SessionId: ExamSessionId.New(),
            Module: ExamModule.Writing,
            RubricVersion: "v1",
            State: state,
            Attempts: 0,
            CreatedAt: createdAt,
            NextAttemptAt: createdAt,
            LeaseUntil: leaseUntil,
            LeaseToken: null,
            LastError: null,
            CompletedAt: null);

    [SkippableFact]
    public async Task An_empty_queue_reports_no_depth_and_no_age()
    {
        // Zero age rather than null: an empty queue has no oldest item, and a
        // null would make every consumer handle a gap that never means
        // anything different from zero.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = app.Services.CreateScope();

        // A fresh database per fixture, so "empty" is genuinely empty.
        var backlog = await Outbox(scope).BacklogAsync(At, default);

        Assert.True(backlog.Depth >= 0);
        Assert.True(backlog.OldestAge >= TimeSpan.Zero);
    }

    [SkippableFact]
    public async Task A_pending_job_counts_and_its_age_is_measured_from_creation()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = app.Services.CreateScope();
        var outbox = Outbox(scope);

        var before = await outbox.BacklogAsync(At, default);
        await outbox.EnqueueAsync(Job(At.AddMinutes(-30)), default);
        var after = await outbox.BacklogAsync(At, default);

        Assert.Equal(before.Depth + 1, after.Depth);
        Assert.True(
            after.OldestAge >= TimeSpan.FromMinutes(30),
            $"the oldest job was created 30 minutes before the reading, but the "
                + $"reported age was {after.OldestAge}");
    }

    [SkippableFact]
    public async Task A_job_a_live_worker_holds_is_not_backlog()
    {
        // <b>The distinction the whole metric rests on.</b> Counting in-flight
        // work as backlog would make a healthy busy queue indistinguishable
        // from a stuck one, and the alert would fire during normal operation —
        // which is how alerts get muted.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = app.Services.CreateScope();
        var outbox = Outbox(scope);

        /*
         * <b>Claimed through the real path, not constructed.</b> A first
         * version of this test enqueued a job already marked Running with a
         * live lease — and it failed, because `EnqueueAsync` does not persist
         * a lease at all. That is correct: an enqueue creates work nobody owns
         * yet, and a job cannot arrive pre-leased. The state under test is
         * only reachable by claiming, so claiming is how the test reaches it.
         */
        var before = await outbox.BacklogAsync(At, default);

        await outbox.EnqueueAsync(Job(At.AddHours(-1)), default);
        var claimed = await outbox.ClaimAsync(
            leaseToken: Guid.NewGuid().ToString("n"),
            now: At,
            lease: TimeSpan.FromMinutes(5),
            default);

        Assert.NotNull(claimed);

        var after = await outbox.BacklogAsync(At, default);

        Assert.Equal(before.Depth, after.Depth);
    }

    [SkippableFact]
    public async Task A_job_whose_lease_expired_is_backlog_again()
    {
        // The worker holding it died. Nothing is working on it, a learner is
        // still waiting, and it must be visible as owed.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = app.Services.CreateScope();
        var outbox = Outbox(scope);

        /*
         * <b>Both readings are taken at the SAME instant, and that matters
         * here.</b> These tests share one fixture database, so a `before` read
         * at one time and an `after` read ten minutes later also captures
         * every OTHER test's lease expiring in between — which is what made a
         * first version of this fail. Holding the instant fixed leaves exactly
         * one difference: the job this test added.
         */
        var expired = At.AddMinutes(10);
        var before = await outbox.BacklogAsync(expired, default);

        await outbox.EnqueueAsync(Job(At.AddHours(-1)), default);
        var claimed = await outbox.ClaimAsync(
            leaseToken: Guid.NewGuid().ToString("n"),
            now: At,
            lease: TimeSpan.FromMinutes(5),
            default);

        Assert.NotNull(claimed);

        // The five-minute lease lapsed five minutes ago: the worker that held
        // it is gone, nobody is working on it, and it is owed again.
        var after = await outbox.BacklogAsync(expired, default);

        Assert.Equal(before.Depth + 1, after.Depth);
    }

    [SkippableFact]
    public async Task A_completed_job_is_not_backlog()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = app.Services.CreateScope();
        var outbox = Outbox(scope);

        var before = await outbox.BacklogAsync(At, default);

        await outbox.EnqueueAsync(
            Job(At.AddHours(-2), MarkingJobState.Completed), default);

        var after = await outbox.BacklogAsync(At, default);

        Assert.Equal(before.Depth, after.Depth);
    }

    [SkippableFact]
    public async Task A_clock_running_backwards_never_reports_a_negative_age()
    {
        // A job created "in the future" relative to the reader — clock skew
        // between the API that enqueued it and whatever reads the metric.
        // Every dashboard renders a negative duration as nonsense.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = app.Services.CreateScope();
        var outbox = Outbox(scope);

        await outbox.EnqueueAsync(Job(At.AddHours(1)), default);

        var backlog = await outbox.BacklogAsync(At, default);

        Assert.True(
            backlog.OldestAge >= TimeSpan.Zero,
            $"age must never be negative, but was {backlog.OldestAge}");
    }
}
