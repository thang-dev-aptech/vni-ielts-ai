using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Domain.Usage;

namespace Vni.Ielts.Domain.Tests.Usage;

/// <summary>
/// The ledger row's own invariants — P-14. Everything that reaches the store
/// goes through one of these three factories, so what they refuse can never
/// be written, whatever Application does upstream.
/// </summary>
public sealed class UsageEntryTests
{
    private static readonly UserId User = new("user-1");
    private static readonly DateTimeOffset At = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_grant_cannot_take_turns_away()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UsageEntry.Grant("grant:user-1", User, At, UsageActions.AccountCreated, -1m));
    }

    [Fact]
    public void An_earn_cannot_take_turns_away()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UsageEntry.Earn("daily:user-1:2026-09-07", User, At, UsageActions.DailyLogin, -1m));
    }

    [Fact]
    public void A_use_cannot_add_turns()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UsageEntry.Use(User, At, UsageActions.SessionOpened, cost: -1m));
    }

    [Fact]
    public void A_use_records_its_cost_as_a_negative_delta()
    {
        var entry = UsageEntry.Use(User, At, UsageActions.WritingMarked, cost: 2m);

        Assert.Equal(-2m, entry.Turns);
        Assert.Equal(UsageKind.Use, entry.Kind);
    }

    [Fact]
    public void A_zero_cost_use_is_recorded_not_refused()
    {
        // An amount nobody has decided yet is a row worth nothing, never a
        // reason to skip recording that the action happened. → G-11
        var entry = UsageEntry.Use(User, At, UsageActions.SessionOpened, cost: 0m);

        Assert.Equal(0m, entry.Turns);
    }

    [Theory]
    [InlineData(-1L)]
    public void Negative_token_counts_are_rejected(long tokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UsageEntry.Use(
                User, At, UsageActions.WritingMarked, cost: 1m,
                sessionId: new ExamSessionId("s-1"), tokensIn: tokens));
    }

    [Fact]
    public void The_daily_id_is_the_same_for_the_same_user_and_day_and_different_otherwise()
    {
        var day = new DateOnly(2026, 9, 7);

        Assert.Equal(UsageEntry.DailyId(User, day), UsageEntry.DailyId(User, day));
        Assert.NotEqual(
            UsageEntry.DailyId(User, day), UsageEntry.DailyId(new UserId("user-2"), day));
        Assert.NotEqual(
            UsageEntry.DailyId(User, day), UsageEntry.DailyId(User, day.AddDays(1)));
    }

    [Fact]
    public void The_grant_and_referral_ids_are_deterministic_per_user()
    {
        Assert.Equal(UsageEntry.GrantId(User), UsageEntry.GrantId(User));
        Assert.Equal(UsageEntry.ReferralId(User), UsageEntry.ReferralId(User));
        Assert.NotEqual(UsageEntry.GrantId(User), UsageEntry.ReferralId(User));
    }

    [Fact]
    public void A_blank_id_is_refused()
    {
        Assert.Throws<ArgumentException>(
            () => UsageEntry.Grant("  ", User, At, UsageActions.AccountCreated, 10m));
    }
}
