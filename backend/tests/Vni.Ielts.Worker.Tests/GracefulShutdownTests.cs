using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Worker.Tests;

/// <summary>
/// F2.3 — "the loop stops claiming and lets the job in hand finish" was the
/// class docstring's claim, and it was not true: <c>PumpAsync</c> passed the
/// same <c>stopping</c> token — cancelled the instant shutdown begins — into
/// the actual marking work. No Mongo, no Docker: everything here is an
/// in-memory fake, because the property under test is which cancellation
/// token one method call receives, and a real database cannot make that
/// observable any more precisely than a fake that notices being cancelled
/// can.
/// </summary>
public sealed class GracefulShutdownTests
{
    [Fact]
    public async Task A_job_in_hand_completes_even_after_shutdown_is_requested()
    {
        var outbox = new FakeMarkingOutbox();

        var job = new MarkingJob(
            "op-shutdown-1",
            new ExamSessionId("session-1"),
            ExamModule.Reading, // short-circuits SectionMarkingRunner before it touches anything else
            "rubric-v1",
            MarkingJobState.Pending,
            Attempts: 0,
            CreatedAt: DateTimeOffset.UtcNow,
            NextAttemptAt: DateTimeOffset.UtcNow,
            LeaseUntil: null,
            LeaseToken: null,
            LastError: null,
            CompletedAt: null);

        await outbox.EnqueueAsync(job, CancellationToken.None);

        var versionId = new ExamVersionId("version-1");
        var session = ExamSession.Rehydrate(
            job.SessionId, new Vni.Ielts.Domain.Common.UserId("user-1"), versionId,
            SessionMode.Single, SessionStatus.Submitted,
            DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow, []);

        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default, 1, 1);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Fixture", ExamVariant.Academic, scoring, timing, []);

        var sessionRepo = new GatedSessionRepository(session);
        var catalogue = new StaticCatalogue(versionId, version);

        var services = new ServiceCollection();
        services.AddSingleton<IMarkingOutbox>(outbox);
        services.AddSingleton<IExamSessionRepository>(sessionRepo);
        services.AddSingleton<IExamCatalogue>(catalogue);
        services.AddSingleton<IAnswerSheetStore>(new UnusedAnswerSheetStore());
        services.AddSingleton<IRubricSource>(new UnusedRubricSource());
        services.AddSingleton<IEnumerable<ISectionEvaluator>>([]);
        services.AddSingleton<ISectionMarkingStore>(new UnusedMarkingStore());
        services.AddSingleton<ITranscriptSource>(new UnusedTranscriptSource());
        services.AddSingleton<SectionMarkingRunner>();

        await using var provider = services.BuildServiceProvider();
        var health = new WorkerHealthState();

        var worker = new MarkingWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new SystemClock(),
            health,
            NullLogger<MarkingWorker>.Instance);

        using var stopping = new CancellationTokenSource();

        await worker.StartAsync(stopping.Token);

        try
        {
            // Wait until the worker has claimed the job and entered the
            // session lookup — the exact moment "a job is in hand" means.
            var entered = await Task.WhenAny(sessionRepo.Entered.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(sessionRepo.Entered.Task, entered);

            // Shutdown, precisely while the job's own work is in flight.
            await stopping.CancelAsync();

            // Let the gated lookup proceed. If the fix regressed — the lookup
            // now receiving the (cancelled) `stopping` token instead of
            // `CancellationToken.None` — this delay throws
            // TaskCanceledException immediately instead of completing.
            await sessionRepo.ReleaseAndWaitAsync();
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var stored = outbox.Jobs[job.OperationId];

        Assert.Equal(MarkingJobState.Completed, stored.State);
        Assert.Equal(1, stored.Attempts); // one claim, no retry burned
        Assert.Null(stored.LastError);
    }

    /// <summary>
    /// Returns the seeded session, but only after the caller both observes
    /// that a lookup is in progress (<see cref="Entered"/>) and releases it
    /// (<see cref="ReleaseAndWaitAsync"/>) — the same "plant a signal instead
    /// of racing a real clock" technique <c>ICommitSignal</c> uses in
    /// <c>IdempotencyContractTests</c>.
    /// </summary>
    private sealed class GatedSessionRepository(ExamSession session) : IExamSessionRepository
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Task? _lookup;

        public Task<ExamSession?> FindAsync(ExamSessionId id, CancellationToken ct)
        {
            Entered.TrySetResult();
            _lookup = WaitThenAsync(ct);
            return _lookup.ContinueWith(_ => (ExamSession?)session, TaskScheduler.Default);
        }

        private async Task WaitThenAsync(CancellationToken ct)
        {
            // The token this receives is the whole point of the test:
            // CancellationToken.None never cancels this wait no matter what
            // happens to the worker's own `stopping` token in the meantime;
            // the pre-fix `stopping` token would cancel it the moment
            // `CancelAsync()` runs, well before `_release` is ever set.
            await _release.Task.WaitAsync(ct);
        }

        public async Task ReleaseAndWaitAsync()
        {
            _release.TrySetResult();
            if (_lookup is not null) await _lookup;
        }

        public Task<ExamSession?> FindOpenForUserAsync(Vni.Ielts.Domain.Common.UserId userId, CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<IReadOnlyList<ExamSession>> ListForUserAsync(
            Vni.Ielts.Domain.Common.UserId userId, int limit, CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task AddAsync(ExamSession newSession, CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<bool> TrySaveAsync(ExamSession toSave, SessionState from, CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by this test.");
    }

    private sealed class StaticCatalogue(ExamVersionId id, ExamVersion version) : IExamCatalogue
    {
        public Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<ExamVersion?> FindAsync(ExamVersionId requested, CancellationToken ct) =>
            Task.FromResult(requested == id ? version : null);

        public Task UpsertAsync(ExamVersion toSave, CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by this test.");
    }

    // ExamModule.Reading makes SectionMarkingRunner return [] before any of
    // these four are ever called — present only because DI needs something
    // constructible for SectionMarkingRunner's other constructor parameters.
    private sealed class UnusedAnswerSheetStore : IAnswerSheetStore
    {
        public Task<IReadOnlyDictionary<string, string?>> LoadAsync(
            ExamSessionId sessionId, ExamModule module, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AnswerSheet> ReadAsync(ExamSessionId sessionId, ExamModule module, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AnswerSheet> CloseAsync(
            ExamSessionId sessionId, ExamModule module, DateTimeOffset at, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<PatchedSheet> PatchAsync(
            ExamSessionId sessionId, ExamModule module, IReadOnlyDictionary<string, string?> changes,
            DateTimeOffset at, CancellationToken ct, IReadOnlyDictionary<string, long>? sequences = null) =>
            throw new NotSupportedException();

        public Task SetAnswerAsync(
            ExamSessionId sessionId, ExamModule module, string questionId, string? value,
            DateTimeOffset at, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedRubricSource : IRubricSource
    {
        public Rubric? For(ExamModule module) => throw new NotSupportedException();
    }

    private sealed class UnusedMarkingStore : ISectionMarkingStore
    {
        public Task SaveAsync(ExamSessionId sessionId, SectionMarking marking, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SectionMarking>> ListAsync(ExamSessionId sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedTranscriptSource : ITranscriptSource
    {
        public Task<string?> ForAsync(
            ExamSessionId sessionId, IReadOnlyList<SpeakingRecording> recordings, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    /// <summary>The marking outbox in memory. Copied from Application.Tests/Assessment/Fakes.cs
    /// (internal there, so not directly reusable) — same state machine, same reasoning.</summary>
    private sealed class FakeMarkingOutbox : IMarkingOutbox
    {
        private readonly Dictionary<string, MarkingJob> _jobs = [];

        public IReadOnlyDictionary<string, MarkingJob> Jobs => _jobs;

        /// <summary>Same definition of "owed" as the real store. → F4.3</summary>
        public Task<QueueBacklog> BacklogAsync(DateTimeOffset asOf, CancellationToken ct)
        {
            var owed = _jobs.Values
                .Where(j => j.State is MarkingJobState.Pending or MarkingJobState.Retryable
                    || (j.State is MarkingJobState.Running
                        && (j.LeaseUntil is null || j.LeaseUntil < asOf)))
                .ToArray();

            if (owed.Length == 0) return Task.FromResult(QueueBacklog.Empty);

            var age = asOf - owed.Min(j => j.CreatedAt);

            return Task.FromResult(
                new QueueBacklog(owed.Length, age > TimeSpan.Zero ? age : TimeSpan.Zero));
        }

        public Task<bool> EnqueueAsync(MarkingJob job, CancellationToken ct)
        {
            if (_jobs.ContainsKey(job.OperationId)) return Task.FromResult(false);
            _jobs[job.OperationId] = job;
            return Task.FromResult(true);
        }

        public Task<MarkingJob?> ClaimAsync(
            string leaseToken, DateTimeOffset now, TimeSpan lease, CancellationToken ct)
        {
            var due = _jobs.Values
                .Where(j => j.State is MarkingJobState.Pending or MarkingJobState.Retryable
                    or MarkingJobState.Running)
                .Where(j => j.NextAttemptAt is null || j.NextAttemptAt <= now)
                .Where(j => j.LeaseUntil is null || j.LeaseUntil < now)
                .OrderBy(j => j.CreatedAt)
                .FirstOrDefault();

            if (due is null) return Task.FromResult<MarkingJob?>(null);

            var claimed = due with
            {
                State = MarkingJobState.Running,
                LeaseToken = leaseToken,
                LeaseUntil = now.Add(lease),
                Attempts = due.Attempts + 1,
            };

            _jobs[claimed.OperationId] = claimed;
            return Task.FromResult<MarkingJob?>(claimed);
        }

        private bool Owned(string operationId, string leaseToken, Func<MarkingJob, MarkingJob> change)
        {
            if (!_jobs.TryGetValue(operationId, out var job)) return false;
            if (job.LeaseToken != leaseToken) return false;
            _jobs[operationId] = change(job);
            return true;
        }

        public Task<bool> RenewAsync(string operationId, string leaseToken, DateTimeOffset until, CancellationToken ct) =>
            Task.FromResult(Owned(operationId, leaseToken, j => j with { LeaseUntil = until }));

        public Task<bool> CompleteAsync(string operationId, string leaseToken, DateTimeOffset at, CancellationToken ct) =>
            Task.FromResult(Owned(operationId, leaseToken, j => j with
            {
                State = MarkingJobState.Completed,
                CompletedAt = at,
                LeaseToken = null,
                LeaseUntil = null,
            }));

        public Task<bool> RetryAsync(
            string operationId, string leaseToken, DateTimeOffset nextAttemptAt, string error, CancellationToken ct) =>
            Task.FromResult(Owned(operationId, leaseToken, j => j with
            {
                State = MarkingJobState.Retryable,
                NextAttemptAt = nextAttemptAt,
                LastError = error,
                LeaseToken = null,
                LeaseUntil = null,
            }));

        public Task<bool> FailAsync(string operationId, string leaseToken, string error, CancellationToken ct) =>
            Task.FromResult(Owned(operationId, leaseToken, j => j with
            {
                State = MarkingJobState.Failed,
                LastError = error,
                LeaseToken = null,
                LeaseUntil = null,
            }));

        public Task<IReadOnlyList<MarkingJob>> ListAsync(ExamSessionId sessionId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MarkingJob>>([.. _jobs.Values.Where(j => j.SessionId == sessionId)]);
    }
}
