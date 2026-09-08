using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Tests.Usage;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Usage;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// The two things registration adds to the usage ledger — the welcome grant
/// (`P-15`) and referral attribution (`P-16`) — kept separate from
/// <see cref="RegisterUserTests"/> so that file's fakes and this one's stay
/// unentangled.
/// </summary>
public sealed class RegisterUserUsageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeUserIdentityRepository Identities { get; } = new();
        public FakeRoleRepository Roles { get; } = new();
        public FakePasswordHasher Hasher { get; } = new();
        public FakeVerificationTokensForUsage Tokens { get; } = new();
        public FakeVerificationMessageSender Sender { get; } = new();
        public FakeTokenService Sessions { get; } = new();
        public FakeUsageLedger Ledger { get; } = new();
        public FakeReferralDirectory Referrals { get; } = new();
        public Vni.Ielts.Application.Usage.UsageOptions Options { get; } = new() { InitialGrantTurns = 10m };

        public RegisterUser Sut => new(
            Users, Identities, Roles, Hasher, Tokens, Sender,
            new FakePermissionResolver(PermissionKeys.ExamRead), Sessions, new FixedClock(Now),
            Referrals, new Vni.Ielts.Application.Usage.UsageRecorder(Ledger, Options, new FakeExamSessionRepositoryForUsage(), new FixedClock(Now)));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeVerificationTokensForUsage : IEmailVerificationTokens
    {
        public Task<string> IssueAsync(UserId userId, CancellationToken ct) => Task.FromResult("token");
        public Task<UserId?> RedeemAsync(string token, CancellationToken ct) => Task.FromResult<UserId?>(null);
        public Task<string> IssueCodeAsync(UserId userId, CancellationToken ct) => Task.FromResult("123456");
        public Task<CodeRedemption> RedeemCodeAsync(UserId userId, string code, CancellationToken ct) =>
            Task.FromResult(CodeRedemption.Verified);
    }

    private static RegisterUserCommand Command(string? referralCode = null) =>
        new("hoc.vien@example.com", "mot-mat-khau-du-dai-2026", "Học viên", referralCode);

    [Fact]
    public async Task Registering_grants_the_configured_initial_turns()
    {
        var h = new Harness();

        await h.Sut.HandleAsync(Command(), default);

        var grant = Assert.Single(h.Ledger.Written);
        Assert.Equal(UsageActions.AccountCreated, grant.Action);
        Assert.Equal(10m, grant.Turns);
    }

    [Fact]
    public async Task A_valid_referral_code_attributes_the_new_account_to_its_owner()
    {
        var h = new Harness();
        var referrer = User.Register(Email.Create("gioi.thieu@example.com"), "Người giới thiệu", Now);
        h.Referrals.Seed(referrer);

        var result = await h.Sut.HandleAsync(Command(referrer.ReferralCode), default);

        Assert.True(result.IsSuccess);
        var created = await h.Users.FindByEmailAsync(Email.Create("hoc.vien@example.com"), default);
        Assert.Equal(referrer.Id, created!.ReferredByUserId);
    }

    [Fact]
    public async Task An_unknown_referral_code_is_ignored_not_rejected()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command("NOSUCHCODE1"), default);

        Assert.True(result.IsSuccess);
        var created = await h.Users.FindByEmailAsync(Email.Create("hoc.vien@example.com"), default);
        Assert.Null(created!.ReferredByUserId);
    }

    [Fact]
    public async Task No_referral_code_leaves_the_account_unattributed()
    {
        var h = new Harness();

        await h.Sut.HandleAsync(Command(), default);

        var created = await h.Users.FindByEmailAsync(Email.Create("hoc.vien@example.com"), default);
        Assert.Null(created!.ReferredByUserId);
    }
}
