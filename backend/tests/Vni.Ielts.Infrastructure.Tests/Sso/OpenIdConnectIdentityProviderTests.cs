using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Infrastructure.Security.Sso;

namespace Vni.Ielts.Infrastructure.Tests.Sso;

/// <summary>
/// ID-token validation, which is the part of social sign-in that is silently
/// catastrophic to get wrong.
///
/// <para>
/// A signature check that is skipped, an audience that is not compared, or a
/// nonce that is read but never matched all produce a sign-in flow that works
/// perfectly in manual testing and accepts forged identities in production.
/// Nothing in a happy-path test notices any of them, so every one of them has
/// a failing case here. → threat T2
/// </para>
///
/// <para>
/// The provider is stood up against a locally generated RSA key rather than
/// Google, so these run offline and deterministically.
/// </para>
/// </summary>
public sealed class OpenIdConnectIdentityProviderTests
{
    private const string Issuer = "https://idp.example";
    private const string ClientId = "vni-client-id";
    private const string Nonce = "nonce-value";

    private static readonly RSA SigningRsa = RSA.Create(2048);
    private static readonly RSA OtherRsa = RSA.Create(2048);

    private static RsaSecurityKey Key(RSA rsa, string keyId) =>
        new(rsa) { KeyId = keyId };

    private sealed class StaticMetadata(OpenIdConnectConfiguration configuration)
        : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            Task.FromResult(configuration);

        public void RequestRefresh() { }
    }

    /// <summary>Returns a canned token endpoint response.</summary>
    private sealed class CannedTokenEndpoint(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(ct);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private static OpenIdConnectConfiguration Configuration()
    {
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = Issuer,
            AuthorizationEndpoint = $"{Issuer}/authorize",
            TokenEndpoint = $"{Issuer}/token",
        };
        configuration.SigningKeys.Add(Key(SigningRsa, "test-key"));
        return configuration;
    }

    private static string IdToken(
        string? audience = ClientId,
        string? issuer = Issuer,
        string? nonce = Nonce,
        string? subject = "provider-subject",
        object? emailVerified = null,
        string? email = "hoc.vien@example.com",
        RSA? signWith = null,
        DateTime? expires = null)
    {
        var claims = new Dictionary<string, object>();
        if (subject is not null) claims["sub"] = subject;
        if (email is not null) claims["email"] = email;
        if (nonce is not null) claims["nonce"] = nonce;
        if (emailVerified is not null) claims["email_verified"] = emailVerified;
        claims["name"] = "Học Viên";

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                Key(signWith ?? SigningRsa, "test-key"), SecurityAlgorithms.RsaSha256),
        });
    }

    private static OpenIdConnectIdentityProvider Build(
        string? idToken,
        HttpStatusCode status = HttpStatusCode.OK,
        CannedTokenEndpoint? endpoint = null)
    {
        var body = idToken is null
            ? "{}"
            : JsonSerializer.Serialize(new Dictionary<string, string> { ["id_token"] = idToken });

        endpoint ??= new CannedTokenEndpoint(status, body);

        return new OpenIdConnectIdentityProvider(
            Vni.Ielts.Domain.Identity.IdentityProvider.Google,
            ["openid", "email", "profile"],
            new SsoProviderOptions
            {
                ClientId = ClientId,
                ClientSecret = "client-secret",
                RedirectUri = "https://api.example/api/v1/auth/sso/google/callback",
            },
            new HttpClient(endpoint),
            new StaticMetadata(Configuration()),
            NullLogger.Instance);
    }

    private static Task<Result<ExternalIdentity>> ExchangeAsync(
        OpenIdConnectIdentityProvider sut, string nonce = Nonce) =>
        sut.ExchangeCodeAsync("auth-code", "code-verifier", nonce, default);

    // ── The authorization URL ──────────────────────────────────────────────

    [Fact]
    public async Task The_authorization_url_carries_PKCE_and_the_nonce()
    {
        var sut = Build(IdToken());

        var url = await sut.BuildAuthorizationUrlAsync(
            new AuthorizationRequest("state-value", "challenge-value", Nonce), default);

        var query = url.Query;
        Assert.Contains("code_challenge=challenge-value", query, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", query, StringComparison.Ordinal);
        Assert.Contains("state=state-value", query, StringComparison.Ordinal);
        Assert.Contains($"nonce={Nonce}", query, StringComparison.Ordinal);
        Assert.Contains("response_type=code", query, StringComparison.Ordinal);
        Assert.StartsWith($"{Issuer}/authorize?", url.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_authorization_url_never_carries_the_client_secret()
    {
        // It is a URL handed to a browser. Nothing further needs saying.
        var sut = Build(IdToken());

        var url = await sut.BuildAuthorizationUrlAsync(
            new AuthorizationRequest("state", "challenge", Nonce), default);

        Assert.DoesNotContain("client-secret", url.ToString(), StringComparison.Ordinal);
    }

    // ── The exchange ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_valid_token_yields_the_identity()
    {
        var result = await ExchangeAsync(Build(IdToken(emailVerified: true)));

        Assert.True(result.IsSuccess);
        Assert.Equal("provider-subject", result.Value!.Subject);
        Assert.Equal("hoc.vien@example.com", result.Value.Email);
        Assert.True(result.Value.EmailVerified);
        Assert.Equal("Học Viên", result.Value.DisplayName);
    }

    [Fact]
    public async Task The_exchange_sends_the_verifier_and_the_secret_and_not_the_nonce()
    {
        var endpoint = new CannedTokenEndpoint(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new Dictionary<string, string> { ["id_token"] = IdToken() }));

        await ExchangeAsync(Build(null, endpoint: endpoint));

        var body = endpoint.LastRequestBody!;
        Assert.Contains("code_verifier=code-verifier", body, StringComparison.Ordinal);
        Assert.Contains("client_secret=client-secret", body, StringComparison.Ordinal);
        Assert.Contains("grant_type=authorization_code", body, StringComparison.Ordinal);
        // The nonce is compared against the token, never sent to the token endpoint.
        Assert.DoesNotContain("nonce", body, StringComparison.Ordinal);
    }

    // ── Everything that must be refused ────────────────────────────────────

    [Fact]
    public async Task A_token_signed_by_an_unknown_key_is_refused()
    {
        // The whole trust chain. Without it, anyone can mint an identity.
        var result = await ExchangeAsync(Build(IdToken(signWith: OtherRsa)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoExchangeFailed, result.Error.Code);
    }

    [Fact]
    public async Task A_token_for_another_application_is_refused()
    {
        // Token substitution: a valid, correctly signed token from the same
        // issuer, minted for somebody else's client id.
        var result = await ExchangeAsync(Build(IdToken(audience: "someone-elses-client")));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoExchangeFailed, result.Error.Code);
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_refused()
    {
        var result = await ExchangeAsync(Build(IdToken(issuer: "https://evil.example")));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoExchangeFailed, result.Error.Code);
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var result = await ExchangeAsync(Build(IdToken(expires: DateTime.UtcNow.AddMinutes(-10))));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoExchangeFailed, result.Error.Code);
    }

    [Fact]
    public async Task A_token_carrying_a_different_nonce_is_refused()
    {
        // This is the replay check. A token captured from another sign-in is
        // valid in every other respect.
        var result = await ExchangeAsync(Build(IdToken(nonce: "some-other-nonce")));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoExchangeFailed, result.Error.Code);
    }

    [Fact]
    public async Task A_token_carrying_no_nonce_is_refused()
    {
        var result = await ExchangeAsync(Build(IdToken(nonce: null)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoExchangeFailed, result.Error.Code);
    }

    [Fact]
    public async Task A_token_carrying_no_subject_is_refused()
    {
        var result = await ExchangeAsync(Build(IdToken(subject: null)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoExchangeFailed, result.Error.Code);
    }

    [Fact]
    public async Task A_refused_exchange_is_refused()
    {
        var result = await ExchangeAsync(Build(IdToken(), HttpStatusCode.BadRequest));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoExchangeFailed, result.Error.Code);
    }

    [Fact]
    public async Task A_response_with_no_id_token_is_refused()
    {
        var result = await ExchangeAsync(Build(null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SsoExchangeFailed, result.Error.Code);
    }

    // ── email_verified ─────────────────────────────────────────────────────

    [Fact]
    public async Task An_absent_email_verified_claim_is_not_verified()
    {
        // Absent must never read as true: this single boolean is what decides
        // whether a matching address links silently. → ADR-0013
        var result = await ExchangeAsync(Build(IdToken(emailVerified: null)));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.EmailVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData(1)]
    [InlineData("1")]
    public async Task Anything_other_than_a_true_email_verified_claim_is_not_verified(object value)
    {
        var result = await ExchangeAsync(Build(IdToken(emailVerified: value)));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.EmailVerified);
    }

    [Theory]
    [InlineData(true)]
    [InlineData("true")]
    [InlineData("True")]
    public async Task A_true_email_verified_claim_is_verified(object value)
    {
        var result = await ExchangeAsync(Build(IdToken(emailVerified: value)));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.EmailVerified);
    }

    [Fact]
    public async Task A_token_with_no_email_yields_no_email()
    {
        var result = await ExchangeAsync(Build(IdToken(email: null)));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Email);
    }
}
