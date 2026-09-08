using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// These are mostly not happy-path tests.
///
/// The security-relevant behaviour of a login endpoint is what it does when it
/// fails, and specifically whether the failures are distinguishable. A test
/// suite that only proves "correct password works" would pass on an
/// implementation that leaks account existence on every request.
///
/// <para>
/// Since 08/09/2026 there are two kinds of handle. The account under test here
/// has both — a number from registration and an address added afterwards —
/// because the interesting property is that either reaches it and neither
/// reaches anything else.
/// </para>
/// </summary>
public sealed class LoginWithPasswordTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private const string Phone = "0912345678";
    private const string NormalisedPhone = "+84912345678";
    private const string Address = "hoc.vien@example.com";
    private const string Correct = "correct-horse-battery";

    private sealed record Harness(
        LoginWithPassword Sut,
        FakePasswordHasher Hasher,
        FakeTokenService Tokens,
        FakeLoginThrottle Throttle,
        User User);

    private static async Task<Harness> BuildAsync(
        bool withAddress = true, bool suspended = false, FakeLoginThrottle? throttle = null)
    {
        var users = new FakeUserRepository();
        var identities = new FakeUserIdentityRepository();
        var hasher = new FakePasswordHasher();
        var tokens = new FakeTokenService();
        var loginThrottle = throttle ?? new FakeLoginThrottle();

        var user = User.Register(PhoneNumber.Create(Phone), "Hoc vien", Now);
        if (withAddress) user.ChangeEmail(Email.Create(Address), hasLinkedProvider: false);
        if (suspended) user.Suspend();

        await users.AddAsync(user, default);
        await identities.AddAsync(
            UserIdentity.ForPassword(user.Id, hasher.Hash(Correct), Now), default);

        var sut = new LoginWithPassword(
            users, identities, hasher, new FakePermissionResolver(PermissionKeys.ExamRead),
            tokens, loginThrottle);

        return new Harness(sut, hasher, tokens, loginThrottle, user);
    }

    [Fact]
    public async Task A_learner_signs_in_with_the_phone_they_registered_with()
    {
        // The ordinary case now: registration asks for a number and nothing
        // else, so for most accounts this is the only handle that exists.
        var h = await BuildAsync(withAddress: false);

        var result = await h.Sut.HandleAsync(new LoginCommand(Phone, Correct), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(h.User.Id, result.Value!.UserId);
        Assert.Equal("access", result.Value.Tokens.AccessToken);
        Assert.Equal(1, h.Tokens.IssueCallCount);
    }

    [Theory]
    [InlineData("0912345678")]
    [InlineData("091 234 5678")]
    [InlineData("091-234-5678")]
    [InlineData("+84912345678")]
    [InlineData("+84 912 345 678")]
    public async Task However_they_spell_it(string typed)
    {
        var h = await BuildAsync(withAddress: false);

        var result = await h.Sut.HandleAsync(new LoginCommand(typed, Correct), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(h.User.Id, result.Value!.UserId);
    }

    [Fact]
    public async Task A_learner_who_added_an_address_can_sign_in_with_it_too()
    {
        // One password row, two handles. The alternative — a second identity
        // row per handle — would mean two copies of one hash.
        var h = await BuildAsync();

        var result = await h.Sut.HandleAsync(new LoginCommand(Address, Correct), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(h.User.Id, result.Value!.UserId);
    }

    [Fact]
    public async Task The_address_is_matched_case_insensitively()
    {
        var h = await BuildAsync();

        var result = await h.Sut.HandleAsync(
            new LoginCommand("HOC.VIEN@Example.COM", Correct), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_phone_and_an_address_belonging_to_different_accounts_do_not_cross()
    {
        /*
         * The handle decides which lookup runs, and the lookups must not bleed
         * into one another: signing in with one account's number and another
         * account's password has to fail, and so does the reverse.
         */
        var users = new FakeUserRepository();
        var identities = new FakeUserIdentityRepository();
        var hasher = new FakePasswordHasher();

        var byPhone = User.Register(PhoneNumber.Create("0912345678"), "Mot", Now);
        await users.AddAsync(byPhone, default);
        await identities.AddAsync(
            UserIdentity.ForPassword(byPhone.Id, hasher.Hash("mat-khau-cua-mot"), Now), default);

        var byEmail = User.Register(PhoneNumber.Create("0987654321"), "Hai", Now);
        byEmail.ChangeEmail(Email.Create("hai@example.com"), hasLinkedProvider: false);
        await users.AddAsync(byEmail, default);
        await identities.AddAsync(
            UserIdentity.ForPassword(byEmail.Id, hasher.Hash("mat-khau-cua-hai"), Now), default);

        var sut = new LoginWithPassword(
            users, identities, hasher, new FakePermissionResolver(), new FakeTokenService(),
            new FakeLoginThrottle());

        var crossed = await sut.HandleAsync(
            new LoginCommand("0912345678", "mat-khau-cua-hai"), default);
        Assert.Equal(ErrorCodes.InvalidCredentials, crossed.Error.Code);

        var mine = await sut.HandleAsync(
            new LoginCommand("hai@example.com", "mat-khau-cua-hai"), default);
        Assert.Equal(byEmail.Id, mine.Value!.UserId);
    }

    [Theory]
    [InlineData(Address, "wrong-password")]        // right account, wrong password
    [InlineData("khong.ton.tai@example.com", "any-password")] // no such address
    [InlineData("0900000000", "any-password")]     // no such number
    [InlineData("not-an-email", "any-password")]   // not a handle at all
    public async Task Every_failure_reports_the_same_code(string handle, string password)
    {
        // The point of this test: an attacker must not be able to tell these
        // apart. If someone later "improves" the error messages, this fails.
        var h = await BuildAsync();

        var result = await h.Sut.HandleAsync(new LoginCommand(handle, password), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.InvalidCredentials, result.Error.Code);
        Assert.Equal(ErrorKind.Unauthorized, result.Error.Kind);
        Assert.Equal(0, h.Tokens.IssueCallCount);
    }

    [Theory]
    [InlineData("khong.ton.tai@example.com")]
    [InlineData("0900000000")]
    [InlineData("not-an-email")]
    public async Task A_hash_is_verified_even_when_the_account_cannot_exist(string handle)
    {
        // Returning early would make a miss measurably faster than a real
        // attempt, which is a timing oracle for account existence.
        var h = await BuildAsync();
        var before = h.Hasher.VerifyCallCount;

        await h.Sut.HandleAsync(new LoginCommand(handle, "any-password"), default);

        Assert.True(
            h.Hasher.VerifyCallCount > before,
            "No hash was verified, so this path returns faster than a real attempt.");
    }

    [Fact]
    public async Task A_suspended_account_is_told_so_rather_than_getting_invalid_credentials()
    {
        // The deliberate exception to the indistinguishability rule. Someone
        // whose access was withdrawn should not spend an afternoon resetting a
        // password that was never wrong.
        var h = await BuildAsync(suspended: true);

        var result = await h.Sut.HandleAsync(new LoginCommand(Address, Correct), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AccountSuspended, result.Error.Code);
        Assert.Equal(ErrorKind.Forbidden, result.Error.Kind);
        Assert.Equal(0, h.Tokens.IssueCallCount);
    }

    [Fact]
    public async Task An_account_with_only_a_social_identity_cannot_be_password_guessed()
    {
        var users = new FakeUserRepository();
        var identities = new FakeUserIdentityRepository();
        var hasher = new FakePasswordHasher();

        var user = User.RegisterFromProvider(
            Email.Create("google.only@example.com"), "Google Only", Now);
        await users.AddAsync(user, default);
        await identities.AddAsync(
            UserIdentity.ForSocial(user.Id, IdentityProvider.Google, "google-subject-123", Now),
            default);

        var sut = new LoginWithPassword(
            users, identities, hasher, new FakePermissionResolver(), new FakeTokenService(),
            new FakeLoginThrottle());

        var result = await sut.HandleAsync(
            new LoginCommand("google.only@example.com", "anything"), default);

        Assert.False(result.IsSuccess);
        // Not "this account uses Google" — that would confirm the address exists.
        Assert.Equal(ErrorCodes.InvalidCredentials, result.Error.Code);
    }

    // ── Bounded guessing ────────────────────────────────────────────────────

    [Fact]
    public async Task Ten_wrong_passwords_lock_the_handle()
    {
        var h = await BuildAsync();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var wrong = await h.Sut.HandleAsync(new LoginCommand(Address, "wrong"), default);
            Assert.Equal(ErrorCodes.InvalidCredentials, wrong.Error.Code);
        }

        // The eleventh is refused before the password is even considered —
        // note this one passes the CORRECT password and is still refused.
        var locked = await h.Sut.HandleAsync(new LoginCommand(Address, Correct), default);

        Assert.False(locked.IsSuccess);
        Assert.Equal(ErrorCodes.TooManyAttempts, locked.Error.Code);
        Assert.Equal(ErrorKind.TooManyRequests, locked.Error.Kind);
    }

    [Fact]
    public async Task A_failed_sign_in_by_phone_counts_against_one_lock_whatever_the_spelling()
    {
        /*
         * <b>Otherwise ten attempts silently becomes forty.</b> The throttle
         * store keys on the string it is handed and only trims and lower-cases
         * it, so `0912345678`, `+84912345678`, `091 234 5678` and
         * `(091) 234-5678` are four independent counters for one account
         * unless the handle is normalised before the check. This was invisible
         * while every handle was an address, because parsing an address
         * lower-cases it anyway.
         */
        var h = await BuildAsync(withAddress: false);

        foreach (var spelling in new[]
                 {
                     "0912345678", "+84912345678", "091 234 5678", "(091) 234-5678",
                     "091-234-5678", "0912345678", "+84 912 345 678", "091.234.5678",
                     "0912 345 678", "+84912345678",
                 })
        {
            await h.Sut.HandleAsync(new LoginCommand(spelling, "wrong"), default);
        }

        Assert.Equal(10, h.Throttle.Failures[NormalisedPhone]);

        var locked = await h.Sut.HandleAsync(new LoginCommand(Phone, Correct), default);
        Assert.Equal(ErrorCodes.TooManyAttempts, locked.Error.Code);
    }

    [Fact]
    public async Task A_handle_with_no_account_is_counted_too()
    {
        // Otherwise the lockout becomes the account-enumeration oracle that
        // every other branch of this handler works to avoid: an attacker would
        // learn which handles are registered by seeing which ones can lock.
        var h = await BuildAsync();

        await h.Sut.HandleAsync(new LoginCommand("nobody@example.com", "guess"), default);

        Assert.Equal(1, h.Throttle.Failures["nobody@example.com"]);
    }

    [Fact]
    public async Task A_successful_sign_in_clears_the_count()
    {
        var h = await BuildAsync();

        await h.Sut.HandleAsync(new LoginCommand(Address, "wrong"), default);
        Assert.Equal(1, h.Throttle.Failures[Address]);

        await h.Sut.HandleAsync(new LoginCommand(Address, Correct), default);

        Assert.Empty(h.Throttle.Failures);
    }

    [Fact]
    public async Task A_suspended_account_signing_in_correctly_does_not_clear_the_count()
    {
        // The suspended branch returns before the clear. Otherwise someone
        // holding the right password for a suspended, locked-out account could
        // reset the counter at will and keep guessing at everything else.
        var h = await BuildAsync(suspended: true);

        await h.Sut.HandleAsync(new LoginCommand(Address, "wrong"), default);

        var suspended = await h.Sut.HandleAsync(new LoginCommand(Address, Correct), default);

        Assert.Equal(ErrorCodes.AccountSuspended, suspended.Error.Code);
        Assert.Equal(1, h.Throttle.Failures[Address]);
    }
}
