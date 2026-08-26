using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Api.Endpoints;

/// <summary>
/// The CMS's read surface.
///
/// <b>Every route checks a permission, and the check is here rather than in
/// the client.</b> The CMS hides a sidebar entry the caller cannot read, but
/// hiding a button is not authorization — an admin client is untrusted code,
/// and constraint 7 of the CMS specification says so in as many words. A
/// caller who types the URL gets a 403 from this layer.
///
/// <b>Every write records who did it, before it answers.</b> The audit entry
/// is written in the same request as the change, not queued for later — an
/// entry that can be lost between the act and the record is not evidence of
/// anything. → `cms-spec.md` ràng buộc 6, threat `T21`
/// </summary>
public sealed record AssignRoleRequest(string RoleId, bool Grant);
public sealed record ReturnExamRequest(string Note);
public sealed record ApproveExamRequest(string? Note);

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // Rate-limited as a group, which the read endpoints do not strictly
        // need and the write endpoints do. An operator account is a far more
        // valuable thing to borrow than a learner account — it publishes
        // content to every candidate and can lock people out — so a stolen
        // admin token should not also be an unmetered one.
        //
        // InSessionRead's bound (300/min per subject) is generous enough that
        // no human clicking through the CMS will meet it.
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.InSessionRead);

        group.MapGet("/exams", ExamsEndpoint)
            .WithName("AdminListExams")
            .WithSummary("Every exam version the caller can see, drafts included");

        group.MapGet("/exams/{examVersionId}", ExamEndpoint)
            .WithName("AdminGetExam")
            .WithSummary("One exam version, with its review history");

        group.MapGet("/users", UsersEndpoint)
            .WithName("AdminListUsers")
            .WithSummary("A page of accounts");

        group.MapGet("/roles", RolesEndpoint)
            .WithName("AdminListRoles")
            .WithSummary("Roles and the permissions each one holds");

        group.MapGet("/users/{userId}", UserEndpoint)
            .WithName("AdminGetUser")
            .WithSummary("One account");

        group.MapGet("/audit", AuditEndpoint)
            .WithName("AdminAudit")
            .WithSummary("Who did what, newest first");

        group.MapPost("/exams/{examVersionId}/submit", SubmitEndpoint)
            .WithName("AdminSubmitExam")
            .WithSummary("Send a draft for review");

        group.MapPost("/exams/{examVersionId}/withdraw", WithdrawEndpoint)
            .WithName("AdminWithdrawExam")
            .WithSummary("Pull a submission back to draft");

        group.MapPost("/exams/{examVersionId}/return", ReturnEndpoint)
            .WithName("AdminReturnExam")
            .WithSummary("Send a submission back to its author with a note");

        group.MapPost("/exams/{examVersionId}/approve", ApproveEndpoint)
            .WithName("AdminApproveExam")
            .WithSummary("Approve a submission — not the same as publishing it");

        group.MapPost("/exams/{examVersionId}/unapprove", UnapproveEndpoint)
            .WithName("AdminUnapproveExam")
            .WithSummary("Reopen an approved version for further review");

        group.MapPost("/exams/{examVersionId}/resume", ResumeEndpoint)
            .WithName("AdminResumeExam")
            .WithSummary("Pick a returned version back up as a draft");

        group.MapPost("/exams/{examVersionId}/publish", PublishEndpoint)
            .WithName("AdminPublishExam")
            .WithSummary("Make an approved version sittable");

        group.MapPost("/exams/{examVersionId}/unpublish", UnpublishEndpoint)
            .WithName("AdminUnpublishExam")
            .WithSummary("Stop new sittings of a published version");

        group.MapPost("/users/{userId}/suspend", SuspendEndpoint)
            .WithName("AdminSuspendUser")
            .WithSummary("Block an account from signing in");

        group.MapPost("/users/{userId}/reinstate", ReinstateEndpoint)
            .WithName("AdminReinstateUser")
            .WithSummary("Let a suspended account sign in again");

        group.MapPost("/users/{userId}/roles", AssignRoleEndpoint)
            .WithName("AdminAssignRole")
            .WithSummary("Grant or revoke one role on one account");
    }

    /// <summary>
    /// <b>Drafts included — that is the whole difference from the learner
    /// route.</b> `/api/v1/exams` filters to published in the query, because a
    /// learner must never be handed content nobody has reviewed. The CMS is
    /// the surface where an unreviewed draft is exactly what you came to look
    /// at — for whichever versions this caller is allowed to see.
    ///
    /// <b>Filtered in memory, not in the query.</b> Unlike
    /// <c>ListSittableAsync</c>'s status filter, ownership here depends on the
    /// caller, not on the data — the catalogue stays a small, cacheable,
    /// caller-independent read, and "who can see this row" is decided once,
    /// at the boundary, the same place every other permission check in this
    /// file lives.
    /// </summary>
    private static async Task<IResult> ExamsEndpoint(
        ClaimsPrincipal principal, IExamCatalogue catalogue, CancellationToken ct)
    {
        if (DeniedList(principal) is { } denial) return denial;

        var all = await catalogue.ListAllAsync(ct);
        var visible = principal.Permissions().Contains(PermissionKeys.ExamReadAny)
            ? all
            : all.Where(v => v.CreatedBy.Value == principal.UserId()).ToList();

        return Results.Ok(new
        {
            exams = visible.Select(v => new
            {
                examVersionId = v.Id.Value,
                definitionId = v.DefinitionId.Value,
                versionNumber = v.VersionNumber,
                title = v.Title,
                variant = v.Variant.ToString().ToLowerInvariant(),
                status = v.Status.ToWire(),
                createdBy = v.CreatedBy.Value,
                submittedAt = v.SubmittedAt,
                reviewedBy = v.ReviewedBy?.Value,
                reviewedAt = v.ReviewedAt,
                publishedAt = v.PublishedAt,
                modules = v.Sections
                    .OrderBy(s => s.Order)
                    .Select(s => new
                    {
                        module = s.Module.ToString().ToLowerInvariant(),
                        questionCount = s.Questions.Count(),
                        durationSeconds = (int)v.Timing.DurationFor(s.Module).TotalSeconds,
                    }),
            }),
        });
    }

    /// <summary>Full detail for one version, including its review notes.</summary>
    private static async Task<IResult> ExamEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue, CancellationToken ct)
    {
        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (DeniedRead(principal, version.CreatedBy) is { } denial) return denial;

        return Results.Ok(new
        {
            examVersionId = version.Id.Value,
            definitionId = version.DefinitionId.Value,
            versionNumber = version.VersionNumber,
            title = version.Title,
            variant = version.Variant.ToString().ToLowerInvariant(),
            status = version.Status.ToWire(),
            createdBy = version.CreatedBy.Value,
            submittedBy = version.SubmittedBy?.Value,
            submittedAt = version.SubmittedAt,
            reviewedBy = version.ReviewedBy?.Value,
            reviewedAt = version.ReviewedAt,
            publishedAt = version.PublishedAt,
            reviewNotes = version.ReviewNotes.Select(n => new
            {
                id = n.Id,
                authorId = n.AuthorId.Value,
                body = n.Body,
                anchor = n.Anchor,
                at = n.At,
            }),
            modules = version.Sections
                .OrderBy(s => s.Order)
                .Select(s => new
                {
                    module = s.Module.ToString().ToLowerInvariant(),
                    questionCount = s.Questions.Count(),
                    durationSeconds = (int)version.Timing.DurationFor(s.Module).TotalSeconds,
                }),
        });
    }

    private static async Task<IResult> UsersEndpoint(
        ClaimsPrincipal principal, IUserRepository users,
        string? search, int? page, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserRead) is { } denial) return denial;

        // Clamped, not trusted. A caller-supplied page size is a way to ask
        // for the whole collection in one response.
        const int PageSize = 25;
        var current = Math.Clamp(page ?? 1, 1, MaxPage);

        var (page1, total) = await users.ListAsync(search, (current - 1) * PageSize, PageSize, ct);

        return Results.Ok(new
        {
            total,
            page = current,
            pageSize = PageSize,
            users = page1.Select(u => new
            {
                userId = u.Id.Value,
                displayName = u.DisplayName,
                // The address is shown to an operator who has `user.read`; it
                // is the only way to tell two accounts apart in a support call.
                email = u.Email.Value,
                emailVerified = u.EmailVerified,
                status = u.Status.ToString().ToLowerInvariant(),
                createdAt = u.CreatedAt,
                roleIds = u.RoleIds.Select(r => r.Value),
            }),
        });
    }

    private static async Task<IResult> RolesEndpoint(
        ClaimsPrincipal principal, IRoleRepository roles, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.RoleRead) is { } denial) return denial;

        var all = await roles.ListAsync(ct);

        return Results.Ok(new
        {
            // The full key set, so the matrix can render a column for a
            // permission no role holds yet. Derived from the domain constants
            // rather than restated in the client — one list, one truth.
            permissions = PermissionKeys.All,
            roles = all.Select(r => new
            {
                roleId = r.Id.Value,
                name = r.Name,
                isSystem = r.IsSystem,
                permissions = r.Permissions,
            }),
        });
    }

    private static async Task<IResult> UserEndpoint(
        string userId, ClaimsPrincipal principal, IUserRepository users,
        IRoleRepository roles, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserRead) is { } denial) return denial;

        var user = await users.FindByIdAsync(new UserId(userId), ct);
        if (user is null) return Results.NotFound();

        var all = await roles.ListAsync(ct);

        return Results.Ok(new
        {
            userId = user.Id.Value,
            displayName = user.DisplayName,
            email = user.Email.Value,
            emailVerified = user.EmailVerified,
            phone = user.Phone?.Value,
            status = user.Status.ToString().ToLowerInvariant(),
            createdAt = user.CreatedAt,
            roles = all
                .Where(r => user.HasRole(r.Id))
                .Select(r => new { roleId = r.Id.Value, name = r.Name }),
            availableRoles = all.Select(r => new { roleId = r.Id.Value, name = r.Name }),
        });
    }

    private static async Task<IResult> AuditEndpoint(
        ClaimsPrincipal principal, IAuditLog audit,
        string? actor, string? action, int? page, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.AuditRead) is { } denial) return denial;

        const int PageSize = 40;

        // Clamped at both ends. An unbounded page number turns into an
        // unbounded `skip`, and Mongo walks every skipped document — a cheap
        // request to send and an expensive one to serve.
        var current = Math.Clamp(page ?? 1, 1, MaxPage);

        var (entries, total) = await audit.ListAsync(
            actor, action, (current - 1) * PageSize, PageSize, ct);

        return Results.Ok(new
        {
            total,
            page = current,
            pageSize = PageSize,
            actions = Enum.GetNames<AuditAction>(),
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

    /// <summary>Draft → InReview. The author's own action — see <see cref="DeniedOwn"/>.</summary>
    private static async Task<IResult> SubmitEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (DeniedOwn(principal, PermissionKeys.ExamSubmit, version.CreatedBy) is { } denial) return denial;

        if (version.Status != ExamVersionStatus.Draft)
            return Conflict("Chỉ bản nháp mới nộp duyệt được.");

        var now = clock.UtcNow;
        version.SubmitForReview(new UserId(principal.UserId()!), now);
        await catalogue.UpsertAsync(version, ct);

        await Record(audit, principal, AuditAction.ExamSubmitted, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", now, ct);

        return Results.Ok(new { status = version.Status.ToWire(), submittedAt = version.SubmittedAt });
    }

    /// <summary>InReview → Draft. The author pulling their own submission back.</summary>
    private static async Task<IResult> WithdrawEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (DeniedOwn(principal, PermissionKeys.ExamSubmit, version.CreatedBy) is { } denial) return denial;

        if (version.Status != ExamVersionStatus.InReview)
            return Conflict("Chỉ bài đang chờ duyệt mới rút về được.");

        version.Withdraw();
        await catalogue.UpsertAsync(version, ct);

        await Record(audit, principal, AuditAction.ExamWithdrawn, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", clock.UtcNow, ct);

        return Results.Ok(new { status = version.Status.ToWire() });
    }

    /// <summary>
    /// InReview → Returned. Any reviewer's action, not the author's — see
    /// <see cref="Denied(ClaimsPrincipal,string)"/>. Requires a note; the
    /// domain method throws without one, but the check is repeated here to
    /// return a 400 (a malformed request) rather than a 500 (an unexpected
    /// server failure).
    /// </summary>
    private static async Task<IResult> ReturnEndpoint(
        string examVersionId, ClaimsPrincipal principal, ReturnExamRequest request,
        IExamCatalogue catalogue, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamReview) is { } denial) return denial;

        if (string.IsNullOrWhiteSpace(request.Note))
            return Results.Problem(
                detail: "Trả lại cần kèm ghi chú.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ValidationFailed });

        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (version.Status != ExamVersionStatus.InReview)
            return Conflict("Chỉ bài đang chờ duyệt mới trả lại được.");

        var now = clock.UtcNow;
        version.Return(new UserId(principal.UserId()!), request.Note, now);
        await catalogue.UpsertAsync(version, ct);

        await Record(audit, principal, AuditAction.ExamReturned, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", now, ct);

        return Results.Ok(new { status = version.Status.ToWire(), reviewedAt = version.ReviewedAt });
    }

    /// <summary>
    /// InReview → Approved. Not publication — approval and publication are
    /// separate authorities held by different roles (`Đ4`).
    /// </summary>
    private static async Task<IResult> ApproveEndpoint(
        string examVersionId, ClaimsPrincipal principal, ApproveExamRequest request,
        IExamCatalogue catalogue, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamReview) is { } denial) return denial;

        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (version.Status != ExamVersionStatus.InReview)
            return Conflict("Chỉ bài đang chờ duyệt mới duyệt được.");

        var now = clock.UtcNow;
        version.Approve(new UserId(principal.UserId()!), now, request.Note);
        await catalogue.UpsertAsync(version, ct);

        await Record(audit, principal, AuditAction.ExamApproved, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", now, ct);

        return Results.Ok(new { status = version.Status.ToWire(), reviewedAt = version.ReviewedAt });
    }

    /// <summary>Approved → InReview. Reopens a decision that was made too early.</summary>
    private static async Task<IResult> UnapproveEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamReview) is { } denial) return denial;

        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (version.Status != ExamVersionStatus.Approved)
            return Conflict("Chỉ bài đã duyệt mới huỷ duyệt được.");

        version.Unapprove();
        await catalogue.UpsertAsync(version, ct);

        await Record(audit, principal, AuditAction.ExamUnapproved, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", clock.UtcNow, ct);

        return Results.Ok(new { status = version.Status.ToWire() });
    }

    /// <summary>Returned → Draft. The author picking a returned submission back up.</summary>
    private static async Task<IResult> ResumeEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (DeniedOwn(principal, PermissionKeys.ExamUpdateOwn, version.CreatedBy) is { } denial)
            return denial;

        if (version.Status != ExamVersionStatus.Returned)
            return Conflict("Chỉ bài bị trả lại mới tiếp tục sửa được.");

        version.Resume();
        await catalogue.UpsertAsync(version, ct);

        await Record(audit, principal, AuditAction.ExamResumed, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", clock.UtcNow, ct);

        return Results.Ok(new { status = version.Status.ToWire() });
    }

    /// <summary>
    /// Approved → Published, or Unpublished → Published (republishing).
    ///
    /// <b>Supersedes whatever was published before it, in the same request.</b>
    /// A definition has at most one live version — publishing this one and
    /// leaving a sibling <c>Published</c> would mean two versions of the same
    /// exam are both sittable, which is a state nothing downstream expects.
    /// </summary>
    private static async Task<IResult> PublishEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamPublish) is { } denial) return denial;

        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (version.Status is not (ExamVersionStatus.Approved or ExamVersionStatus.Unpublished))
            return Conflict("Chỉ bài đã duyệt hoặc đã gỡ xuất bản mới xuất bản được.");

        var now = clock.UtcNow;

        var siblings = await catalogue.ListAllAsync(ct);
        var previouslyPublished = siblings.FirstOrDefault(v =>
            v.DefinitionId == version.DefinitionId
            && v.Id != version.Id
            && v.Status == ExamVersionStatus.Published);

        if (previouslyPublished is not null)
        {
            previouslyPublished.Unpublish();
            await catalogue.UpsertAsync(previouslyPublished, ct);

            await Record(audit, principal, AuditAction.ExamUnpublished, "exam-version",
                previouslyPublished.Id.Value,
                $"{previouslyPublished.Title} v{previouslyPublished.VersionNumber}", now, ct);
        }

        version.Publish(now);
        await catalogue.UpsertAsync(version, ct);

        await Record(audit, principal, AuditAction.ExamPublished, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", now, ct);

        return Results.Ok(new { status = version.Status.ToWire() });
    }

    /// <summary>
    /// Blocks new sittings. Does <b>not</b> end sittings already running —
    /// terminating a timed exam mid-attempt is a scoring incident, not an
    /// administrative action. The domain says so too. → `M-15`
    /// </summary>
    private static async Task<IResult> UnpublishEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamUnpublish) is { } denial) return denial;

        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (version.Status != ExamVersionStatus.Published)
            return Conflict("Version này chưa được xuất bản.");

        version.Unpublish();
        await catalogue.UpsertAsync(version, ct);

        await Record(audit, principal, AuditAction.ExamUnpublished, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", clock.UtcNow, ct);

        return Results.Ok(new { status = version.Status.ToWire() });
    }

    private static Task<IResult> SuspendEndpoint(
        string userId, ClaimsPrincipal principal, IUserRepository users,
        ITokenService tokens, IAuditLog audit, IClock clock, CancellationToken ct) =>
        ChangeStatus(userId, principal, users, tokens, audit, clock, suspend: true, ct);

    private static Task<IResult> ReinstateEndpoint(
        string userId, ClaimsPrincipal principal, IUserRepository users,
        ITokenService tokens, IAuditLog audit, IClock clock, CancellationToken ct) =>
        ChangeStatus(userId, principal, users, tokens, audit, clock, suspend: false, ct);

    /// <summary>
    /// Suspend or reinstate, and — on suspend — cut the account's ability to
    /// keep going.
    ///
    /// <b>Revoking the refresh families is the part that was missing.</b>
    /// `RefreshTokens` already refuses a suspended account, so a suspended
    /// user could not have obtained a <i>new</i> access token. But the families
    /// stayed alive in storage, which meant the suspension left no mark on the
    /// thing an attacker actually holds, and a later reinstatement silently
    /// re-armed every session the account had ever opened — including one
    /// opened by whoever the suspension was a response to.
    ///
    /// <b>What this still does not do is revoke the access token already
    /// issued.</b> A JWT cannot be recalled; the window is
    /// <c>Jwt:AccessTokenMinutes</c>, 15 by default. That is a deliberate
    /// trade — the alternative is a database read on every request — but it
    /// has to be said out loud, and the operator's confirmation dialog says it
    /// rather than promising an instant cut-off.
    /// </summary>
    private static async Task<IResult> ChangeStatus(
        string userId, ClaimsPrincipal principal, IUserRepository users,
        ITokenService tokens, IAuditLog audit, IClock clock, bool suspend,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserSuspend) is { } denial) return denial;

        // Suspending yourself locks the last administrator out of the product,
        // and the only way back is a database edit. Refused here rather than
        // hidden in the UI, because the UI is not the enforcement.
        if (principal.UserId() == userId)
            return Conflict("Không thể tự khoá tài khoản của chính mình.");

        var user = await users.FindByIdAsync(new UserId(userId), ct);
        if (user is null) return Results.NotFound();

        if (suspend) user.Suspend();
        else user.Reinstate();

        await users.SaveAsync(user, ct);

        // After the save, not before: a revocation that succeeds against an
        // account whose status write then fails leaves the account usable and
        // its sessions gone, which is the worst of both.
        if (suspend) await tokens.RevokeAllForUserAsync(user.Id, ct);

        await Record(
            audit, principal,
            suspend ? AuditAction.UserSuspended : AuditAction.UserReinstated,
            "user", user.Id.Value, user.Email.Value, clock.UtcNow, ct);

        return Results.Ok(new { status = user.Status.ToString().ToLowerInvariant() });
    }

    private static async Task<IResult> AssignRoleEndpoint(
        string userId, ClaimsPrincipal principal, AssignRoleRequest request,
        IUserRepository users, IRoleRepository roles,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.RoleAssign) is { } denial) return denial;

        var user = await users.FindByIdAsync(new UserId(userId), ct);
        if (user is null) return Results.NotFound();

        var role = await roles.FindByIdAsync(new RoleId(request.RoleId), ct);
        if (role is null) return Results.NotFound();

        // Dropping your own admin role is the same lockout as suspending
        // yourself, one step removed.
        if (principal.UserId() == userId && !request.Grant && role.Name == SystemRoles.Admin)
            return Conflict("Không thể tự gỡ vai admin của chính mình.");

        if (request.Grant) user.AssignRole(role.Id);
        else user.RemoveRole(role.Id);

        await users.SaveAsync(user, ct);

        await Record(
            audit, principal,
            request.Grant ? AuditAction.RoleAssigned : AuditAction.RoleRemoved,
            "user", user.Id.Value, user.Email.Value, clock.UtcNow, ct,
            new Dictionary<string, string> { ["role"] = role.Name });

        return Results.Ok(new { roles = user.RoleIds.Select(r => r.Value) });
    }

    /// <summary>
    /// Writes the audit entry.
    ///
    /// The actor's address comes from the token rather than from a lookup:
    /// the log has to stay readable after the account is renamed or deleted,
    /// and a foreign key into a table that can lose rows is not a record.
    /// </summary>
    private static Task Record(
        IAuditLog audit, ClaimsPrincipal principal, AuditAction action,
        string targetType, string targetId, string targetLabel,
        DateTimeOffset now, CancellationToken ct,
        IReadOnlyDictionary<string, string>? detail = null) =>
        audit.AppendAsync(
            AuditEntry.Record(
                new UserId(principal.UserId() ?? "unknown"),
                principal.Email() ?? principal.DisplayName(),
                action, targetType, targetId, targetLabel, now, detail),
            ct);

    /// <summary>
    /// The highest page any listing will serve.
    ///
    /// Paging is implemented with `skip`, and Mongo walks every skipped
    /// document — so `?page=100000000` is one cheap request that costs a full
    /// collection scan. The cap is far above any listing a person will page
    /// through by hand; anyone who genuinely needs to reach the end of a large
    /// collection needs a filter, not a deeper page.
    /// </summary>
    private const int MaxPage = 10_000;

    private static IResult Conflict(string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ValidationFailed });

    /// <summary>
    /// 403 with a stable code, not 404.
    ///
    /// The opposite call from an exam session, and for the opposite reason:
    /// there, hiding existence stops one learner enumerating another's
    /// sittings. Here the caller is a named operator who needs to be told they
    /// lack a permission — screen 1.2 of the specification exists precisely to
    /// say so rather than to look broken. The ownership-scoped checks below
    /// keep this rule: an author who cannot see someone else's draft is told
    /// they lack `exam.read.any`, not shown a 404 that pretends it does not
    /// exist.
    /// </summary>
    private static IResult? Denied(ClaimsPrincipal principal, string permission)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        return principal.Permissions().Contains(permission) ? null : Forbidden(permission);
    }

    /// <summary>
    /// The ownership-scoped counterpart to <see cref="Denied(ClaimsPrincipal,string)"/>.
    ///
    /// Mirrors <c>apps/admin/src/lib/lifecycle.ts</c>'s <c>allows()</c> exactly:
    /// the caller needs <paramref name="permission"/>, and is either the
    /// resource's creator or holds <c>exam.update.any</c> — the single
    /// override key every own-scoped transition defers to, not a matching
    /// `.any` variant of each individual permission. That is a real asymmetry
    /// with <see cref="DeniedRead"/> below, which does have its own `.any` key
    /// (`exam.read.any`) — reading and acting are different permission
    /// families with different override shapes, and this follows the shape
    /// already pinned by the frontend's tests rather than inventing a more
    /// uniform-looking one.
    /// </summary>
    private static IResult? DeniedOwn(ClaimsPrincipal principal, string permission, UserId createdBy)
    {
        if (principal.UserId() is null) return Results.Unauthorized();

        var perms = principal.Permissions();
        var allowed = perms.Contains(permission)
            && (principal.UserId() == createdBy.Value || perms.Contains(PermissionKeys.ExamUpdateAny));

        return allowed ? null : Forbidden(permission);
    }

    /// <summary>Can the caller list exams at all — either scope is enough; row filtering happens after.</summary>
    private static IResult? DeniedList(ClaimsPrincipal principal)
    {
        if (principal.UserId() is null) return Results.Unauthorized();

        var perms = principal.Permissions();
        if (perms.Contains(PermissionKeys.ExamReadAny) || perms.Contains(PermissionKeys.ExamReadOwn))
            return null;

        return Forbidden(PermissionKeys.ExamReadOwn);
    }

    /// <summary>Can the caller read this one version — `exam.read.any`, or `exam.read.own` and ownership.</summary>
    private static IResult? DeniedRead(ClaimsPrincipal principal, UserId createdBy)
    {
        if (principal.UserId() is null) return Results.Unauthorized();

        var perms = principal.Permissions();
        if (perms.Contains(PermissionKeys.ExamReadAny)) return null;
        if (perms.Contains(PermissionKeys.ExamReadOwn) && principal.UserId() == createdBy.Value) return null;

        return Forbidden(PermissionKeys.ExamReadAny);
    }

    private static IResult Forbidden(string permission) =>
        Results.Problem(
            detail: $"This account does not hold {permission}.",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ErrorCodes.PermissionDenied,
                ["permission"] = permission,
            });
}

/// <summary>
/// The wire form of <see cref="ExamVersionStatus"/> — kebab-case, matching
/// <c>apps/admin/src/lib/lifecycle.ts</c>'s <c>ExamState</c> union exactly, so
/// a future client switching off the mock store needs no translation layer.
/// <c>Enum.ToString().ToLowerInvariant()</c> would produce <c>"inreview"</c>,
/// not <c>"in-review"</c> — close enough to look right and wrong regardless.
/// </summary>
internal static class ExamVersionStatusExtensions
{
    public static string ToWire(this ExamVersionStatus status) => status switch
    {
        ExamVersionStatus.Draft => "draft",
        ExamVersionStatus.InReview => "in-review",
        ExamVersionStatus.Returned => "returned",
        ExamVersionStatus.Approved => "approved",
        ExamVersionStatus.Published => "published",
        ExamVersionStatus.Unpublished => "unpublished",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped exam version status."),
    };
}
