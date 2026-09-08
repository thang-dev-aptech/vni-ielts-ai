using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// Creating and changing a password while signed in.
///
/// <para>
/// <b>"Forgot my password" is no longer here.</b> Recovery used to be a link
/// mailed to the account's address; registration takes no address as of
/// 08/09/2026, so for most accounts there was nowhere to send one. The owner
/// replaced it with a Zalo contact link and an operator using
/// <c>POST /admin/users/{id}/password</c>. What remains in this file is the
/// signed-in path, which is unaffected.
/// </para>
///
/// <para>
/// The case that matters most is still the one the owner hit on 21/08/2026: an
/// account created through Google has no password row at all, so this has to
/// create one rather than assume it exists.
/// </para>
/// </summary>
public sealed class PasswordManagementTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);
    private const string Strong = "mot-mat-khau-du-dai-2026";

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeUserIdentityRepository Identities { get; } = new();
        public FakePasswordHasher Hasher { get; } = new();
        public FakeTokenService Sessions { get; } = new();

        public SetPassword Set => new(Users, Identities, Hasher, Sessions, new FixedClock(Now));

        /// <param name="password">Null models an account created through Google.</param>
        public async Task<User> SeedAsync(string? password, bool suspended = false)
        {
            var user = password is null
                ? User.RegisterFromProvider(Email.Create("hoc.vien@example.com"), "Hoc vien", Now)
                : User.Register(PhoneNumber.Create("0912345678"), "Hoc vien", Now);

            if (suspended) user.Suspend();

            await Users.AddAsync(user, default);

            if (password is not null)
            {
                await Identities.AddAsync(
                    UserIdentity.ForPassword(user.Id, Hasher.Hash(password), Now), default);
            }
            else
            {
                await Identities.AddAsync(
                    UserIdentity.ForSocial(user.Id, IdentityProvider.Google, "google-sub", Now),
                    default);
            }

            return user;
        }

        public string? StoredHash(UserId id) =>
            Identities.ListForUserAsync(id, default).Result
                .FirstOrDefault(i => i.Provider == IdentityProvider.Password)?.PasswordHash;
    }

    [Fact]
    public async Task A_Google_account_can_create_a_first_password_with_nothing_to_prove()
    {
        // There is no current password to ask for, and asking would be asking
        // for something that does not exist.
        var h = new Harness();
        var user = await h.SeedAsync(password: null);

        var result = await h.Set.HandleAsync(
            new SetPasswordCommand(user.Id, CurrentPassword: null, Strong, "fam-here"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(h.Hasher.Hash(Strong), h.StoredHash(user.Id));
    }

    [Fact]
    public async Task The_password_row_it_creates_is_keyed_by_the_account()
    {
        /*
         * Not by the address. A Google account may go on to add a phone number
         * and sign in with either, and one row keyed by account is what makes
         * that possible without a second row holding a duplicate hash.
         */
        var h = new Harness();
        var user = await h.SeedAsync(password: null);

        await h.Set.HandleAsync(
            new SetPasswordCommand(user.Id, null, Strong, "fam-here"), default);

        var identity = await h.Identities.FindByProviderAsync(
            IdentityProvider.Password, user.Id.Value, default);

        Assert.NotNull(identity);
    }

    [Fact]
    public async Task Changing_an_existing_password_requires_the_current_one()
    {
        // A stolen access token must not be enough to lock the real owner out.
        var h = new Harness();
        var user = await h.SeedAsync("old-password-here");

        var result = await h.Set.HandleAsync(
            new SetPasswordCommand(user.Id, "doan-sai", Strong, "fam-here"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CurrentPasswordWrong, result.Error.Code);
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
    public async Task A_weak_password_is_refused_before_anything_is_written()
    {
        var h = new Harness();
        var user = await h.SeedAsync("old-password-here");

        var result = await h.Set.HandleAsync(
            new SetPasswordCommand(user.Id, "old-password-here", "ngan", "fam-here"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(h.Hasher.Hash("old-password-here"), h.StoredHash(user.Id));
    }

    [Fact]
    public async Task A_suspended_account_cannot_set_one()
    {
        var h = new Harness();
        var user = await h.SeedAsync("old-password-here", suspended: true);

        var result = await h.Set.HandleAsync(
            new SetPasswordCommand(user.Id, "old-password-here", Strong, "fam-here"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AccountSuspended, result.Error.Code);
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
