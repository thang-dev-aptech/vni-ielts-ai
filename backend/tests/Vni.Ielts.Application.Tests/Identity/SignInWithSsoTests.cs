using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// The M-1 decision, as tests.
///
/// <para>
/// The owner's decision was short — *one email is one account* — and most of
/// this file exists because that sentence has two dangerous edges. Linking on
/// an address the provider will not vouch for is threat <c>T1</c> forwards;
/// linking into an account that never verified its own address is the same
/// threat backwards, and it is the one that is easy to build without noticing.
/// Both have a named test here, and neither should be deleted without
/// superseding ADR-0013.
/// </para>
/// </summary>
public sealed class SignInWithSsoTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
    private const string Address = "hoc.vien@example.com";
    private const string Subject = "google-subject-1";

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeUserIdentityRepository Identities { get; } = new();
        public FakeRoleRepository Roles { get; } = new();
        public FakeTokenService Tokens { get; } = new();
        public FakeSsoStateStore States { get; } = new();
        public FakeHandoffCodeStore Handoffs { get; } = new();
        public FakeExternalIdentityProvider Provider { get; init; } = new();

        public SignInWithSso Sut => new(
            new FakeProviderRegistry(Provider), States, Users, Identities, Roles, Tokens, Handoffs,
            new FixedClock(Now));

        /// <summary>Puts a live state in the store and returns the callback command.</summary>
        public async Task<SsoCallbackCommand> ArmAsync(
            string? code = "auth-code", string? providerError = null, TimeSpan? age = null)
        {
            var state = "state-" + Guid.NewGuid().ToString("n");
            await States.StoreAsync(
                new SsoState(
                    state, Provider.Provider, "verifier", "nonce", "/hoc",
                    Now.Add(age ?? TimeSpan.FromMinutes(10))),
                default);

            return new SsoCallbackCommand(Provider.Key, code, state, providerError);
        }

        public async Task<User> SeedUserAsync(bool emailVerified, bool suspended = false,
                                              string? passwordHash = "fake:old-password")
        {
            var email = Email.Create(Address);
            var user = User.Register(email, "Học viên", Now);
            if (emailVerified) user.MarkEmailVerified();
            if (suspended) user.Suspend();

            await Users.AddAsync(user, default);
            if (passwordHash is not null)
                await Identities.AddAsync(UserIdentity.ForEmail(user.Id, email, passwordHash, Now), default);

            return user;
        }
    }

    private static ExternalIdentity Identity(
        bool emailVerified = true, string? email = Address, string subject = Subject,
        IdentityProvider provider = IdentityProvider.Google) =>
        new(provider, subject, email, emailVerified, "Học Viên Google");

    // ── Creating an account ────────────────────────────────────────────────

    [Fact]
    public async Task Unknown_identity_and_unknown_email_creates_an_account()
    {
        var h = new Harness();
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("/hoc", result.Value!.ReturnTo);

        var created = await h.Users.FindByEmailAsync(Email.Create(Address), default);
        Assert.NotNull(created);
        Assert.Single(created!.RoleIds);

        var identity = await h.Identities.FindByProviderAsync(IdentityProvider.Google, Subject, default);
        Assert.NotNull(identity);
        Assert.Null(identity!.PasswordHash);
    }

    [Fact]
    public async Task A_provider_verified_address_arrives_already_verified()
    {
        // This is what makes social sign-up worth having: the account starts
        // past the gate entitlement accrual waits on. → T4
        var h = new Harness();
        h.Provider.Result = Identity(emailVerified: true);

        await h.Sut.HandleAsync(await h.ArmAsync(), default);

        var created = await h.Users.FindByEmailAsync(Email.Create(Address), default);
        Assert.True(created!.EmailVerified);
    }

    [Fact]
    public async Task An_unverified_address_creates_an_unverified_account()
    {
        var h = new Harness { Provider = new FakeExternalIdentityProvider(IdentityProvider.Facebook, false) };
        h.Provider.Result = Identity(emailVerified: false, provider: IdentityProvider.Facebook);

        await h.Sut.HandleAsync(await h.ArmAsync(), default);

        var created = await h.Users.FindByEmailAsync(Email.Create(Address), default);
        Assert.False(created!.EmailVerified);
    }

    [Fact]
    public async Task A_concurrent_signup_for_the_same_address_links_instead_of_failing()
    {
        var h = new Harness();
        h.Provider.Result = Identity();
        var winner = await h.SeedUserAsync(emailVerified: true, passwordHash: null);
        h.Users.ThrowDuplicateOnNextAdd = true;

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        var identity = await h.Identities.FindByProviderAsync(IdentityProvider.Google, Subject, default);
        Assert.Equal(winner.Id, identity!.UserId);
    }

    [Fact]
    public async Task Losing_the_race_to_link_an_identity_still_signs_in()
    {
        // Two tabs finishing at the same instant. The unique index rejects the
        // second insert, and the person on that request is signed in anyway —
        // they are, after all, exactly who they said they were.
        var h = new Harness();
        h.Provider.Result = Identity();

        var winner = await h.SeedUserAsync(emailVerified: true, passwordHash: null);
        h.Identities.LoseNextAddTo =
            UserIdentity.ForSocial(winner.Id, IdentityProvider.Google, Subject, Now);
        h.Users.ThrowDuplicateOnNextAdd = true;

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        Assert.Single(await h.Identities.ListForUserAsync(winner.Id, default));
    }

    // ── Signing in again ───────────────────────────────────────────────────

    [Fact]
    public async Task A_known_identity_signs_in_without_creating_anything()
    {
        var h = new Harness();
        var user = await h.SeedUserAsync(emailVerified: true, passwordHash: null);
        await h.Identities.AddAsync(
            UserIdentity.ForSocial(user.Id, IdentityProvider.Google, Subject, Now), default);
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        Assert.Single(await h.Identities.ListForUserAsync(user.Id, default));
    }

    [Fact]
    public async Task A_known_identity_signs_in_even_if_the_address_changed_at_the_provider()
    {
        // Keying on the subject rather than the address is what makes this
        // work; keying on email would strand the account.
        var h = new Harness();
        var user = await h.SeedUserAsync(emailVerified: true, passwordHash: null);
        await h.Identities.AddAsync(
            UserIdentity.ForSocial(user.Id, IdentityProvider.Google, Subject, Now), default);
        h.Provider.Result = Identity(email: "moi@example.com");

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        Assert.Null(await h.Users.FindByEmailAsync(Email.Create("moi@example.com"), default));
    }

    // ── The M-1 branch ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_matching_address_links_to_the_existing_account()
    {
        var h = new Harness();
        var user = await h.SeedUserAsync(emailVerified: true);
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        var identities = await h.Identities.ListForUserAsync(user.Id, default);
        Assert.Equal(2, identities.Count);
        Assert.Single(identities, i => i.Provider == IdentityProvider.Google);
    }

    [Fact]
    public async Task Linking_to_a_verified_account_leaves_its_password_alone()
    {
        var h = new Harness();
        var user = await h.SeedUserAsync(emailVerified: true);
        h.Provider.Result = Identity();

        await h.Sut.HandleAsync(await h.ArmAsync(), default);

        var email = (await h.Identities.ListForUserAsync(user.Id, default))
            .Single(i => i.Provider == IdentityProvider.Email);
        Assert.Equal("fake:old-password", email.PasswordHash);
        Assert.Empty(h.Tokens.RevokedAllFor);
    }

    [Fact]
    public async Task Linking_to_an_unverified_account_evicts_whoever_set_its_password()
    {
        // The squatter case, and the reason ADR-0013 could not simply say
        // "link on matching email". Registration creates the account before
        // the address is proven, so an attacker registers the victim's address
        // and waits. Marking it verified without removing the password would
        // hand them the merged account.
        var h = new Harness();
        var user = await h.SeedUserAsync(emailVerified: false);
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);

        var refreshed = await h.Users.FindByIdAsync(user.Id, default);
        Assert.True(refreshed!.EmailVerified);

        var email = (await h.Identities.ListForUserAsync(user.Id, default))
            .Single(i => i.Provider == IdentityProvider.Email);
        Assert.Null(email.PasswordHash);

        Assert.Contains(user.Id, h.Tokens.RevokedAllFor);
    }

    [Fact]
    public async Task Eviction_also_takes_back_the_display_name()
    {
        // Whoever registered the unproven address chose the name on it. The
        // person who actually owns the address should not inherit it.
        var h = new Harness();
        var user = await h.SeedUserAsync(emailVerified: false);
        h.Provider.Result = Identity();

        await h.Sut.HandleAsync(await h.ArmAsync(), default);

        var refreshed = await h.Users.FindByIdAsync(user.Id, default);
        Assert.Equal("Học Viên Google", refreshed!.DisplayName);
    }

    [Fact]
    public async Task Linking_to_a_verified_account_leaves_its_display_name_alone()
    {
        // The opposite case, and the reason the rename is not unconditional:
        // on a proven account the name is the person's own.
        var h = new Harness();
        var user = await h.SeedUserAsync(emailVerified: true);
        h.Provider.Result = Identity();

        await h.Sut.HandleAsync(await h.ArmAsync(), default);

        var refreshed = await h.Users.FindByIdAsync(user.Id, default);
        Assert.Equal("Học viên", refreshed!.DisplayName);
    }

    [Fact]
    public async Task A_provider_that_does_not_vouch_for_the_address_cannot_link()
    {
        // Facebook returns an address and asserts nothing about it. Linking on
        // that is threat T1 with no mitigation at all.
        var h = new Harness { Provider = new FakeExternalIdentityProvider(IdentityProvider.Facebook, false) };
        await h.SeedUserAsync(emailVerified: true);
        h.Provider.Result = Identity(provider: IdentityProvider.Facebook, emailVerified: true);

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.IdentityLinkRequired, result.Error.Code);
    }

    [Fact]
    public async Task A_vouching_provider_that_reports_the_address_unverified_cannot_link()
    {
        var h = new Harness();
        await h.SeedUserAsync(emailVerified: true);
        h.Provider.Result = Identity(emailVerified: false);

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.IdentityLinkRequired, result.Error.Code);
    }

    [Fact]
    public async Task A_refused_link_changes_nothing()
    {
        var h = new Harness();
        var user = await h.SeedUserAsync(emailVerified: true);
        h.Provider.Result = Identity(emailVerified: false);

        await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.Single(await h.Identities.ListForUserAsync(user.Id, default));
    }

    // ── Refusals ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_suspended_account_cannot_be_linked_into()
    {
        var h = new Harness();
        await h.SeedUserAsync(emailVerified: true, suspended: true);
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AccountSuspended, result.Error.Code);
    }

    [Fact]
    public async Task A_suspended_account_cannot_sign_in_through_a_linked_identity()
    {
        var h = new Harness();
        var user = await h.SeedUserAsync(emailVerified: true, suspended: true, passwordHash: null);
        await h.Identities.AddAsync(
            UserIdentity.ForSocial(user.Id, IdentityProvider.Google, Subject, Now), default);
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AccountSuspended, result.Error.Code);
    }

    [Fact]
    public async Task A_provider_that_shares_no_address_is_refused()
    {
        var h = new Harness { Provider = new FakeExternalIdentityProvider(IdentityProvider.Facebook, false) };
        h.Provider.Result = Identity(email: null, provider: IdentityProvider.Facebook);

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoEmailMissing, result.Error.Code);
    }

    [Fact]
    public async Task A_failed_exchange_reports_nothing_about_why()
    {
        var h = new Harness();
        h.Provider.Result = null;

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoExchangeFailed, result.Error.Code);
    }

    [Fact]
    public async Task An_unknown_provider_is_refused()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(
            new SsoCallbackCommand("linkedin", "code", "state", null), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoProviderUnknown, result.Error.Code);
    }

    // ── The state value ────────────────────────────────────────────────────

    [Fact]
    public async Task A_state_cannot_be_used_twice()
    {
        var h = new Harness();
        h.Provider.Result = Identity();
        var command = await h.ArmAsync();

        Assert.True((await h.Sut.HandleAsync(command, default)).IsSuccess);

        var replay = await h.Sut.HandleAsync(command, default);
        Assert.False(replay.IsSuccess);
        Assert.Equal(ErrorCodes.SsoStateInvalid, replay.Error.Code);
    }

    [Fact]
    public async Task A_missing_or_unknown_state_is_refused()
    {
        var h = new Harness();
        h.Provider.Result = Identity();

        foreach (var state in new[] { null, "", "never-issued" })
        {
            var result = await h.Sut.HandleAsync(
                new SsoCallbackCommand(h.Provider.Key, "code", state, null), default);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.SsoStateInvalid, result.Error.Code);
        }
    }

    [Fact]
    public async Task An_expired_state_is_refused()
    {
        var h = new Harness();
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(
            await h.ArmAsync(age: TimeSpan.FromMinutes(-1)), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoStateInvalid, result.Error.Code);
    }

    [Fact]
    public async Task A_state_issued_for_one_provider_cannot_be_spent_at_another()
    {
        var google = new FakeExternalIdentityProvider();
        var facebook = new FakeExternalIdentityProvider(IdentityProvider.Facebook, false);
        var h = new Harness { Provider = google };
        var states = h.States;

        await states.StoreAsync(
            new SsoState("cross", IdentityProvider.Facebook, "v", "n", null, Now.AddMinutes(5)), default);

        var sut = new SignInWithSso(
            new FakeProviderRegistry(google, facebook), states, h.Users, h.Identities, h.Roles,
            h.Tokens, h.Handoffs, new FixedClock(Now));

        var result = await sut.HandleAsync(
            new SsoCallbackCommand("google", "code", "cross", null), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoStateInvalid, result.Error.Code);
    }

    [Fact]
    public async Task A_cancelled_sign_in_still_spends_its_state()
    {
        // Otherwise a cancelled attempt leaves a live state behind, and the
        // whole point of a state is that it is spent exactly once.
        var h = new Harness();
        var command = await h.ArmAsync(providerError: "access_denied");

        var first = await h.Sut.HandleAsync(command, default);
        Assert.Equal(ErrorCodes.SsoDenied, first.Error.Code);

        h.Provider.Result = Identity();
        var second = await h.Sut.HandleAsync(command with { ProviderError = null }, default);
        Assert.Equal(ErrorCodes.SsoStateInvalid, second.Error.Code);
    }

    [Fact]
    public async Task The_stored_verifier_and_nonce_are_what_reach_the_provider()
    {
        // The adapter is stateless, so if the handler passed the wrong values
        // the exchange would fail against a real provider and pass here.
        var h = new Harness();
        h.Provider.Result = Identity();

        await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.Equal("verifier", h.Provider.LastVerifier);
        Assert.Equal("nonce", h.Provider.LastNonce);
        Assert.Equal("auth-code", h.Provider.LastCode);
    }
}
