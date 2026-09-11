using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Importing;

namespace Vni.Ielts.Infrastructure.Tests.Persistence.Importing;

/// <summary>
/// The import outbox, against a real MongoDB.
///
/// <b>Twin of the marking outbox's own store test, deliberately.</b> Every
/// rule here is one an in-memory dictionary gets right for free and a
/// database does not: a unique insert that a second caller loses, a claim
/// that exactly one of two callers wins, a lease that expires, a lease-guarded
/// write that a former owner cannot perform. A fake under a lock would agree
/// with a two-statement claim for as long as nobody looked, and the bug that
/// leaves undetected is two paid Cambridge parses for one upload.
///
/// One fresh database per test run — same technique as
/// <c>MongoImportDraftStoreTests</c> — and indexes are created for real,
/// because the duplicate-upload test depends on the unique index actually
/// existing rather than on the store remembering to check first.
/// </summary>
public sealed class MongoImportOutboxTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static async Task<(MongoImportOutbox Outbox, MongoContext Context)> NewOutboxAsync()
    {
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_import_outbox_test_{Guid.NewGuid():n}",
        }));

        await context.EnsureIndexesAsync(default);

        return (new MongoImportOutbox(context, new SystemClock()), context);
    }

    private static ImportJob NewJob(string sourceHash = "abc", string parsePromptVersion = "k") =>
        ImportJob.New(new ExamDefinitionId("cam-16"), 1, sourceHash, parsePromptVersion, "archive-key", Now);

    /// <summary>
    /// Two workers racing for one job: exactly one may win.
    ///
    /// <b>Gated so both calls genuinely overlap.</b> Two sequential
    /// <c>await</c>s never exercise the race a two-statement claim loses —
    /// the second call would simply see the first one's write already in
    /// place. A <see cref="TaskCompletionSource"/> release is what makes both
    /// claims start from the same unclaimed state, the same technique
    /// <c>MarkingOutboxTests.Exactly_one_of_two_workers_claims_a_job</c> uses
    /// for its twin.
    /// </summary>
    [Fact]
    public async Task Only_one_worker_can_claim_a_job()
    {
        var (outbox, _) = await NewOutboxAsync();
        var job = NewJob();
        await outbox.EnqueueAsync(job, default);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<ImportJob?> WorkerAsync(string token)
        {
            await gate.Task;
            return await outbox.ClaimAsync(token, TimeSpan.FromMinutes(5), default);
        }

        var a = WorkerAsync("worker-a");
        var b = WorkerAsync("worker-b");
        gate.SetResult();

        var claims = await Task.WhenAll(a, b);

        Assert.Single(claims, c => c is not null);
    }

    /// <summary>The unique index, not the caller, is what makes a retried upload harmless.</summary>
    [Fact]
    public async Task Enqueuing_the_same_upload_twice_creates_one_job()
    {
        var (outbox, _) = await NewOutboxAsync();
        var job = NewJob();

        Assert.True(await outbox.EnqueueAsync(job, default));
        Assert.False(await outbox.EnqueueAsync(job, default));
    }

    /// <summary>A worker that died mid-import must not hold the job forever.</summary>
    [Fact]
    public async Task An_expired_lease_lets_another_worker_take_over()
    {
        var (outbox, _) = await NewOutboxAsync();
        var job = NewJob();
        await outbox.EnqueueAsync(job, default);
        await outbox.ClaimAsync("dead-worker", TimeSpan.FromMilliseconds(1), default);

        await Task.Delay(50);

        Assert.NotNull(await outbox.ClaimAsync("live-worker", TimeSpan.FromMinutes(5), default));
    }

    /// <summary>
    /// A stage already reached is money already spent. A resumed job must not
    /// walk backwards into a paid stage.
    /// </summary>
    [Fact]
    public async Task A_recorded_stage_survives_a_retry()
    {
        var (outbox, _) = await NewOutboxAsync();
        var job = NewJob();
        await outbox.EnqueueAsync(job, default);
        var claimed = await outbox.ClaimAsync("w", TimeSpan.FromMinutes(5), default);
        await outbox.AdvanceAsync(claimed!.OperationId, "w", ImportJobStage.Keying, Guid.NewGuid(), default);
        await outbox.FailAsync(claimed.OperationId, "w", "transient", default);

        var again = await outbox.FindAsync(claimed.OperationId, default);

        Assert.Equal(ImportJobStage.Keying, again!.Stage);
    }

    /// <summary>
    /// The property the claim rests on: every lease-guarded write is filtered
    /// on the caller's own lease token, so a worker whose lease was reclaimed
    /// is told rather than allowed to overwrite the new owner's progress.
    /// </summary>
    [Fact]
    public async Task A_worker_that_lost_its_lease_cannot_advance_complete_retry_or_fail_the_job()
    {
        var (outbox, _) = await NewOutboxAsync();
        var job = NewJob();
        await outbox.EnqueueAsync(job, default);

        var died = await outbox.ClaimAsync("worker-that-died", TimeSpan.FromMilliseconds(1), default);
        Assert.NotNull(died);

        await Task.Delay(50);
        var takenOver = await outbox.ClaimAsync("worker-b", TimeSpan.FromMinutes(5), default);
        Assert.NotNull(takenOver);

        // Every lease-guarded transition on the old lease is refused, not
        // silently accepted.
        Assert.False(await outbox.RenewAsync(job.OperationId, "worker-that-died", TimeSpan.FromMinutes(5), default));
        Assert.False(
            await outbox.AdvanceAsync(
                job.OperationId, "worker-that-died", ImportJobStage.Parsing, null, default));
        Assert.False(await outbox.CompleteAsync(job.OperationId, "worker-that-died", default));
        Assert.False(
            await outbox.RetryAsync(
                job.OperationId, "worker-that-died", Now.AddMinutes(10), "stale", default));
        Assert.False(await outbox.FailAsync(job.OperationId, "worker-that-died", "stale", default));

        // The new owner's lease still works.
        Assert.True(
            await outbox.AdvanceAsync(job.OperationId, "worker-b", ImportJobStage.Parsing, null, default));
    }

    /// <summary>
    /// Unlike a marking job, an import pays separately for several stages —
    /// the parse, the transcription, forty explanations. A caller (or a
    /// confused retry) that tries to move the recorded stage backwards must
    /// be refused, or a resumed worker re-enters a paid stage and buys it a
    /// second time. Asserts both the return value and a re-read, so a store
    /// that returns <c>false</c> but writes anyway would still fail this.
    /// </summary>
    [Fact]
    public async Task A_stage_cannot_be_moved_backwards()
    {
        var (outbox, _) = await NewOutboxAsync();
        var job = NewJob();
        await outbox.EnqueueAsync(job, default);
        var claimed = await outbox.ClaimAsync("w", TimeSpan.FromMinutes(5), default);

        Assert.True(
            await outbox.AdvanceAsync(claimed!.OperationId, "w", ImportJobStage.Keying, null, default));

        var rewound = await outbox.AdvanceAsync(
            claimed.OperationId, "w", ImportJobStage.Parsing, null, default);

        Assert.False(rewound);

        var again = await outbox.FindAsync(claimed.OperationId, default);
        Assert.Equal(ImportJobStage.Keying, again!.Stage);
    }

    /// <summary>
    /// A future deployment can write a stage integer this binary's
    /// <see cref="ImportJobStage"/> does not define — say, behind a rollback.
    /// Without the fix, `ClaimAsync` maps that back to `ImportJobStage.Extracting`
    /// and hands the caller a job that looks like it never started; a resumed
    /// worker then re-buys the parse it already paid for. `G-11`: no answer is
    /// safer than an invented one. The document is written directly, bypassing
    /// the store — this is what a rolled-back deployment's leftover write looks
    /// like on disk, not something <see cref="ImportJob"/> can construct.
    /// </summary>
    [Fact]
    public async Task A_stage_this_binary_does_not_recognise_is_refused_not_reset_to_extracting()
    {
        var (outbox, context) = await NewOutboxAsync();
        var job = NewJob();

        await context.ImportJobs.InsertOneAsync(new ImportJobDocument
        {
            OperationId = job.OperationId,
            DefinitionId = job.DefinitionId.Value,
            VersionNumber = job.VersionNumber,
            SourceSha256 = job.SourceSha256,
            ParsePromptVersion = job.ParsePromptVersion,
            ArchiveKey = job.ArchiveKey,
            Stage = 999, // not defined by ImportJobStage — a future stage, or corruption
            State = ImportJobState.Pending.ToString(),
            Attempts = 0,
            CreatedAt = job.CreatedAt.UtcDateTime,
            NextAttemptAt = job.CreatedAt.UtcDateTime,
        });

        var claimed = await outbox.ClaimAsync("w", TimeSpan.FromMinutes(5), default);

        // Refused, not handed back as a fresh-looking job.
        Assert.Null(claimed);

        var raw = await context.ImportJobs
            .Find(Builders<ImportJobDocument>.Filter.Eq(d => d.OperationId, job.OperationId))
            .FirstOrDefaultAsync();

        // Dead-lettered, not silently reset — and the unreadable stage
        // integer is preserved rather than overwritten with 0 (Extracting).
        Assert.Equal(ImportJobState.Failed.ToString(), raw!.State);
        Assert.Contains("999", raw.LastError);
        Assert.Equal(999, raw.Stage);
    }
}
