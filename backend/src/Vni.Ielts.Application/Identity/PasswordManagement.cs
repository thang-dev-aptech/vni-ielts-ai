using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

public sealed record RequestPasswordResetCommand(string Email);

/// <summary>
/// "I forgot my password."
///
/// <para>
/// <b>Always reports success.</b> An unknown address, an address with no
/// password identity, and a real one all return the same answer, because the
/// alternative turns this endpoint into a free account-enumeration oracle —
/// and unlike registration, nothing here needs to tell the caller anything.
/// Rate limiting is the control that matters. → threat T4
/// </para>
///
/// <para>
/// <b>It works for an account created through Google.</b> That account has an
/// address Google already verified, so a link sent there reaches its owner —
/// which is precisely how someone who only ever used Google gains a password
/// without anyone having to trust an unverified claim.
/// </para>
/// </summary>
public sealed class RequestPasswordReset(
    IUserRepository users,
    IPasswordResetTokens tokens,
    IVerificationMessageSender sender)
{
    public async Task HandleAsync(RequestPasswordResetCommand command, CancellationToken ct)
    {
        if (!Email.TryCreate(command.Email, out var email)) return;

        var user = await users.FindByEmailAsync(email, ct);

        // A suspended account does not get a way back in by email.
        if (user is null || !user.CanAuthenticate) return;

        await sender.SendPasswordResetAsync(email, await tokens.IssueAsync(user.Id, ct), ct);
    }
}

public sealed record ResetPasswordCommand(string Token, string NewPassword);

/// <summary>
/// Redeems a reset link and sets the password.
///
/// <para>
/// <b>Every other session ends.</b> A reset is what someone does when they
/// think their account is compromised, and leaving the attacker's session
/// alive would make the reset theatre. The person doing it is not signed in
/// here — they are following a link from their mailbox — so there is no
/// current session to preserve. → threat T5
/// </para>
///
/// <para>
/// <b>It creates the email identity if there is not one.</b> An account made
/// through Google has no password row at all; without this, "forgot password"
/// would succeed and change nothing.
/// </para>
/// </summary>
public sealed class ResetPassword(
    IUserRepository users,
    IUserIdentityRepository identities,
    IPasswordResetTokens tokens,
    IPasswordHasher hasher,
    ITokenService sessions,
    IClock clock)
{
    public async Task<Result<UserId>> HandleAsync(ResetPasswordCommand command, CancellationToken ct)
    {
        var password = PasswordPolicy.Validate(command.NewPassword);
        if (!password.IsSuccess) return password.Error;

        var userId = await tokens.RedeemAsync(command.Token, ct);
        if (userId is null)
        {
            return Error.Validation(
                ErrorCodes.ResetTokenInvalid,
                "This link is no longer valid. Request a new one.");
        }

        var user = await users.FindByIdAsync(userId.Value, ct);
        if (user is null || !user.CanAuthenticate)
            return Error.Validation(ErrorCodes.ResetTokenInvalid, "This link is no longer valid.");

        await SetOnEmailIdentityAsync(identities, hasher, clock, user, password.Value!, ct);

        // Redeeming a link proves the mailbox, which is the same proof
        // verification asks for. Someone who reset their password has
        // demonstrably received mail at that address.
        if (!user.EmailVerified)
        {
            user.MarkEmailVerified();
            await users.SaveAsync(user, ct);
        }

        await sessions.RevokeAllForUserAsync(user.Id, ct);

        return user.Id;
    }

    /// <summary>
    /// Writes the hash onto the account's email identity, creating that
    /// identity when the account has never had one.
    /// </summary>
    internal static async Task SetOnEmailIdentityAsync(
        IUserIdentityRepository identities,
        IPasswordHasher hasher,
        IClock clock,
        User user,
        string password,
        CancellationToken ct)
    {
        var hash = hasher.Hash(password);
        var existing = (await identities.ListForUserAsync(user.Id, ct))
            .FirstOrDefault(i => i.Provider == IdentityProvider.Email);

        if (existing is null)
        {
            await identities.AddAsync(
                UserIdentity.ForEmail(user.Id, user.Email, hash, clock.UtcNow), ct);
            return;
        }

        existing.SetPasswordHash(hash);
        await identities.SaveAsync(existing, ct);
    }
}

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
            .FirstOrDefault(i => i.Provider == IdentityProvider.Email);

        if (existing?.PasswordHash is not null)
        {
            if (string.IsNullOrEmpty(command.CurrentPassword)
                || !hasher.Verify(command.CurrentPassword, existing.PasswordHash))
            {
                return Error.Validation(
                    ErrorCodes.CurrentPasswordWrong, "Your current password is not correct.");
            }
        }

        await ResetPassword.SetOnEmailIdentityAsync(
            identities, hasher, clock, user, password.Value!, ct);

        // Other devices go; this one stays. Someone setting a password from
        // their own profile page should not be signed out of the page they are
        // standing on — but anything else holding a session predates the
        // change and should not survive it.
        await sessions.RevokeAllExceptAsync(user.Id, command.CurrentFamilyId ?? string.Empty, ct);

        return true;
    }
}
