using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Learning;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Api.Endpoints;

public sealed record CreateStaffRequest(string Email, string DisplayName, IReadOnlyList<string> Roles);
public sealed record InviteStaffRequest(string Email, string DisplayName, IReadOnlyList<string> Roles);
public sealed record BulkSuspendRequest(IReadOnlyList<string> UserIds, string Reason);
public sealed record PatchAdminUserRequest(
    string? DisplayName = null,
    string? Email = null,
    bool UpdateEmail = false,
    string? Phone = null,
    bool UpdatePhone = false);
public sealed record CreatePrivacyRequestBody(string Type, string Reason);
public sealed record AcceptStaffInvitationRequest(string Token, string Password, string DisplayName);

public static partial class AdminEndpoints
{
    private static void MapUserAdministration(RouteGroupBuilder group)
    {
        group.MapPost("/users", CreateStaffEndpoint)
            .WithName("AdminCreateStaff")
            .WithSummary("Create a staff account now and send set-password mail");

        group.MapPost("/users/bulk-suspend", BulkSuspendEndpoint)
            .WithName("AdminBulkSuspendUsers")
            .WithSummary("Suspend many accounts; each id gets its own result");

        group.MapPatch("/users/{userId}", PatchUserEndpoint)
            .WithName("AdminPatchUser")
            .WithSummary("Edit another user's display name, email, or phone");

        group.MapPost("/users/{userId}/resend-verification", ResendUserVerificationEndpoint)
            .WithName("AdminResendVerification")
            .WithSummary("Email verification was removed on main (ADR-0018); always POLICY_NOT_CONFIGURED");

        group.MapPost("/users/{userId}/force-password-reset", ForcePasswordResetEndpoint)
            .WithName("AdminForcePasswordReset")
            .WithSummary("Send a password-reset mail and revoke refresh tokens")
            .RequireRateLimiting(RateLimitPolicies.Registration);

        group.MapGet("/users/{userId}/activity", UserActivityEndpoint)
            .WithName("AdminUserActivity")
            .WithSummary("Learner activity days for one account");

        group.MapGet("/users/{userId}/audit", UserTargetAuditEndpoint)
            .WithName("AdminUserTargetAudit")
            .WithSummary("Audit entries whose target is this account");

        group.MapGet("/users/{userId}/exams", UserExamsEndpoint)
            .WithName("AdminUserExams")
            .WithSummary("Exam versions this account created");

        group.MapGet("/users/{userId}/sittings", UserSittingsEndpoint)
            .WithName("AdminUserSittings")
            .WithSummary("Exam sittings belonging to this account");

        group.MapGet("/users/{userId}/results", UserResultsEndpoint)
            .WithName("AdminUserResults")
            .WithSummary("Section scores for this account — store-side page pending I2-E");

        group.MapGet("/users/{userId}/tokens", UserTokensEndpoint)
            .WithName("AdminUserTokens")
            .WithSummary("Token ledger for this account — unbuilt, always empty");

        group.MapPost("/users/{userId}/export", RequestUserExportEndpoint)
            .WithName("AdminRequestUserExport")
            .WithSummary("Request a personal-data export");

        group.MapGet("/invitations", ListInvitationsEndpoint)
            .WithName("AdminListInvitations")
            .WithSummary("Pending staff invitations");

        group.MapPost("/invitations", InviteStaffEndpoint)
            .WithName("AdminInviteStaff")
            .WithSummary("Send a staff invitation; the account is created on accept");

        group.MapPost("/invitations/{invitationId}/resend", ResendInvitationEndpoint)
            .WithName("AdminResendInvitation")
            .WithSummary("Replace the invitation token and send again");

        group.MapPost("/invitations/{invitationId}/revoke", RevokeInvitationEndpoint)
            .WithName("AdminRevokeInvitation")
            .WithSummary("Revoke a pending invitation");

        group.MapGet("/users/{userId}/privacy-requests", ListPrivacyRequestsEndpoint)
            .WithName("AdminListPrivacyRequests");

        group.MapPost("/users/{userId}/privacy-requests", CreatePrivacyRequestEndpoint)
            .WithName("AdminCreatePrivacyRequest");

        group.MapPost("/privacy-requests/{requestId}/approve", ApprovePrivacyRequestEndpoint)
            .WithName("AdminApprovePrivacyRequest");

        group.MapPost("/privacy-requests/{requestId}/execute", ExecutePrivacyRequestEndpoint)
            .WithName("AdminExecutePrivacyRequest");
    }

    private static async Task<IResult> CreateStaffEndpoint(
        CreateStaffRequest request, ClaimsPrincipal principal, CreateStaffAccount handler,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserUpdate) is { } denial) return denial;
        var actor = principal.UserId();
        if (actor is null) return Results.Unauthorized();

        var result = await handler.HandleAsync(
            new CreateStaffCommand(new UserId(actor), request.Email, request.DisplayName, request.Roles), ct);
        if (!result.IsSuccess) return FromError(result.Error);

        await Record(audit, principal, AuditAction.UserCreated, "user", result.Value!.UserId,
            result.Value.UserId, clock.UtcNow, ct, new Dictionary<string, string> { ["fields"] = "email,displayName,roles" });

        return Results.Created($"/api/v1/admin/users/{result.Value.UserId}", new
        {
            userId = result.Value.UserId,
            verificationEmailSent = result.Value.VerificationEmailSent,
            passwordResetEmailSent = result.Value.PasswordResetEmailSent,
        });
    }

    private static async Task<IResult> InviteStaffEndpoint(
        InviteStaffRequest request, ClaimsPrincipal principal, InviteStaff handler,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserUpdate) is { } denial) return denial;
        var actor = principal.UserId();
        if (actor is null) return Results.Unauthorized();

        var result = await handler.HandleAsync(
            new InviteStaffCommand(new UserId(actor), request.Email, request.DisplayName, request.Roles), ct);
        if (!result.IsSuccess) return FromError(result.Error);

        await Record(audit, principal, AuditAction.UserInvited, "staff-invitation", result.Value!.InvitationId,
            result.Value.InvitationId, clock.UtcNow, ct, new Dictionary<string, string> { ["emailSent"] = result.Value.EmailSent.ToString() });

        return Results.Ok(new { invitationId = result.Value.InvitationId, emailSent = result.Value.EmailSent });
    }

    private static async Task<IResult> ListInvitationsEndpoint(
        ClaimsPrincipal principal, IStaffInvitationRepository invitations, IClock clock,
        int? page, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserRead) is { } denial) return denial;
        const int PageSize = 25;
        var current = Math.Clamp(page ?? 1, 1, MaxPage);
        var (rows, total) = await invitations.ListPendingAsync((current - 1) * PageSize, PageSize, clock.UtcNow, ct);
        return Results.Ok(new
        {
            total,
            page = current,
            pageSize = PageSize,
            invitations = rows.Select(i => new
            {
                invitationId = i.Id,
                email = i.Email.Value,
                status = i.Status.ToString().ToLowerInvariant(),
                expiresAt = i.ExpiresAt,
                createdAt = i.CreatedAt,
            }),
        });
    }

    private static async Task<IResult> ResendInvitationEndpoint(
        string invitationId, ClaimsPrincipal principal, ResendStaffInvitation handler,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserUpdate) is { } denial) return denial;
        var actor = principal.UserId();
        if (actor is null) return Results.Unauthorized();

        var result = await handler.HandleAsync(new ResendStaffInvitationCommand(new UserId(actor), invitationId), ct);
        if (!result.IsSuccess) return FromError(result.Error);

        await Record(audit, principal, AuditAction.UserInvitationResent, "staff-invitation",
            invitationId, invitationId, clock.UtcNow, ct);
        return Results.Ok(new { invitationId = result.Value!.InvitationId, emailSent = result.Value.EmailSent });
    }

    private static async Task<IResult> RevokeInvitationEndpoint(
        string invitationId, ClaimsPrincipal principal, RevokeStaffInvitation handler,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserUpdate) is { } denial) return denial;
        var actor = principal.UserId();
        if (actor is null) return Results.Unauthorized();

        var result = await handler.HandleAsync(new RevokeStaffInvitationCommand(new UserId(actor), invitationId), ct);
        if (!result.IsSuccess) return FromError(result.Error);

        await Record(audit, principal, AuditAction.UserInvitationRevoked, "staff-invitation",
            invitationId, invitationId, clock.UtcNow, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> BulkSuspendEndpoint(
        BulkSuspendRequest request, ClaimsPrincipal principal, BulkSuspendUsers handler,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserSuspend) is { } denial) return denial;
        var actor = principal.UserId();
        if (actor is null) return Results.Unauthorized();

        var batchId = Guid.NewGuid().ToString("n");
        var result = await handler.HandleAsync(
            new BulkSuspendCommand(new UserId(actor), request.UserIds, request.Reason), ct);
        if (!result.IsSuccess) return FromError(result.Error);

        foreach (var item in result.Value!.Items.Where(i => i.Outcome is "suspended"))
        {
            await Record(
                audit, principal, AuditAction.UserBulkSuspended, "user", item.UserId, item.UserId,
                clock.UtcNow, ct,
                new Dictionary<string, string> { ["batchId"] = batchId, ["fields"] = "status" });
        }

        return Results.Ok(new { batchId, items = result.Value.Items });
    }

    private static async Task<IResult> PatchUserEndpoint(
        string userId, PatchAdminUserRequest request, ClaimsPrincipal principal,
        UpdateAdminUserProfile handler, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserUpdate) is { } denial) return denial;
        var actor = principal.UserId();
        if (actor is null) return Results.Unauthorized();

        var fields = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.DisplayName)) fields.Add("displayName");
        if (request.UpdateEmail) fields.Add("email");
        if (request.UpdatePhone) fields.Add("phone");

        var result = await handler.HandleAsync(
            new UpdateAdminUserCommand(
                new UserId(actor), new UserId(userId), request.DisplayName,
                request.Email, request.UpdateEmail, request.Phone, request.UpdatePhone), ct);
        if (!result.IsSuccess) return FromError(result.Error);

        await Record(
            audit, principal, AuditAction.UserProfileUpdated, "user", userId, userId,
            clock.UtcNow, ct, new Dictionary<string, string> { ["fields"] = string.Join(",", fields) });

        return Results.Ok(new
        {
            userId = result.Value!.Id.Value,
            displayName = result.Value.DisplayName,
            email = result.Value.Email?.Value,
            phone = result.Value.Phone?.Value,
        });
    }

    private static IResult ResendUserVerificationEndpoint(ClaimsPrincipal principal)
    {
        if (Denied(principal, PermissionKeys.UserUpdate) is { } denial) return denial;
        return Problem(
            ErrorCodes.PolicyNotConfigured,
            "Email verification was removed. Accounts are not verified on this deployment. → ADR-0018",
            StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ForcePasswordResetEndpoint(
        string userId, ClaimsPrincipal principal, ForceStaffPasswordReset handler,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserUpdate) is { } denial) return denial;

        var result = await handler.HandleAsync(new ForceStaffPasswordResetCommand(new UserId(userId)), ct);
        if (!result.IsSuccess) return FromError(result.Error);

        await Record(audit, principal, AuditAction.UserPasswordResetForced, "user", userId, userId,
            clock.UtcNow, ct, new Dictionary<string, string> { ["fields"] = "password,refreshTokens" });

        return Results.Ok(new { emailSent = result.Value!.EmailSent });
    }

    private static async Task<IResult> UserActivityEndpoint(
        string userId, ClaimsPrincipal principal, ILearnerActivityLog activity, IClock clock,
        int? page, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserRead) is { } denial) return denial;
        const int PageSize = 25;
        var current = Math.Clamp(page ?? 1, 1, MaxPage);
        var to = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = to.AddDays(-365);
        var (days, total) = await activity.ListPageAsync(
            new UserId(userId), from, to, (current - 1) * PageSize, PageSize, ct);
        return Results.Ok(new
        {
            total,
            page = current,
            pageSize = PageSize,
            days = days.Select(d => new { day = d.Date, kinds = d.Kinds.Select(k => k.ToString()) }),
        });
    }

    private static async Task<IResult> UserTargetAuditEndpoint(
        string userId, ClaimsPrincipal principal, IAuditLog audit, int? page, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.AuditRead) is { } denial) return denial;
        const int PageSize = 25;
        var current = Math.Clamp(page ?? 1, 1, MaxPage);
        var (entries, total) = await audit.ListForTargetAsync(userId, (current - 1) * PageSize, PageSize, ct);
        return Results.Ok(new
        {
            total,
            page = current,
            pageSize = PageSize,
            entries = entries.Select(e => new
            {
                id = e.Id,
                at = e.At,
                actorEmail = e.ActorEmail,
                action = e.Action.ToString(),
                targetType = e.TargetType,
                targetId = e.TargetId,
                targetLabel = e.TargetLabel,
                detail = e.Detail,
            }),
        });
    }

    private static async Task<IResult> UserExamsEndpoint(
        string userId, ClaimsPrincipal principal, IExamCatalogue catalogue, int? page, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamReadAny) is { } denial) return denial;
        const int PageSize = 25;
        var current = Math.Clamp(page ?? 1, 1, MaxPage);
        var (versions, total) = await catalogue.ListByCreatedByAsync(
            new UserId(userId), (current - 1) * PageSize, PageSize, ct);
        return Results.Ok(new
        {
            total,
            page = current,
            pageSize = PageSize,
            exams = versions.Select(v => new
            {
                examVersionId = v.Id.Value,
                title = v.Title,
                status = v.Status.ToString(),
            }),
        });
    }

    private static async Task<IResult> UserSittingsEndpoint(
        string userId, ClaimsPrincipal principal, IExamSessionRepository sessions, int? page, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.EvaluationRead) is { } denial) return denial;
        const int PageSize = 25;
        var current = Math.Clamp(page ?? 1, 1, MaxPage);
        var (sittings, total) = await sessions.ListPageForUserAsync(
            new UserId(userId), (current - 1) * PageSize, PageSize, ct);
        return Results.Ok(new
        {
            total,
            page = current,
            pageSize = PageSize,
            sittings = sittings.Select(s => new
            {
                sessionId = s.Id.Value,
                status = s.Status.ToString(),
                mode = s.Mode.ToString(),
                startedAt = s.StartedAt,
            }),
        });
    }

    private static IResult UserResultsEndpoint(ClaimsPrincipal principal, int? page)
    {
        if (Denied(principal, PermissionKeys.EvaluationRead) is { } denial) return denial;
        // Store-side per-user results paging lands with I2-E Mongo work; fail closed empty + note.
        return Results.Ok(new
        {
            total = 0,
            page = Math.Clamp(page ?? 1, 1, MaxPage),
            pageSize = 25,
            results = Array.Empty<object>(),
            note = "store-side-paging-pending",
        });
    }

    private static IResult UserTokensEndpoint(ClaimsPrincipal principal, int? page)
    {
        if (Denied(principal, PermissionKeys.TokenRead) is { } denial) return denial;
        return Results.Ok(new
        {
            total = 0,
            page = Math.Clamp(page ?? 1, 1, MaxPage),
            pageSize = 25,
            tokens = Array.Empty<object>(),
            note = "unbuilt",
        });
    }

    private static async Task<IResult> RequestUserExportEndpoint(
        string userId, ClaimsPrincipal principal, Microsoft.Extensions.Options.IOptions<PrivacyOptions> privacy,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserExport) is { } denial) return denial;

        await Record(audit, principal, AuditAction.UserExportRequested, "user", userId, userId,
            clock.UtcNow, ct, new Dictionary<string, string> { ["fields"] = "export" });

        return Problem(
            ErrorCodes.PolicyNotConfigured,
            "Export retention is not configured. Nothing was stored.",
            StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListPrivacyRequestsEndpoint(
        string userId, ClaimsPrincipal principal, IPrivacyRequestRepository requests, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserDelete) is { } denial) return denial;
        var rows = await requests.ListForSubjectAsync(new UserId(userId), ct);
        return Results.Ok(new
        {
            requests = rows.Select(r => new
            {
                requestId = r.Id,
                type = r.Type.ToString().ToLowerInvariant(),
                status = r.Status.ToString(),
                createdAt = r.CreatedAt,
                requesterId = r.RequesterId.Value,
            }),
        });
    }

    private static async Task<IResult> CreatePrivacyRequestEndpoint(
        string userId, CreatePrivacyRequestBody request, ClaimsPrincipal principal,
        CreatePrivacyRequest handler, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserDelete) is { } denial) return denial;
        var actor = principal.UserId();
        if (actor is null) return Results.Unauthorized();

        var result = await handler.HandleAsync(
            new CreatePrivacyRequestCommand(new UserId(actor), new UserId(userId), request.Type, request.Reason), ct);
        if (!result.IsSuccess) return FromError(result.Error);

        await Record(audit, principal, AuditAction.PrivacyRequestCreated, "privacy-request",
            result.Value!.Id, userId, clock.UtcNow, ct, new Dictionary<string, string> { ["type"] = request.Type });

        return Results.Ok(new { requestId = result.Value.Id, status = result.Value.Status.ToString() });
    }

    private static async Task<IResult> ApprovePrivacyRequestEndpoint(
        string requestId, ClaimsPrincipal principal, ApprovePrivacyRequest handler,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserDelete) is { } denial) return denial;
        var actor = principal.UserId();
        if (actor is null) return Results.Unauthorized();

        var result = await handler.HandleAsync(new ApprovePrivacyRequestCommand(new UserId(actor), requestId), ct);
        if (!result.IsSuccess) return FromError(result.Error);

        await Record(audit, principal, AuditAction.PrivacyRequestApproved, "privacy-request",
            requestId, requestId, clock.UtcNow, ct);
        return Results.Ok(new { requestId, status = result.Value!.Status.ToString() });
    }

    private static async Task<IResult> ExecutePrivacyRequestEndpoint(
        string requestId, ClaimsPrincipal principal, ExecutePrivacyRequest handler,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserDelete) is { } denial) return denial;
        var actor = principal.UserId();
        if (actor is null) return Results.Unauthorized();

        var result = await handler.HandleAsync(new ExecutePrivacyRequestCommand(new UserId(actor), requestId), ct);
        await Record(audit, principal, AuditAction.PrivacyRequestExecuteRefused, "privacy-request",
            requestId, requestId, clock.UtcNow, ct, new Dictionary<string, string> { ["code"] = result.Error.Code });
        return FromError(result.Error);
    }
}
