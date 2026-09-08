using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Tests.Usage;
using Vni.Ielts.Application.Usage;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Usage;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// The referral half of `P-16`: a referrer is paid when, and only when,
/// their invitee's address is actually confirmed — never on the bare act of
/// sharing a link, which no platform can verify (`M-27`, ADR-0009).
/// </summary>
public sealed class EmailVerificationUsageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Referrer = new("referrer-1");

    private sealed class CodeTokens : IEmailVerificationTokens
    {
        public CodeRedemption NextResult { get; set; } = CodeRedemption.Verified;
        public Task<string> IssueAsync(UserId userId, CancellationToken ct) => Task.FromResult("token");
        public Task<UserId?> RedeemAsync(string token, CancellationToken ct) => Task.FromResult<UserId?>(null);
        public Task<string> IssueCodeAsync(UserId userId, CancellationToken ct) => Task.FromResult("123456");
        public Task<CodeRedemption> RedeemCodeAsync(UserId userId, string code, CancellationToken ct) =>
            Task.FromResult(NextResult);
    }

    private sealed class LinkTokens : IEmailVerificationTokens
    {
        public UserId? NextRedeem { get; set; }
        public Task<string> IssueAsync(UserId userId, CancellationToken ct) => Task.FromResult("token");
        public Task<UserId?> RedeemAsync(string token, CancellationToken ct) => Task.FromResult(NextRedeem);
        public Task<string> IssueCodeAsync(UserId userId, CancellationToken ct) => Task.FromResult("123456");
        public Task<CodeRedemption> RedeemCodeAsync(UserId userId, string code, CancellationToken ct) =>
            Task.FromResult(CodeRedemption.Verified);
    }

    private static UsageRecorder Recorder(FakeUsageLedger ledger) =>
        new(ledger, new UsageOptions { ReferralTurns = 1m }, new FakeExamSessionRepositoryForUsage(),
            new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private static User ReferredInvitee()
    {
        var user = User.Register(Email.Create("invitee@example.com"), "Người được giới thiệu", Now);
        user.AttributeReferral(Referrer);
        return user;
    }

    // ── Code path (ConfirmEmailCode) ────────────────────────────────────────

    [Fact]
    public async Task Confirming_the_code_earns_the_referrer()
    {
        var users = new FakeUserRepository();
        var user = ReferredInvitee();
        await users.AddAsync(user, default);
        var ledger = new FakeUsageLedger();
        var sut = new ConfirmEmailCode(users, new CodeTokens(), Recorder(ledger));

        var result = await sut.HandleAsync(new ConfirmEmailCodeCommand(user.Id, "123456"), default);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(ledger.Written);
        Assert.Equal(Referrer, row.UserId);
        Assert.Equal(UsageActions.ReferralVerified, row.Action);
    }

    [Fact]
    public async Task Confirming_the_code_for_an_unreferred_account_earns_nobody()
    {
        var users = new FakeUserRepository();
        var user = User.Register(Email.Create("solo@example.com"), "Một mình", Now);
        await users.AddAsync(user, default);
        var ledger = new FakeUsageLedger();
        var sut = new ConfirmEmailCode(users, new CodeTokens(), Recorder(ledger));

        await sut.HandleAsync(new ConfirmEmailCodeCommand(user.Id, "123456"), default);

        Assert.Empty(ledger.Written);
    }

    [Fact]
    public async Task Confirming_an_already_verified_account_does_not_earn_a_second_time()
    {
        var users = new FakeUserRepository();
        var user = ReferredInvitee();
        user.MarkEmailVerified();
        await users.AddAsync(user, default);
        var ledger = new FakeUsageLedger();
        var sut = new ConfirmEmailCode(users, new CodeTokens(), Recorder(ledger));

        var result = await sut.HandleAsync(new ConfirmEmailCodeCommand(user.Id, "123456"), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(ledger.Written);
    }

    // ── Link path (VerifyEmail) ─────────────────────────────────────────────

    [Fact]
    public async Task Verifying_through_the_link_earns_the_referrer()
    {
        var users = new FakeUserRepository();
        var user = ReferredInvitee();
        await users.AddAsync(user, default);
        var ledger = new FakeUsageLedger();
        var tokens = new LinkTokens { NextRedeem = user.Id };
        var sut = new VerifyEmail(users, tokens, Recorder(ledger));

        var result = await sut.HandleAsync(new VerifyEmailCommand("a-real-token"), default);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(ledger.Written);
        Assert.Equal(Referrer, row.UserId);
    }

    [Fact]
    public async Task A_double_click_on_the_verification_link_does_not_earn_twice()
    {
        var users = new FakeUserRepository();
        var user = ReferredInvitee();
        user.MarkEmailVerified(); // the first click already landed
        await users.AddAsync(user, default);
        var ledger = new FakeUsageLedger();
        var tokens = new LinkTokens { NextRedeem = user.Id };
        var sut = new VerifyEmail(users, tokens, Recorder(ledger));

        await sut.HandleAsync(new VerifyEmailCommand("a-real-token"), default);

        Assert.Empty(ledger.Written);
    }
}
