using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Infrastructure.Security.Sso;

/// <summary>
/// An OpenID Connect provider, driven entirely by its discovery document.
///
/// <para>
/// Google is one instance of this; any provider that publishes
/// <c>/.well-known/openid-configuration</c> is another, which is what makes
/// <c>AU-6</c> — accommodating more providers without rework — a matter of
/// configuration rather than code.
/// </para>
///
/// <para>
/// <b>Facebook is not one of them.</b> Its web login is OAuth 2.0 with a Graph
/// call for the profile and no ID token, so it needs its own adapter. That is
/// also why it cannot assert <c>email_verified</c>, which is what
/// <see cref="IExternalIdentityProvider.AssertsEmailVerification"/> exists to
/// record. → ADR-0013
/// </para>
///
/// <para>
/// Everything this class validates is validated <b>server-side against keys it
/// fetched itself</b>: signature, issuer, audience, expiry and nonce. Accepting
/// any of it from the client is the token-substitution half of threat
/// <c>T2</c>.
/// </para>
/// </summary>
internal sealed class OpenIdConnectIdentityProvider(
    IdentityProvider provider,
    string[] scopes,
    SsoProviderOptions options,
    HttpClient http,
    IConfigurationManager<OpenIdConnectConfiguration> metadata,
    ILogger logger) : IExternalIdentityProvider
{
    public IdentityProvider Provider { get; } = provider;

    /// <summary>
    /// True for every OIDC provider reached this way: the claim is part of the
    /// standard profile scope and arrives inside a signed token.
    /// → OpenID Connect Core §5.1
    /// </summary>
    public bool AssertsEmailVerification => true;

    public async Task<Uri> BuildAuthorizationUrlAsync(AuthorizationRequest request, CancellationToken ct)
    {
        var configuration = await metadata.GetConfigurationAsync(ct);

        var query = new (string Key, string Value)[]
        {
            ("client_id", options.ClientId),
            ("redirect_uri", options.RedirectUri),
            ("response_type", "code"),
            ("scope", string.Join(' ', scopes)),
            ("state", request.State),
            ("nonce", request.Nonce),
            ("code_challenge", request.CodeChallenge),
            ("code_challenge_method", "S256"),
        };

        // Escaped by hand rather than through an ASP.NET Core helper, because
        // Infrastructure does not reference ASP.NET Core and should not start
        // for one query string. The values are all base64url or configuration,
        // but escaping them anyway is what keeps that from becoming an
        // assumption someone later breaks.
        var encoded = string.Join(
            '&', query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        var separator = configuration.AuthorizationEndpoint.Contains('?') ? '&' : '?';
        return new Uri($"{configuration.AuthorizationEndpoint}{separator}{encoded}");
    }

    public async Task<Result<ExternalIdentity>> ExchangeCodeAsync(
        string code, string codeVerifier, string nonce, CancellationToken ct)
    {
        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await metadata.GetConfigurationAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Could not read the {Provider} discovery document.", Provider);
            return Failed;
        }

        TokenResponse? tokens;
        try
        {
            using var response = await http.PostAsync(
                configuration.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = options.RedirectUri,
                    ["client_id"] = options.ClientId,
                    ["client_secret"] = options.ClientSecret,
                    ["code_verifier"] = codeVerifier,
                }),
                ct);

            if (!response.IsSuccessStatusCode)
            {
                // The provider's body can echo attacker-influenced values and
                // may contain the code itself, so the status is logged and the
                // body is not.
                logger.LogWarning(
                    "{Provider} refused the code exchange with {Status}.",
                    Provider, (int)response.StatusCode);
                return Failed;
            }

            tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "The {Provider} code exchange failed.", Provider);
            return Failed;
        }

        if (string.IsNullOrWhiteSpace(tokens?.IdToken))
        {
            logger.LogWarning("{Provider} returned no ID token.", Provider);
            return Failed;
        }

        return await ValidateAsync(tokens.IdToken, nonce, configuration);
    }

    private async Task<Result<ExternalIdentity>> ValidateAsync(
        string idToken, string expectedNonce, OpenIdConnectConfiguration configuration)
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = [configuration.Issuer],
            ValidateAudience = true,
            // The audience is our own client id. Without this check, a token
            // minted for a different application of the same issuer would be
            // accepted — the classic OAuth token-substitution attack.
            ValidAudience = options.ClientId,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateLifetime = true,
            // Non-zero, unlike our own tokens: this clock belongs to someone
            // else and a few seconds of drift between two correct servers is
            // ordinary. Thirty seconds is short enough to be uninteresting to
            // an attacker holding an expired token.
            ClockSkew = TimeSpan.FromSeconds(30),
        });

        if (!result.IsValid)
        {
            logger.LogWarning(
                result.Exception, "A {Provider} ID token failed validation.", Provider);
            return Failed;
        }

        var claims = result.Claims;

        // The nonce ties this token to the authorization request that started
        // sixty seconds ago in this browser. A replayed token from elsewhere
        // carries a different one. → threat T2
        if (!TryGetString(claims, "nonce", out var nonce) ||
            !CryptographicEquals(nonce!, expectedNonce))
        {
            logger.LogWarning("A {Provider} ID token carried the wrong nonce.", Provider);
            return Failed;
        }

        if (!TryGetString(claims, "sub", out var subject))
        {
            logger.LogWarning("A {Provider} ID token carried no subject.", Provider);
            return Failed;
        }

        TryGetString(claims, "email", out var email);
        TryGetString(claims, "name", out var name);

        return new ExternalIdentity(
            Provider,
            subject!,
            email,
            EmailVerified: claims.TryGetValue("email_verified", out var verified) && IsTrue(verified),
            DisplayName: name);
    }

    private static readonly Error Failed = Error.Unauthorized(
        ErrorCodes.SsoExchangeFailed, "Sign-in could not be completed. Please try again.");

    private static bool TryGetString(IDictionary<string, object> claims, string name, out string? value)
    {
        value = claims.TryGetValue(name, out var raw) ? raw as string ?? raw?.ToString() : null;
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Google sends <c>email_verified</c> as a JSON boolean; some providers
    /// send the string "true". Both are accepted, and <b>nothing else is</b> —
    /// a truthy-ish coercion here would silently promote an unverified address
    /// into one that links silently. → ADR-0013
    /// </summary>
    private static bool IsTrue(object value) => value switch
    {
        bool b => b,
        string s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase),
        JsonElement { ValueKind: JsonValueKind.True } => true,
        _ => false,
    };

    private static bool CryptographicEquals(string left, string right) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(left), System.Text.Encoding.UTF8.GetBytes(right));

    private sealed record TokenResponse(
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("access_token")] string? AccessToken);
}
