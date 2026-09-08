using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Content;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content;
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

/// <summary>`P-20`'s "Trả về kèm lý do" — a return with no reason is refused.</summary>
public sealed record ReturnExamToDraftRequest(string Reason);

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
            .WithSummary("Every exam version, drafts included");

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

        group.MapGet("/content-sources", ContentSourcesEndpoint)
            .WithName("AdminListContentSources")
            .WithSummary("Where the source material came from and what may be done with it");

        group.MapPost("/exams/{examVersionId}/publish", PublishEndpoint)
            .WithName("AdminPublishExam")
            .WithSummary("Make a draft version sittable");

        group.MapPost("/exams/{examVersionId}/unpublish", UnpublishEndpoint)
            .WithName("AdminUnpublishExam")
            .WithSummary("Stop new sittings of a published version");

        group.MapPost("/exams/{examVersionId}/submit-for-review", SubmitForReviewEndpoint)
            .WithName("AdminSubmitExamForReview")
            .WithSummary("Author sends a draft to review — P-20, Draft → InReview");

        group.MapPost("/exams/{examVersionId}/approve", ApproveEndpoint)
            .WithName("AdminApproveExam")
            .WithSummary("A different person signs off the content — P-20, InReview → Approved. "
                + "403 REVIEWER_IS_AUTHOR if the caller authored this version.");

        group.MapPost("/exams/{examVersionId}/return-to-draft", ReturnToDraftEndpoint)
            .WithName("AdminReturnExamToDraft")
            .WithSummary("Reviewer sends a version back with a reason — P-20, InReview → Draft");

        group.MapPost("/users/{userId}/suspend", SuspendEndpoint)
            .WithName("AdminSuspendUser")
            .WithSummary("Block an account from signing in");

        group.MapPost("/users/{userId}/reinstate", ReinstateEndpoint)
            .WithName("AdminReinstateUser")
            .WithSummary("Let a suspended account sign in again");

        group.MapPost("/users/{userId}/roles", AssignRoleEndpoint)
            .WithName("AdminAssignRole")
            .WithSummary("Grant or revoke one role on one account");

        /*
         * <b>The recovery path, and the most dangerous thing in this file.</b>
         *
         * There is no self-service password reset any more: registration takes
         * a phone number, so for most accounts there is no address to mail a
         * link to. The owner chose this shape on 08/09/2026 — the learner
         * reaches support over Zalo and an operator sets a new password.
         *
         * What it costs is not hidden: whoever holds `user.reset-password` can
         * sign in as anybody. That is why it is its own permission rather than
         * part of `user.update`, why only `admin` is seeded with it, and why it
         * writes its own audit action. → ADR-0018, threat `T21`
         */
        group.MapPost("/users/{userId}/password", ResetUserPasswordEndpoint)
            .WithName("AdminResetUserPassword")
            .WithSummary("Set a new password for an account that cannot sign in");
    }

    /// <summary>The new password, in the clear over TLS and never logged.</summary>
    public sealed record ResetUserPasswordRequest(string NewPassword);

    /// <summary>
    /// <b>Drafts included — that is the whole difference from the learner
    /// route.</b> `/api/v1/exams` filters to published in the query, because a
    /// learner must never be handed content nobody has reviewed. The CMS is
    /// the surface where an unreviewed draft is exactly what you came to look
    /// at.
    /// </summary>
    private static async Task<IResult> ExamsEndpoint(
        ClaimsPrincipal principal, IExamCatalogue catalogue, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamRead) is { } denial) return denial;

        var versions = await catalogue.ListAllAsync(ct);

        return Results.Ok(new
        {
            exams = versions.Select(v => new
            {
                examVersionId = v.Id.Value,
                definitionId = v.DefinitionId.Value,
                versionNumber = v.VersionNumber,
                title = v.Title,
                variant = v.Variant.ToString().ToLowerInvariant(),
                status = v.Status.ToString().ToLowerInvariant(),
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
                // Both handles are shown to an operator who has `user.read`.
                // The address may be absent — registration asks for a number —
                // so the number is what a support call is usually traced by.
                email = u.Email?.Value,
                phone = u.Phone?.Value,
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
            email = user.Email?.Value,
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

    /// <summary>
    /// Every registered source, and what it may be used for.
    ///
    /// <b>The screen an operator needs the moment a publish is refused.</b>
    /// Without it, "CONTENT_RIGHT_MISSING" is indistinguishable from a bug —
    /// with it, the answer is a row saying which material the paper came from,
    /// which environments it is registered for, and who reviewed it (today:
    /// nobody).
    ///
    /// Behind <c>package.read</c> rather than <c>exam.read</c>: these rows name
    /// where third-party material sits on disk and what its licence position
    /// is, which is provenance rather than catalogue.
    /// </summary>
    private static async Task<IResult> ContentSourcesEndpoint(
        ClaimsPrincipal principal, IContentRightsRegistry registry, IClock clock,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.PackageRead) is { } denial) return denial;

        var now = clock.UtcNow;
        var sources = await registry.ListAsync(ct);

        return Results.Ok(new
        {
            // Stated rather than implied. An empty list means nothing may be
            // published — a reader must not have to infer that.
            note = "A source with no entry here holds no rights at all. "
                + "learner-production is granted to nothing while M-53 is open.",
            sources = sources.Select(s => new
            {
                sourceId = s.Id.Value,
                title = s.Title,
                owner = s.Owner,
                rootPath = s.RootPath,
                allowedEnvironments = s.AllowedEnvironments
                    .Select(Describe).Order().ToArray(),
                expiresAt = s.ExpiresAt,
                licenceReference = s.Proof?.Reference,
                reviewer = s.Proof?.Reviewer,
                reviewedAt = s.Proof?.ReviewedAt,

                // The one derived field, because it is the question being
                // asked and computing it in the client would put the rights
                // rule in two places.
                mayReachLearners = ContentRightsPolicy
                    .Evaluate(s, ContentEnvironment.LearnerProduction, now).Allowed,

                fileCount = s.Files.Count,
                hashedFileCount = s.Files.Count(f => f.Sha256 is not null),
                examDefinitionIds = s.BoundExamDefinitionIds,
                examVersionIds = s.BoundExamVersionIds,
            }),
        });
    }

    private static async Task<IResult> PublishEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue,
        ContentPublishGuard rights, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamPublish) is { } denial) return denial;

        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        /*
         * ── The content rights gate ──────────────────────────────────────
         *
         * <b>Importing content and shipping it to learners are separate
         * authorities.</b> `exam.publish` says this operator is allowed to
         * publish; it says nothing about whether this *material* may be
         * published, and the two are refused in different places for a reason.
         * `exam/Exam1` is the standing example — a complete, working,
         * sittable paper whose own README says "do not ship it to a learner".
         *
         * <b>Before the already-published check, deliberately.</b> "You may not
         * publish this at all" outranks "you already did": a version that was
         * published before the registry existed must not have its rights
         * problem hidden behind a status message.
         *
         * <b>Refuses by default.</b> An exam the registry knows nothing about
         * resolves to no source, and no source is no right. Presence in the
         * catalogue is not permission. → `M-53`, `G-11`
         */
        var decision = await rights.MayPublishToLearnersAsync(version, ct);

        if (!decision.Allowed)
        {
            return Results.Problem(
                detail: decision.Explanation,
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = ErrorCodes.ContentRightMissing,
                    ["reason"] = Describe(decision.Denial),
                    ["sourceId"] = decision.SourceId,
                });
        }

        if (version.Status == ExamVersionStatus.Published)
            return Conflict("Version này đã được xuất bản.");

        /*
         * `P-20`: the only path to Published is Approved (a fresh review
         * sign-off) or Unpublished (re-publishing unchanged content — the
         * "xuất bản lại" case; content is frozen once first published, so no
         * second review is owed). A Draft or a version still InReview has not
         * earned this yet.
         *
         * <b>Deliberately not enforced inside `ExamVersion.Publish` itself.</b>
         * A dozen existing unit and integration test fixtures across three
         * test projects build a sittable paper with `CreateDraft` followed
         * directly by `Publish`, with no review step, as a fixture shortcut
         * unrelated to this feature. Tightening the domain method's guard
         * would break every one of them for a workflow they do not exercise.
         * The endpoint is the one caller that matters for real traffic, so
         * the gate lives here — the same place the rights gate and the
         * already-published gate already live. → S7 report
         */
        if (version.Status is not (ExamVersionStatus.Approved or ExamVersionStatus.Unpublished))
            return Conflict("Version này chưa được duyệt, không thể xuất bản.");

        var now = clock.UtcNow;
        version.Publish(now);
        await catalogue.UpsertAsync(version, ct);

        await Record(audit, principal, AuditAction.ExamPublished, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", now, ct);

        return Results.Ok(new { status = version.Status.ToString().ToLowerInvariant() });
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
        await catalogue.SetStatusAsync(version.Id, ExamVersionStatus.Unpublished, ct);

        await Record(audit, principal, AuditAction.ExamUnpublished, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", clock.UtcNow, ct);

        return Results.Ok(new { status = version.Status.ToString().ToLowerInvariant() });
    }

    /// <summary>
    /// The author sends a draft to review. `P-20`, first step of
    /// <c>Draft → InReview → Approved → Published</c>.
    /// </summary>
    private static async Task<IResult> SubmitForReviewEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamSubmit) is { } denial) return denial;

        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (version.Status != ExamVersionStatus.Draft)
            return Conflict("Chỉ bản nháp mới nộp duyệt được.");

        var now = clock.UtcNow;
        version.SubmitForReview();
        await catalogue.SetStatusAsync(version.Id, version.Status, ct);

        await Record(audit, principal, AuditAction.ExamSubmittedForReview, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", now, ct);

        return Results.Ok(new { status = version.Status.ToString().ToLowerInvariant() });
    }

    /// <summary>
    /// A different person signs off the content. `P-20`'s central rule —
    /// reviewer ≠ author — is enforced inside <see cref="ExamVersion.Approve"/>,
    /// not by this endpoint's permission check, so it holds even for a caller
    /// who holds <see cref="PermissionKeys.ExamReview"/> and happens to be the
    /// author: <see cref="ReviewerIsAuthorException"/> becomes a 403, not a
    /// silently-ignored no-op and not a 500.
    /// </summary>
    private static async Task<IResult> ApproveEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamReview) is { } denial) return denial;
        if (principal.UserId() is not { } reviewerId) return Results.Unauthorized();

        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (version.Status != ExamVersionStatus.InReview)
            return Conflict("Chỉ version đang chờ duyệt mới duyệt được.");

        var now = clock.UtcNow;

        try
        {
            version.Approve(new UserId(reviewerId));
        }
        catch (ReviewerIsAuthorException e)
        {
            return Results.Problem(
                detail: e.Message,
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ReviewerIsAuthor });
        }

        await catalogue.SetStatusAsync(version.Id, version.Status, ct);

        await Record(audit, principal, AuditAction.ExamApproved, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", now, ct);

        return Results.Ok(new { status = version.Status.ToString().ToLowerInvariant() });
    }

    /// <summary>The reviewer sends a version back. `P-20`: "Trả về kèm lý do".</summary>
    private static async Task<IResult> ReturnToDraftEndpoint(
        string examVersionId, ReturnExamToDraftRequest request, ClaimsPrincipal principal,
        IExamCatalogue catalogue, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamReview) is { } denial) return denial;

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Conflict("Trả về đề cần kèm lý do.");

        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (version.Status != ExamVersionStatus.InReview)
            return Conflict("Chỉ version đang chờ duyệt mới trả về được.");

        var now = clock.UtcNow;
        version.ReturnToDraft(request.Reason);
        await catalogue.SetStatusAsync(version.Id, version.Status, ct);

        await Record(audit, principal, AuditAction.ExamReturnedToDraft, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", now, ct,
            new Dictionary<string, string> { ["reason"] = request.Reason });

        return Results.Ok(new { status = version.Status.ToString().ToLowerInvariant() });
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
            "user", user.Id.Value, Label(user), clock.UtcNow, ct);

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
            "user", user.Id.Value, Label(user), clock.UtcNow, ct,
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
                ActorLabel(principal),
                action, targetType, targetId, targetLabel, now, detail),
            ct);

    /// <summary>
    /// Sets another account's password.
    ///
    /// <para>
    /// <b>Every session of the target ends.</b> Whatever the learner could not
    /// sign in to, somebody else may well be signed in to — a shared machine,
    /// a phone that was handed on, or the compromise that caused the call in
    /// the first place. Leaving those alive would make the reset theatre.
    /// </para>
    ///
    /// <para>
    /// <b>Revoked after the write, not before</b>, for the same reason
    /// suspension is: a revocation that succeeds against an account whose
    /// password write then fails leaves the person signed out of everything
    /// and still unable to get back in.
    /// </para>
    ///
    /// <para>
    /// <b>An operator cannot use this on themselves.</b> Not because it would
    /// be dangerous, but because it would be a worse version of the
    /// self-service change they already have — and it would write an audit row
    /// claiming an account takeover that did not happen.
    /// </para>
    /// </summary>
    private static async Task<IResult> ResetUserPasswordEndpoint(
        string userId,
        ClaimsPrincipal principal,
        ResetUserPasswordRequest request,
        IUserRepository users,
        IUserIdentityRepository identities,
        IPasswordHasher hasher,
        ITokenService tokens,
        IAuditLog audit,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserResetPassword) is { } denial) return denial;

        if (principal.UserId() == userId)
        {
            return Conflict(
                "Không thể tự đặt lại mật khẩu của chính mình ở đây. "
                + "Hãy đổi mật khẩu trong trang hồ sơ.");
        }

        var password = PasswordPolicy.Validate(request.NewPassword);
        if (!password.IsSuccess) return ApiProblem.From(password.Error, http);

        var user = await users.FindByIdAsync(new UserId(userId), ct);
        if (user is null) return Results.NotFound();

        await PasswordIdentity.SetAsync(identities, hasher, clock, user, password.Value!, ct);

        await tokens.RevokeAllForUserAsync(user.Id, ct);

        // The row records that it happened and to whom. It carries no password
        // and no hash, and never will — this is the log that has to survive
        // being read by everyone who can read logs.
        await Record(
            audit, principal, AuditAction.UserPasswordReset,
            "user", user.Id.Value, Label(user), clock.UtcNow, ct);

        return Results.NoContent();
    }

    /// <summary>
    /// A stable name for whoever performed an act.
    ///
    /// <para>
    /// <b>The display name is the last resort, not the second.</b> An operator
    /// may now have a phone number and no address, and falling straight to the
    /// display name would put a field its owner can edit into the record of
    /// what they did — which is exactly the record that must not be rewritable
    /// by the person it names. The account id is worse to read but cannot be
    /// changed at all, so it sits underneath both handles rather than under
    /// none. → threat `T21`
    /// </para>
    /// </summary>
    private static string ActorLabel(ClaimsPrincipal principal) =>
        principal.Email()
        ?? principal.Phone()
        ?? principal.UserId()
        ?? principal.DisplayName();

    /// <summary>
    /// How an account is named in an audit row. Same ordering, same reason.
    /// </summary>
    private static string Label(Domain.Identity.User user) =>
        user.Email?.Value ?? user.Phone?.Value ?? user.Id.Value;

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

    /// <summary>
    /// The wire spelling of an environment. Kebab-case, matching the three
    /// names the plan uses — <c>fixture</c>, <c>internal-review</c>,
    /// <c>learner-production</c> — rather than the CLR member name.
    /// </summary>
    private static string Describe(ContentEnvironment environment) => environment switch
    {
        ContentEnvironment.Fixture => "fixture",
        ContentEnvironment.InternalReview => "internal-review",
        ContentEnvironment.LearnerProduction => "learner-production",
        _ => environment.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Why a publish was refused, for an operator to act on.
    ///
    /// Carried in a <c>reason</c> extension beside a single stable
    /// <c>code</c>: the caller is a named operator who needs to know whether to
    /// register the source, renew a licence or record a reviewer, but a client
    /// branching on the distinction would be branching on a policy `M-53` has
    /// not settled.
    /// </summary>
    private static string Describe(ContentRightsDenial? denial) => denial switch
    {
        ContentRightsDenial.NoRegistryEntry => "no-registry-entry",
        ContentRightsDenial.EnvironmentNotGranted => "environment-not-granted",
        ContentRightsDenial.RightExpired => "right-expired",
        ContentRightsDenial.ProofMissing => "proof-missing",
        _ => "unknown",
    };

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
    /// say so rather than to look broken.
    /// </summary>
    private static IResult? Denied(ClaimsPrincipal principal, string permission)
    {
        if (principal.UserId() is null) return Results.Unauthorized();

        if (principal.Permissions().Contains(permission)) return null;

        return Results.Problem(
            detail: $"This account does not hold {permission}.",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ErrorCodes.PermissionDenied,
                ["permission"] = permission,
            });
    }
}
