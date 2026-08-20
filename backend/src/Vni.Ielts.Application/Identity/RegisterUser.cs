using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

public sealed record RegisterUserCommand(string Email, string Password, string DisplayName);

public sealed record RegisterUserResult(UserId UserId, bool EmailVerificationRequired);

/// <summary>
/// Email registration.
///
/// Note what this does <b>not</b> return: tokens. A freshly registered account
/// is not signed in, because the address is still an unproven claim. Several
/// downstream protections depend on verification being a real gate —
/// entitlement accrual (T4) and referral attribution confirmation (T13) both
/// wait on it, and both are farmable if registration alone is enough.
/// </summary>
public sealed class RegisterUser(
    IUserRepository users,
    IUserIdentityRepository identities,
    IRoleRepository roles,
    IPasswordHasher hasher,
    IEmailVerificationTokens verificationTokens,
    IVerificationMessageSender sender,
    IClock clock)
{
    public async Task<Result<RegisterUserResult>> HandleAsync(
        RegisterUserCommand command, CancellationToken ct)
    {
        if (!Email.TryCreate(command.Email, out var email))
            return Error.Validation(ErrorCodes.EmailInvalid, "That is not a valid email address.");

        var password = PasswordPolicy.Validate(command.Password);
        if (!password.IsSuccess)
            return password.Error;

        if (string.IsNullOrWhiteSpace(command.DisplayName))
            return Error.Validation(ErrorCodes.ValidationFailed, "A display name is required.");

        if (await users.EmailExistsAsync(email, ct))
        {
            // Deliberately the same shape as any other validation failure.
            // There is a real tension here: this response tells an attacker the
            // address is registered. The alternative — pretending to succeed
            // and sending a "someone tried to register with your address"
            // email — costs a confusing experience for every genuine typo, and
            // the address can be enumerated through the password-reset flow
            // anyway. Rate limiting on this endpoint is the control that
            // actually matters. → threat T4
            return Error.Conflict(
                ErrorCodes.EmailAlreadyRegistered, "That email address is already registered.");
        }

        var now = clock.UtcNow;
        var user = User.Register(email, command.DisplayName, now);

        var learner = await roles.FindByNameAsync(SystemRoles.Learner, ct);
        if (learner is not null)
            user.AssignRole(learner.Id);

        try
        {
            await users.AddAsync(user, ct);
            await identities.AddAsync(
                UserIdentity.ForEmail(user.Id, email, hasher.Hash(password.Value!), now), ct);
        }
        catch (DuplicateEmailException)
        {
            // The EmailExistsAsync check above lost a race — two registrations
            // for the same address arrived together and both passed it. The
            // unique index is what actually enforces the rule; this returns the
            // identical error the pre-check would have, so the caller cannot
            // tell the two paths apart and nobody sees a 500.
            return Error.Conflict(
                ErrorCodes.EmailAlreadyRegistered, "That email address is already registered.");
        }

        // Actually issue and send it. An earlier version returned
        // `emailVerificationRequired: true` with no token, no endpoint, and no
        // message — the API reported a step the product could not perform.
        var token = await verificationTokens.IssueAsync(user.Id, ct);
        await sender.SendAsync(email, token, ct);

        return new RegisterUserResult(user.Id, EmailVerificationRequired: true);
    }
}
