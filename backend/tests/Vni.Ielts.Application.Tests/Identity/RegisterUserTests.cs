using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// Registration, after the 08/09/2026 owner decision.
///
/// <para>
/// <i>"register chỉ cần điền: Họ và tên, số điện thoại, mật khẩu, nhập lại mật
/// khẩu → tạo xong ở profile phần email bỏ trống → như vậy sẽ không cần tính
/// năng verify nữa bỏ luôn"</i> — a name, a number and a password; the address
/// field starts empty; verification is gone.
/// </para>
///
/// <para>
/// Two of these exist to stop a well-meaning reinstatement. The handler used
/// to argue that a fresh account must not be signed in because its address was
/// an unproven claim; there is no address at registration any more, and there
/// never was any code that refused an unverified account anything.
/// </para>
/// </summary>
public sealed class RegisterUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeUserIdentityRepository Identities { get; } = new();
        public FakeRoleRepository Roles { get; } = new();
        public FakePasswordHasher Hasher { get; } = new();
        public FakeTokenService Sessions { get; } = new();

        public RegisterUser Sut => new(
            Users, Identities, Roles, Hasher,
            new FakePermissionResolver(PermissionKeys.ExamRead), Sessions, new FixedClock(Now));
    }

    private const string Password = "mot-mat-khau-du-dai-2026";

    private static RegisterUserCommand Command(string phone = "0912345678") =>
        new(phone, Password, "Hoc vien");

    [Fact]
    public async Task Registering_signs_the_new_account_in()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("access", result.Value!.Session.Tokens.AccessToken);
        Assert.Equal("refresh", result.Value.Session.Tokens.RefreshToken);
        Assert.Equal("Hoc vien", result.Value.Session.DisplayName);
        Assert.Equal(1, h.Sessions.IssueCallCount);
    }

    [Fact]
    public async Task The_session_starts_a_new_token_family()
    {
        // `familyId: null`, exactly as a password sign-in does. Continuing a
        // family that does not exist is how reuse detection ends up covering
        // nothing. → ITokenService.IssueAsync
        var h = new Harness();

        await h.Sut.HandleAsync(Command(), default);

        Assert.Equal([null], h.Sessions.IssuedFamilies);
    }

    [Fact]
    public async Task The_account_starts_with_the_number_and_no_address()
    {
        // The profile shows an empty email field, which is the owner's
        // instruction — not a placeholder, not the phone number repeated into
        // a fake address to satisfy a non-null column.
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command(), default);

        var user = await h.Users.FindByIdAsync(result.Value!.Session.UserId, default);
        Assert.Null(user!.Email);
        Assert.Equal("+84912345678", user.Phone!.Value.Value);
    }

    [Fact]
    public async Task The_number_is_stored_normalised_however_it_was_typed()
    {
        // Two people typing the same number two ways must not become two
        // accounts — the number is the handle they will sign in with.
        var h = new Harness();

        await h.Sut.HandleAsync(Command("091 234 5678"), default);
        var again = await h.Sut.HandleAsync(Command("+84912345678"), default);

        Assert.False(again.IsSuccess);
        Assert.Equal(ErrorCodes.PhoneAlreadyRegistered, again.Error.Code);
    }

    [Fact]
    public async Task The_learner_role_is_assigned()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command(), default);

        var learner = await h.Roles.FindByNameAsync(SystemRoles.Learner, default);
        var user = await h.Users.FindByIdAsync(result.Value!.Session.UserId, default);
        Assert.True(user!.HasRole(learner!.Id));
    }

    [Fact]
    public async Task A_password_identity_is_created_keyed_by_the_account()
    {
        /*
         * <b>Keyed by the account id, not by a handle.</b> That is what lets
         * one row serve both sign-in routes: the learner can add an address
         * later and use either, without a second row carrying a duplicate hash
         * that the next password change would forget to update.
         */
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command(), default);
        var userId = result.Value!.Session.UserId;

        var identity = await h.Identities.FindByProviderAsync(
            IdentityProvider.Password, userId.Value, default);

        Assert.NotNull(identity);
        Assert.Equal(h.Hasher.Hash(Password), identity!.PasswordHash);
    }

    [Fact]
    public async Task A_number_that_already_has_an_account_is_refused_without_a_session()
    {
        var h = new Harness();
        await h.Sut.HandleAsync(Command(), default);

        var again = await h.Sut.HandleAsync(Command(), default);

        Assert.False(again.IsSuccess);
        Assert.Equal(ErrorCodes.PhoneAlreadyRegistered, again.Error.Code);

        // One session from the first registration, none from the refusal.
        Assert.Equal(1, h.Sessions.IssueCallCount);
    }

    [Fact]
    public async Task Losing_the_unique_index_race_reads_as_the_same_conflict()
    {
        // The pre-check is a courtesy that produces a clean message; the index
        // is the guarantee. Both paths have to answer identically or the loser
        // of a race sees a 500 for an ordinary duplicate.
        var h = new Harness();
        h.Users.ThrowDuplicatePhoneOnNextAdd = true;

        var result = await h.Sut.HandleAsync(Command(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PhoneAlreadyRegistered, result.Error.Code);
        Assert.Equal(0, h.Sessions.IssueCallCount);
    }

    [Theory]
    [InlineData("khong-phai-so")]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("912345678")]
    public async Task A_number_that_is_not_usable_is_refused(string phone)
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command(phone), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PhoneInvalid, result.Error.Code);
        Assert.Equal(0, h.Sessions.IssueCallCount);
    }

    [Fact]
    public async Task A_weak_password_is_refused_before_anything_is_written()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(
            new RegisterUserCommand("0912345678", "ngan", "Hoc vien"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, h.Sessions.IssueCallCount);

        var (users, _) = await h.Users.ListAsync(null, 0, 10, default);
        Assert.Empty(users);
    }

    [Fact]
    public async Task A_missing_display_name_is_refused()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(
            new RegisterUserCommand("0912345678", Password, "  "), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error.Code);
    }
}
