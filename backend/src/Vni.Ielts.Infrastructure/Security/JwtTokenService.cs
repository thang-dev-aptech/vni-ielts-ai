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
    MongoContext ctx, IOptions<JwtOptions> options, IClock clock, IRequestDevice device) : ITokenService
{
    private readonly JwtOptions _opt = options.Value;

    public async Task<TokenPair> IssueAsync(
        User user, IReadOnlyCollection<string> permissions, string? familyId, CancellationToken ct,
        string? rotatedFromHash = null)
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
            // The address, so an audit entry can name the actor without a
            // lookup — and stay readable after that account is renamed or
            // deleted. A display name is user-editable and makes a poor
            // record of who did something.
            new("email", user.Email.Value),
            new("email_verified", user.EmailVerified ? "true" : "false"),
            // The family this token belongs to. Without it the session list
            // cannot say which entry is the device asking, and would offer
            // "sign out this device" on the one you are holding.
            new("fam", familyId),
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

        var refreshHash = HashToken(refreshToken);

        await ctx.RefreshTokens.InsertOneAsync(
            new RefreshTokenDocument
            {
                Id = Guid.NewGuid().ToString("n"),
                TokenHash = refreshHash,
                UserId = user.Id.Value,
                FamilyId = familyId,
                ExpiresAt = refreshExpires.UtcDateTime,
                CreatedAt = now.UtcDateTime,
                // Trimmed rather than trusted: a User-Agent is attacker-supplied
                // text of unbounded length, and it ends up rendered in a list.
                UserAgent = Trim(device.UserAgent),
            },
            cancellationToken: ct);

        /*
         * <b>Point the spent token at its replacement.</b>
         *
         * Written after the successor exists rather than before, so a crash
         * between the two leaves a used token with no successor recorded —
         * which reads as "not recoverable" and falls through to the family
         * revocation. That is the safe direction to be wrong in: it costs a
         * sign-in, where the other direction costs the reuse detection.
         */
        if (rotatedFromHash is not null)
        {
            await ctx.RefreshTokens.UpdateOneAsync(
                t => t.TokenHash == rotatedFromHash,
                Builders<RefreshTokenDocument>.Update.Set(t => t.SuccessorTokenHash, refreshHash),
                cancellationToken: ct);
        }

        return new TokenPair(accessToken, accessExpires, refreshToken, refreshExpires);
    }

    private static string? Trim(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;

        var value = userAgent.Trim();
        return value.Length > 400 ? value[..400] : value;
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
            /*
             * ── A used token is two different events ──────────────────────
             *
             * <b>Written 2026-08-28. Until then it was one, and the wrong one
             * was the common case.</b>
             *
             * Rotation marks a token used and issues its successor. If the
             * response carrying that successor never reaches the client — a
             * phone leaving a tunnel, a proxy timing out, a WebView the OS
             * suspended mid-response — the client retries with the only token
             * it has, which is the one just marked used. Reuse detection then
             * revoked the whole family and signed the learner out, mid-exam,
             * for a dropped packet. On a mobile network that is not an edge
             * case; it is Tuesday.
             *
             * <b>The successor tells the two apart.</b> If it was never used,
             * nobody ever received it: this is the lost response, and the
             * session is recoverable. If it <i>was</i> used, then two parties
             * hold tokens from this chain and one of them stole it — which is
             * what the family revocation exists for, and it still fires.
             *
             * <b>Bounded in time as well.</b> A retry after a lost response
             * happens within seconds; a stolen token is presented whenever the
             * thief gets round to it. The window is what keeps a theft hours
             * later from being read as a slow retry.
             *
             * <b>And the orphaned successor is revoked.</b> Reissuing without
             * that would leave two live tokens in one family — the very state
             * this whole mechanism exists to make impossible.
             */
            var recoverable =
                stored.SuccessorTokenHash is { } successorHash
                && stored.UsedAt.Value.Add(ReplayWindow) > now
                && stored.RevokedAt is null
                && await SuccessorIsUntouchedAsync(successorHash, ct);

            if (!recoverable)
            {
                await RevokeFamilyAsync(new UserId(stored.UserId), stored.FamilyId, ct);
                return Error.Unauthorized(
                    ErrorCodes.RefreshTokenReused,
                    "This session was ended for security reasons. Please sign in again.");
            }

            /*
             * <b>Claimed atomically, so two retries of one lost response do not
             * both recover.</b> The filter names the successor this document
             * pointed at; whichever request clears it first owns the recovery,
             * and the other finds no successor recorded and is treated as an
             * ordinary reuse. Reading and then writing would let both through
             * under exactly the retry pressure that produced the situation.
             */
            var claimedRecovery = await ctx.RefreshTokens.UpdateOneAsync(
                t => t.TokenHash == hash && t.SuccessorTokenHash == stored.SuccessorTokenHash,
                Builders<RefreshTokenDocument>.Update.Unset(t => t.SuccessorTokenHash),
                cancellationToken: ct);

            if (claimedRecovery.ModifiedCount == 0)
            {
                await RevokeFamilyAsync(new UserId(stored.UserId), stored.FamilyId, ct);
                return Error.Unauthorized(
                    ErrorCodes.RefreshTokenReused,
                    "This session was ended for security reasons. Please sign in again.");
            }

            // The successor nobody received is retired. One live token per
            // family, which is the invariant the whole design rests on.
            await ctx.RefreshTokens.UpdateOneAsync(
                t => t.TokenHash == stored.SuccessorTokenHash && t.RevokedAt == null,
                Builders<RefreshTokenDocument>.Update.Set(t => t.RevokedAt, now),
                cancellationToken: ct);

            return new RefreshOutcome(new UserId(stored.UserId), stored.FamilyId, hash);
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

        return new RefreshOutcome(new UserId(stored.UserId), stored.FamilyId, hash);
    }

    /// <summary>
    /// How long after a rotation a re-presentation of the parent is read as a
    /// retry rather than as a theft.
    ///
    /// <b>Sixty seconds, and the number is a trade rather than a preference.</b>
    /// A client that lost a response retries within seconds — the transport's
    /// own retry, or the coordinator's backoff, both of which are far inside a
    /// minute. A thief presents a stolen token whenever they get round to it.
    /// Longer widens the window in which a stolen parent is honoured; shorter
    /// starts signing out learners whose phone took a while to notice the
    /// network had come back.
    ///
    /// The window is not the only guard: the successor must also be untouched,
    /// which means a thief only benefits if the legitimate client never
    /// received the successor at all.
    /// </summary>
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Has the successor been used or revoked by anybody?
    ///
    /// If it has, the client did receive it, and a presentation of its parent
    /// is a second party holding a token from this chain — a theft, and the
    /// family goes.
    /// </summary>
    private async Task<bool> SuccessorIsUntouchedAsync(string successorHash, CancellationToken ct)
    {
        var successor = await ctx.RefreshTokens
            .Find(t => t.TokenHash == successorHash)
            .FirstOrDefaultAsync(ct);

        // No successor row at all means the rotation did not complete, so
        // nothing was ever issued to receive. Treated as not recoverable: the
        // safe direction is a sign-in, not a weakened reuse check.
        return successor is not null && successor.UsedAt is null && successor.RevokedAt is null;
    }

    public async Task<int> RevokeAllExceptAsync(UserId userId, string keepFamilyId, CancellationToken ct)
    {
        var result = await ctx.RefreshTokens.UpdateManyAsync(
            t => t.UserId == userId.Value && t.FamilyId != keepFamilyId && t.RevokedAt == null,
            Builders<RefreshTokenDocument>.Update.Set(t => t.RevokedAt, clock.UtcNow.UtcDateTime),
            cancellationToken: ct);

        return (int)result.ModifiedCount;
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
