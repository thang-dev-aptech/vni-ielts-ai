using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// Setting, changing and removing the account's address.
///
/// <para>
/// <b>The lock is gone, and these tests are what says so deliberately.</b> The
/// address used to freeze the moment it was verified, because it was the
/// account's route back in. Verification no longer exists and the owner asked
/// for the opposite behaviour on 08/09/2026: changing the address moves the
/// account onto it. What replaces the lock is a narrower guarantee — the
/// account may never be left with nothing anyone can type to reach it.
/// → ADR-0018
/// </para>
/// </summary>
public sealed class ChangeEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeUserIdentityRepository Identities { get; } = new();

        public ChangeEmail Sut => new(Users, Identities);

        /// <summary>An ordinary account: registered with a number, no address yet.</summary>
        public async Task<User> WithPhoneAsync(string phone = "0912345678")
        {
            var user = User.Register(PhoneNumber.Create(phone), "Hoc vien", Now);
            await Users.AddAsync(user, default);
            await Identities.AddAsync(UserIdentity.ForPassword(user.Id, "hash", Now), default);
            return user;
        }

        /// <summary>An account created through Google: an address and nothing else.</summary>
        public async Task<User> FromProviderAsync(string email = "cu@example.com")
        {
            var user = User.RegisterFromProvider(Email.Create(email), "Hoc vien", Now);
            await Users.AddAsync(user, default);
            await Identities.AddAsync(
                UserIdentity.ForSocial(user.Id, IdentityProvider.Google, "sub-1", Now), default);
            return user;
        }
    }

    [Fact]
    public async Task An_account_registered_with_a_number_can_add_an_address()
    {
        // The profile field starts empty; filling it in is the ordinary path,
        // and it is also how that account gains a second way to sign in.
        var h = new Harness();
        var user = await h.WithPhoneAsync();

        var result = await h.Sut.HandleAsync(
            new ChangeEmailCommand(user.Id, "toi@example.com"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("toi@example.com", result.Value!.Email);

        var stored = await h.Users.FindByIdAsync(user.Id, default);
        Assert.Equal("toi@example.com", stored!.Email!.Value.Value);
    }

    [Fact]
    public async Task An_address_that_was_already_set_can_still_be_changed()
    {
        /*
         * This is the owner's scenario, from the account's side: the data does
         * not move, the label does. Nothing is copied and no new account
         * appears — the same id keeps its sittings and its ledger and simply
         * answers to a different address afterwards.
         */
        var h = new Harness();
        var user = await h.FromProviderAsync("ngdthang.dev@example.com");

        var result = await h.Sut.HandleAsync(
            new ChangeEmailCommand(user.Id, "nguyendoanthang16@example.com"), default);

        Assert.True(result.IsSuccess);

        var stored = await h.Users.FindByIdAsync(user.Id, default);
        Assert.Equal(user.Id, stored!.Id);
        Assert.Equal("nguyendoanthang16@example.com", stored.Email!.Value.Value);
    }

    [Fact]
    public async Task The_address_is_normalised_so_two_spellings_are_one_account()
    {
        var h = new Harness();
        var user = await h.WithPhoneAsync();

        await h.Sut.HandleAsync(new ChangeEmailCommand(user.Id, "  Toi@Example.COM "), default);

        var stored = await h.Users.FindByIdAsync(user.Id, default);
        Assert.Equal("toi@example.com", stored!.Email!.Value.Value);
    }

    [Fact]
    public async Task An_address_someone_else_already_has_is_refused()
    {
        var h = new Harness();
        await h.FromProviderAsync("da.co@example.com");
        var user = await h.WithPhoneAsync();

        var result = await h.Sut.HandleAsync(
            new ChangeEmailCommand(user.Id, "da.co@example.com"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.EmailAlreadyRegistered, result.Error.Code);
    }

    [Fact]
    public async Task Losing_the_unique_index_race_reads_as_the_same_conflict()
    {
        /*
         * <b>This path had never been exercised.</b> The catch around the save
         * has been in the handler all along, but the repository translated
         * nothing on the replace path, so a real race surfaced as a 500 and
         * sailed straight past it. → MongoUserRepository.SaveAsync
         */
        var h = new Harness();
        var user = await h.WithPhoneAsync();
        h.Users.ThrowDuplicateEmailOnNextSave = true;

        var result = await h.Sut.HandleAsync(
            new ChangeEmailCommand(user.Id, "toi@example.com"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.EmailAlreadyRegistered, result.Error.Code);
    }

    [Fact]
    public async Task Resubmitting_the_same_address_is_a_success_that_changes_nothing()
    {
        var h = new Harness();
        var user = await h.FromProviderAsync("toi@example.com");

        var result = await h.Sut.HandleAsync(
            new ChangeEmailCommand(user.Id, "toi@example.com"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("toi@example.com", result.Value!.Email);
    }

    [Fact]
    public async Task An_address_can_be_removed_while_the_account_keeps_its_number()
    {
        var h = new Harness();
        var user = await h.WithPhoneAsync();
        await h.Sut.HandleAsync(new ChangeEmailCommand(user.Id, "toi@example.com"), default);

        var result = await h.Sut.HandleAsync(new ChangeEmailCommand(user.Id, null), default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Email);

        var stored = await h.Users.FindByIdAsync(user.Id, default);
        Assert.Null(stored!.Email);
    }

    [Fact]
    public async Task An_account_whose_only_handle_is_its_address_cannot_clear_it()
    {
        /*
         * A Google account has no number and no password. Clearing its address
         * would leave a row holding sittings, recordings and PDPL obligations
         * with no door at all — and it looks like an ordinary profile edit
         * right up until the session expires.
         */
        var h = new Harness();
        var user = User.RegisterFromProvider(Email.Create("toi@example.com"), "Hoc vien", Now);
        await h.Users.AddAsync(user, default);

        var result = await h.Sut.HandleAsync(new ChangeEmailCommand(user.Id, ""), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SignInMethodRequired, result.Error.Code);

        var stored = await h.Users.FindByIdAsync(user.Id, default);
        Assert.Equal("toi@example.com", stored!.Email!.Value.Value);
    }

    [Fact]
    public async Task The_guard_counts_a_linked_provider_as_a_handle()
    {
        // Same account, but with Google attached: the link is a way back in,
        // so clearing the address is allowed.
        var h = new Harness();
        var user = await h.FromProviderAsync("toi@example.com");

        var result = await h.Sut.HandleAsync(new ChangeEmailCommand(user.Id, ""), default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Email);
    }

    [Fact]
    public async Task A_malformed_address_is_refused()
    {
        var h = new Harness();
        var user = await h.WithPhoneAsync();

        var result = await h.Sut.HandleAsync(
            new ChangeEmailCommand(user.Id, "khong-phai-email"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.EmailInvalid, result.Error.Code);
    }

    [Fact]
    public async Task An_account_that_does_not_exist_is_a_not_found()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(
            new ChangeEmailCommand(new Vni.Ielts.Domain.Common.UserId("nobody"), "toi@example.com"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.NotFound, result.Error.Code);
    }
}
