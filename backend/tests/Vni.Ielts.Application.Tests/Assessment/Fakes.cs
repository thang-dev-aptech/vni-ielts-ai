using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Tests.Assessment;

/// <summary>Answer sheets held in memory, keyed the way the real store keys them.</summary>
internal sealed class FakeAnswerSheetStore(
    Dictionary<ExamModule, Dictionary<string, string?>>? seeded = null) : IAnswerSheetStore
{
    private readonly Dictionary<ExamModule, Dictionary<string, string?>> _sheets = seeded ?? [];

    public Task<IReadOnlyDictionary<string, string?>> LoadAsync(
        ExamSessionId sessionId, ExamModule module, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, string?>>(
            _sheets.TryGetValue(module, out var sheet) ? sheet : new Dictionary<string, string?>());

    private readonly Dictionary<ExamModule, int> _revisions = [];

    public Task<AnswerSheet> ReadAsync(
        ExamSessionId sessionId, ExamModule module, CancellationToken ct) =>
        Task.FromResult(
            _sheets.TryGetValue(module, out var sheet)
                ? new AnswerSheet(sheet, _revisions.GetValueOrDefault(module))
                : AnswerSheet.Empty);

    /// <summary>
    /// <b>The patch is modelled, not skipped.</b> A fake that replaced the
    /// sheet would make a caller sending whole sheets look correct — which is
    /// the bug this port was reshaped to remove, so the fake that hides it is
    /// worse than no fake at all.
    ///
    /// The empty-patch no-op is modelled too, because a revision that moves
    /// when nothing changed tells every other reader it is behind.
    /// </summary>
    private readonly HashSet<ExamModule> _closed = [];

    /// <summary>
    /// <b>The refusal is modelled, because it is the property under test.</b>
    /// A fake that took a write after the freeze would let every barrier test
    /// pass while the protocol it is checking did not exist.
    /// </summary>
    public Task<AnswerSheet> CloseAsync(
        ExamSessionId sessionId, ExamModule module, DateTimeOffset at, CancellationToken ct)
    {
        _closed.Add(module);

        var frozen = _sheets.TryGetValue(module, out var sheet)
            ? new Dictionary<string, string?>(sheet) : [];

        return Task.FromResult(
            new AnswerSheet(frozen, _revisions.GetValueOrDefault(module)));
    }

    private readonly Dictionary<ExamModule, Dictionary<string, long>> _sequences = [];

    public Task<PatchedSheet> PatchAsync(
        ExamSessionId sessionId, ExamModule module,
        IReadOnlyDictionary<string, string?> changes,
        DateTimeOffset at, CancellationToken ct,
        IReadOnlyDictionary<string, long>? sequences = null)
    {
        if (_closed.Contains(module)) throw new SectionSheetClosedException(module);

        var previous = _revisions.GetValueOrDefault(module);

        if (changes.Count == 0)
        {
            var unchanged = _sheets.TryGetValue(module, out var held)
                ? new Dictionary<string, string?>(held) : [];
            return Task.FromResult(
                new PatchedSheet(new AnswerSheet(unchanged, previous), previous));
        }

        if (!_sheets.TryGetValue(module, out var sheet)) _sheets[module] = sheet = [];
        if (!_sequences.TryGetValue(module, out var seen)) _sequences[module] = seen = [];

        /*
         * <b>The ordering rule is modelled, not skipped.</b> A fake that took
         * every write would let a caller that has stopped sending tokens look
         * correct — and the whole point of the token is that the write which
         * arrives last is not necessarily the edit that came last.
         */
        foreach (var change in changes)
        {
            if (sequences is not null && sequences.TryGetValue(change.Key, out var seq))
            {
                if (seq <= seen.GetValueOrDefault(change.Key, -1)) continue;
                seen[change.Key] = seq;
            }

            sheet[change.Key] = change.Value;
        }

        _revisions[module] = previous + 1;

        return Task.FromResult(
            new PatchedSheet(
                new AnswerSheet(new Dictionary<string, string?>(sheet), previous + 1), previous));
    }

    public Task SetAnswerAsync(
        ExamSessionId sessionId, ExamModule module, string questionId, string? value,
        DateTimeOffset at, CancellationToken ct)
    {
        /*
         * <b>Speaking's write path is behind the same barrier, and the fake has
         * to say so.</b> A recording is filed through here rather than through
         * a patch, so a fake that took this write after a freeze would let the
         * Speaking barrier test pass while the guard it checks did not exist —
         * which is the same shape of hole the real store had before ADR-0015.
         */
        if (_closed.Contains(module)) throw new SectionSheetClosedException(module);

        if (!_sheets.TryGetValue(module, out var sheet)) _sheets[module] = sheet = [];
        sheet[questionId] = value;
        _revisions[module] = _revisions.GetValueOrDefault(module) + 1;
        return Task.CompletedTask;
    }
}

internal sealed class FakeMarkingStore : ISectionMarkingStore
{
    public List<SectionMarking> Saved { get; } = [];

    public Task SaveAsync(ExamSessionId sessionId, SectionMarking marking, CancellationToken ct)
    {
        Saved.Add(marking);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SectionMarking>> ListAsync(
        ExamSessionId sessionId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SectionMarking>>(Saved);
}

internal sealed class FakeRubricSource(params Rubric[] rubrics) : IRubricSource
{
    public Rubric? For(ExamModule module) => rubrics.FirstOrDefault(r => r.Module == module);
}

/// <summary>
/// A transcript that exists. The real one returns null — no ASR is selected.
///
/// It records what it was handed, because the thing worth asserting is that
/// the recordings reach it at all: for most of this pipeline's life they did
/// not, and nothing failed loudly enough to notice.
/// </summary>
internal sealed class FakeTranscriptSource(string? transcript) : ITranscriptSource
{
    public List<IReadOnlyList<SpeakingRecording>> Requests { get; } = [];

    public Task<string?> ForAsync(
        ExamSessionId sessionId, IReadOnlyList<SpeakingRecording> recordings, CancellationToken ct)
    {
        Requests.Add(recordings);
        return Task.FromResult(transcript);
    }
}

/// <summary>
/// An evaluator that answers with whatever the test tells it to.
///
/// <b>It records what it was asked.</b> Several of these tests are about
/// something <i>not</i> happening — no essay reaching a provider when the
/// rubric is missing, no second call after a refusal — and the only way to
/// assert that is to count the calls.
/// </summary>
internal sealed class StubEvaluator(
    ExamModule module, Func<EvaluationRequest, ClaimedEvaluation> respond) : ISectionEvaluator
{
    public ExamModule Module { get; } = module;

    public bool IsConfigured => true;

    public List<EvaluationRequest> Requests { get; } = [];

    public Task<ClaimedEvaluation> EvaluateAsync(EvaluationRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}

/// <summary>
/// The marking outbox in memory, with the state machine modelled.
///
/// <b>The claim is modelled as a claim.</b> A fake that handed out the same job
/// to two callers would let a test assert "the worker ran it" while the
/// property that matters — <i>exactly one</i> worker runs it, because each run
/// is a paid provider call — went unchecked.
/// </summary>
internal sealed class FakeMarkingOutbox : IMarkingOutbox
{
    private readonly Dictionary<string, MarkingJob> _jobs = [];

    public IReadOnlyDictionary<string, MarkingJob> Jobs => _jobs;

    /// <summary>
    /// The same definition of "owed" the real store uses: Pending, Retryable,
    /// or Running with an expired lease. Implemented rather than stubbed to
    /// zero — a fake that disagrees with the contract is how a test suite
    /// certifies behaviour the real implementation does not have. → F3.1
    /// </summary>
    public Task<QueueBacklog> BacklogAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        var owed = _jobs.Values
            .Where(j => j.State is MarkingJobState.Pending or MarkingJobState.Retryable
                || (j.State is MarkingJobState.Running && (j.LeaseUntil is null || j.LeaseUntil < asOf)))
            .ToArray();

        if (owed.Length == 0) return Task.FromResult(QueueBacklog.Empty);

        var oldest = owed.Min(j => j.CreatedAt);
        var age = asOf - oldest;

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
            .Where(j => j.State is MarkingJobState.Pending
                or MarkingJobState.Retryable or MarkingJobState.Running)
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

    public Task<bool> RenewAsync(
        string operationId, string leaseToken, DateTimeOffset until, CancellationToken ct) =>
        Task.FromResult(Owned(operationId, leaseToken, j => j with { LeaseUntil = until }));

    public Task<bool> CompleteAsync(
        string operationId, string leaseToken, DateTimeOffset at, CancellationToken ct) =>
        Task.FromResult(Owned(operationId, leaseToken, j => j with
        {
            State = MarkingJobState.Completed,
            CompletedAt = at,
            LeaseToken = null,
            LeaseUntil = null,
        }));

    public Task<bool> RetryAsync(
        string operationId, string leaseToken, DateTimeOffset nextAttemptAt, string error,
        CancellationToken ct) =>
        Task.FromResult(Owned(operationId, leaseToken, j => j with
        {
            State = MarkingJobState.Retryable,
            NextAttemptAt = nextAttemptAt,
            LastError = error,
            LeaseToken = null,
            LeaseUntil = null,
        }));

    public Task<bool> FailAsync(
        string operationId, string leaseToken, string error, CancellationToken ct) =>
        Task.FromResult(Owned(operationId, leaseToken, j => j with
        {
            State = MarkingJobState.Failed,
            LastError = error,
            LeaseToken = null,
            LeaseUntil = null,
        }));

    public Task<IReadOnlyList<MarkingJob>> ListAsync(ExamSessionId sessionId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MarkingJob>>(
            [.. _jobs.Values.Where(j => j.SessionId == sessionId)]);
}
