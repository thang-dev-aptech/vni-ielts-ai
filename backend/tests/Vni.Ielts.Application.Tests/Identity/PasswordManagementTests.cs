using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// Forgetting, resetting and creating a password.
///
/// The case that matters most is the one the owner hit on 21/08/2026: an
/// account created through Google, which has no password row at all. Both
/// paths here have to create one rather than assume it exists.
/// </summary>
public sealed class PasswordManagementTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
    private const string Address = "hoc.vien@example.com";
    private const string Strong = "mot-mat-khau-du-dai-2026";

    private sealed class FakeResetTokens : IPasswordResetTokens
    {
        private readonly Dictionary<string, UserId> _issued = [];
        public List<UserId> IssuedFor { get; } = [];

        public Task<string> IssueAsync(UserId userId, CancellationToken ct)
        {
            var token = $"reset-{_issued.Count + 1}";
            _issued[token] = userId;
            IssuedFor.Add(userId);
            return Task.FromResult(token);
        }

        public Task<UserId?> RedeemAsync(string token, CancellationToken ct) =>
            Task.FromResult(_issued.Remove(token, out var id) ? id : (UserId?)null);
    }

    private sealed class FakeSender : IVerificationMessageSender
    {
        public List<(string Address, string Token)> Resets { get; } = [];

        public Task SendAsync(Email address, string token, CancellationToken ct) => Task.CompletedTask;

        public Task SendPasswordResetAsync(Email address, string token, CancellationToken ct)
        {
            Resets.Add((address.Value, token));
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeUserIdentityRepository Identities { get; } = new();
        public FakePasswordHasher Hasher { get; } = new();
        public FakeTokenService Sessions { get; } = new();
        public FakeResetTokens Tokens { get; } = new();
        public FakeSender Sender { get; } = new();

        public RequestPasswordReset Request => new(Users, Tokens, Sender);
        public ResetPassword Reset =>
            new(Users, Identities, Tokens, Hasher, Sessions, new FixedClock(Now));
        public SetPassword Set => new(Users, Identities, Hasher, Sessions, new FixedClock(Now));

        /// <param name="password">Null models an account created through Google.</param>
        public async Task<User> SeedAsync(string? password, bool verified = true, bool suspended = false)
        {
            var email = Email.Create(Address);
            var user = User.Register(email, "Học viên", Now);
            if (verified) user.MarkEmailVerified();
            if (suspended) user.Suspend();

            await Users.AddAsync(user, default);

            if (password is not null)
            {
                await Identities.AddAsync(
                    UserIdentity.ForEmail(user.Id, email, Hasher.Hash(password), Now), default);
            }
            else
            {
                await Identities.AddAsync(
                    UserIdentity.ForSocial(user.Id, IdentityProvider.Google, "google-sub", Now), default);
            }

            return user;
        }

        public string? StoredHash(UserId id) =>
            Identities.ListForUserAsync(id, default).Result
                .FirstOrDefault(i => i.Provider == IdentityProvider.Email)?.PasswordHash;
    }

    // ── Forgetting ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_reset_link_is_sent_for_a_real_address()
    {
        var h = new Harness();
        await h.SeedAsync("old-password-here");

        await h.Request.HandleAsync(new RequestPasswordResetCommand(Address), default);

        Assert.Single(h.Sender.Resets);
    }

    [Fact]
    public async Task An_account_created_through_Google_can_still_ask_for_one()
    {
        // The whole point. Google verified the address, so a link sent there
        // reaches its owner — this is how someone who only ever pressed the
        // Google button gains a password.
        var h = new Harness();
        await h.SeedAsync(password: null);

        await h.Request.HandleAsync(new RequestPasswordResetCommand(Address), default);

        Assert.Single(h.Sender.Resets);
    }

    [Theory]
    [InlineData("khong-ai-dung@example.com")]
    [InlineData("khong-phai-email")]
    public async Task An_unknown_address_is_silently_ignored(string address)
    {
        // Same outward behaviour as a real one. Anything else is a free
        // account-enumeration oracle. → threat T4
        var h = new Harness();
        await h.SeedAsync("old-password-here");

        await h.Request.HandleAsync(new RequestPasswordResetCommand(address), default);

        Assert.Empty(h.Sender.Resets);
    }

    [Fact]
    public async Task A_suspended_account_gets_no_way_back_in()
    {
        var h = new Harness();
        await h.SeedAsync("old-password-here", suspended: true);

        await h.Request.HandleAsync(new RequestPasswordResetCommand(Address), default);

        Assert.Empty(h.Sender.Resets);
    }

    // ── Resetting ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_valid_link_sets_the_password_and_ends_every_session()
    {
        // A reset is what someone does when they fear the account is
        // compromised. Leaving the attacker's session alive makes it theatre.
        var h = new Harness();
        var user = await h.SeedAsync("old-password-here");
        var token = await h.Tokens.IssueAsync(user.Id, default);

        var result = await h.Reset.HandleAsync(new ResetPasswordCommand(token, Strong), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(h.Hasher.Hash(Strong), h.StoredHash(user.Id));
        Assert.Contains(user.Id, h.Sessions.RevokedAllFor);
    }

    [Fact]
    public async Task Resetting_a_Google_account_creates_the_password_it_never_had()
    {
        var h = new Harness();
        var user = await h.SeedAsync(password: null);
        var token = await h.Tokens.IssueAsync(user.Id, default);

        var result = await h.Reset.HandleAsync(new ResetPasswordCommand(token, Strong), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(h.Hasher.Hash(Strong), h.StoredHash(user.Id));
    }

    [Fact]
    public async Task Redeeming_a_link_also_verifies_the_address()
    {
        // Receiving mail at an address is the same proof verification asks for.
        var h = new Harness();
        var user = await h.SeedAsync("old-password-here", verified: false);
        var token = await h.Tokens.IssueAsync(user.Id, default);

        await h.Reset.HandleAsync(new ResetPasswordCommand(token, Strong), default);

        Assert.True((await h.Users.FindByIdAsync(user.Id, default))!.EmailVerified);
    }

    [Fact]
    public async Task A_link_works_once()
    {
        var h = new Harness();
        var user = await h.SeedAsync("old-password-here");
        var token = await h.Tokens.IssueAsync(user.Id, default);

        Assert.True((await h.Reset.HandleAsync(new ResetPasswordCommand(token, Strong), default)).IsSuccess);

        var replay = await h.Reset.HandleAsync(new ResetPasswordCommand(token, Strong), default);
        Assert.Equal(ErrorCodes.ResetTokenInvalid, replay.Error.Code);
    }

    [Fact]
    public async Task A_weak_password_is_refused_before_the_link_is_spent()
    {
        // Order matters: validating after redemption would burn the token and
        // leave someone with a dead link and the same weak password.
        var h = new Harness();
        var user = await h.SeedAsync("old-password-here");
        var token = await h.Tokens.IssueAsync(user.Id, default);

        var weak = await h.Reset.HandleAsync(new ResetPasswordCommand(token, "123"), default);
        Assert.Equal(ErrorCodes.PasswordTooWeak, weak.Error.Code);

        var retry = await h.Reset.HandleAsync(new ResetPasswordCommand(token, Strong), default);
        Assert.True(retry.IsSuccess);
    }

    // ── Creating and changing while signed in ──────────────────────────────

    [Fact]
    public async Task A_Google_account_can_create_a_first_password_with_nothing_to_prove()
    {
        // There is no current password to ask for, and demanding one would
        // make this impossible — which is the dead end the owner reported.
        var h = new Harness();
        var user = await h.SeedAsync(password: null);

        var result = await h.Set.HandleAsync(
            new SetPasswordCommand(user.Id, CurrentPassword: null, Strong, "fam-here"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(h.Hasher.Hash(Strong), h.StoredHash(user.Id));
    }

    [Fact]
    public async Task Changing_an_existing_password_requires_the_current_one()
    {
        // A stolen access token must not be enough to lock the owner out.
        var h = new Harness();
        var user = await h.SeedAsync("old-password-here");

        var wrong = await h.Set.HandleAsync(
            new SetPasswordCommand(user.Id, "doan-sai", Strong, "fam-here"), default);

        Assert.Equal(ErrorCodes.CurrentPasswordWrong, wrong.Error.Code);
        Assert.Equal(h.Hasher.Hash("old-password-here"), h.StoredHash(user.Id));
    }

    [Fact]
    public async Task Changing_with_the_right_current_password_works()
    {
        var h = new Harness();
        var user = await h.SeedAsync("old-password-here");

        var result = await h.Set.HandleAsync(
            new SetPasswordCommand(user.Id, "old-password-here", Strong, "fam-here"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(h.Hasher.Hash(Strong), h.StoredHash(user.Id));
    }

    [Fact]
    public async Task Setting_a_password_signs_other_devices_out_but_not_this_one()
    {
        // Being signed out of the page you are standing on is a bug; leaving a
        // month-old session alive through a password change is a hole.
        var h = new Harness();
        var user = await h.SeedAsync(password: null);

        await h.Set.HandleAsync(
            new SetPasswordCommand(user.Id, null, Strong, "fam-here"), default);

        Assert.Equal([(user.Id, "fam-here")], h.Sessions.RevokedAllExcept);
        Assert.Empty(h.Sessions.RevokedAllFor);
    }
}
