using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Importing;

public sealed class ImportJobTests
{
    /// <summary>
    /// Re-uploading the same bytes for the same version must not buy a second
    /// parse. The id is derived from what the work IS, never generated, so a
    /// retried upload collides with the job already running.
    /// </summary>
    [Fact]
    public void The_operation_id_is_derived_from_the_work_not_generated()
    {
        var a = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "abc123", "exam-import/1");
        var b = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "abc123", "exam-import/1");

        Assert.Equal(a, b);
    }

    [Fact]
    public void A_different_upload_for_the_same_version_is_a_different_job()
    {
        var a = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "abc123", "exam-import/1");
        var b = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "def456", "exam-import/1");

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// An improved parse prompt is a genuinely different piece of work, not a
    /// retry of the old one. Without the prompt version in the id, a package
    /// re-uploaded byte-for-byte after the prompt changed would collide with
    /// the job already keyed to the old prompt, and nothing could force the
    /// re-parse — the same failure this repository already lived once with a
    /// rubric version that was never carried into a re-mark.
    /// </summary>
    [Fact]
    public void A_new_parse_prompt_version_over_identical_bytes_is_a_different_job()
    {
        var a = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "abc123", "exam-import/3");
        var b = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "abc123", "exam-import/4");

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// A stage is how far the money went. Restarting a job that already paid
    /// for a parse must not pay again, so the stage has to be ordered and
    /// recorded, not inferred from what happens to be on the draft.
    /// </summary>
    [Fact]
    public void Stages_advance_in_one_direction()
    {
        Assert.True(ImportJobStage.Extracting < ImportJobStage.Parsing);
        Assert.True(ImportJobStage.Parsing < ImportJobStage.Transcribing);
        Assert.True(ImportJobStage.Transcribing < ImportJobStage.Keying);
        Assert.True(ImportJobStage.Keying < ImportJobStage.Checking);
        Assert.True(ImportJobStage.Checking < ImportJobStage.Explaining);
        Assert.True(ImportJobStage.Explaining < ImportJobStage.Done);
    }

    [Fact]
    public void A_job_out_of_attempts_is_failed_rather_than_retried_forever()
    {
        var job = ImportJob.New(
            new ExamDefinitionId("cam-16"), 1, "abc123", "exam-import/1", "uploads/abc123.zip",
            DateTimeOffset.UtcNow);

        var exhausted = job with { Attempts = ImportJob.MaxAttempts };

        Assert.False(exhausted.MayRetry);
    }

    /// <summary>
    /// The property <see cref="IImportOutbox.RenewAsync"/>'s bool exists to
    /// prove: once another worker has reclaimed a job, the worker that lost
    /// it can no longer act on it under its old lease. Without that signal a
    /// worker whose lease has already expired carries on believing it still
    /// owns the job, and a Cambridge parse runs twice against one draft.
    ///
    /// This drives <see cref="IImportOutbox"/> against a minimal in-memory
    /// double built for this test alone — Task 1 defines the contract, not an
    /// implementation, so there is nothing else yet to drive it against.
    /// </summary>
    [Fact]
    public async Task A_worker_whose_lease_was_reclaimed_learns_it_from_the_return_value()
    {
        var outbox = new InMemoryImportOutbox();
        var job = ImportJob.New(
            new ExamDefinitionId("cam-16"), 1, "abc123", "exam-import/1", "uploads/abc123.zip",
            outbox.Now);
        await outbox.EnqueueAsync(job, CancellationToken.None);

        // Worker A claims it, but with a lease that is already in the past —
        // standing in for A having gone silent before its first renewal.
        var claimedByA = await outbox.ClaimAsync(
            "worker-a", TimeSpan.FromSeconds(-1), CancellationToken.None);
        Assert.NotNull(claimedByA);

        // Worker B claims the same job once A's lease is due.
        var claimedByB = await outbox.ClaimAsync(
            "worker-b", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(claimedByB);

        // A, unaware it lost the job, tries to renew under its old token.
        var stillOwnedByA = await outbox.RenewAsync(
            job.OperationId, "worker-a", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.False(stillOwnedByA);
    }

    /// <summary>
    /// Minimal in-memory <see cref="IImportOutbox"/> for
    /// <see cref="A_worker_whose_lease_was_reclaimed_learns_it_from_the_return_value"/>
    /// alone. It exists to make the interface's own contract — an operation
    /// only succeeds under the lease token that currently owns the job —
    /// something a test can drive; it is not the worker-facing implementation
    /// Tasks 2 and 3 build.
    /// </summary>
    private sealed class InMemoryImportOutbox : IImportOutbox
    {
        public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

        private readonly Dictionary<string, ImportJob> _jobs = [];

        public Task<bool> EnqueueAsync(ImportJob job, CancellationToken ct)
        {
            if (_jobs.ContainsKey(job.OperationId)) return Task.FromResult(false);
            _jobs[job.OperationId] = job;
            return Task.FromResult(true);
        }

        public Task<ImportJob?> ClaimAsync(string leaseToken, TimeSpan lease, CancellationToken ct)
        {
            var due = _jobs.Values
                .Where(j => j.State is ImportJobState.Pending
                    or ImportJobState.Retryable or ImportJobState.Running)
                .Where(j => j.NextAttemptAt is null || j.NextAttemptAt <= Now)
                .Where(j => j.LeaseUntil is null || j.LeaseUntil < Now)
                .OrderBy(j => j.CreatedAt)
                .FirstOrDefault();

            if (due is null) return Task.FromResult<ImportJob?>(null);

            var claimed = due with
            {
                State = ImportJobState.Running,
                LeaseToken = leaseToken,
                LeaseUntil = Now.Add(lease),
                Attempts = due.Attempts + 1,
            };

            _jobs[claimed.OperationId] = claimed;
            return Task.FromResult<ImportJob?>(claimed);
        }

        private bool Owned(string operationId, string leaseToken, Func<ImportJob, ImportJob> change)
        {
            if (!_jobs.TryGetValue(operationId, out var job)) return false;
            if (job.LeaseToken != leaseToken) return false;

            _jobs[operationId] = change(job);
            return true;
        }

        public Task<bool> RenewAsync(
            string operationId, string leaseToken, TimeSpan lease, CancellationToken ct) =>
            Task.FromResult(Owned(operationId, leaseToken, j => j with { LeaseUntil = Now.Add(lease) }));

        public Task<bool> AdvanceAsync(
            string operationId, string leaseToken, ImportJobStage stage, Guid? draftId,
            CancellationToken ct) =>
            Task.FromResult(Owned(operationId, leaseToken,
                j => j with { Stage = stage, DraftId = draftId ?? j.DraftId }));

        public Task<bool> CompleteAsync(string operationId, string leaseToken, CancellationToken ct) =>
            Task.FromResult(Owned(operationId, leaseToken, j => j with
            {
                State = ImportJobState.Completed,
                CompletedAt = Now,
                LeaseToken = null,
                LeaseUntil = null,
            }));

        public Task<bool> RetryAsync(
            string operationId, string leaseToken, DateTimeOffset nextAttemptAt, string error,
            CancellationToken ct) =>
            Task.FromResult(Owned(operationId, leaseToken, j => j with
            {
                State = ImportJobState.Retryable,
                NextAttemptAt = nextAttemptAt,
                LastError = error,
                LeaseToken = null,
                LeaseUntil = null,
            }));

        public Task<bool> FailAsync(
            string operationId, string leaseToken, string error, CancellationToken ct) =>
            Task.FromResult(Owned(operationId, leaseToken, j => j with
            {
                State = ImportJobState.Failed,
                LastError = error,
                LeaseToken = null,
                LeaseUntil = null,
            }));

        public Task<ImportJob?> FindAsync(string operationId, CancellationToken ct) =>
            Task.FromResult(_jobs.GetValueOrDefault(operationId));
    }
}
