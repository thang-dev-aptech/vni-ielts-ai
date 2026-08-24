using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Api.Endpoints;

public sealed record SessionResponseItem(
    string Id,
    string Device,
    DateTimeOffset SignedInAt,
    DateTimeOffset LastUsedAt,
    bool IsCurrent);

public sealed record SessionsResponse(IReadOnlyCollection<SessionResponseItem> Sessions);

public sealed record SetPasswordRequest(string? CurrentPassword, string NewPassword);

public sealed record SetPhoneRequest(string? Phone);

public sealed record ChangeEmailRequest(string Email);

/// <summary>
/// The account's own surfaces: which providers it can sign in with, and which
/// devices currently hold a session.
/// </summary>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me").WithTags("Identity").RequireAuthorization();

        group.MapGet("/sessions", ListSessionsEndpoint)
            .WithName("ListSessions")
            .WithSummary("List the devices signed in to this account");

        group.MapPost("/email", ChangeEmailEndpoint)
            .WithName("ChangeEmail")
            .WithSummary("Correct an email address that has not been verified yet")
            .RequireRateLimiting(RateLimitPolicies.Registration);

        group.MapPost("/phone", SetPhoneEndpoint)
            .WithName("SetPhone")
            .WithSummary("Add, change or remove the contact phone number")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapPost("/verify-email/resend", ResendVerificationEndpoint)
            .WithName("ResendVerification")
            .WithSummary("Send the email verification message again")
            .RequireRateLimiting(RateLimitPolicies.Registration);

        group.MapPost("/password", SetPasswordEndpoint)
            .WithName("SetPassword")
            .WithSummary("Create or change this account's password")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapDelete("/sessions", RevokeOthersEndpoint)
            .WithName("RevokeOtherSessions")
            .WithSummary("Sign every other device out of this account")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapDelete("/sessions/{familyId}", RevokeSessionEndpoint)
            .WithName("RevokeSession")
            .WithSummary("Sign one device out of this account")
            .RequireRateLimiting(RateLimitPolicies.Authentication);
    }

    private static async Task<IResult> ListSessionsEndpoint(
        ClaimsPrincipal principal, ListSessions handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        var sessions = await handler.HandleAsync(
            new ListSessionsQuery(new UserId(id), principal.FamilyId()), ct);

        return Results.Ok(new SessionsResponse(
        [
            .. sessions.Select(s => new SessionResponseItem(
                s.FamilyId,
                DeviceLabel.From(s.UserAgent),
                s.SignedInAt,
                s.LastUsedAt,
                s.IsCurrent)),
        ]));
    }

    /// <summary>
    /// Registration policy, not sign-in: this sends a verification mail, so
    /// the cost of abusing it lands on whoever owns the address typed in.
    /// </summary>
    private static async Task<IResult> ChangeEmailEndpoint(
        [Microsoft.AspNetCore.Mvc.FromBody] ChangeEmailRequest request,
        ClaimsPrincipal principal,
        ChangeEmail handler,
        HttpContext http,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        var result = await handler.HandleAsync(
            new ChangeEmailCommand(new UserId(id), request.Email), ct);

        return result.Match(
            email => Results.Ok(new { email }),
            error => ApiProblem.From(error, http));
    }

    private static async Task<IResult> SetPhoneEndpoint(
        [Microsoft.AspNetCore.Mvc.FromBody] SetPhoneRequest request,
        ClaimsPrincipal principal,
        SetPhone handler,
        HttpContext http,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        var result = await handler.HandleAsync(new SetPhoneCommand(new UserId(id), request.Phone), ct);

        return result.Match(
            phone => Results.Ok(new { phone }),
            error => ApiProblem.From(error, http));
    }

    /// <summary>
    /// Rate-limited under the registration policy rather than the sign-in one:
    /// this sends mail, and the cost of abusing it lands on the person whose
    /// address is in the To field.
    /// </summary>
    private static async Task<IResult> ResendVerificationEndpoint(
        ClaimsPrincipal principal,
        ResendVerification handler,
        HttpContext http,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        var result = await handler.HandleAsync(new ResendVerificationCommand(new UserId(id)), ct);

        return result.Match(_ => Results.Accepted(), error => ApiProblem.From(error, http));
    }

    private static async Task<IResult> SetPasswordEndpoint(
        [Microsoft.AspNetCore.Mvc.FromBody] SetPasswordRequest request,
        ClaimsPrincipal principal,
        SetPassword handler,
        HttpContext http,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        var result = await handler.HandleAsync(
            new SetPasswordCommand(
                new UserId(id), request.CurrentPassword, request.NewPassword, principal.FamilyId()),
            ct);

        return result.Match(_ => Results.NoContent(), error => ApiProblem.From(error, http));
    }

    private static async Task<IResult> RevokeOthersEndpoint(
        ClaimsPrincipal principal,
        RevokeOtherSessions handler,
        HttpContext http,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        var result = await handler.HandleAsync(
            new RevokeOtherSessionsCommand(new UserId(id), principal.FamilyId()), ct);

        return result.Match(
            count => Results.Ok(new { signedOut = count }),
            error => ApiProblem.From(error, http));
    }

    private static async Task<IResult> RevokeSessionEndpoint(
        string familyId,
        ClaimsPrincipal principal,
        RevokeSession handler,
        HttpContext http,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        var result = await handler.HandleAsync(
            new RevokeSessionCommand(new UserId(id), familyId, principal.FamilyId()), ct);

        return result.Match(_ => Results.NoContent(), error => ApiProblem.From(error, http));
    }
}

/// <summary>
/// Turns a User-Agent into something a person recognises.
///
/// <para>
/// <b>Derived when read, not when stored.</b> The raw string is kept; this
/// runs over it on the way out. Improving the parser then costs nothing —
/// no migration, and old sessions get the better label immediately.
/// </para>
///
/// <para>
/// <b>Deliberately crude.</b> Full User-Agent parsing is a library, a data
/// file and a subscription to keep it current. This answers the only question
/// the screen asks — *"is that me, or someone I don't know?"* — and a person
/// recognises "Chrome trên Windows" without needing a version number. When it
/// cannot tell, it says so instead of guessing.
/// </para>
/// </summary>
internal static class DeviceLabel
{
    public static string From(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Thiết bị không xác định";

        var ua = userAgent;
        var browser = Browser(ua);
        var platform = Platform(ua);

        if (browser is null && platform is null) return "Thiết bị không xác định";
        if (browser is null) return platform!;
        if (platform is null) return browser;

        return $"{browser} trên {platform}";
    }

    /// <summary>
    /// Order matters and is the whole trick: Edge and Opera both claim to be
    /// Chrome, and Chrome claims to be Safari. Checking the most specific
    /// marker first is what stops every browser reporting as Safari.
    /// </summary>
    private static string? Browser(string ua) =>
        Has(ua, "Edg") ? "Edge"
        : Has(ua, "OPR") || Has(ua, "Opera") ? "Opera"
        : Has(ua, "Firefox") ? "Firefox"
        : Has(ua, "SamsungBrowser") ? "Samsung Internet"
        : Has(ua, "Chrome") || Has(ua, "CriOS") ? "Chrome"
        : Has(ua, "Safari") ? "Safari"
        : null;

    /// <summary>
    /// Android before Linux, because every Android User-Agent also says Linux.
    /// iPhone and iPad before Mac for the same reason.
    /// </summary>
    private static string? Platform(string ua) =>
        Has(ua, "Android") ? "Android"
        : Has(ua, "iPhone") ? "iPhone"
        : Has(ua, "iPad") ? "iPad"
        : Has(ua, "Windows") ? "Windows"
        : Has(ua, "Mac OS X") || Has(ua, "Macintosh") ? "macOS"
        : Has(ua, "Linux") ? "Linux"
        : null;

    private static bool Has(string ua, string marker) =>
        ua.Contains(marker, StringComparison.OrdinalIgnoreCase);
}
