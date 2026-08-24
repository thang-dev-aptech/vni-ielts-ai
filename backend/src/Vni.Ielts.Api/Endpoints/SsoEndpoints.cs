using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Infrastructure.Security.Sso;

namespace Vni.Ielts.Api.Endpoints;

public sealed record StartSsoRequest(string? ReturnTo);
public sealed record StartSsoResponse(string AuthorizationUrl);
public sealed record CompleteSsoRequest(string HandoffCode);
public sealed record SsoProviderSummary(string Key, string DisplayName);
public sealed record SsoProvidersResponse(IReadOnlyCollection<SsoProviderSummary> Providers);

/// <summary>
/// Social sign-in.
///
/// <para>
/// Four endpoints, and the shape is the decision: the backend runs the whole
/// OAuth exchange and the client receives a one-time handoff code rather than
/// a token. The client never sees a client id, a client secret, the PKCE
/// verifier, the <c>state</c> or the <c>nonce</c>. → ADR-0014
/// </para>
///
/// <para>
/// The contract these expose is written up for the client in
/// <c>docs/api/sso-contract.md</c>.
/// </para>
/// </summary>
public static class SsoEndpoints
{
    public static void MapSsoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth/sso").WithTags("Auth");

        group.MapGet("/providers", Providers)
            .WithName("SsoProviders")
            .WithSummary("List the social sign-in providers this deployment has configured");

        group.MapPost("/{provider}/start", Start)
            .WithName("SsoStart")
            .WithSummary("Begin a social sign-in")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapGet("/{provider}/callback", Callback)
            .WithName("SsoCallback")
            .WithSummary("Receive the provider redirect and hand the client a one-time code")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapPost("/complete", Complete)
            .WithName("SsoComplete")
            .WithSummary("Exchange a one-time handoff code for a session")
            .RequireRateLimiting(RateLimitPolicies.Authentication);
    }

    /// <summary>
    /// Which buttons the sign-in page should enable.
    ///
    /// <para>
    /// This exists so the client does not hard-code three providers and render
    /// two of them dead. A provider missing any of its credentials is absent
    /// from this list rather than present and broken.
    /// </para>
    /// </summary>
    private static IResult Providers(IExternalIdentityProviderRegistry registry) =>
        Results.Ok(new SsoProvidersResponse(
            [.. registry.Enabled.Select(p => new SsoProviderSummary(
                p.Provider.ToString().ToLowerInvariant(), p.Provider.ToString()))]));

    private static async Task<IResult> Start(
        string provider,
        [FromBody] StartSsoRequest? request,
        StartSsoSignIn handler,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new StartSsoCommand(provider, request?.ReturnTo), ct);

        return result.Match(
            ok => Results.Ok(new StartSsoResponse(ok.AuthorizationUrl.ToString())),
            error => ApiProblem.From(error, http));
    }

    /// <summary>
    /// The provider's redirect lands here.
    ///
    /// <para>
    /// <b>Always a redirect, never a body.</b> This response is rendered in a
    /// browser the client does not control at this moment, so an error has to
    /// travel as a query parameter on the way back to a page that can show it.
    /// The parameter carries a stable <i>code</i> and never the provider's own
    /// message, which is attacker-influenced text.
    /// </para>
    ///
    /// <para>
    /// The handoff code is in the URL and the token pair is not. That is the
    /// whole point of the extra round trip: a URL ends up in history, in a
    /// <c>Referer</c> header and in any proxy log on the path, and a
    /// sixty-second single-use code surviving that is very different from a
    /// thirty-day refresh token surviving it. → threat T3
    /// </para>
    /// </summary>
    private static async Task<IResult> Callback(
        string provider,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery(Name = "error")] string? providerError,
        SignInWithSso handler,
        IOptions<SsoOptions> options,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new SsoCallbackCommand(provider, code, state, providerError), ct);

        var client = options.Value;
        var target = $"{client.ClientBaseUrl.TrimEnd('/')}{client.ClientCallbackPath}";

        return result.Match(
            ok =>
            {
                var url = $"{target}?code={Uri.EscapeDataString(ok.HandoffCode)}";
                if (ok.ReturnTo is not null)
                    url += $"&returnTo={Uri.EscapeDataString(ok.ReturnTo)}";

                return Results.Redirect(url);
            },
            error => Results.Redirect($"{target}?error={Uri.EscapeDataString(error.Code)}"));
    }

    /// <summary>
    /// Returns the same body as <c>POST /api/v1/auth/login</c>, so the client
    /// has one session-handling path rather than two that can drift.
    /// </summary>
    private static async Task<IResult> Complete(
        [FromBody] CompleteSsoRequest request,
        CompleteSsoSignIn handler,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new CompleteSsoCommand(request.HandoffCode), ct);

        return result.Match(
            ok => Results.Ok(AuthEndpoints.ToSession(ok)),
            error => ApiProblem.From(error, http));
    }
}
