using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Learning;
using Vni.Ielts.Domain.Learning;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Api.Endpoints;

/// <param name="ReferralCode">
/// The inviter's code, from a shared link. Optional, and an unrecognised code
/// is silently ignored rather than rejected — nobody's registration should
/// fail because someone else's link went stale. → `P-16`
/// </param>
public sealed record RegisterRequest(
    string Phone, string Password, string DisplayName, string? ReferralCode = null);

/// <param name="Identifier">
/// A phone number or an email address — one box on the client, and the server
/// works out which from whether it contains an `@`. Accounts registered since
/// 08/09/2026 have a number and no address; older accounts, accounts created
/// through Google that later set a password, and every operator account have an
/// address and no number. → <see cref="LoginWithPassword"/>
/// </param>
public sealed record LoginRequest(string Identifier, string Password);
public sealed record RefreshRequest(string RefreshToken);

public sealed record SessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    string UserId,
    string DisplayName);

/// <summary>
/// What registration answers with.
///
/// <para>
/// <b>A session, because registering signs the learner in.</b> `[QUYẾT ĐỊNH]`
/// chủ sản phẩm, 27/08/2026 — see <see cref="RegisterUser"/>. The session is
/// nested rather than flattened so the client parses exactly the same object
/// here as it does from <c>/login</c>; a near-copy of six fields is how the
/// two shapes drift.
/// </para>
/// </summary>
public sealed record RegisterResponse(SessionResponse Session);

public sealed record MeResponse(
    string UserId,
    string DisplayName,
    /// <summary>Null on every account that registered with a phone number.</summary>
    string? Email,
    string? Phone,
    IReadOnlyCollection<string> Permissions,
    /// <summary>Lower-case provider keys this account can sign in with: password, google.</summary>
    IReadOnlyCollection<string> Providers,
    bool HasPassword);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register", Register)
            .WithName("Register")
            .WithSummary("Register with a phone number and password")
            .RequireRateLimiting(RateLimitPolicies.Registration);

        group.MapPost("/login", Login)
            .WithName("Login")
            .WithSummary("Sign in with a phone number or email address and a password")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapPost("/refresh", Refresh)
            .WithName("Refresh")
            .WithSummary("Exchange a refresh token for a new pair")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        /*
         * <b>There is no /verify, /forgot-password or /reset-password.</b> All
         * three were removed on 08/09/2026 with email verification itself.
         * Registration asks for no address, so a reset link had nowhere to go
         * for most accounts; recovery is a Zalo contact link on the sign-in
         * page and an operator using POST /admin/users/{id}/password.
         * → ADR-0018, PermissionKeys.UserResetPassword
         */

        /*
         * <b>Signing out was a local act, and it should never have been.</b>
         *
         * Until 2026-08-28 the client cleared `localStorage` and that was all
         * of it: the refresh-token family stayed live on the server for its
         * full thirty days. So "sign out" on a shared machine, a library
         * computer, or a phone being handed on left a working credential behind
         * — recoverable from a browser profile backup, or from anything that
         * had already copied the value.
         *
         * <b>Authorised, and it revokes the caller's own family only.</b> The
         * family id comes from the access token's `fam` claim, so there is no
         * parameter through which one session could end another's. Ending every
         * device is a different, deliberate act, and it already has its own
         * screen. → `I4.6`, threat `T3`
         */
        group.MapPost("/logout", Logout)
            .WithName("Logout")
            .WithSummary("End this session on the server, not only in this browser")
            .RequireAuthorization();

        app.MapGet("/api/v1/me", Me).WithName("Me").WithTags("Identity").RequireAuthorization();
    }

    private static async Task<IResult> Logout(
        ClaimsPrincipal principal, ITokenService tokens, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        /*
         * <b>No family claim means nothing to revoke, and that is a success.</b>
         * An access token minted before the claim existed still identifies a
         * caller who is signing out; answering 401 would leave the client
         * unable to complete an action it must always be able to complete.
         */
        if (principal.FamilyId() is { } family)
            await tokens.RevokeFamilyAsync(new UserId(id), family, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterRequest request,
        RegisterUser handler,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new RegisterUserCommand(
                request.Phone, request.Password, request.DisplayName, request.ReferralCode),
            ct);

        return result.Match(
            // 201 with a session. Registration signs the learner in.
            ok => Results.Created(
                $"/api/v1/users/{ok.Session.UserId.Value}",
                new RegisterResponse(ToSession(ok.Session))),
            error => ApiProblem.From(error, http));
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        LoginWithPassword handler,
        LearnerPresence presence,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new LoginCommand(request.Identifier, request.Password), ct);
        if (result.IsSuccess) await presence.TouchAsync(result.Value!.UserId, ActivityKind.SignIn, ct);
        return result.Match(ok => Results.Ok(ToSession(ok)), error => ApiProblem.From(error, http));
    }

    private static async Task<IResult> Refresh(
        [FromBody] RefreshRequest request,
        RefreshTokens handler,
        LearnerPresence presence,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new RefreshCommand(request.RefreshToken), ct);
        // A refresh is a learner still here today; the streak counts it.
        if (result.IsSuccess) await presence.TouchAsync(result.Value!.UserId, ActivityKind.SignIn, ct);
        return result.Match(ok => Results.Ok(ToSession(ok)), error => ApiProblem.From(error, http));
    }

    /// <summary>
    /// The caller's account.
    ///
    /// <para>
    /// <b>This reads the database, unlike the rest of the token-backed
    /// surface.</b> Which providers are linked cannot travel in a token that
    /// lives fifteen minutes: someone who links Google must see it
    /// immediately, not when the access token next rolls over. Permissions
    /// still come from the token, where the fifteen-minute staleness is a
    /// documented trade.
    /// </para>
    /// </summary>
    private static async Task<IResult> Me(
        ClaimsPrincipal principal, GetMyAccount handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        var account = await handler.HandleAsync(new UserId(id), ct);
        if (account is null) return Results.Unauthorized();

        return Results.Ok(new MeResponse(
            account.UserId.Value,
            account.DisplayName,
            account.Email,
            account.Phone,
            principal.Permissions(),
            account.Providers,
            account.HasPassword));
    }

    /// <summary>
    /// Internal rather than private because social sign-in returns the same
    /// body. One shape, one place it is built. → ADR-0014
    /// </summary>
    internal static SessionResponse ToSession(LoginResult r) => new(
        r.Tokens.AccessToken,
        r.Tokens.AccessTokenExpiresAt,
        r.Tokens.RefreshToken,
        r.Tokens.RefreshTokenExpiresAt,
        r.UserId.Value,
        r.DisplayName);
}
