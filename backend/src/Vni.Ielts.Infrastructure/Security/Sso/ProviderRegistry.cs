using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Infrastructure.Security.Sso;

/// <summary>
/// The providers this deployment has credentials for.
///
/// <para>
/// Built once at startup. A provider missing its client id, secret or redirect
/// URI is simply absent — the sign-in page asks this registry which buttons to
/// enable, so a half-configured provider must not appear at all rather than
/// appearing and failing on click.
/// </para>
/// </summary>
internal sealed class ProviderRegistry : IExternalIdentityProviderRegistry
{
    private readonly Dictionary<string, IExternalIdentityProvider> _byKey;

    public ProviderRegistry(IEnumerable<IExternalIdentityProvider> providers)
    {
        _byKey = providers.ToDictionary(
            p => p.Provider.ToString(), StringComparer.OrdinalIgnoreCase);
        Enabled = [.. _byKey.Values];
    }

    public IReadOnlyCollection<IExternalIdentityProvider> Enabled { get; }

    public bool TryResolve(string providerKey, out IExternalIdentityProvider provider)
    {
        provider = null!;
        if (string.IsNullOrWhiteSpace(providerKey))
            return false;

        return _byKey.TryGetValue(providerKey, out provider!);
    }
}

/// <summary>
/// Google, as an OpenID Connect provider.
///
/// <para>
/// The endpoints are not written down here. Everything — authorization
/// endpoint, token endpoint, issuer and signing keys — comes from Google's
/// discovery document, which is fetched once and refreshed on a schedule by
/// <see cref="ConfigurationManager{T}"/>. That refresh is what makes key
/// rotation a non-event: Google rotates its signing keys regularly, and a
/// pinned key would fail sign-ins at an unpredictable moment.
/// </para>
///
/// <para>
/// Source: <see href="https://developers.google.com/identity/openid-connect/openid-connect"/>.
/// </para>
/// </summary>
internal static class GoogleProvider
{
    private const string MetadataAddress = "https://accounts.google.com/.well-known/openid-configuration";

    /// <summary>
    /// <c>openid email profile</c> and nothing more. Every additional scope is
    /// a consent screen the learner has to read and a broader set of personal
    /// data crossing a border. → <c>B-2</c>
    /// </summary>
    private static readonly string[] Scopes = ["openid", "email", "profile"];

    public static IExternalIdentityProvider Create(
        SsoProviderOptions options, TimeSpan timeout, ILoggerFactory loggers)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var metadata = new ConfigurationManager<OpenIdConnectConfiguration>(
            MetadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(new HttpClient { Timeout = timeout }) { RequireHttps = true });

        return new OpenIdConnectIdentityProvider(
            IdentityProvider.Google,
            Scopes,
            options,
            http,
            metadata,
            loggers.CreateLogger("Vni.Ielts.Sso.Google"));
    }
}

/// <summary>
/// A provider that signs in a fixed account without contacting anyone.
///
/// <para>
/// <b>Development only, and enforced as such in <c>AddInfrastructure</c>.</b>
/// This is an unauthenticated route to a session; if it were ever registered
/// in a real deployment it would be a complete authentication bypass. It
/// exists because the whole flow — state, PKCE, handoff code, linking rules —
/// is testable end to end without anyone holding a Google client secret, which
/// is what lets the frontend be built before the credentials exist.
/// </para>
/// </summary>
internal sealed class StubIdentityProvider(SsoOptions options) : IExternalIdentityProvider
{
    public IdentityProvider Provider => IdentityProvider.Google;

    public bool AssertsEmailVerification => true;

    public Task<Uri> BuildAuthorizationUrlAsync(AuthorizationRequest request, CancellationToken ct)
    {
        // Straight back to our own callback, so the browser round trip is real
        // even though the provider is not. It reuses Sso:Google:RedirectUri —
        // which is just "where the provider sends the browser back" and needs
        // no credentials to fill in, so the stub runs on the same setting the
        // real adapter will later use.
        if (string.IsNullOrWhiteSpace(options.Google.RedirectUri))
        {
            throw new InvalidOperationException(
                "The stub sign-in provider needs Sso:Google:RedirectUri set to this API's own "
                + "callback URL, e.g. http://localhost:5080/api/v1/auth/sso/google/callback.");
        }

        var separator = options.Google.RedirectUri.Contains('?') ? '&' : '?';

        return Task.FromResult(new Uri(
            $"{options.Google.RedirectUri}{separator}code=stub-code"
            + $"&state={Uri.EscapeDataString(request.State)}"));
    }

    public Task<Result<ExternalIdentity>> ExchangeCodeAsync(
        string code, string codeVerifier, string nonce, CancellationToken ct) =>
        Task.FromResult<Result<ExternalIdentity>>(
            new ExternalIdentity(
                IdentityProvider.Google,
                Subject: "stub-google-subject",
                Email: "stub.learner@example.com",
                EmailVerified: true,
                DisplayName: "Học viên thử"));
}
