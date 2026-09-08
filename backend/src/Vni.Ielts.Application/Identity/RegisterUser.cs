using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

public sealed record RegisterUserCommand(
    string Phone, string Password, string DisplayName, string? ReferralCode = null);

/// <summary>What registration produces: a signed-in session.</summary>
public sealed record RegisterUserResult(LoginResult Session);

/// <summary>
/// Registration: a name, a phone number and a password.
///
/// <para>
/// <b>No address is asked for.</b> `[QUYẾT ĐỊNH]` chủ sản phẩm, 08/09/2026:
/// <i>"register chỉ cần điền: Họ và tên, số điện thoại, mật khẩu, nhập lại mật
/// khẩu → tạo xong ở profile phần email bỏ trống → như vậy sẽ không cần tính
/// năng verify nữa bỏ luôn"</i>. The account starts with
/// <see cref="User.Email"/> null and the learner may add one later, at which
/// point they can sign in with either. → ADR-0018
/// </para>
///
/// <para>
/// <b>The confirm-password box is the client's business.</b> It exists to catch
/// a typo before the request is sent; the server receives one password and has
/// nothing to compare. Adding a second field here would put one rule in two
/// places and guarantee they drift.
/// </para>
///
/// <para>
/// <b>It signs the new account in</b> (`[QUYẾT ĐỊNH]` chủ sản phẩm,
/// 27/08/2026), and there is no longer a verification message to report on:
/// registration takes no address, so there is nowhere to send one and nothing
/// for the screen to claim.
/// </para>
/// </summary>
public sealed class RegisterUser(
    IUserRepository users,
    IUserIdentityRepository identities,
    IRoleRepository roles,
    IPasswordHasher hasher,
    IPermissionResolver permissions,
    ITokenService tokens,
    IClock clock,
    Usage.IReferralDirectory? referrals = null,
    Usage.UsageRecorder? usage = null)
{
    public async Task<Result<RegisterUserResult>> HandleAsync(
        RegisterUserCommand command, CancellationToken ct)
    {
        if (!PhoneNumber.TryCreate(command.Phone, out var phone))
            return Error.Validation(ErrorCodes.PhoneInvalid, "That does not look like a phone number.");

        var password = PasswordPolicy.Validate(command.Password);
        if (!password.IsSuccess)
            return password.Error;

        if (string.IsNullOrWhiteSpace(command.DisplayName))
            return Error.Validation(ErrorCodes.ValidationFailed, "A display name is required.");

        if (await users.PhoneExistsAsync(phone, ct))
        {
            // Deliberately the same shape as any other validation failure.
            // There is a real tension here: this response tells an attacker the
            // number is registered. The alternative — pretending to succeed —
            // costs a confusing experience for every genuine typo, and the
            // handle can be enumerated through sign-in anyway. Rate limiting on
            // this endpoint is the control that actually matters. → threat T4
            return Error.Conflict(
                ErrorCodes.PhoneAlreadyRegistered, "That phone number is already registered.");
        }

        var now = clock.UtcNow;
        var user = User.Register(phone, command.DisplayName, now);

        var learner = await roles.FindByNameAsync(SystemRoles.Learner, ct);
        if (learner is not null)
            user.AssignRole(learner.Id);

        // An unrecognised or absent code is silently ignored — a stale link
        // must never fail somebody's registration. → `P-16`
        if (referrals is not null
            && Domain.Identity.ReferralCode.Normalise(command.ReferralCode) is { } code
            && await referrals.FindByReferralCodeAsync(code, ct) is { } referrer)
        {
            user.AttributeReferral(referrer.Id);
        }

        try
        {
            await users.AddAsync(user, ct);
            await identities.AddAsync(
                UserIdentity.ForPassword(user.Id, hasher.Hash(password.Value!), now), ct);
        }
        catch (DuplicatePhoneException)
        {
            // The PhoneExistsAsync check above lost a race — two registrations
            // for the same number arrived together and both passed it. The
            // unique index is what actually enforces the rule; this returns the
            // identical error the pre-check would have, so the caller cannot
            // tell the two paths apart and nobody sees a 500.
            return Error.Conflict(
                ErrorCodes.PhoneAlreadyRegistered, "That phone number is already registered.");
        }

        if (usage is not null) await usage.AccountCreatedAsync(user, ct);

        // A fresh sign-in, so `familyId: null` — this starts a token family
        // rather than continuing one, exactly as LoginWithPassword does.
        var granted = await permissions.ResolveAsync(user, ct);
        var pair = await tokens.IssueAsync(user, granted, familyId: null, ct);

        return new RegisterUserResult(new LoginResult(pair, user.Id, user.DisplayName));
    }
}
