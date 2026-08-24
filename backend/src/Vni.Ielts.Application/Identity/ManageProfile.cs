using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

public sealed record SetPhoneCommand(UserId UserId, string? Phone);

/// <summary>
/// Adds, changes or removes the learner's contact number.
///
/// <para>
/// <b>Nothing is verified, and nothing pretends to be.</b> There is no OTP
/// here because whether a number must be proven has not been decided — and
/// inventing a verification flow would be inventing the policy behind it.
/// An empty value clears the number, which is the only way back out for
/// someone who typed the wrong one.
/// </para>
/// </summary>
public sealed class SetPhone(IUserRepository users)
{
    public async Task<Result<string?>> HandleAsync(SetPhoneCommand command, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(command.UserId, ct);
        if (user is null) return Error.NotFound(ErrorCodes.NotFound, "Account not found.");

        if (string.IsNullOrWhiteSpace(command.Phone))
        {
            user.SetPhone(null);
            await users.SaveAsync(user, ct);
            return (string?)null;
        }

        if (!PhoneNumber.TryCreate(command.Phone, out var phone))
        {
            return Error.Validation(
                ErrorCodes.PhoneInvalid, "That does not look like a phone number.");
        }

        user.SetPhone(phone);
        await users.SaveAsync(user, ct);

        return phone.Value;
    }
}

public sealed record ChangeEmailCommand(UserId UserId, string Email);

/// <summary>
/// Corrects an address that has not been verified yet.
///
/// <para>
/// <b>Only while it is unverified.</b> Someone who typed `gmial.com` has no
/// other way out — the link that would fix it goes to the address that is
/// wrong. Once the address is proven it becomes the account's route back in,
/// and letting a stolen session move it to another mailbox would hand the
/// account over permanently.
/// </para>
///
/// <para>
/// <b>It writes the address in two places, and both are required.</b> The
/// email identity is keyed by the address itself — that is what password
/// sign-in looks up — so updating only <c>User.Email</c> leaves an account
/// that displays the new address and cannot be signed in to at either one.
/// </para>
/// </summary>
public sealed class ChangeEmail(
    IUserRepository users,
    IUserIdentityRepository identities,
    IEmailVerificationTokens tokens,
    IVerificationMessageSender sender)
{
    public async Task<Result<string>> HandleAsync(ChangeEmailCommand command, CancellationToken ct)
    {
        if (!Email.TryCreate(command.Email, out var email))
            return Error.Validation(ErrorCodes.EmailInvalid, "That is not a valid email address.");

        var user = await users.FindByIdAsync(command.UserId, ct);
        if (user is null) return Error.NotFound(ErrorCodes.NotFound, "Account not found.");

        if (user.EmailVerified)
        {
            return Error.Conflict(
                ErrorCodes.EmailLocked,
                "This address has been verified and can no longer be changed.");
        }

        if (user.Email == email)
        {
            // Not an error, and not worth a second verification mail either.
            return email.Value;
        }

        if (await users.EmailExistsAsync(email, ct))
        {
            return Error.Conflict(
                ErrorCodes.EmailAlreadyRegistered, "That email address is already registered.");
        }

        var emailIdentity = (await identities.ListForUserAsync(user.Id, ct))
            .FirstOrDefault(i => i.Provider == IdentityProvider.Email);

        user.ChangeEmail(email);

        try
        {
            await users.SaveAsync(user, ct);
        }
        catch (DuplicateEmailException)
        {
            // Lost the race against another account claiming the address. The
            // unique index is what enforces it; the check above is a courtesy
            // that produces a clean message.
            return Error.Conflict(
                ErrorCodes.EmailAlreadyRegistered, "That email address is already registered.");
        }

        if (emailIdentity is not null)
        {
            emailIdentity.ChangeEmailAddress(email);
            await identities.SaveAsync(emailIdentity, ct);
        }

        // Straight to the new address — the whole point of the change is that
        // the old one could not receive anything.
        await sender.SendAsync(email, await tokens.IssueAsync(user.Id, ct), ct);

        return email.Value;
    }
}

public sealed record ResendVerificationCommand(UserId UserId);

/// <summary>
/// Sends the verification email again.
///
/// <para>
/// Exists because the first one is easy to miss and there was no way to ask
/// for another — an account could sit unverified forever with the profile
/// showing "chưa xác minh" and offering nothing to do about it.
/// </para>
///
/// <para>
/// <b>Already-verified is a success, not an error.</b> Someone who presses it
/// twice, or who verified in another tab, has nothing to fix; reporting a
/// failure would send them looking for a problem that does not exist. No
/// second mail goes out either.
/// </para>
/// </summary>
public sealed class ResendVerification(
    IUserRepository users,
    IEmailVerificationTokens tokens,
    IVerificationMessageSender sender)
{
    public async Task<Result<bool>> HandleAsync(
        ResendVerificationCommand command, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(command.UserId, ct);
        if (user is null) return Error.NotFound(ErrorCodes.NotFound, "Account not found.");

        if (!user.CanAuthenticate)
            return Error.Forbidden(ErrorCodes.AccountSuspended, "This account has been suspended.");

        if (user.EmailVerified) return true;

        await sender.SendAsync(user.Email, await tokens.IssueAsync(user.Id, ct), ct);
        return true;
    }
}
