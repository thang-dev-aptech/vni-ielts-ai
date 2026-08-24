using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Vni.Ielts.Api.Common;

/// <summary>
/// The one place that answers "who is calling".
///
/// <para>
/// <b>This exists because three call sites disagreed and two were silently
/// wrong.</b> The <c>/me</c> endpoint read <see cref="ClaimTypes.NameIdentifier"/>
/// and worked; the rate limiter and the idempotency middleware read
/// <c>"sub"</c> and always got null, because ASP.NET Core's JWT handler maps
/// inbound <c>sub</c> onto the long WS-Federation URI by default.
/// </para>
///
/// <para>
/// Nothing failed visibly. Rate limits quietly partitioned every authenticated
/// caller by IP — so every learner behind one mobile carrier NAT shared a
/// bucket, and an attacker only had to rotate address. Idempotency keys quietly
/// partitioned under <c>"anonymous"</c>, which means one caller's stored
/// response could be replayed to another who happened to use the same key
/// string. That is a cross-account data leak waiting for the first
/// authenticated POST endpoint.
/// </para>
///
/// <para>
/// The token itself is unchanged; <c>MapInboundClaims</c> is now off so claim
/// names survive as written. This helper is the belt to that braces: one
/// function, so a future disagreement is impossible rather than merely unlikely.
/// </para>
/// </summary>
public static class CallerIdentity
{
    /// <summary>The authenticated user's id, or null when the caller is anonymous.</summary>
    public static string? UserId(this ClaimsPrincipal principal) =>
        principal.FindFirst("sub")?.Value
        ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public static string? UserId(this HttpContext context) => context.User.UserId();

    /// <summary>
    /// A partition key for rate limiting and idempotency scoping.
    ///
    /// The prefix matters: without it a user whose id happened to look like an
    /// address would share a bucket with that address.
    /// </summary>
    public static string PartitionKey(this HttpContext context) =>
        context.UserId() is { } id
            ? $"user:{id}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    public static string DisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirst("name")?.Value ?? string.Empty;

    /// <summary>The signed-in address. Used by the audit trail to name the actor.</summary>
    public static string? Email(this ClaimsPrincipal principal) =>
        principal.FindFirst("email")?.Value;

    public static bool EmailVerified(this ClaimsPrincipal principal) =>
        principal.FindFirst("email_verified")?.Value == "true";

    public static IReadOnlyCollection<string> Permissions(this ClaimsPrincipal principal) =>
        [.. principal.FindAll("perm").Select(c => c.Value)];

    /// <summary>
    /// The refresh-token family this access token was issued into.
    ///
    /// It is what lets the device list say "this one is you" — and, more
    /// importantly, refuse to end the session doing the asking.
    /// </summary>
    public static string? FamilyId(this ClaimsPrincipal principal) =>
        principal.FindFirst("fam")?.Value;
}
