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
/// </summary>
public sealed class LoginWithPasswordTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private static async Task<(LoginWithPassword Sut, FakePasswordHasher Hasher, FakeTokenService Tokens, User User)>
        BuildAsync(string email = "hoc.vien@example.com", string password = "correct-horse-battery",
                   bool suspended = false)
    {
        var users = new FakeUserRepository();
        var identities = new FakeUserIdentityRepository();
        var hasher = new FakePasswordHasher();
        var tokens = new FakeTokenService();

        var address = Email.Create(email);
        var user = User.Register(address, "Học viên", Now);
        if (suspended) user.Suspend();

        await users.AddAsync(user, default);
        await identities.AddAsync(
            UserIdentity.ForEmail(user.Id, address, hasher.Hash(password), Now), default);

        var sut = new LoginWithPassword(
            users, identities, hasher, new FakePermissionResolver(PermissionKeys.ExamRead), tokens);

        return (sut, hasher, tokens, user);
    }

    [Fact]
    public async Task Correct_credentials_issue_a_token_pair()
    {
        var (sut, _, tokens, user) = await BuildAsync();

        var result = await sut.HandleAsync(
            new LoginCommand("hoc.vien@example.com", "correct-horse-battery"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value!.UserId);
        Assert.Equal("access", result.Value.Tokens.AccessToken);
        Assert.Equal(1, tokens.IssueCallCount);
    }

    [Fact]
    public async Task Email_is_matched_case_insensitively()
    {
        // Otherwise one person ends up with two accounts depending on how
        // their keyboard capitalised the first letter.
        var (sut, _, _, _) = await BuildAsync();

        var result = await sut.HandleAsync(
            new LoginCommand("Hoc.Vien@EXAMPLE.com", "correct-horse-battery"), default);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("hoc.vien@example.com", "wrong-password")]   // right user, wrong password
    [InlineData("khong.ton.tai@example.com", "any-password")] // no such user
    [InlineData("not-an-email", "any-password")]              // malformed address
    public async Task Every_failure_reports_the_same_code(string email, string password)
    {
        // The point of this test: an attacker must not be able to tell these
        // apart. If someone later "improves" the error messages, this fails.
        var (sut, _, tokens, _) = await BuildAsync();

        var result = await sut.HandleAsync(new LoginCommand(email, password), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.InvalidCredentials, result.Error.Code);
        Assert.Equal(ErrorKind.Unauthorized, result.Error.Kind);
        Assert.Equal(0, tokens.IssueCallCount);
    }

    [Theory]
    [InlineData("khong.ton.tai@example.com")]
    [InlineData("not-an-email")]
    public async Task A_hash_is_verified_even_when_the_user_cannot_exist(string email)
    {
        // Returning early would make a miss measurably faster than a real
        // attempt, which is a timing oracle for account existence.
        var (sut, hasher, _, _) = await BuildAsync();
        var before = hasher.VerifyCallCount;

        await sut.HandleAsync(new LoginCommand(email, "any-password"), default);

        Assert.True(
            hasher.VerifyCallCount > before,
            "No hash was verified, so this path returns faster than a real attempt.");
    }

    [Fact]
    public async Task A_suspended_account_is_told_so_rather_than_getting_invalid_credentials()
    {
        // The deliberate exception to the indistinguishability rule. Someone
        // whose access was withdrawn should not spend an afternoon resetting a
        // password that was never wrong.
        var (sut, _, tokens, _) = await BuildAsync(suspended: true);

        var result = await sut.HandleAsync(
            new LoginCommand("hoc.vien@example.com", "correct-horse-battery"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AccountSuspended, result.Error.Code);
        Assert.Equal(ErrorKind.Forbidden, result.Error.Kind);
        Assert.Equal(0, tokens.IssueCallCount);
    }

    [Fact]
    public async Task An_account_with_only_a_social_identity_cannot_be_password_guessed()
    {
        var users = new FakeUserRepository();
        var identities = new FakeUserIdentityRepository();
        var hasher = new FakePasswordHasher();

        var address = Email.Create("google.only@example.com");
        var user = User.Register(address, "Google Only", Now);
        await users.AddAsync(user, default);
        await identities.AddAsync(
            UserIdentity.ForSocial(user.Id, IdentityProvider.Google, "google-subject-123", Now), default);

        var sut = new LoginWithPassword(
            users, identities, hasher, new FakePermissionResolver(), new FakeTokenService());

        var result = await sut.HandleAsync(
            new LoginCommand("google.only@example.com", "anything"), default);

        Assert.False(result.IsSuccess);
        // Not "this account uses Google" — that would confirm the address exists.
        Assert.Equal(ErrorCodes.InvalidCredentials, result.Error.Code);
    }
}
