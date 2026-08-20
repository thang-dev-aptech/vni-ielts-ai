using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Identity;

namespace Vni.Ielts.Api.Endpoints;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record VerifyEmailRequest(string Token);

public sealed record SessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    string UserId,
    string DisplayName);

public sealed record MeResponse(
    string UserId, string DisplayName, bool EmailVerified, IReadOnlyCollection<string> Permissions);

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

        app.MapGet("/api/v1/me", Me).WithName("Me").WithTags("Identity").RequireAuthorization();
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
            // 201 with no tokens. Registration does not sign anyone in — the
            // address is still an unproven claim, and entitlement accrual and
            // referral confirmation both wait on verification. → T4, T13
            ok => Results.Created($"/api/v1/users/{ok.UserId}", new
            {
                userId = ok.UserId.Value,
                emailVerificationRequired = ok.EmailVerificationRequired,
            }),
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

    private static Ok<MeResponse> Me(ClaimsPrincipal principal) =>
        TypedResults.Ok(new MeResponse(
            principal.UserId() ?? string.Empty,
            principal.DisplayName(),
            principal.EmailVerified(),
            principal.Permissions()));

    private static SessionResponse ToSession(LoginResult r) => new(
        r.Tokens.AccessToken,
        r.Tokens.AccessTokenExpiresAt,
        r.Tokens.RefreshToken,
        r.Tokens.RefreshTokenExpiresAt,
        r.UserId.Value,
        r.DisplayName);
}
