using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Api.Endpoints;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record VerifyEmailRequest(string Token);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);

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
/// <param name="EmailVerified">
/// Always false on a fresh registration. Sent anyway so the client never has
/// to assume it, and so the field means the same thing as it does on
/// <c>/me</c>.
/// </param>
/// <param name="VerificationEmailSent">
/// <b>Whether a message actually left the server.</b> False is the normal
/// answer today — the only configured sender writes the link to the server
/// log — and a client that shows "check your inbox" over a false here is
/// telling the learner to wait for something that will never arrive.
/// </param>
public sealed record RegisterResponse(
    SessionResponse Session,
    bool EmailVerified,
    bool VerificationEmailSent);

public sealed record MeResponse(
    string UserId,
    string DisplayName,
    string? Email,
    bool EmailVerified,
    string? Phone,
    IReadOnlyCollection<string> Permissions,
    /// <summary>Lower-case provider keys this account can sign in with: email, google.</summary>
    IReadOnlyCollection<string> Providers,
    bool HasPassword);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register", Register)
            .WithName("Register")
            .WithSummary("Register with an email address and password")
            .RequireRateLimiting(RateLimitPolicies.Registration);

        group.MapPost("/login", Login)
            .WithName("Login")
            .WithSummary("Sign in with an email address and password")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapPost("/refresh", Refresh)
            .WithName("Refresh")
            .WithSummary("Exchange a refresh token for a new pair")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapPost("/verify", Verify)
            .WithName("VerifyEmail")
            .WithSummary("Redeem an email verification token")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapPost("/forgot-password", ForgotPassword)
            .WithName("ForgotPassword")
            .WithSummary("Send a password reset link, if the address has an account")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        group.MapPost("/reset-password", ResetPasswordEndpoint)
            .WithName("ResetPassword")
            .WithSummary("Redeem a reset link and set a new password")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

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
            new RegisterUserCommand(request.Email, request.Password, request.DisplayName), ct);

        return result.Match(
            // 201 with a session. Registration signs the learner in and the
            // address is verified later from the profile page. → `M-45`
            ok => Results.Created(
                $"/api/v1/users/{ok.Session.UserId.Value}",
                new RegisterResponse(
                    ToSession(ok.Session),
                    EmailVerified: false,
                    VerificationEmailSent: ok.VerificationMessage == MessageDelivery.Sent)),
            error => ApiProblem.From(error, http));
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        LoginWithPassword handler,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password), ct);
        return result.Match(ok => Results.Ok(ToSession(ok)), error => ApiProblem.From(error, http));
    }

    private static async Task<IResult> Refresh(
        [FromBody] RefreshRequest request,
        RefreshTokens handler,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new RefreshCommand(request.RefreshToken), ct);
        return result.Match(ok => Results.Ok(ToSession(ok)), error => ApiProblem.From(error, http));
    }

    /// <summary>
    /// Read straight from the validated token. No database round trip — the
    /// permissions were resolved when the token was issued.
    ///
    /// The trade-off is stated in JwtTokenService: a permission revoked
    /// mid-session stays effective until the access token expires. That is
    /// what bounds AccessTokenMinutes at 15.
    /// </summary>
    private static async Task<IResult> Verify(
        [FromBody] VerifyEmailRequest request,
        VerifyEmail handler,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new VerifyEmailCommand(request.Token), ct);

        return result.Match(
            userId => Results.Ok(new { userId = userId.Value, emailVerified = true }),
            error => ApiProblem.From(error, http));
    }

    /// <summary>
    /// The caller's account.
    ///
    /// <para>
    /// <b>This now reads the database, and that is a deliberate change.</b> It
    /// used to answer entirely from the validated token, which was free. But
    /// which providers are linked cannot travel in a token that lives fifteen
    /// minutes: someone who links Google must see it immediately, not when the
    /// access token next rolls over. Permissions still come from the token,
    /// where the fifteen-minute staleness is a documented trade.
    /// </para>
    /// </summary>
    /// <summary>
    /// <b>Always 202, whatever happened.</b> Telling the caller whether the
    /// address exists would make this a free account-enumeration oracle, and
    /// nobody legitimate needs the answer — they are about to go and look in
    /// their mailbox either way. → threat T4
    /// </summary>
    private static async Task<IResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        RequestPasswordReset handler,
        CancellationToken ct)
    {
        await handler.HandleAsync(new RequestPasswordResetCommand(request.Email), ct);
        return Results.Accepted();
    }

    private static async Task<IResult> ResetPasswordEndpoint(
        [FromBody] ResetPasswordRequest request,
        ResetPassword handler,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new ResetPasswordCommand(request.Token, request.NewPassword), ct);

        return result.Match(
            userId => Results.Ok(new { userId = userId.Value }),
            error => ApiProblem.From(error, http));
    }

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
            account.EmailVerified,
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
