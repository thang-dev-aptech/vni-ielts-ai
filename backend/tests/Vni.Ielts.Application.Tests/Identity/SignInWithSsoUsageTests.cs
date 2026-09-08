using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Tests.Usage;
using Vni.Ielts.Application.Usage;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Usage;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// The welcome grant (`P-15`) on the social sign-up path — kept separate from
/// <see cref="SignInWithSsoTests"/> so that file's large fixture stays
/// untouched by a second, usage-only concern.
/// </summary>
public sealed class SignInWithSsoUsageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);
    private const string Address = "hoc.vien@example.com";
    private const string Subject = "google-subject-1";

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeUserIdentityRepository Identities { get; } = new();
        public FakeRoleRepository Roles { get; } = new();
        public FakeTokenService Tokens { get; } = new();
        public FakeSsoStateStore States { get; } = new();
        public FakeHandoffCodeStore Handoffs { get; } = new();
        public FakeExternalIdentityProvider Provider { get; } = new(IdentityProvider.Google, true);
        public FakeUsageLedger Ledger { get; } = new();

        public SignInWithSso Sut => new(
            new FakeProviderRegistry(Provider), States, Users, Identities, Roles, Tokens, Handoffs,
            new FixedClock(Now),
            new UsageRecorder(
                Ledger, new UsageOptions { InitialGrantTurns = 10m },
                new FakeExamSessionRepositoryForUsage(), new FixedClock(Now)));

        public async Task<SsoCallbackCommand> ArmAsync()
        {
            var state = "state-" + Guid.NewGuid().ToString("n");
            await States.StoreAsync(
                new SsoState(state, Provider.Provider, "verifier", "nonce", "/hoc", Now.AddMinutes(10)),
                default);
            return new SsoCallbackCommand(Provider.Key, "auth-code", state, null);
        }
    }

    private static ExternalIdentity Identity(bool emailVerified = true) =>
        new(IdentityProvider.Google, Subject, Address, emailVerified, "Học Viên Google");

    [Fact]
    public async Task A_brand_new_social_account_is_granted_the_welcome_turns()
    {
        var h = new Harness();
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(h.Ledger.Written);
        Assert.Equal(UsageActions.AccountCreated, row.Action);
        Assert.Equal(10m, row.Turns);
    }

    [Fact]
    public async Task Signing_in_again_with_the_same_identity_does_not_grant_a_second_time()
    {
        var h = new Harness();
        h.Provider.Result = Identity();

        await h.Sut.HandleAsync(await h.ArmAsync(), default);
        await h.Sut.HandleAsync(await h.ArmAsync(), default);

        var row = Assert.Single(h.Ledger.Written);
        Assert.Equal(UsageActions.AccountCreated, row.Action);
    }
}
