using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

/// <summary>
/// Shared by the two profile mutators: whether this account has a social
/// identity to fall back on.
///
/// <para>
/// The entity enforces "an account must keep a way in" but cannot see the
/// identity table, and must not reach for it — CLAUDE.md rule 7, enforced by
/// the architecture tests. So the use case reads the one fact and passes it in.
/// </para>
/// </summary>
internal static class SignInMethods
{
    public static async Task<bool> HasLinkedProviderAsync(
        IUserIdentityRepository identities, UserId userId, CancellationToken ct) =>
        (await identities.ListForUserAsync(userId, ct))
            .Any(i => i.Provider is not IdentityProvider.Password);

    public static readonly Error Required = Error.Conflict(
        ErrorCodes.SignInMethodRequired,
        "Your account needs at least one way to sign in. Add a phone number or an email "
        + "address before removing this one.");
}

public sealed record SetPhoneCommand(UserId UserId, string? Phone);

/// <summary>
/// Adds, changes or removes the learner's contact number.
///
/// <para>
/// <b>Nothing is verified, and nothing pretends to be.</b> There is no OTP
/// here because whether a number must be proven has not been decided — and
/// inventing a verification flow would be inventing the policy behind it.
/// </para>
///
/// <para>
/// <b>It is unique, though.</b> The number is a sign-in handle now, so two
/// accounts sharing one would make "sign in with your phone number" ambiguous.
/// An empty value still clears it — but only while the account keeps another
/// way in.
/// </para>
/// </summary>
public sealed class SetPhone(IUserRepository users, IUserIdentityRepository identities)
{
    public async Task<Result<string?>> HandleAsync(SetPhoneCommand command, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(command.UserId, ct);
        if (user is null) return Error.NotFound(ErrorCodes.NotFound, "Account not found.");

        // Read once and pass the real answer down. The entity refuses to leave
        // the account with no way in and cannot see the identity table itself;
        // handing it a hard-coded `true` would satisfy the compiler and quietly
        // disable the guard.
        var hasLinked = await SignInMethods.HasLinkedProviderAsync(identities, user.Id, ct);

        if (string.IsNullOrWhiteSpace(command.Phone))
        {
            if (user.Email is null && !hasLinked) return SignInMethods.Required;

            user.SetPhone(null, hasLinked);
            await users.SaveAsync(user, ct);
            return (string?)null;
        }

        if (!PhoneNumber.TryCreate(command.Phone, out var phone))
        {
            return Error.Validation(
                ErrorCodes.PhoneInvalid, "That does not look like a phone number.");
        }

        if (user.Phone == phone)
            return phone.Value;

        if (await users.PhoneExistsAsync(phone, ct))
        {
            return Error.Conflict(
                ErrorCodes.PhoneAlreadyRegistered, "That phone number is already registered.");
        }

        user.SetPhone(phone, hasLinked);

        try
        {
            await users.SaveAsync(user, ct);
        }
        catch (DuplicatePhoneException)
        {
            // Lost the race against another account claiming the number. The
            // unique index is what enforces it; the check above is a courtesy
            // that produces a clean message.
            return Error.Conflict(
                ErrorCodes.PhoneAlreadyRegistered, "That phone number is already registered.");
        }

        return phone.Value;
    }
}

public sealed record ChangeEmailCommand(UserId UserId, string? Email);

/// <summary>The address as stored, or null once it has been removed.</summary>
public sealed record ChangeEmailResult(string? Email);

/// <summary>
/// Sets, changes or removes the account's address.
///
/// <para>
/// <b>The address moves the account, and that is the point.</b>
/// `[QUYẾT ĐỊNH]` chủ sản phẩm, 08/09/2026: <i>"user đổi email thành
/// nguyendoanthang16@gmail.com → thì dữ liệu của ngdthang.dev@gmail.com sẽ được
/// chuyển thành của nguyendoanthang16@gmail.com và bây giờ nếu login bằng
/// ngdthang.dev@gmail.com sẽ là 1 tài khoản mới"</i>. Nothing is copied and
/// nothing is migrated: the account keeps its id, its sittings and its ledger,
/// and simply answers to a different address. The old one becomes unclaimed.
/// → ADR-0018
/// </para>
///
/// <para>
/// <b>There is no verified lock any more.</b> The address used to freeze once
/// proven, because it was the account's route back in. Verification is gone and
/// the owner asked for the opposite behaviour, so the lock went with it — the
/// consequences for a stolen session are recorded in the threat model rather
/// than papered over here.
/// </para>
///
/// <para>
/// <b>It writes one place now.</b> The password row is keyed by account id, so
/// unlike before there is no second copy of the address to keep in step.
/// </para>
/// </summary>
public sealed class ChangeEmail(IUserRepository users, IUserIdentityRepository identities)
{
    public async Task<Result<ChangeEmailResult>> HandleAsync(
        ChangeEmailCommand command, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(command.UserId, ct);
        if (user is null) return Error.NotFound(ErrorCodes.NotFound, "Account not found.");

        // See SetPhone: read the real answer rather than asserting one.
        var hasLinked = await SignInMethods.HasLinkedProviderAsync(identities, user.Id, ct);

        if (string.IsNullOrWhiteSpace(command.Email))
        {
            if (user.Phone is null && !hasLinked) return SignInMethods.Required;

            user.ChangeEmail(null, hasLinked);
            await users.SaveAsync(user, ct);
            return new ChangeEmailResult(null);
        }

        if (!Email.TryCreate(command.Email, out var email))
            return Error.Validation(ErrorCodes.EmailInvalid, "That is not a valid email address.");

        if (user.Email == email)
            return new ChangeEmailResult(email.Value);

        if (await users.EmailExistsAsync(email, ct))
        {
            return Error.Conflict(
                ErrorCodes.EmailAlreadyRegistered, "That email address is already registered.");
        }

        user.ChangeEmail(email, hasLinked);

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

        return new ChangeEmailResult(email.Value);
    }
}
