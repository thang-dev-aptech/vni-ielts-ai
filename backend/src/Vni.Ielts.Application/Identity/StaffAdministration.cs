using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

public sealed record CreateStaffCommand(
    UserId ActorId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> RoleNames);

public sealed record CreateStaffResult(
    string UserId,
    bool VerificationEmailSent,
    bool PasswordResetEmailSent);

/// <summary>
/// Creates a staff account immediately.
///
/// <para>
/// Adapted for main identity: registration is phone-first for learners, so
/// staff with an address use <see cref="User.RegisterFromProvider"/>. Email
/// verification was removed (ADR-0018), so <see cref="CreateStaffResult.VerificationEmailSent"/>
/// is always false. A password-reset mail is sent when the account has an
/// address so the person can set a password without an operator typing one.
/// </para>
/// </summary>
public sealed class CreateStaffAccount(
    IUserRepository users,
    IRoleRepository roles,
    IPasswordResetTokens resets,
    IVerificationMessageSender sender,
    IClock clock)
{
    public async Task<Result<CreateStaffResult>> HandleAsync(CreateStaffCommand command, CancellationToken ct)
    {
        if (!Email.TryCreate(command.Email, out var email))
            return Error.Validation(ErrorCodes.EmailInvalid, "That is not a valid email address.");

        if (string.IsNullOrWhiteSpace(command.DisplayName))
            return Error.Validation(ErrorCodes.ValidationFailed, "A display name is required.");

        var roleIds = await ResolveCmsRolesAsync(roles, command.RoleNames, ct);
        if (!roleIds.IsSuccess) return roleIds.Error;

        if (await users.EmailExistsAsync(email, ct))
            return Error.Conflict(ErrorCodes.EmailAlreadyRegistered, "That email address is already registered.");

        var user = User.RegisterFromProvider(email, command.DisplayName.Trim(), clock.UtcNow);
        foreach (var roleId in roleIds.Value!)
            user.AssignRole(roleId);

        try
        {
            await users.AddAsync(user, ct);
        }
        catch (DuplicateEmailException)
        {
            return Error.Conflict(ErrorCodes.EmailAlreadyRegistered, "That email address is already registered.");
        }

        var resetToken = await resets.IssueAsync(user.Id, ct);
        var resetDelivery = await sender.SendPasswordResetAsync(email, resetToken, ct);

        return new CreateStaffResult(
            user.Id.Value,
            VerificationEmailSent: false,
            PasswordResetEmailSent: resetDelivery == MessageDelivery.Sent);
    }

    internal static async Task<Result<IReadOnlyList<RoleId>>> ResolveCmsRolesAsync(
        IRoleRepository roles, IReadOnlyList<string> roleNames, CancellationToken ct)
    {
        if (roleNames.Count == 0)
            return Error.Validation(ErrorCodes.ValidationFailed, "At least one CMS role is required.");

        var ids = new List<RoleId>();
        foreach (var name in roleNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsCmsOperatorRole(name))
                return Error.NotFound(ErrorCodes.NotFound, "That role cannot be assigned from the CMS.");

            var role = await roles.FindByNameAsync(name, ct);
            if (role is null)
                return Error.NotFound(ErrorCodes.NotFound, "That role cannot be assigned from the CMS.");

            ids.Add(role.Id);
        }

        return ids;
    }

    internal static bool IsCmsOperatorRole(string roleName) =>
        roleName is SystemRoles.ExamAuthor or SystemRoles.AcademicLead or SystemRoles.Admin;
}

public sealed record InviteStaffCommand(
    UserId ActorId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> RoleNames);

public sealed record InviteStaffResult(string InvitationId, bool EmailSent);

public sealed class InviteStaff(
    IUserRepository users,
    IRoleRepository roles,
    IStaffInvitationRepository invitations,
    IVerificationMessageSender sender,
    IClock clock,
    IOptions<AdminUserOperationOptions> options)
{
    public async Task<Result<InviteStaffResult>> HandleAsync(InviteStaffCommand command, CancellationToken ct)
    {
        if (!Email.TryCreate(command.Email, out var email))
            return Error.Validation(ErrorCodes.EmailInvalid, "That is not a valid email address.");

        if (string.IsNullOrWhiteSpace(command.DisplayName))
            return Error.Validation(ErrorCodes.ValidationFailed, "A display name is required.");

        var roleIds = await CreateStaffAccount.ResolveCmsRolesAsync(roles, command.RoleNames, ct);
        if (!roleIds.IsSuccess) return roleIds.Error;

        if (await users.EmailExistsAsync(email, ct))
            return Error.Conflict(ErrorCodes.EmailAlreadyRegistered, "That email address is already registered.");

        var now = clock.UtcNow;
        await invitations.ExpireStalePendingByEmailAsync(email, now, ct);

        if (await invitations.FindLiveByEmailAsync(email, now, ct) is not null)
            return Error.Conflict(ErrorCodes.EmailAlreadyInvited, "A live invitation already exists for that address.");

        var (raw, hash) = OneTimeToken.New();
        var lifetime = TimeSpan.FromHours(Math.Max(1, options.Value.InvitationLifetimeHours));
        var invitation = StaffInvitation.Create(email, roleIds.Value!, command.ActorId, hash, now, lifetime);
        try
        {
            await invitations.AddAsync(invitation, ct);
        }
        catch (DuplicateLiveInvitationException)
        {
            return Error.Conflict(ErrorCodes.EmailAlreadyInvited, "A live invitation already exists for that address.");
        }

        var delivery = await sender.SendStaffInvitationAsync(email, raw, ct);
        return new InviteStaffResult(invitation.Id, delivery == MessageDelivery.Sent);
    }
}

public sealed record ResendStaffInvitationCommand(UserId ActorId, string InvitationId);

public sealed class ResendStaffInvitation(
    IStaffInvitationRepository invitations,
    IVerificationMessageSender sender,
    IClock clock,
    IOptions<AdminUserOperationOptions> options)
{
    public async Task<Result<InviteStaffResult>> HandleAsync(ResendStaffInvitationCommand command, CancellationToken ct)
    {
        var invitation = await invitations.FindByIdAsync(command.InvitationId, ct);
        if (invitation is null)
            return Error.NotFound(ErrorCodes.NotFound, "Invitation not found.");

        var now = clock.UtcNow;
        var (raw, hash) = OneTimeToken.New();
        var lifetime = TimeSpan.FromHours(Math.Max(1, options.Value.InvitationLifetimeHours));
        invitation.ReplaceHash(hash, now, lifetime);
        await invitations.SaveAsync(invitation, ct);

        var delivery = await sender.SendStaffInvitationAsync(invitation.Email, raw, ct);
        return new InviteStaffResult(invitation.Id, delivery == MessageDelivery.Sent);
    }
}

public sealed record RevokeStaffInvitationCommand(UserId ActorId, string InvitationId);

public sealed class RevokeStaffInvitation(IStaffInvitationRepository invitations)
{
    public async Task<Result<bool>> HandleAsync(RevokeStaffInvitationCommand command, CancellationToken ct)
    {
        var invitation = await invitations.FindByIdAsync(command.InvitationId, ct);
        if (invitation is null)
            return Error.NotFound(ErrorCodes.NotFound, "Invitation not found.");

        invitation.Revoke();
        await invitations.SaveAsync(invitation, ct);
        return true;
    }
}

public sealed record AcceptStaffInvitationCommand(string Token, string Password, string DisplayName);

public sealed record AcceptStaffInvitationResult(string UserId);

public sealed class AcceptStaffInvitation(
    IStaffInvitationAcceptance acceptance,
    IPasswordHasher hasher,
    IClock clock)
{
    public async Task<Result<AcceptStaffInvitationResult>> HandleAsync(
        AcceptStaffInvitationCommand command, CancellationToken ct)
    {
        var password = PasswordPolicy.Validate(command.Password);
        if (!password.IsSuccess) return password.Error;

        if (string.IsNullOrWhiteSpace(command.DisplayName))
            return Error.Validation(ErrorCodes.ValidationFailed, "A display name is required.");

        // Hash before the persistence transaction so Application never hands a
        // raw password across the Infrastructure boundary, and so a failed
        // claim never leaves a user without an identity.
        return await acceptance.AcceptAsync(
            OneTimeToken.Hash(command.Token),
            command.DisplayName.Trim(),
            hasher.Hash(password.Value!),
            clock.UtcNow,
            ct);
    }
}

internal static class OneTimeToken
{
    public static (string Raw, string Hash) New()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return (raw, Hash(raw));
    }

    public static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
