using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Usage;

namespace Vni.Ielts.Application.Usage;

/// <summary>
/// The usage ledger. Append and read; nothing else.
///
/// <b>No update and no delete, by construction.</b> `P-14` is "record, never
/// block", and the blueprint's reason for a ledger over a counter is that
/// blocking later is one added check over these rows. A port that could
/// rewrite history would make that sum untrustworthy on the day it starts to
/// matter.
/// </summary>
public interface IUsageLedger
{
    /// <summary>
    /// Appends one row. Returns false — and writes nothing — when a row with
    /// the same id already exists. That is how a deterministic id makes an
    /// earn idempotent: a second daily-login touch, a second verification
    /// request, a retried registration all collide here rather than paying
    /// twice.
    /// </summary>
    Task<bool> AppendAsync(UsageEntry entry, CancellationToken ct);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<UsageEntry>> ListAsync(UserId userId, int take, CancellationToken ct);

    /// <summary>The sum of <see cref="UsageEntry.Turns"/> over every row the learner has.</summary>
    Task<decimal> BalanceAsync(UserId userId, CancellationToken ct);
}

/// <summary>
/// Finds the account behind a referral code.
///
/// Its own port rather than a method on <c>IUserRepository</c> so that the
/// referral seam can be wired — or left unwired — without touching the
/// identity contract every fake in the test suite implements.
/// </summary>
public interface IReferralDirectory
{
    /// <summary>Null when no account carries that code. Never an error: an unknown code is ignored. → `P-16`</summary>
    Task<User?> FindByReferralCodeAsync(string code, CancellationToken ct);
}

/// <summary>
/// The amounts. <b>A configured seam, and every value defaults to zero.</b>
///
/// <para>
/// `P-15` fixed the welcome allowance at ten. For the daily login and the
/// referral reward the owner named the mechanism and not the number, and
/// for what a use costs the word "lượt" implies one but nobody has said
/// which actions count. So the code has no opinion: with the <c>Usage</c>
/// section absent, every grant and earn is a row worth zero and every use is
/// a row worth zero. The rows still exist — the history is the product; the
/// prices are configuration. The deployment's values live in
/// <c>appsettings.json</c> where the business can change them without a
/// deploy. → `G-11`
/// </para>
/// </summary>
public sealed class UsageOptions
{
    public const string SectionName = "Usage";

    /// <summary>Turns a new account starts with. → `P-15`</summary>
    public decimal InitialGrantTurns { get; set; }

    /// <summary>Turns for the first activity of a calendar day. → `P-16`</summary>
    public decimal DailyLoginTurns { get; set; }

    /// <summary>Turns the referrer receives when the invitee verifies their email. → `P-16`</summary>
    public decimal ReferralTurns { get; set; }

    /// <summary>What one use costs unless <see cref="TurnCostByAction"/> says otherwise.</summary>
    public decimal TurnCostPerUse { get; set; }

    /// <summary>
    /// Per-action overrides keyed by <see cref="UsageActions"/> name, so that
    /// "opening a paper is free but a Writing mark is one turn" is a
    /// configuration change rather than a release.
    /// </summary>
    public Dictionary<string, decimal> TurnCostByAction { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public decimal CostOf(string action) =>
        TurnCostByAction.TryGetValue(action, out var cost) ? cost : TurnCostPerUse;
}
