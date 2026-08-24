namespace Vni.Ielts.Infrastructure.Security.Sso;

/// <summary>
/// Social sign-in configuration.
///
/// <para>
/// <b>Client secrets come from environment configuration and nothing else.</b>
/// <c>Sso__Google__ClientSecret</c>, supplied by the deployment. Never an
/// <c>appsettings</c> file, never a committed file of any kind — the
/// .gitignore and a PreToolUse hook both block <c>.env*</c>, and CI scans for
/// credential-shaped strings. → CLAUDE.md rule 6
/// </para>
///
/// <para>
/// See <c>docs/development/sso-provider-setup.md</c> for how to obtain these
/// values and which redirect URIs to register.
/// </para>
/// </summary>
public sealed class SsoOptions
{
    public const string SectionName = "Sso";

    public SsoProviderOptions Google { get; set; } = new();

    /// <summary>
    /// Where the client application lives, e.g. <c>https://hoc.vni.edu.vn</c>.
    /// The provider callback redirects the browser back here.
    ///
    /// <para>
    /// Configuration, never a request value. A redirect target taken from the
    /// request is an open redirect on an authentication endpoint, which is
    /// how a phishing chain gets a link that genuinely starts at our domain.
    /// </para>
    /// </summary>
    public string ClientBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The client route that receives the handoff code and calls
    /// <c>/auth/sso/complete</c>. A path, appended to <see cref="ClientBaseUrl"/>.
    /// </summary>
    public string ClientCallbackPath { get; set; } = "/login/sso";

    /// <summary>
    /// Registers a fake provider that signs in a fixed test account without
    /// contacting anyone.
    ///
    /// <para>
    /// <b>Refused outside Development.</b> It is an unauthenticated sign-in
    /// endpoint; the check that it cannot be switched on in production is in
    /// <c>AddInfrastructure</c> and is not a formality.
    /// </para>
    /// </summary>
    public bool EnableStubProvider { get; set; }

    /// <summary>
    /// How long the API waits on the provider before giving up.
    ///
    /// This runs inside a user-facing request, so a provider outage must
    /// surface as a failed sign-in rather than a held connection.
    /// </summary>
    public int ProviderTimeoutSeconds { get; set; } = 10;
}

public sealed class SsoProviderOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The API's own callback URL, e.g.
    /// <c>https://api.vni.edu.vn/api/v1/auth/sso/google/callback</c>.
    ///
    /// It must match a redirect URI registered with the provider exactly,
    /// including scheme, host, port and trailing characters. Google compares
    /// the string, not the meaning of it.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// A provider missing any of its three values is absent rather than
    /// present and broken — the sign-in page asks which providers are
    /// configured and enables only those, so a half-configured provider must
    /// not offer a button that fails on click.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RedirectUri);
}
