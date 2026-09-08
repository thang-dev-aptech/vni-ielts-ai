using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Usage;

namespace Vni.Ielts.Application.Usage;

public sealed record UsageEntryView(
    string Id,
    DateTimeOffset At,
    string Action,
    /// <summary><c>grant</c> · <c>earn</c> · <c>use</c></summary>
    string Kind,
    decimal Turns,
    string? SessionId,
    string? Provider,
    string? Model,
    long? TokensIn,
    long? TokensOut,
    decimal? CostEstimate);

/// <param name="Balance">The sum of every row's turns. Can be negative; nothing refuses a negative balance. → `P-14`</param>
/// <param name="InitialGrant">What a new account starts with, so the client can show "x of 10". → `P-15`</param>
/// <param name="ReferralCode">The code to put in a share link. → `P-16`</param>
public sealed record UsageView(
    decimal Balance,
    decimal InitialGrant,
    string? ReferralCode,
    IReadOnlyList<UsageEntryView> Entries);

public sealed record GetMyUsageQuery(UserId UserId, int Take = GetMyUsage.DefaultTake);

/// <summary>
/// Balance and history for the signed-in learner.
///
/// The referral code is assigned here for accounts that predate it — a read
/// that writes once, so a learner who registered before referrals existed
/// still gets a link the first time they open the screen.
/// </summary>
public sealed class GetMyUsage(IUsageLedger ledger, IUserRepository users, UsageOptions options)
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    public async Task<UsageView> HandleAsync(GetMyUsageQuery query, CancellationToken ct)
    {
        var take = Math.Clamp(query.Take, 1, MaxTake);

        var balance = await ledger.BalanceAsync(query.UserId, ct);
        var entries = await ledger.ListAsync(query.UserId, take, ct);

        string? code = null;
        if (await users.FindByIdAsync(query.UserId, ct) is { } user)
        {
            if (user.EnsureReferralCode())
                await users.SaveAsync(user, ct);
            code = user.ReferralCode;
        }

        return new UsageView(
            balance,
            options.InitialGrantTurns,
            code,
            entries.Select(ToView).ToList());
    }

    private static UsageEntryView ToView(UsageEntry e) => new(
        e.Id,
        e.At,
        e.Action,
        e.Kind.ToString().ToLowerInvariant(),
        e.Turns,
        e.SessionId?.Value,
        e.Provider,
        e.Model,
        e.TokensIn,
        e.TokensOut,
        e.CostEstimate);
}
