using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Tests.Usage;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Usage;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// What registration adds to the usage ledger — the welcome grant (`P-15`) and
/// referral attribution (`P-16`) — kept separate from
/// <see cref="RegisterUserTests"/> so that file's fakes and this one's stay
/// unentangled.
/// </summary>
public sealed class RegisterUserUsageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeUserIdentityRepository Identities { get; } = new();
        public FakeRoleRepository Roles { get; } = new();
        public FakePasswordHasher Hasher { get; } = new();
        public FakeTokenService Sessions { get; } = new();
        public FakeUsageLedger Ledger { get; } = new();
        public FakeReferralDirectory Referrals { get; } = new();

        public Vni.Ielts.Application.Usage.UsageOptions Options { get; } =
            new() { InitialGrantTurns = 10m, ReferralTurns = 3m };

        public RegisterUser Sut => new(
            Users, Identities, Roles, Hasher,
            new FakePermissionResolver(PermissionKeys.ExamReadOwn), Sessions, new FixedClock(Now),
            Referrals,
            new Vni.Ielts.Application.Usage.UsageRecorder(
                Ledger, Options, new FakeExamSessionRepositoryForUsage(), new FixedClock(Now)));
    }

    private static RegisterUserCommand Command(string? referralCode = null) =>
        new("0912345678", "mot-mat-khau-du-dai-2026", "Hoc vien", referralCode);

    private static User Referrer() =>
        User.Register(PhoneNumber.Create("0987654321"), "Nguoi gioi thieu", Now);

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
        var referrer = Referrer();
        h.Referrals.Seed(referrer);

        var result = await h.Sut.HandleAsync(Command(referrer.ReferralCode), default);

        Assert.True(result.IsSuccess);
        var created = await h.Users.FindByIdAsync(result.Value!.Session.UserId, default);
        Assert.Equal(referrer.Id, created!.ReferredByUserId);
    }

    [Fact]
    public async Task The_referrer_is_paid_at_registration_rather_than_at_a_verification_that_no_longer_happens()
    {
        /*
         * <b>The gate moved, and this is the test that says where to.</b> The
         * credit used to land when the invitee proved their address. There is
         * no such moment any more, so the owner moved it to registration
         * (08/09/2026). Left unmoved it would simply never pay — the referral
         * feature would exist, record attribution, and quietly owe forever.
         *
         * What replaces the old fraud control is the unique phone number:
         * farming referrals now costs a real SIM per account rather than a
         * disposable address. → threat T13
         */
        var h = new Harness();
        var referrer = Referrer();
        h.Referrals.Seed(referrer);

        await h.Sut.HandleAsync(Command(referrer.ReferralCode), default);

        var earn = Assert.Single(h.Ledger.Written, e => e.Action == UsageActions.ReferralQualified);
        Assert.Equal(referrer.Id, earn.UserId);
        Assert.Equal(3m, earn.Turns);
    }

    [Fact]
    public async Task Nobody_is_paid_when_no_referral_code_was_used()
    {
        var h = new Harness();

        await h.Sut.HandleAsync(Command(), default);

        Assert.DoesNotContain(h.Ledger.Written, e => e.Action == UsageActions.ReferralQualified);
    }

    [Fact]
    public async Task An_unknown_referral_code_is_ignored_not_rejected()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command("NOSUCHCODE1"), default);

        Assert.True(result.IsSuccess);
        var created = await h.Users.FindByIdAsync(result.Value!.Session.UserId, default);
        Assert.Null(created!.ReferredByUserId);
    }

    [Fact]
    public async Task No_referral_code_leaves_the_account_unattributed()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command(), default);

        var created = await h.Users.FindByIdAsync(result.Value!.Session.UserId, default);
        Assert.Null(created!.ReferredByUserId);
    }
}
