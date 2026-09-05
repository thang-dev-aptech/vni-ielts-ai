using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

public sealed record RegisterUserCommand(string Email, string Password, string DisplayName);

/// <summary>
/// What registration produces: a signed-in session, and an honest account of
/// what happened to the verification message.
/// </summary>
/// <param name="Session">
/// The same shape a password sign-in returns, deliberately — registering *is*
/// a sign-in now, and one body means one place builds it.
/// </param>
/// <param name="VerificationMessage">
/// Whether a message actually went out. <see cref="MessageDelivery.NotSent"/>
/// is the normal answer in development and must reach the screen; a client
/// that says "check your inbox" over it is lying on our behalf.
/// </param>
public sealed record RegisterUserResult(LoginResult Session, MessageDelivery VerificationMessage);

/// <summary>
/// Email registration.
///
/// <para>
/// <b>It signs the new account in.</b> `[QUYẾT ĐỊNH]` chủ sản phẩm,
/// 27/08/2026: <i>"tạo tài khoản với email pass cho login như bình thường
/// nhưng sẽ xác minh ở trang hồ sơ học sinh sau cũng được"</i>. Registration
/// used to return no tokens on the reasoning that the address was still an
/// unproven claim — but nothing in the product refused an unverified account
/// anything, so the only thing that reasoning actually bought was a screen
/// telling a new learner to go and read an email that, today, is never sent.
/// </para>
///
/// <para>
/// <b>What it does not do is decide what an unverified account may not do.</b>
/// The owner settled login. Entitlement accrual (<c>T4</c>) and referral
/// attribution (<c>T13</c>) are the two places a real restriction would
/// belong, both are unbuilt, and choosing one here would be inventing the
/// policy. The account's verified state is recorded truthfully and nothing
/// reads it as a permission. → `M-45`, `G-11`
/// </para>
///
/// <para>
/// The verification message still goes out at registration — the profile page
/// carries a resend for whoever misses it, not instead of it.
/// </para>
/// </summary>
public sealed class RegisterUser(
    IUserRepository users,
    IUserIdentityRepository identities,
    IRoleRepository roles,
    IPasswordHasher hasher,
    IEmailVerificationTokens verificationTokens,
    IVerificationMessageSender sender,
    IPermissionResolver permissions,
    ITokenService tokens,
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
        var delivery = await sender.SendAsync(email, token, ct);

        // A fresh sign-in, so `familyId: null` — this starts a token family
        // rather than continuing one, exactly as LoginWithPassword does.
        var granted = await permissions.ResolveAsync(user, ct);
        var pair = await tokens.IssueAsync(user, granted, familyId: null, ct);

        return new RegisterUserResult(
            new LoginResult(pair, user.Id, user.DisplayName), delivery);
    }
}
