using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

public sealed record BulkSuspendCommand(
    UserId ActorId,
    IReadOnlyList<string> UserIds,
    string Reason);

public sealed record BulkSuspendItem(string UserId, string Outcome, string? Detail);

public sealed record BulkSuspendResult(IReadOnlyList<BulkSuspendItem> Items);

public sealed class BulkSuspendUsers(
    IProtectedAdminMutation protectedAdmin,
    ITokenService tokens,
    IOptions<AdminUserOperationOptions> options)
{
    public async Task<Result<BulkSuspendResult>> HandleAsync(BulkSuspendCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            return Error.Validation(ErrorCodes.ValidationFailed, "A reason is required.");

        var distinct = command.UserIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var max = Math.Max(1, options.Value.BulkUserOperationMax);
        if (distinct.Count > max)
            return Error.Validation(ErrorCodes.BulkTooLarge, "Too many accounts in one request.");

        var items = new List<BulkSuspendItem>(distinct.Count);
        foreach (var id in distinct)
        {
            if (id == command.ActorId.Value)
            {
                items.Add(new BulkSuspendItem(id, "refused", "Không thể tự khoá tài khoản của chính mình."));
                continue;
            }

            var mutation = await protectedAdmin.TrySuspendAsync(new UserId(id), ct);
            switch (mutation.Outcome)
            {
                case ProtectedAdminMutationOutcome.NotFound:
                    items.Add(new BulkSuspendItem(id, "not-found", null));
                    break;
                case ProtectedAdminMutationOutcome.AlreadyApplied:
                    items.Add(new BulkSuspendItem(id, "already-suspended", null));
                    break;
                case ProtectedAdminMutationOutcome.WouldLeaveZeroActiveAdmins:
                    items.Add(new BulkSuspendItem(id, "refused", "Không thể khoá quản trị viên cuối cùng đang hoạt động."));
                    break;
                case ProtectedAdminMutationOutcome.Applied:
                    await tokens.RevokeAllForUserAsync(mutation.User!.Id, ct);
                    items.Add(new BulkSuspendItem(id, "suspended", null));
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected outcome {mutation.Outcome}.");
            }
        }

        return new BulkSuspendResult(items);
    }
}

public sealed record UpdateAdminUserCommand(
    UserId ActorId,
    UserId TargetId,
    string? DisplayName,
    string? Email,
    bool UpdateEmail,
    string? Phone,
    bool UpdatePhone);

public sealed class UpdateAdminUserProfile(
    IUserRepository users,
    IUserIdentityRepository identities)
{
    public async Task<Result<User>> HandleAsync(UpdateAdminUserCommand command, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(command.TargetId, ct);
        if (user is null)
            return Error.NotFound(ErrorCodes.NotFound, "Account not found.");

        if (!string.IsNullOrWhiteSpace(command.DisplayName))
            user.Rename(command.DisplayName);

        var linked = await identities.ListForUserAsync(user.Id, ct);
        var hasLinkedProvider = linked.Any(i =>
            i.Provider is IdentityProvider.Google or IdentityProvider.Facebook);

        if (command.UpdateEmail)
        {
            Email? email = null;
            if (command.Email is not null)
            {
                if (!Email.TryCreate(command.Email, out var parsed))
                    return Error.Validation(ErrorCodes.EmailInvalid, "That is not a valid email address.");
                email = parsed;
            }

            if (email is not null)
            {
                var existing = await users.FindByEmailAsync(email.Value, ct);
                if (existing is not null && existing.Id != user.Id)
                    return Error.Conflict(ErrorCodes.EmailAlreadyRegistered, "That email address is already registered.");
            }

            try
            {
                user.ChangeEmail(email, hasLinkedProvider);
            }
            catch (InvalidOperationException)
            {
                return Error.Validation(
                    ErrorCodes.SignInMethodRequired,
                    "An account must keep at least one way to sign in.");
            }
        }

        if (command.UpdatePhone)
        {
            PhoneNumber? phone = null;
            if (!string.IsNullOrWhiteSpace(command.Phone))
            {
                if (!PhoneNumber.TryCreate(command.Phone, out var parsed))
                    return Error.Validation(ErrorCodes.ValidationFailed, "That is not a valid phone number.");
                phone = parsed;

                if (await users.PhoneExistsAsync(phone.Value, ct))
                {
                    var holder = await users.FindByPhoneAsync(phone.Value, ct);
                    if (holder is not null && holder.Id != user.Id)
                        return Error.Conflict(ErrorCodes.PhoneAlreadyRegistered, "That phone number is already registered.");
                }
            }

            try
            {
                user.SetPhone(phone, hasLinkedProvider);
            }
            catch (InvalidOperationException)
            {
                return Error.Validation(
                    ErrorCodes.SignInMethodRequired,
                    "An account must keep at least one way to sign in.");
            }
        }

        try
        {
            await users.SaveAsync(user, ct);
        }
        catch (DuplicateEmailException)
        {
            return Error.Conflict(ErrorCodes.EmailAlreadyRegistered, "That email address is already registered.");
        }
        catch (DuplicatePhoneException)
        {
            return Error.Conflict(ErrorCodes.PhoneAlreadyRegistered, "That phone number is already registered.");
        }

        return user;
    }
}

public sealed record ForceStaffPasswordResetCommand(UserId TargetId);

public sealed record ForceStaffPasswordResetResult(bool EmailSent);

public sealed class ForceStaffPasswordReset(
    IUserRepository users,
    IUserIdentityRepository identities,
    IPasswordResetTokens tokens,
    IVerificationMessageSender sender,
    ITokenService sessions)
{
    public async Task<Result<ForceStaffPasswordResetResult>> HandleAsync(
        ForceStaffPasswordResetCommand command, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(command.TargetId, ct);
        if (user is null)
            return Error.NotFound(ErrorCodes.NotFound, "Account not found.");

        var linked = await identities.ListForUserAsync(user.Id, ct);
        if (!linked.Any(i => i.Provider == IdentityProvider.Password && i.PasswordHash is not null))
        {
            return Error.Conflict(
                ErrorCodes.PasswordIdentityNotFound,
                "This account has no password. The person must set one themselves or use the invitation they were sent.");
        }

        if (user.Email is null)
        {
            return Error.Conflict(
                ErrorCodes.PolicyNotConfigured,
                "This account has no email address. An operator must set a new password from the CMS.");
        }

        var delivery = await sender.SendPasswordResetAsync(
            user.Email.Value, await tokens.IssueAsync(user.Id, ct), ct);
        await sessions.RevokeAllForUserAsync(user.Id, ct);
        return new ForceStaffPasswordResetResult(delivery == MessageDelivery.Sent);
    }
}

public sealed record CreatePrivacyRequestCommand(
    UserId ActorId,
    UserId SubjectId,
    string Type,
    string Reason);

public sealed class CreatePrivacyRequest(
    IUserRepository users,
    IPrivacyRequestRepository requests,
    IClock clock)
{
    public async Task<Result<PrivacyRequest>> HandleAsync(CreatePrivacyRequestCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            return Error.Validation(ErrorCodes.ValidationFailed, "A reason is required.");

        var type = command.Type.Trim().ToLowerInvariant() switch
        {
            "anonymize" => PrivacyRequestType.Anonymize,
            "hard-delete" or "harddelete" => PrivacyRequestType.HardDelete,
            _ => (PrivacyRequestType?)null,
        };
        if (type is null)
            return Error.Validation(ErrorCodes.ValidationFailed, "Type must be anonymize or hard-delete.");

        var subject = await users.FindByIdAsync(command.SubjectId, ct);
        if (subject is null)
            return Error.NotFound(ErrorCodes.NotFound, "Account not found.");

        var request = PrivacyRequest.Create(
            command.SubjectId, type.Value, command.Reason.Trim(), command.ActorId, clock.UtcNow);
        await requests.AddAsync(request, ct);
        return request;
    }
}

public sealed record ApprovePrivacyRequestCommand(UserId ActorId, string RequestId);

public sealed class ApprovePrivacyRequest(IPrivacyRequestRepository requests, IClock clock)
{
    public async Task<Result<PrivacyRequest>> HandleAsync(ApprovePrivacyRequestCommand command, CancellationToken ct)
    {
        var request = await requests.FindByIdAsync(command.RequestId, ct);
        if (request is null)
            return Error.NotFound(ErrorCodes.NotFound, "Privacy request not found.");

        try
        {
            request.Approve(command.ActorId, clock.UtcNow);
        }
        catch (InvalidOperationException e)
        {
            return Error.Conflict(ErrorCodes.ValidationFailed, e.Message);
        }

        await requests.SaveAsync(request, ct);
        return request;
    }
}

public sealed record ExecutePrivacyRequestCommand(UserId ActorId, string RequestId);

public sealed class ExecutePrivacyRequest(
    IPrivacyRequestRepository requests,
    IUserRepository users,
    ILastActiveAdminGuard lastAdmin,
    IOptions<PrivacyOptions> privacy)
{
    public async Task<Result<bool>> HandleAsync(ExecutePrivacyRequestCommand command, CancellationToken ct)
    {
        var request = await requests.FindByIdAsync(command.RequestId, ct);
        if (request is null)
            return Error.NotFound(ErrorCodes.NotFound, "Privacy request not found.");

        if (request.Status != PrivacyRequestStatus.Approved)
            return Error.Conflict(ErrorCodes.ValidationFailed, "Only an approved request can be executed.");

        var subject = await users.FindByIdAsync(request.SubjectId, ct);
        if (subject is not null
            && await lastAdmin.WouldLeaveZeroActiveAdminsAsync(subject, LastAdminOperation.Erase, ct))
        {
            return Error.Conflict(
                ErrorCodes.LastAdminProtected,
                "Không thể xoá quản trị viên cuối cùng đang hoạt động.");
        }

        if (string.IsNullOrWhiteSpace(privacy.Value.ErasureMode))
        {
            return Error.Conflict(
                ErrorCodes.PolicyNotConfigured,
                "Erasure is not configured. The request stays on file until the owner decides the PDPL matrix.");
        }

        return Error.Conflict(
            ErrorCodes.PolicyNotConfigured,
            "Erasure is not configured. The request stays on file until the owner decides the PDPL matrix.");
    }
}
