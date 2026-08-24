using System.Security.Cryptography;
using System.Text;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// Opening a sign-in: what the client is given, and what it is not.
/// </summary>
public sealed class StartSsoSignInTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);

    private static (StartSsoSignIn Sut, FakeSsoStateStore States, FakeExternalIdentityProvider Provider) Build()
    {
        var provider = new FakeExternalIdentityProvider();
        var states = new FakeSsoStateStore();
        return (new StartSsoSignIn(new FakeProviderRegistry(provider), states, new FixedClock(Now)),
                states, provider);
    }

    [Fact]
    public async Task Returns_an_authorization_url_and_stores_the_state()
    {
        var (sut, states, provider) = Build();

        var result = await sut.HandleAsync(new StartSsoCommand("google", "/hoc"), default);

        Assert.True(result.IsSuccess);
        var state = provider.LastRequest!.State;
        Assert.Contains(state, result.Value!.AuthorizationUrl.Query);

        var stored = await states.ConsumeAsync(state, default);
        Assert.NotNull(stored);
        Assert.Equal(IdentityProvider.Google, stored!.Provider);
        Assert.Equal("/hoc", stored.ReturnTo);
        Assert.Equal(Now.Add(StartSsoSignIn.StateLifetime), stored.ExpiresAt);
    }

    [Fact]
    public async Task The_challenge_is_the_S256_hash_of_the_stored_verifier()
    {
        // A plain challenge, or one that does not correspond to the verifier,
        // makes PKCE decorative. → threat T2
        var (sut, states, provider) = Build();

        await sut.HandleAsync(new StartSsoCommand("google", null), default);

        var request = provider.LastRequest!;
        var stored = await states.ConsumeAsync(request.State, default);

        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(stored!.CodeVerifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(expected, request.CodeChallenge);
        Assert.NotEqual(stored.CodeVerifier, request.CodeChallenge);
    }

    [Fact]
    public async Task The_verifier_is_within_the_length_RFC_7636_allows()
    {
        var (sut, states, provider) = Build();

        await sut.HandleAsync(new StartSsoCommand("google", null), default);
        var stored = await states.ConsumeAsync(provider.LastRequest!.State, default);

        Assert.InRange(stored!.CodeVerifier.Length, 43, 128);
    }

    [Fact]
    public async Task Two_sign_ins_never_share_a_state_or_a_verifier()
    {
        var (sut, states, provider) = Build();

        await sut.HandleAsync(new StartSsoCommand("google", null), default);
        var first = provider.LastRequest!;
        var firstStored = await states.ConsumeAsync(first.State, default);

        await sut.HandleAsync(new StartSsoCommand("google", null), default);
        var second = provider.LastRequest!;
        var secondStored = await states.ConsumeAsync(second.State, default);

        Assert.NotEqual(first.State, second.State);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(firstStored!.CodeVerifier, secondStored!.CodeVerifier);
    }

    [Fact]
    public async Task An_unconfigured_provider_is_refused()
    {
        var (sut, _, _) = Build();

        var result = await sut.HandleAsync(new StartSsoCommand("microsoft", null), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoProviderUnknown, result.Error.Code);
    }

    [Theory]
    // An open redirect on an authentication callback is how a credential
    // phishing chain gets a link that genuinely starts at our domain.
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("/\\evil.example")]
    [InlineData("/hoc\\..\\..")]
    [InlineData("javascript:alert(1)")]
    [InlineData("hoc")]
    [InlineData("/hoc\nSet-Cookie: a=b")]
    public async Task A_return_target_that_could_leave_the_site_is_discarded(string returnTo)
    {
        var (sut, states, provider) = Build();

        await sut.HandleAsync(new StartSsoCommand("google", returnTo), default);
        var stored = await states.ConsumeAsync(provider.LastRequest!.State, default);

        Assert.Null(stored!.ReturnTo);
    }

    [Theory]
    [InlineData("/hoc")]
    [InlineData("/ho-so?tab=token")]
    public async Task A_same_site_path_survives(string returnTo)
    {
        var (sut, states, provider) = Build();

        await sut.HandleAsync(new StartSsoCommand("google", returnTo), default);
        var stored = await states.ConsumeAsync(provider.LastRequest!.State, default);

        Assert.Equal(returnTo, stored!.ReturnTo);
    }
}

/// <summary>
/// Redeeming the handoff code — the last step, and the one that actually
/// issues credentials.
/// </summary>
public sealed class CompleteSsoSignInTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);

    private static async Task<(CompleteSsoSignIn Sut, FakeHandoffCodeStore Codes, FakeUserRepository Users, User User)>
        BuildAsync(bool suspended = false)
    {
        var users = new FakeUserRepository();
        var codes = new FakeHandoffCodeStore();
        var user = User.Register(Email.Create("hoc.vien@example.com"), "Học viên", Now);
        if (suspended) user.Suspend();
        await users.AddAsync(user, default);

        var sut = new CompleteSsoSignIn(
            codes, users, new FakePermissionResolver(PermissionKeys.ExamRead), new FakeTokenService());

        return (sut, codes, users, user);
    }

    [Fact]
    public async Task A_valid_code_returns_the_same_shape_as_a_password_login()
    {
        var (sut, codes, _, user) = await BuildAsync();
        var code = await codes.IssueAsync(user.Id, default);

        var result = await sut.HandleAsync(new CompleteSsoCommand(code), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value!.UserId);
        Assert.Equal("access", result.Value.Tokens.AccessToken);
        Assert.Equal("Học viên", result.Value.DisplayName);
    }

    [Fact]
    public async Task A_code_works_once()
    {
        var (sut, codes, _, user) = await BuildAsync();
        var code = await codes.IssueAsync(user.Id, default);

        Assert.True((await sut.HandleAsync(new CompleteSsoCommand(code), default)).IsSuccess);

        var replay = await sut.HandleAsync(new CompleteSsoCommand(code), default);
        Assert.False(replay.IsSuccess);
        Assert.Equal(ErrorCodes.SsoHandoffInvalid, replay.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("never-issued")]
    public async Task An_unknown_code_is_refused(string code)
    {
        var (sut, _, _, _) = await BuildAsync();

        var result = await sut.HandleAsync(new CompleteSsoCommand(code), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoHandoffInvalid, result.Error.Code);
    }

    [Fact]
    public async Task Suspension_between_the_callback_and_the_exchange_is_caught()
    {
        // Sixty seconds is short, but this is the call that mints credentials,
        // so it re-checks rather than trusting the earlier decision.
        var (sut, codes, users, user) = await BuildAsync();
        var code = await codes.IssueAsync(user.Id, default);

        user.Suspend();
        await users.SaveAsync(user, default);

        var result = await sut.HandleAsync(new CompleteSsoCommand(code), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AccountSuspended, result.Error.Code);
    }
}
