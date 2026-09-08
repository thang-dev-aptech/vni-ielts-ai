using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// The phone number, now that it is a sign-in handle rather than a contact
/// detail.
///
/// <para>
/// Two things changed on 08/09/2026 and both are load-bearing: it must be
/// unique, because "sign in with your phone number" is otherwise ambiguous;
/// and it cannot be cleared away when it is the only handle the account has,
/// because registration no longer collects an address to fall back on.
/// </para>
///
/// <para>
/// What did <i>not</i> change: nothing verifies it. There is no OTP here and
/// no requirement asking for one, so nothing in this file should grow one by
/// accident.
/// </para>
/// </summary>
public sealed class SetPhoneTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeUserIdentityRepository Identities { get; } = new();

        public SetPhone Sut => new(Users, Identities);

        public async Task<User> WithPhoneAsync(string phone = "0912345678")
        {
            var user = User.Register(PhoneNumber.Create(phone), "Hoc vien", Now);
            await Users.AddAsync(user, default);
            await Identities.AddAsync(UserIdentity.ForPassword(user.Id, "hash", Now), default);
            return user;
        }

        public async Task<User> FromProviderAsync(string email = "toi@example.com")
        {
            var user = User.RegisterFromProvider(Email.Create(email), "Hoc vien", Now);
            await Users.AddAsync(user, default);
            await Identities.AddAsync(
                UserIdentity.ForSocial(user.Id, IdentityProvider.Google, "sub-1", Now), default);
            return user;
        }
    }

    [Fact]
    public async Task A_number_is_stored_normalised_however_it_was_typed()
    {
        var h = new Harness();
        var user = await h.FromProviderAsync();

        var result = await h.Sut.HandleAsync(
            new SetPhoneCommand(user.Id, "091 234 5678"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("+84912345678", result.Value);
    }

    [Fact]
    public async Task A_number_another_account_already_has_is_refused()
    {
        // Two accounts sharing one number would make sign-in by number
        // ambiguous, and nothing downstream could resolve it.
        var h = new Harness();
        await h.WithPhoneAsync("0912345678");
        var second = await h.FromProviderAsync();

        var result = await h.Sut.HandleAsync(
            new SetPhoneCommand(second.Id, "+84 912 345 678"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PhoneAlreadyRegistered, result.Error.Code);
    }

    [Fact]
    public async Task Losing_the_unique_index_race_reads_as_the_same_conflict()
    {
        /*
         * This path reaches the index through a replace rather than an insert,
         * and the repository translated nothing there until 08/09/2026 — so a
         * real race surfaced as a 500 rather than the conflict the caller
         * already knows how to render. → MongoUserRepository.SaveAsync
         */
        var h = new Harness();
        var user = await h.FromProviderAsync();
        h.Users.ThrowDuplicatePhoneOnNextSave = true;

        var result = await h.Sut.HandleAsync(
            new SetPhoneCommand(user.Id, "0912345678"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PhoneAlreadyRegistered, result.Error.Code);
    }

    [Fact]
    public async Task Keeping_your_own_number_is_not_a_conflict_with_yourself()
    {
        var h = new Harness();
        var user = await h.WithPhoneAsync("0912345678");

        var result = await h.Sut.HandleAsync(
            new SetPhoneCommand(user.Id, "+84912345678"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("+84912345678", result.Value);
    }

    [Fact]
    public async Task A_learner_with_no_email_cannot_clear_their_phone()
    {
        // The ordinary account since 08/09/2026: a number and nothing else.
        // Clearing it is an ordinary-looking profile edit that locks the
        // account shut permanently.
        var h = new Harness();
        var user = await h.WithPhoneAsync();

        var result = await h.Sut.HandleAsync(new SetPhoneCommand(user.Id, null), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SignInMethodRequired, result.Error.Code);

        var stored = await h.Users.FindByIdAsync(user.Id, default);
        Assert.Equal("+84912345678", stored!.Phone!.Value.Value);
    }

    [Fact]
    public async Task A_number_can_be_cleared_once_an_address_is_on_the_account()
    {
        var h = new Harness();
        var user = await h.WithPhoneAsync();
        user.ChangeEmail(Email.Create("toi@example.com"), hasLinkedProvider: false);
        await h.Users.SaveAsync(user, default);

        var result = await h.Sut.HandleAsync(new SetPhoneCommand(user.Id, "  "), default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task The_guard_counts_a_linked_provider_as_a_handle()
    {
        var h = new Harness();
        var user = await h.FromProviderAsync();
        await h.Sut.HandleAsync(new SetPhoneCommand(user.Id, "0912345678"), default);
        // Clear the address so the link is the only thing left besides the number.
        var stored = await h.Users.FindByIdAsync(user.Id, default);
        stored!.ChangeEmail(null, hasLinkedProvider: true);
        await h.Users.SaveAsync(stored, default);

        var result = await h.Sut.HandleAsync(new SetPhoneCommand(user.Id, null), default);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("khong-phai-so")]
    [InlineData("12345")]
    [InlineData("912345678")]
    public async Task A_number_that_is_not_usable_is_explained(string typed)
    {
        var h = new Harness();
        var user = await h.FromProviderAsync();

        var result = await h.Sut.HandleAsync(new SetPhoneCommand(user.Id, typed), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PhoneInvalid, result.Error.Code);
    }
}
