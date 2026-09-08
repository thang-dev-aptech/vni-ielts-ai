using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Usage;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Domain.Usage;

namespace Vni.Ielts.Application.Tests.Usage;

/// <summary>
/// Mirrors <c>MongoUsageLedger</c>'s one real behaviour that matters to a
/// caller: a row with an id that already exists does not get written twice,
/// and <see cref="AppendAsync"/> says so by returning <c>false</c>. Without
/// that, a test using this fake could not tell a ledger that de-duplicates
/// from one that silently pays twice.
/// </summary>
internal sealed class FakeUsageLedger : IUsageLedger
{
    private readonly Dictionary<string, UsageEntry> _byId = [];

    /// <summary>Every row actually written, in the order it was written.</summary>
    public List<UsageEntry> Written { get; } = [];

    /// <summary>Set to make the next append throw — proves a caller that must never fail because of this.</summary>
    public Exception? ThrowOnNextAppend { get; set; }

    public Task<bool> AppendAsync(UsageEntry entry, CancellationToken ct)
    {
        if (ThrowOnNextAppend is { } ex)
        {
            ThrowOnNextAppend = null;
            throw ex;
        }

        if (!_byId.TryAdd(entry.Id, entry)) return Task.FromResult(false);

        Written.Add(entry);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<UsageEntry>> ListAsync(UserId userId, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UsageEntry>>(
        [
            .. _byId.Values
                .Where(e => e.UserId == userId)
                .OrderByDescending(e => e.At)
                .Take(take),
        ]);

    public Task<decimal> BalanceAsync(UserId userId, CancellationToken ct) =>
        Task.FromResult(_byId.Values.Where(e => e.UserId == userId).Sum(e => e.Turns));
}

internal sealed class FakeReferralDirectory : IReferralDirectory
{
    private readonly Dictionary<string, User> _byCode = new(StringComparer.Ordinal);

    public void Seed(User referrer)
    {
        if (referrer.ReferralCode is { } code) _byCode[code] = referrer;
    }

    public Task<User?> FindByReferralCodeAsync(string code, CancellationToken ct) =>
        Task.FromResult(_byCode.GetValueOrDefault(code));
}

/// <summary>
/// Just enough of a sitting for <c>UsageRecorder.WritingMarkedAsync</c> to
/// resolve an owner from a session id — no exam content, no attempts.
/// </summary>
internal sealed class FakeExamSessionRepositoryForUsage : IExamSessionRepository
{
    private readonly Dictionary<string, ExamSession> _byId = [];

    public void Seed(ExamSessionId id, UserId owner) =>
        _byId[id.Value] = ExamSession.Rehydrate(
            id, owner, new ExamVersionId("version-1"), SessionMode.Single, SessionStatus.Submitted,
            new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero), null, []);

    public Task<ExamSession?> FindAsync(ExamSessionId id, CancellationToken ct) =>
        Task.FromResult(_byId.GetValueOrDefault(id.Value));

    public Task<ExamSession?> FindOpenForUserAsync(UserId userId, CancellationToken ct) =>
        Task.FromResult<ExamSession?>(null);

    public Task<IReadOnlyList<ExamSession>> ListForUserAsync(UserId userId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExamSession>>([]);

    public Task AddAsync(ExamSession session, CancellationToken ct) => Task.CompletedTask;

    public Task<bool> TrySaveAsync(ExamSession session, SessionState from, CancellationToken ct) =>
        Task.FromResult(true);
}
