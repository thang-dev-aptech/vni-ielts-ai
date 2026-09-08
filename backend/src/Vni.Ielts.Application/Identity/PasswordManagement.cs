using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

/*
 * <b>"Forgot my password" no longer lives here, and that is a product
 * decision rather than an omission.</b>
 *
 * Recovery used to be a link mailed to the account's address. Registration
 * takes no address any more, so for most accounts there is nowhere to send
 * one — and the owner chose the replacement outright on 08/09/2026: a Zalo
 * contact link on the sign-in page, and an operator who sets a new password
 * from the CMS. The endpoint that used to be here would have been a button
 * that silently did nothing for every phone-registered learner.
 *
 * What that costs is written down rather than hidden: an operator who can set
 * a password can sign in as that person, so the act carries its own permission
 * key and its own audit action. → PermissionKeys.UserResetPassword, ADR-0018
 */

public sealed record SetPasswordCommand(
    UserId UserId, string? CurrentPassword, string NewPassword, string? CurrentFamilyId);

/// <summary>
/// Setting or changing a password while signed in.
///
/// <para>
/// <b>Two situations, one endpoint, and the difference is whether a password
/// already exists.</b> An account created through Google has none — this is
/// the "Tạo mật khẩu" the owner asked for on 21/08/2026, and there is nothing
/// to ask for first. An account that has one must prove it, because a stolen
/// access token would otherwise be enough to lock the real owner out.
/// </para>
/// </summary>
public sealed class SetPassword(
    IUserRepository users,
    IUserIdentityRepository identities,
    IPasswordHasher hasher,
    ITokenService sessions,
    IClock clock)
{
    public async Task<Result<bool>> HandleAsync(SetPasswordCommand command, CancellationToken ct)
    {
        var password = PasswordPolicy.Validate(command.NewPassword);
        if (!password.IsSuccess) return password.Error;

        var user = await users.FindByIdAsync(command.UserId, ct);
        if (user is null) return Error.NotFound(ErrorCodes.NotFound, "Account not found.");
        if (!user.CanAuthenticate)
            return Error.Forbidden(ErrorCodes.AccountSuspended, "This account has been suspended.");

        var existing = (await identities.ListForUserAsync(user.Id, ct))
            .FirstOrDefault(i => i.Provider == IdentityProvider.Password);

        if (existing?.PasswordHash is not null)
        {
            if (string.IsNullOrEmpty(command.CurrentPassword)
                || !hasher.Verify(command.CurrentPassword, existing.PasswordHash))
            {
                return Error.Validation(
                    ErrorCodes.CurrentPasswordWrong, "Your current password is not correct.");
            }
        }

        await PasswordIdentity.SetAsync(identities, hasher, clock, user, password.Value!, ct);

        // Other devices go; this one stays. Someone setting a password from
        // their own profile page should not be signed out of the page they are
        // standing on — but anything else holding a session predates the
        // change and should not survive it.
        await sessions.RevokeAllExceptAsync(user.Id, command.CurrentFamilyId ?? string.Empty, ct);

        return true;
    }
}

/// <summary>
/// Writes a password hash onto the account's single password identity,
/// creating that identity when the account has never had one.
///
/// <para>
/// <b>One row per account, keyed by account id.</b> It used to be keyed by the
/// email address, which meant an account signing in with both a phone number
/// and an address would need two rows carrying the same hash — two places to
/// update, and one of them eventually missed. → UserIdentity.ProviderUserId
/// </para>
/// </summary>
public static class PasswordIdentity
{
    public static async Task SetAsync(
        IUserIdentityRepository identities,
        IPasswordHasher hasher,
        IClock clock,
        User user,
        string password,
        CancellationToken ct)
    {
        var hash = hasher.Hash(password);
        var existing = (await identities.ListForUserAsync(user.Id, ct))
            .FirstOrDefault(i => i.Provider == IdentityProvider.Password);

        if (existing is null)
        {
            await identities.AddAsync(UserIdentity.ForPassword(user.Id, hash, clock.UtcNow), ct);
            return;
        }

        existing.SetPasswordHash(hash);
        await identities.SaveAsync(existing, ct);
    }
}
