using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Identity;

namespace Vni.Ielts.Infrastructure.Security;

/// <summary>
/// JWT access tokens plus rotating refresh tokens with reuse detection.
/// </summary>
internal sealed class JwtTokenService(
    MongoContext ctx, IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    private readonly JwtOptions _opt = options.Value;

    public async Task<TokenPair> IssueAsync(
        User user, IReadOnlyCollection<string> permissions, string? familyId, CancellationToken ct)
    {
        // null means a fresh sign-in, which starts a new family. Rotation
        // passes the redeemed token's family so the chain stays intact.
        familyId ??= Guid.NewGuid().ToString("n");

        var now = clock.UtcNow;
        var accessExpires = now.AddMinutes(_opt.AccessTokenMinutes);
        var refreshExpires = now.AddDays(_opt.RefreshTokenDays);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.Value),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n")),
            new("name", user.DisplayName),
            new("email_verified", user.EmailVerified ? "true" : "false"),
        };

        // Permissions travel in the token so the API does not hit the database
        // on every request. The cost of that choice is real and worth stating:
        // a permission revoked mid-session stays effective until the access
        // token expires. That window is AccessTokenMinutes, which is why it is
        // 15 and not 8 hours.
        claims.AddRange(permissions.Select(p => new Claim("perm", p)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: accessExpires.UtcDateTime,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);

        // 256 bits from a CSPRNG. Not a JWT: a refresh token carries no claims
        // anyone needs to read, and an opaque value cannot be parsed for
        // information if it leaks.
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        await ctx.RefreshTokens.InsertOneAsync(
            new RefreshTokenDocument
            {
                Id = Guid.NewGuid().ToString("n"),
                TokenHash = HashToken(refreshToken),
                UserId = user.Id.Value,
                FamilyId = familyId,
                ExpiresAt = refreshExpires.UtcDateTime,
                CreatedAt = now.UtcDateTime,
            },
            cancellationToken: ct);

        return new TokenPair(accessToken, accessExpires, refreshToken, refreshExpires);
    }

    /// <summary>
    /// Redeems a refresh token, rotating it.
    ///
    /// The reuse branch is the whole point. A token that has already been used
    /// means two parties hold it; one of them stole it, and nothing here can
    /// tell which. So the entire family is revoked and the real user signs in
    /// again — an inconvenience, against an attacker holding a live session
    /// indefinitely. → threat T3
    /// </summary>
    public async Task<Result<RefreshOutcome>> RedeemRefreshTokenAsync(
        string refreshToken, CancellationToken ct)
    {
        var hash = HashToken(refreshToken);
        var now = clock.UtcNow.UtcDateTime;

        var stored = await ctx.RefreshTokens.Find(t => t.TokenHash == hash).FirstOrDefaultAsync(ct);

        if (stored is null)
            return Error.Unauthorized(ErrorCodes.RefreshTokenInvalid, "This session is no longer valid.");

        if (stored.UsedAt is not null)
        {
            await RevokeFamilyAsync(new UserId(stored.UserId), stored.FamilyId, ct);
            return Error.Unauthorized(
                ErrorCodes.RefreshTokenReused,
                "This session was ended for security reasons. Please sign in again.");
        }

        if (stored.RevokedAt is not null || stored.ExpiresAt <= now)
            return Error.Unauthorized(ErrorCodes.RefreshTokenInvalid, "This session is no longer valid.");

        // Atomic mark-as-used. The filter includes `UsedAt == null`, so two
        // concurrent redemptions of the same token cannot both succeed — the
        // loser sees zero modified and is treated as a reuse. A read-then-write
        // would let both through under exactly the retry pressure mobile
        // clients generate.
        var claimed = await ctx.RefreshTokens.UpdateOneAsync(
            t => t.TokenHash == hash && t.UsedAt == null,
            Builders<RefreshTokenDocument>.Update.Set(t => t.UsedAt, now),
            cancellationToken: ct);

        if (claimed.ModifiedCount == 0)
        {
            await RevokeFamilyAsync(new UserId(stored.UserId), stored.FamilyId, ct);
            return Error.Unauthorized(
                ErrorCodes.RefreshTokenReused,
                "This session was ended for security reasons. Please sign in again.");
        }

        return new RefreshOutcome(new UserId(stored.UserId), stored.FamilyId);
    }

    public Task RevokeFamilyAsync(UserId userId, string familyId, CancellationToken ct) =>
        ctx.RefreshTokens.UpdateManyAsync(
            t => t.UserId == userId.Value && t.FamilyId == familyId && t.RevokedAt == null,
            Builders<RefreshTokenDocument>.Update.Set(t => t.RevokedAt, clock.UtcNow.UtcDateTime),
            cancellationToken: ct);

    public Task RevokeAllForUserAsync(UserId userId, CancellationToken ct) =>
        ctx.RefreshTokens.UpdateManyAsync(
            t => t.UserId == userId.Value && t.RevokedAt == null,
            Builders<RefreshTokenDocument>.Update.Set(t => t.RevokedAt, clock.UtcNow.UtcDateTime),
            cancellationToken: ct);

    /// <summary>
    /// SHA-256, not Argon2id. The token is 256 bits of CSPRNG output, so there
    /// is no dictionary to defend against and a deliberately slow hash would
    /// only add latency to every refresh.
    /// </summary>
    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
