using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests.Persistence.Exams;

public sealed class MongoMarkingOutboxQueryTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 21, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Failed_jobs_are_listed_newest_first_across_sessions()
    {
        var (outbox, _) = await NewOutboxAsync();
        var first = ExamSessionId.New();
        var second = ExamSessionId.New();

        await EnqueueAndFailAsync(outbox, first, "first");
        await Task.Delay(15);
        await EnqueueAndFailAsync(outbox, second, "second");

        var page = await outbox.QueryAsync(new MarkingJobQuery(State: MarkingJobState.Failed), default);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["second", "first"], page.Items.Select(j => j.RubricVersion));
        Assert.All(page.Items, j => Assert.Equal(MarkingJobState.Failed, j.State));
        Assert.True(page.Items[0].FailedAt >= page.Items[1].FailedAt);
    }

    [Fact]
    public async Task Reopening_is_atomic_across_concurrent_keys()
    {
        var (outbox, _) = await NewOutboxAsync();
        var session = ExamSessionId.New();
        var id = await EnqueueAndFailAsync(outbox, session, "writing-v1");

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<MarkingJobReopenResult> CallAsync(string key)
        {
            await gate.Task;
            return await outbox.ReopenFailedAsync(id, key, At.AddHours(1), default);
        }

        var a = CallAsync("key-a");
        var b = CallAsync("key-b");
        gate.SetResult();

        var results = await Task.WhenAll(a, b);

        Assert.Single(results, r => r.Status == MarkingJobReopenStatus.Reopened);
        Assert.Single(results, r => r.Status == MarkingJobReopenStatus.Conflict);

        var listed = Assert.Single(await outbox.ListAsync(session, default));
        Assert.Equal(MarkingJobState.Pending, listed.State);
        Assert.Equal(0, listed.Attempts);
        Assert.False(string.IsNullOrEmpty(listed.ReopenKey));
    }

    [Fact]
    public async Task The_same_reopen_key_replays_after_the_job_has_moved_on()
    {
        var (outbox, _) = await NewOutboxAsync();
        var session = ExamSessionId.New();
        var id = await EnqueueAndFailAsync(outbox, session, "writing-v1");

        var first = await outbox.ReopenFailedAsync(id, "retry-1", At.AddHours(1), default);
        Assert.Equal(MarkingJobReopenStatus.Reopened, first.Status);

        var claimed = await outbox.ClaimAsync("w2", At.AddHours(1), TimeSpan.FromMinutes(2), default);
        Assert.NotNull(claimed);
        await outbox.CompleteAsync(id, "w2", At.AddHours(2), default);

        var replay = await outbox.ReopenFailedAsync(id, "retry-1", At.AddHours(3), default);
        Assert.Equal(MarkingJobReopenStatus.Replayed, replay.Status);
        Assert.Equal(MarkingJobState.Completed, replay.CurrentState);

        var illegal = await outbox.ReopenFailedAsync(id, "retry-2", At.AddHours(4), default);
        Assert.Equal(MarkingJobReopenStatus.Illegal, illegal.Status);
    }

    [Fact]
    public async Task A_rejected_attempt_keeps_raw_output_and_attaches_the_superseding_version()
    {
        var context = NewContext();
        await context.EnsureIndexesAsync(default);
        var attempts = new MongoEvaluationAttemptStore(context);
        var markings = new MongoSectionMarkingStore(context, new FixedClock(At));
        var session = ExamSessionId.New();
        const string operation = "op-1";
        const string raw = """{"secret":"RAW_JSON_MUST_NOT_BE_LOGGED","criteria":{}}""";

        await attempts.RecordAsync(
            EvaluationAttempt.Capture(
                operation, session, ExamModule.Writing, 1,
                "OpenAi", "gpt-test", "req-reject",
                At, At.AddSeconds(1),
                EvaluationAttemptOutcome.Rejected, "SCHEMA_REJECTED", "schema failed",
                raw),
            default);

        var rejected = Assert.Single(await attempts.ListByOperationAsync(operation, default));
        Assert.Equal(raw, rejected.RawOutput);
        Assert.Null(rejected.MarkingId);

        await markings.SaveAsync(session, Marking("first"), default);
        await attempts.AttachLatestUnmarkedAsync(
            operation, ExamModule.Writing, 1, "first", 1, default);

        await attempts.RecordAsync(
            EvaluationAttempt.Capture(
                operation, session, ExamModule.Writing, 1,
                "OpenAi", "gpt-test", "req-ok",
                At.AddMinutes(1), At.AddMinutes(1).AddSeconds(1),
                EvaluationAttemptOutcome.Succeeded, null, null, """{"ok":true}"""),
            default);

        await markings.SaveAsync(session, Marking("second"), default);
        var current = Assert.Single(await markings.ListAsync(session, default));
        await attempts.AttachLatestUnmarkedAsync(
            operation, ExamModule.Writing, 1, current.MarkingId!, current.Version, default);

        var history = await attempts.ListByOperationAsync(operation, default);
        Assert.Equal(2, history.Count);
        Assert.Equal("second", history[0].MarkingId);
        Assert.Equal(2, history[0].MarkingVersion);
        Assert.Equal("first", history[1].MarkingId);
        Assert.Equal(1, history[1].MarkingVersion);
        Assert.Equal(raw, history[1].RawOutput);
    }

    private static async Task<(MongoMarkingOutbox Outbox, MongoContext Context)> NewOutboxAsync()
    {
        var context = NewContext();
        await context.EnsureIndexesAsync(default);
        return (new MongoMarkingOutbox(context), context);
    }

    private static MongoContext NewContext() =>
        new(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_eval_attempts_{Guid.NewGuid():n}",
        }));

    private static async Task<string> EnqueueAndFailAsync(
        MongoMarkingOutbox outbox, ExamSessionId session, string rubric)
    {
        var job = new MarkingJob(
            MarkingJob.IdFor(session, ExamModule.Writing, rubric),
            session,
            ExamModule.Writing,
            rubric,
            MarkingJobState.Pending,
            Attempts: 0,
            CreatedAt: At,
            NextAttemptAt: At,
            LeaseUntil: null,
            LeaseToken: null,
            LastError: null,
            CompletedAt: null);

        await outbox.EnqueueAsync(job, default);
        await outbox.ClaimAsync("w", At, TimeSpan.FromMinutes(2), default);
        await outbox.FailAsync(job.OperationId, "w", "refused", default);
        return job.OperationId;
    }

    private static SectionMarking Marking(string id) => new(
        ExamModule.Writing,
        "writing-v1",
        [CriterionAssessment.Create("taskResponse", BandScore.Create(6m), "feedback", ["evidence"])],
        BandScore.Create(6m),
        null,
        [],
        [],
        TaskNumber: 1,
        MarkingId: id);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
