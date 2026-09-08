using Vni.Ielts.Application.Usage;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Domain.Usage;

namespace Vni.Ielts.Application.Tests.Usage;

/// <summary>
/// The one place a ledger row is written from — P-14. Every amount here comes
/// from <see cref="UsageOptions"/>, never a literal, so a test that wants a
/// real number configures one explicitly rather than reading a default that
/// might quietly become a business decision nobody made.
/// </summary>
public sealed class UsageRecorderTests
{
    private static readonly UserId Learner = new("user-1");
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public FakeUsageLedger Ledger { get; } = new();
        public FakeExamSessionRepositoryForUsage Sessions { get; } = new();
        public UsageOptions Options { get; } = new();

        public UsageRecorder Sut => new(Ledger, Options, Sessions, new FixedClock(Now));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    [Fact]
    public async Task Opening_a_session_records_the_configured_cost_under_session_opened()
    {
        var h = new Harness { Options = { TurnCostByAction = { [UsageActions.SessionOpened] = 0m } } };
        var sessionId = new ExamSessionId("s-1");

        await h.Sut.SessionOpenedAsync(Learner, sessionId, default);

        var row = Assert.Single(h.Ledger.Written);
        Assert.Equal(UsageActions.SessionOpened, row.Action);
        Assert.Equal(UsageKind.Use, row.Kind);
        Assert.Equal(Learner, row.UserId);
        Assert.Equal(sessionId, row.SessionId);
        Assert.Equal(0m, row.Turns);
    }

    [Fact]
    public async Task Opening_a_session_never_fails_the_caller_when_the_ledger_is_unavailable()
    {
        // This is the half of P-14 that is easy to miss: not just "no
        // balance check", but "a ledger hiccup cannot refuse the action it is
        // only supposed to be watching".
        var h = new Harness();
        h.Ledger.ThrowOnNextAppend = new TimeoutException("ledger unreachable");

        await h.Sut.SessionOpenedAsync(Learner, new ExamSessionId("s-1"), default);

        Assert.Empty(h.Ledger.Written);
    }

    [Fact]
    public async Task A_new_account_is_granted_the_configured_initial_turns_exactly_once()
    {
        var h = new Harness { Options = { InitialGrantTurns = 10m } };
        var learner = Domain.Identity.User.Rehydrate(
            Learner, null, "Hoc vien", null, Domain.Identity.UserStatus.Active, Now, []);

        await h.Sut.AccountCreatedAsync(learner, default);
        await h.Sut.AccountCreatedAsync(learner, default); // a retried request, say

        var row = Assert.Single(h.Ledger.Written);
        Assert.Equal(UsageActions.AccountCreated, row.Action);
        Assert.Equal(UsageKind.Grant, row.Kind);
        Assert.Equal(10m, row.Turns);
        Assert.Equal(UsageEntry.GrantId(Learner), row.Id);
    }

    [Fact]
    public async Task The_first_activity_of_a_day_earns_and_a_second_touch_the_same_day_does_not()
    {
        var h = new Harness { Options = { DailyLoginTurns = 1m } };
        var day = new DateOnly(2026, 9, 7);

        var first = await h.Sut.DailyActivityAsync(Learner, day, default);
        var second = await h.Sut.DailyActivityAsync(Learner, day, default);

        Assert.True(first);
        Assert.False(second);
        Assert.Single(h.Ledger.Written);
    }

    [Fact]
    public async Task The_next_days_activity_earns_again()
    {
        var h = new Harness { Options = { DailyLoginTurns = 1m } };

        await h.Sut.DailyActivityAsync(Learner, new DateOnly(2026, 9, 7), default);
        var next = await h.Sut.DailyActivityAsync(Learner, new DateOnly(2026, 9, 8), default);

        Assert.True(next);
        Assert.Equal(2, h.Ledger.Written.Count);
    }

    [Fact]
    public async Task An_unreferred_account_earns_nobody_anything()
    {
        var h = new Harness { Options = { ReferralTurns = 1m } };
        var user = Domain.Identity.User.Register(
            Domain.Identity.PhoneNumber.Create("0912345678"), "Hoc vien", Now);

        var earned = await h.Sut.ReferralQualifiedAsync(user, default);

        Assert.False(earned);
        Assert.Empty(h.Ledger.Written);
    }

    [Fact]
    public async Task The_referrer_earns_once_when_their_invitee_registers_even_if_asked_twice()
    {
        /*
         * <b>The id is still `referral:{inviteeId}`, and that is deliberate.</b>
         * Rows written under the old verification gate carry the same id, so an
         * account that was already paid for cannot be paid for a second time
         * across the change of trigger. Only the action name moved.
         */
        var h = new Harness { Options = { ReferralTurns = 1m } };
        var referrerId = new UserId("referrer-1");
        var invitee = Domain.Identity.User.Register(
            Domain.Identity.PhoneNumber.Create("0912345678"), "Nguoi duoc gioi thieu", Now);
        invitee.AttributeReferral(referrerId);

        var first = await h.Sut.ReferralQualifiedAsync(invitee, default);
        var second = await h.Sut.ReferralQualifiedAsync(invitee, default);

        Assert.True(first);
        Assert.False(second);
        var row = Assert.Single(h.Ledger.Written);
        Assert.Equal(referrerId, row.UserId);
        Assert.Equal(UsageActions.ReferralQualified, row.Action);
        Assert.Equal(UsageEntry.ReferralId(invitee.Id), row.Id);
        Assert.Equal(1m, row.Turns);
    }

    [Fact]
    public async Task A_writing_mark_is_billed_to_the_sittings_owner_with_the_providers_token_counts()
    {
        var h = new Harness { Options = { TurnCostByAction = { [UsageActions.WritingMarked] = 1m } } };
        var sessionId = new ExamSessionId("s-2");
        h.Sessions.Seed(sessionId, Learner);

        await h.Sut.WritingMarkedAsync(
            sessionId, new EvaluationUsage("openai", "gpt-5", 400, 120), default);

        var row = Assert.Single(h.Ledger.Written);
        Assert.Equal(Learner, row.UserId);
        Assert.Equal(UsageActions.WritingMarked, row.Action);
        Assert.Equal(-1m, row.Turns);
        Assert.Equal("openai", row.Provider);
        Assert.Equal("gpt-5", row.Model);
        Assert.Equal(400, row.TokensIn);
        Assert.Equal(120, row.TokensOut);
    }

    [Fact]
    public async Task A_writing_mark_for_an_unknown_sitting_writes_nothing_rather_than_throwing()
    {
        var h = new Harness();

        await h.Sut.WritingMarkedAsync(new ExamSessionId("ghost"), usage: null, default);

        Assert.Empty(h.Ledger.Written);
    }

    [Fact]
    public async Task An_explanation_is_billed_to_the_learner_who_asked_for_it()
    {
        var h = new Harness { Options = { TurnCostByAction = { [UsageActions.ExplanationGenerated] = 1m } } };
        var sessionId = new ExamSessionId("s-3");

        await h.Sut.ExplanationGeneratedAsync(Learner, sessionId, "gemini", "gemini-2.5", default);

        var row = Assert.Single(h.Ledger.Written);
        Assert.Equal(UsageActions.ExplanationGenerated, row.Action);
        Assert.Equal(sessionId, row.SessionId);
        Assert.Equal("gemini", row.Provider);
    }
}
