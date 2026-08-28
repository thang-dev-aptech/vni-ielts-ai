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
/// The address as stored, and what became of the verification message sent to
/// it. The second half is not decoration: the screen that shows the new
/// address is the same screen that would otherwise claim a mail is on its way.
/// </summary>
public sealed record ChangeEmailResult(string Email, MessageDelivery VerificationMessage);

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
    public async Task<Result<ChangeEmailResult>> HandleAsync(
        ChangeEmailCommand command, CancellationToken ct)
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
            // Not an error, and not worth a second verification mail either —
            // so nothing was sent, and the result says so rather than letting
            // the screen infer one from a success.
            return new ChangeEmailResult(email.Value, MessageDelivery.NotSent);
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
        var delivery = await sender.SendAsync(email, await tokens.IssueCodeAsync(user.Id, ct), ct);

        return new ChangeEmailResult(email.Value, delivery);
    }
}

public sealed record ResendVerificationCommand(UserId UserId);

/// <summary>What the resend actually did.</summary>
/// <param name="AlreadyVerified">
/// True when there was nothing to send because the address is already proven.
/// A success, not a failure.
/// </param>
/// <param name="VerificationMessage">
/// What became of the message. The button that triggered this is the one place
/// a learner is told an email is coming, so it is the one place a wrong answer
/// here is visible as a lie.
/// </param>
public sealed record ResendVerificationResult(
    bool AlreadyVerified, MessageDelivery VerificationMessage);

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
    public async Task<Result<ResendVerificationResult>> HandleAsync(
        ResendVerificationCommand command, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(command.UserId, ct);
        if (user is null) return Error.NotFound(ErrorCodes.NotFound, "Account not found.");

        if (!user.CanAuthenticate)
            return Error.Forbidden(ErrorCodes.AccountSuspended, "This account has been suspended.");

        if (user.EmailVerified)
            return new ResendVerificationResult(AlreadyVerified: true, MessageDelivery.NotSent);

        var delivery = await sender.SendAsync(user.Email, await tokens.IssueCodeAsync(user.Id, ct), ct);
        return new ResendVerificationResult(AlreadyVerified: false, delivery);
    }
}

public sealed record ConfirmEmailCodeCommand(UserId UserId, string Code);

/// <summary>
/// Turns a six-digit code into a verified address.
///
/// <b>`[QUYẾT ĐỊNH]` chủ sản phẩm, 28/08/2026: xác minh bằng mã 6 số.</b> The
/// learner is already signed in and already on their profile page — the owner's
/// own decision of 27/08 put verification there — so a link would open in
/// whatever browser the mail app chose, usually an in-app webview with no
/// session, and verify an account in a window the learner never sees again.
///
/// <b>Authenticated, and that is what makes six digits safe.</b> The account is
/// known from the token rather than found from the code, so the attempt cap is
/// per account and nobody can spray a guess across every account at once. Five
/// wrong answers kills the code.
/// → <see cref="IEmailVerificationTokens.RedeemCodeAsync"/>
/// </summary>
public sealed class ConfirmEmailCode(
    IUserRepository users,
    IEmailVerificationTokens tokens)
{
    public async Task<Result<CodeRedemption>> HandleAsync(
        ConfirmEmailCodeCommand command, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(command.UserId, ct);
        if (user is null) return Error.NotFound(ErrorCodes.NotFound, "Account not found.");

        if (!user.CanAuthenticate)
            return Error.Forbidden(ErrorCodes.AccountSuspended, "This account has been suspended.");

        /*
         * <b>Already verified is a success, not an error.</b> Somebody pressing
         * the button twice, or opening the page on a second device, has nothing
         * to fix — and an error would send them looking for a problem that is
         * not there.
         */
        if (user.EmailVerified) return CodeRedemption.Verified;

        /*
         * <b>The shape is checked before the store is asked.</b> Not for
         * safety — the store compares a hash and would refuse anything wrong —
         * but so that a stray space or a pasted line does not spend one of the
         * five attempts that make this mechanism work.
         */
        var code = command.Code?.Trim() ?? string.Empty;

        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
            return CodeRedemption.Incorrect;

        var outcome = await tokens.RedeemCodeAsync(command.UserId, code, ct);

        if (outcome is not CodeRedemption.Verified) return outcome;

        user.MarkEmailVerified();
        await users.SaveAsync(user, ct);

        return CodeRedemption.Verified;
    }
}
