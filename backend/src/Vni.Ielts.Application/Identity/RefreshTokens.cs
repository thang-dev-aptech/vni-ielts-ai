using Vni.Ielts.Application.Common;

namespace Vni.Ielts.Application.Identity;

public sealed record RefreshCommand(string RefreshToken);

/// <summary>
/// Exchanges a refresh token for a new pair, rotating it.
///
/// The interesting behaviour lives in <see cref="ITokenService"/>: a token
/// that has already been redeemed does not merely fail, it <b>revokes the
/// whole family</b>. Two parties holding the same refresh token means one of
/// them stole it, and there is no way to tell which — so both lose access and
/// the real user signs in again.
///
/// Rotation without reuse detection would leave a stolen token quietly usable
/// until expiry, and the theft undetected. → threat T3
/// </summary>
public sealed class RefreshTokens(
    IUserRepository users,
    IPermissionResolver permissions,
    ITokenService tokens)
{
    public async Task<Result<LoginResult>> HandleAsync(RefreshCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return Error.Unauthorized(ErrorCodes.RefreshTokenInvalid, "A refresh token is required.");

        var redeemed = await tokens.RedeemRefreshTokenAsync(command.RefreshToken, ct);
        if (!redeemed.IsSuccess)
            return redeemed.Error;

        var user = await users.FindByIdAsync(redeemed.Value!.UserId, ct);
        if (user is null)
            return Error.Unauthorized(ErrorCodes.RefreshTokenInvalid, "This session is no longer valid.");

        if (!user.CanAuthenticate)
        {
            // A suspension taking effect must end existing sessions, not wait
            // for an access token to expire on its own.
            await tokens.RevokeAllForUserAsync(user.Id, ct);
            return Error.Forbidden(ErrorCodes.AccountSuspended, "This account has been suspended.");
        }

        var granted = await permissions.ResolveAsync(user, ct);
        // Continue the family rather than starting a new one, so reuse
        // detection still spans the whole chain of rotations.
        var pair = await tokens.IssueAsync(user, granted, redeemed.Value.FamilyId, ct);

        return new LoginResult(pair, user.Id, user.DisplayName);
    }
}
