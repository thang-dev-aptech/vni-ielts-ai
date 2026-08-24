using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// Correcting an address that has not been verified yet.
///
/// <para>
/// Two rules carry the weight. It is locked once verified, because a verified
/// address is the account's route back in. And it must be written in
/// <b>both</b> places — the account and the identity that password sign-in is
/// keyed by — or the account keeps working right up until someone tries to
/// sign in.
/// </para>
/// </summary>
public sealed class ChangeEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
    private const string Typo = "hoc.vien@gmial.com";
    private const string Fixed = "hoc.vien@gmail.com";

    private sealed class FakeTokens : IEmailVerificationTokens
    {
        public List<UserId> Issued { get; } = [];

        public Task<string> IssueAsync(UserId userId, CancellationToken ct)
        {
            Issued.Add(userId);
            return Task.FromResult("token");
        }

        public Task<UserId?> RedeemAsync(string token, CancellationToken ct) =>
            Task.FromResult<UserId?>(null);
    }

    private sealed class FakeSender : IVerificationMessageSender
    {
        public List<string> SentTo { get; } = [];

        public Task SendAsync(Email address, string token, CancellationToken ct)
        {
            SentTo.Add(address.Value);
            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(Email address, string token, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeUserIdentityRepository Identities { get; } = new();
        public FakePasswordHasher Hasher { get; } = new();
        public FakeTokens Tokens { get; } = new();
        public FakeSender Sender { get; } = new();

        public ChangeEmail Sut => new(Users, Identities, Tokens, Sender);

        public async Task<User> SeedAsync(string address = Typo, bool verified = false)
        {
            var email = Email.Create(address);
            var user = User.Register(email, "Học viên", Now);
            if (verified) user.MarkEmailVerified();

            await Users.AddAsync(user, default);
            await Identities.AddAsync(
                UserIdentity.ForEmail(user.Id, email, Hasher.Hash("mat-khau-du-dai-2026"), Now), default);

            return user;
        }

        public string? IdentityAddress(UserId id) =>
            Identities.ListForUserAsync(id, default).Result
                .FirstOrDefault(i => i.Provider == IdentityProvider.Email)?.ProviderUserId;
    }

    [Fact]
    public async Task A_typo_can_be_corrected_while_it_is_still_only_a_claim()
    {
        var h = new Harness();
        var user = await h.SeedAsync();

        var result = await h.Sut.HandleAsync(new ChangeEmailCommand(user.Id, Fixed), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(Fixed, (await h.Users.FindByIdAsync(user.Id, default))!.Email.Value);
    }

    [Fact]
    public async Task The_identity_password_sign_in_uses_moves_with_it()
    {
        // The failure this pins is silent: change only User.Email and the
        // profile shows the new address while password sign-in works at
        // neither the old one nor the new one.
        var h = new Harness();
        var user = await h.SeedAsync();

        await h.Sut.HandleAsync(new ChangeEmailCommand(user.Id, Fixed), default);

        Assert.Equal(Fixed, h.IdentityAddress(user.Id));
        Assert.NotNull(await h.Identities.FindByProviderAsync(IdentityProvider.Email, Fixed, default));
        Assert.Null(await h.Identities.FindByProviderAsync(IdentityProvider.Email, Typo, default));
    }

    [Fact]
    public async Task The_verification_link_goes_to_the_new_address()
    {
        // Sending it to the old one would be sending it to the address that
        // could not receive anything, which is the reason for the change.
        var h = new Harness();
        var user = await h.SeedAsync();

        await h.Sut.HandleAsync(new ChangeEmailCommand(user.Id, Fixed), default);

        Assert.Equal([Fixed], h.Sender.SentTo);
    }

    [Fact]
    public async Task A_verified_address_is_locked()
    {
        // It is the account's route back in. A stolen session must not be able
        // to move it to somebody else's mailbox.
        var h = new Harness();
        var user = await h.SeedAsync(verified: true);

        var result = await h.Sut.HandleAsync(new ChangeEmailCommand(user.Id, Fixed), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.EmailLocked, result.Error.Code);
        Assert.Equal(Typo, (await h.Users.FindByIdAsync(user.Id, default))!.Email.Value);
        Assert.Empty(h.Sender.SentTo);
    }

    [Fact]
    public async Task An_address_someone_else_already_has_is_refused()
    {
        var h = new Harness();
        await h.SeedAsync(Fixed);
        var user = await h.SeedAsync(Typo);

        var result = await h.Sut.HandleAsync(new ChangeEmailCommand(user.Id, Fixed), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.EmailAlreadyRegistered, result.Error.Code);
    }

    [Fact]
    public async Task Re_submitting_the_same_address_costs_nothing()
    {
        // No second verification mail for someone who pressed save twice.
        var h = new Harness();
        var user = await h.SeedAsync();

        var result = await h.Sut.HandleAsync(new ChangeEmailCommand(user.Id, Typo), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(h.Sender.SentTo);
    }

    [Theory]
    [InlineData("khong-phai-email")]
    [InlineData("")]
    public async Task A_malformed_address_is_refused(string address)
    {
        var h = new Harness();
        var user = await h.SeedAsync();

        var result = await h.Sut.HandleAsync(new ChangeEmailCommand(user.Id, address), default);

        Assert.Equal(ErrorCodes.EmailInvalid, result.Error.Code);
    }
}
