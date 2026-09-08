using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// Social sign-in, after the 08/09/2026 owner decision.
///
/// <para>
/// <b>The address identifies whichever account currently holds it.</b>
/// <i>"user đổi email thành nguyendoanthang16@gmail.com → thì dữ liệu của
/// ngdthang.dev@gmail.com sẽ được chuyển thành của nguyendoanthang16@gmail.com
/// và bây giờ nếu login bằng ngdthang.dev@gmail.com sẽ là 1 tài khoản mới"</i>.
/// That reverses <c>AU-7</c> and supersedes ADR-0013; the three tests under
/// "moving an address" are the ones that say so, and they should not be
/// deleted without superseding ADR-0018 in turn.
/// </para>
///
/// <para>
/// Most of the rest of this file guards the edges of that rule. Dropping a link
/// is destructive and irreversible, so it happens only when a vouching provider
/// asserts a usable address that differs from the one the account holds — three
/// conditions, three named tests. And an account that has a password is refused
/// rather than taken over, which is the half that replaced ADR-0013's squatter
/// eviction.
/// </para>
/// </summary>
public sealed class SignInWithSsoTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);
    private const string Address = "ngdthang.dev@example.com";
    private const string Moved = "nguyendoanthang16@example.com";
    private const string Subject = "google-subject-1";

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeUserIdentityRepository Identities { get; } = new();
        public FakeRoleRepository Roles { get; } = new();
        public FakeHandoffCodeStore Handoffs { get; } = new();
        public FakeSsoStateStore States { get; } = new();
        public FakeExternalIdentityProvider Provider { get; init; } = new();

        public SignInWithSso Sut => new(
            new FakeProviderRegistry(Provider), States, Users, Identities, Roles, Handoffs,
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

        /// <summary>An account holding an address, optionally with a password.</summary>
        public async Task<User> SeedUserAsync(
            string address = Address, bool suspended = false,
            string? passwordHash = "fake:old-password")
        {
            var user = User.RegisterFromProvider(Email.Create(address), "Hoc vien", Now);
            if (suspended) user.Suspend();

            await Users.AddAsync(user, default);
            if (passwordHash is not null)
                await Identities.AddAsync(UserIdentity.ForPassword(user.Id, passwordHash, Now), default);

            return user;
        }

        /// <summary>Attaches the provider link this subject would have after a first sign-in.</summary>
        public Task LinkAsync(User user, string subject = Subject) =>
            Identities.AddAsync(
                UserIdentity.ForSocial(user.Id, Provider.Provider, subject, Now), default);
    }

    private static ExternalIdentity Identity(
        bool emailVerified = true, string? email = Address, string subject = Subject,
        IdentityProvider provider = IdentityProvider.Google) =>
        new(provider, subject, email, emailVerified, "Hoc Vien Google");

    // ── Creating an account ────────────────────────────────────────────────

    [Fact]
    public async Task Unknown_identity_and_unknown_address_creates_an_account()
    {
        var h = new Harness();
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("/hoc", result.Value!.ReturnTo);

        var created = await h.Users.FindByEmailAsync(Email.Create(Address), default);
        Assert.NotNull(created);
        Assert.Single(created!.RoleIds);
        Assert.Null(created.Phone);

        var identity = await h.Identities.FindByProviderAsync(IdentityProvider.Google, Subject, default);
        Assert.NotNull(identity);
        Assert.Null(identity!.PasswordHash);
    }

    [Fact]
    public async Task A_known_identity_signs_straight_in()
    {
        var h = new Harness();
        var user = await h.SeedUserAsync(passwordHash: null);
        await h.LinkAsync(user);
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, await h.Handoffs.ResolveAsync(result.Value!.HandoffCode));
    }

    // ── Moving an address ──────────────────────────────────────────────────

    [Fact]
    public async Task A_provider_that_now_asserts_a_different_address_does_not_sign_into_the_old_account()
    {
        /*
         * The account moved to a new address; this subject still points at it.
         * Signing in with the address it left behind must not land back in it,
         * because that address is no longer what identifies this account.
         */
        var h = new Harness();
        var user = await h.SeedUserAsync(passwordHash: null);
        await h.LinkAsync(user);

        // The learner changes their address in the profile.
        user.ChangeEmail(Email.Create(Moved), hasLinkedProvider: true);
        await h.Users.SaveAsync(user, default);

        // …and then signs in with Google, which still asserts the old address.
        h.Provider.Result = Identity(email: Address);
        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(user.Id, await h.Handoffs.ResolveAsync(result.Value!.HandoffCode));
    }

    [Fact]
    public async Task Signing_in_with_the_freed_address_creates_a_new_account()
    {
        var h = new Harness();
        var user = await h.SeedUserAsync(passwordHash: null);
        await h.LinkAsync(user);
        user.ChangeEmail(Email.Create(Moved), hasLinkedProvider: true);
        await h.Users.SaveAsync(user, default);

        h.Provider.Result = Identity(email: Address);
        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        var fresh = await h.Users.FindByEmailAsync(Email.Create(Address), default);
        Assert.NotNull(fresh);
        Assert.NotEqual(user.Id, fresh!.Id);
        Assert.Equal(fresh.Id, await h.Handoffs.ResolveAsync(result.Value!.HandoffCode));

        // And the account that moved is untouched — same id, new address.
        var moved = await h.Users.FindByIdAsync(user.Id, default);
        Assert.Equal(Moved, moved!.Email!.Value.Value);
    }

    [Fact]
    public async Task Signing_in_at_the_new_address_returns_to_the_migrated_account()
    {
        // The other half of the owner's scenario, and the one that makes the
        // change survivable: the learner's own data is still reachable.
        var h = new Harness();
        var user = await h.SeedUserAsync(passwordHash: null);
        await h.LinkAsync(user);
        user.ChangeEmail(Email.Create(Moved), hasLinkedProvider: true);
        await h.Users.SaveAsync(user, default);

        // A different Google account — a new subject — at the new address.
        h.Provider.Result = Identity(email: Moved, subject: "google-subject-2");
        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, await h.Handoffs.ResolveAsync(result.Value!.HandoffCode));
    }

    // ── The three conditions on dropping a link ────────────────────────────

    [Fact]
    public async Task A_provider_that_does_not_vouch_cannot_unlink_an_identity()
    {
        /*
         * <b>Otherwise changing your address at a non-vouching provider is an
         * unlink primitive.</b> Facebook never asserts that an address is
         * verified, so a claim from it proves nothing — and acting on it would
         * let an unverified assertion detach a link the account depends on.
         */
        var h = new Harness { Provider = new FakeExternalIdentityProvider(IdentityProvider.Facebook, false) };
        var user = await h.SeedUserAsync(passwordHash: null);
        await h.LinkAsync(user);

        h.Provider.Result = Identity(
            email: "khac@example.com", provider: IdentityProvider.Facebook);

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, await h.Handoffs.ResolveAsync(result.Value!.HandoffCode));
        Assert.NotNull(
            await h.Identities.FindByProviderAsync(IdentityProvider.Facebook, Subject, default));
    }

    [Fact]
    public async Task A_provider_that_shares_no_address_does_not_destroy_the_existing_link()
    {
        /*
         * <b>Ordering bug, and a permanent one.</b> "No address asserted" is
         * trivially different from the address the account holds, so a
         * stale-check that ran before the address was parsed would drop the
         * link and *then* fail the sign-in with SSO_EMAIL_MISSING — leaving
         * the person unable to get back in, ever.
         */
        var h = new Harness();
        var user = await h.SeedUserAsync(passwordHash: null);
        await h.LinkAsync(user);

        h.Provider.Result = Identity(email: null);
        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, await h.Handoffs.ResolveAsync(result.Value!.HandoffCode));
        Assert.NotNull(
            await h.Identities.FindByProviderAsync(IdentityProvider.Google, Subject, default));
    }

    [Fact]
    public async Task An_account_that_cleared_its_address_keeps_its_link()
    {
        // Null is not "a different address". Treating it as one would mean
        // emptying a profile field silently detaches the provider the person
        // signs in with, and mints them a second account on the next attempt.
        var h = new Harness();
        var user = await h.SeedUserAsync(passwordHash: null);
        await h.LinkAsync(user);
        user.ChangeEmail(null, hasLinkedProvider: true);
        await h.Users.SaveAsync(user, default);

        h.Provider.Result = Identity();
        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, await h.Handoffs.ResolveAsync(result.Value!.HandoffCode));
    }

    // ── Linking into an account that already exists ────────────────────────

    [Fact]
    public async Task A_matching_address_links_to_an_account_that_has_no_password()
    {
        var h = new Harness();
        var user = await h.SeedUserAsync(passwordHash: null);
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, await h.Handoffs.ResolveAsync(result.Value!.HandoffCode));

        var linked = await h.Identities.FindByProviderAsync(IdentityProvider.Google, Subject, default);
        Assert.Equal(user.Id, linked!.UserId);
    }

    [Fact]
    public async Task An_account_with_a_password_is_refused_rather_than_having_it_cleared()
    {
        /*
         * <b>This replaced ADR-0013's squatter eviction, and the difference
         * matters.</b> That branch cleared the existing account's password and
         * revoked its sessions, on the reasoning that an unverified address was
         * only ever a claim. Nothing is verified any more, so the same branch
         * would now fire on the ordinary case: a learner who registered with a
         * phone number, added their own address, and pressed the Google button.
         * Destroying their password for that is indefensible, and refusing
         * costs them nothing — the password is the route the owner intends
         * them to use.
         */
        var h = new Harness();
        var user = await h.SeedUserAsync(passwordHash: "fake:their-own-password");
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.IdentityLinkRequired, result.Error.Code);

        var identity = Assert.Single(await h.Identities.ListForUserAsync(user.Id, default));
        Assert.Equal("fake:their-own-password", identity.PasswordHash);
    }

    [Fact]
    public async Task A_provider_that_does_not_vouch_for_the_address_cannot_link()
    {
        // Linking on an address nobody stands behind is threat T1 with no
        // mitigation at all.
        var h = new Harness { Provider = new FakeExternalIdentityProvider(IdentityProvider.Facebook, false) };
        await h.SeedUserAsync();
        h.Provider.Result = Identity(provider: IdentityProvider.Facebook, emailVerified: true);

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.IdentityLinkRequired, result.Error.Code);
    }

    [Fact]
    public async Task A_vouching_provider_that_reports_the_address_unverified_cannot_link()
    {
        var h = new Harness();
        await h.SeedUserAsync();
        h.Provider.Result = Identity(emailVerified: false);

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.IdentityLinkRequired, result.Error.Code);
    }

    [Fact]
    public async Task A_refused_link_changes_nothing()
    {
        var h = new Harness();
        var user = await h.SeedUserAsync();
        h.Provider.Result = Identity(emailVerified: false);

        await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.Single(await h.Identities.ListForUserAsync(user.Id, default));
    }

    // ── Refusals ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_suspended_account_cannot_be_linked_into()
    {
        var h = new Harness();
        await h.SeedUserAsync(suspended: true, passwordHash: null);
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AccountSuspended, result.Error.Code);
    }

    [Fact]
    public async Task A_suspended_account_cannot_sign_in_through_a_linked_identity()
    {
        var h = new Harness();
        var user = await h.SeedUserAsync(suspended: true, passwordHash: null);
        await h.LinkAsync(user);
        h.Provider.Result = Identity();

        var result = await h.Sut.HandleAsync(await h.ArmAsync(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AccountSuspended, result.Error.Code);
    }

    [Fact]
    public async Task A_provider_that_shares_no_address_on_a_first_sign_in_is_refused()
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
            h.Handoffs, new FixedClock(Now));

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
