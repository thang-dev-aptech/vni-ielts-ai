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

        /*
         * <b>Authenticated, and that is what makes six digits safe.</b>
         *
         * The account comes from the token rather than from the code, so the
         * attempt cap is per account and nobody can spray one guess across
         * every account at once. An unauthenticated endpoint that found the
         * account <i>from</i> a six-digit code would let one guess match
         * somebody else's — which is the reason a bare code is normally unsafe,
         * and the reason this one is not.
         *
         * Rate-limited under the registration policy rather than the
         * authentication one: this is the same budget as asking for the code,
         * and both are far tighter than sign-in. → `I6.1`
         */
        group.MapPost("/verify-email", ConfirmEmailCodeEndpoint)
            .WithName("ConfirmEmailCode")
            .WithSummary("Verify this account's email with the six-digit code we sent")
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
            ok => Results.Ok(new
            {
                email = ok.Email,
                // The screen that renders the new address is the screen that
                // would otherwise say a link is on its way to it.
                verificationEmailSent = ok.VerificationMessage == MessageDelivery.Sent,
            }),
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

        // 200 with the facts, not 202 with none. "Accepted" is a promise that
        // the work will happen, and with the logging sender configured it does
        // not — the caller has to be able to tell the learner that nothing was
        // actually sent. → `M-45`
        return result.Match(
            ok => Results.Ok(new
            {
                emailVerified = ok.AlreadyVerified,
                verificationEmailSent = ok.VerificationMessage == MessageDelivery.Sent,
            }),
            error => ApiProblem.From(error, http));
    }

    /// <summary>The six digits, as the learner typed them.</summary>
    public sealed record ConfirmEmailCodeRequest(string Code);

    private static async Task<IResult> ConfirmEmailCodeEndpoint(
        [Microsoft.AspNetCore.Mvc.FromBody] ConfirmEmailCodeRequest request,
        ClaimsPrincipal principal,
        ConfirmEmailCode handler,
        HttpContext http,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        var result = await handler.HandleAsync(
            new ConfirmEmailCodeCommand(new UserId(id), request.Code), ct);

        return result.Match(
            outcome => outcome switch
            {
                CodeRedemption.Verified => Results.Ok(new { emailVerified = true }),

                /*
                 * <b>Three refusals, not one, because the learner's next move
                 * differs for each.</b> "Wrong code" sends them back to what
                 * they typed; "expired" sends them to the resend button; "too
                 * many attempts" has to say why the code in their hand stopped
                 * working, or they will keep trying it from the same email.
                 *
                 * A single "invalid code" would be the same failure the results
                 * screen had before `I3.6` — one sentence covering situations
                 * with different answers.
                 */
                CodeRedemption.Expired => Problem(
                    "VERIFICATION_CODE_EXPIRED",
                    "Mã đã hết hạn. Hãy bấm gửi lại để nhận mã mới.",
                    StatusCodes.Status400BadRequest),

                CodeRedemption.TooManyAttempts => Problem(
                    "VERIFICATION_CODE_ATTEMPTS_EXCEEDED",
                    "Bạn đã nhập sai quá nhiều lần và mã này không còn dùng được. "
                    + "Hãy bấm gửi lại để nhận mã mới.",
                    StatusCodes.Status400BadRequest),

                _ => Problem(
                    "VERIFICATION_CODE_INCORRECT",
                    "Mã không đúng. Hãy kiểm tra lại email và thử lần nữa.",
                    StatusCodes.Status400BadRequest),
            },
            error => ApiProblem.From(error, http));
    }

    private static IResult Problem(string code, string detail, int status) =>
        Results.Problem(
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

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
