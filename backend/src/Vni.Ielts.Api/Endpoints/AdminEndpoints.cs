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
using Vni.Ielts.Infrastructure.Content;

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

public sealed record RegisterContentSourceFileRequest(
    string Path, string? Sha256 = null, long? SizeBytes = null);

public sealed record RegisterContentSourceProofRequest(
    string Reference, string Reviewer, DateTimeOffset ReviewedAt);

public sealed record RegisterContentSourceRequest(
    string SourceId,
    string Title,
    string RootPath,
    IReadOnlyList<string> AllowedEnvironments,
    string? Owner = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyList<RegisterContentSourceFileRequest>? Files = null,
    RegisterContentSourceProofRequest? Proof = null);

public sealed record CreateExamRequest(string Title, string Variant);

public static partial class AdminEndpoints
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

        group.MapPost("/exams", CreateExamEndpoint)
            .WithName("AdminCreateExam")
            .WithSummary("Start a blank draft exam from the CMS");

        group.MapGet("/exams/{examVersionId}", GetExamEndpoint)
            .WithName("AdminGetExam")
            .WithSummary("One exam version for the CMS authoring workspace");

        group.MapGet("/exams/{examVersionId}/preview", ExamPreviewEndpoint)
            .WithName("AdminExamPreview")
            .WithSummary("Reviewer preview of content and answer keys");

        group.MapGet("/exams/{examVersionId}/content", ExamContentEndpoint)
            .WithName("AdminGetExamContent")
            .WithSummary("Authoring document for the question builder");

        group.MapPut("/exams/{examVersionId}/content", SaveExamContentEndpoint)
            .WithName("AdminSaveExamContent")
            .WithSummary("Replace a draft's content through the same schema gate as ZIP import");

        group.MapPost("/exams/{examVersionId}/validate", ValidateExamContentEndpoint)
            .WithName("AdminValidateExamContent")
            .WithSummary("Read-only schema checklist for the authoring workspace");

        group.MapDelete("/exams/{examVersionId}", DeleteExamEndpoint)
            .WithName("AdminDeleteExam")
            .WithSummary("Permanently remove a draft exam version");

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

        group.MapPost("/content-sources", RegisterContentSourceEndpoint)
            .WithName("AdminRegisterContentSource")
            .WithSummary("Register a content-source rights grant — never overwrites an existing id");

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

        MapUserAdministration(group);
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
        if (principal.UserId() is null) return Results.Unauthorized();

        var perms = principal.Permissions();
        var any = perms.Contains(PermissionKeys.ExamReadAny);
        var own = perms.Contains(PermissionKeys.ExamReadOwn);
        if (!any && !own)
            return Denied(principal, PermissionKeys.ExamReadOwn)!;

        var versions = await catalogue.ListAllAsync(ct);
        var userId = principal.UserId();
        var visible = any
            ? versions
            : versions.Where(v => v.AuthorId is { } a && a.Value == userId).ToList();

        return Results.Ok(new
        {
            exams = visible.Select(v => new
            {
                examVersionId = v.Id.Value,
                definitionId = v.DefinitionId.Value,
                versionNumber = v.VersionNumber,
                title = v.Title,
                variant = v.Variant.ToString().ToLowerInvariant(),
                status = v.Status.ToString().ToLowerInvariant(),
                publishedAt = v.PublishedAt,
                authorId = v.AuthorId?.Value,
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

    private static async Task<IResult> CreateExamEndpoint(
        ClaimsPrincipal principal, CreateExamRequest request, IExamCatalogue catalogue,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamCreate) is { } denial) return denial;

        if (string.IsNullOrWhiteSpace(request.Title))
            return Problem(ErrorCodes.ValidationFailed, "A title is required.", StatusCodes.Status400BadRequest);

        var variant = request.Variant == "general" ? ExamVariant.General : ExamVariant.Academic;
        var version = ExamVersion.CreateBlankDraft(
            ExamDefinitionId.New(), versionNumber: 1, request.Title.Trim(), variant,
            new UserId(principal.UserId()!));

        await catalogue.UpsertAsync(version, ct);

        var now = clock.UtcNow;
        await Record(audit, principal, AuditAction.ExamCreated, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", now, ct);

        return Results.Created($"/api/v1/admin/exams/{version.Id.Value}", new
        {
            examVersionId = version.Id.Value,
            definitionId = version.DefinitionId.Value,
            versionNumber = version.VersionNumber,
            status = version.Status.ToString().ToLowerInvariant(),
            authorId = version.AuthorId?.Value,
        });
    }

    private static async Task<IResult> GetExamEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue, CancellationToken ct)
    {
        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (DeniedRead(principal, version.AuthorId) is { } denial) return denial;

        return Results.Ok(new
        {
            examVersionId = version.Id.Value,
            definitionId = version.DefinitionId.Value,
            versionNumber = version.VersionNumber,
            title = version.Title,
            variant = version.Variant.ToString().ToLowerInvariant(),
            status = version.Status.ToString().ToLowerInvariant(),
            publishedAt = version.PublishedAt,
            authorId = version.AuthorId?.Value,
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

    private static async Task<IResult> ExamPreviewEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue, CancellationToken ct)
    {
        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (DeniedRead(principal, version.AuthorId) is { } denial) return denial;
        if (Denied(principal, PermissionKeys.ExamPreview) is { } previewDenial) return previewDenial;

        return Results.Ok(new
        {
            examVersionId = version.Id.Value,
            title = version.Title,
            sections = version.Sections
                .OrderBy(s => s.Order)
                .Select(s => new
                {
                    module = s.Module.ToString().ToLowerInvariant(),
                    parts = s.Parts
                        .OrderBy(p => p.Order)
                        .Select(p => new
                        {
                            title = p.Title,
                            body = p.Body,
                            transcript = p.Transcript,
                            cueCard = p.CueCard == null
                                ? null
                                : new { topic = p.CueCard.Topic, bullets = p.CueCard.Bullets },
                            questions = p.Questions
                                .OrderBy(q => q.Order)
                                .Select(q => new
                                {
                                    id = q.Id,
                                    order = q.Order,
                                    type = q.Type.ToString().ToLowerInvariant(),
                                    prompt = q.Prompt,
                                    options = q.Options.Select(o => new { key = o.Key, text = o.Text }),
                                    answerKey = q.AnswerKey == null
                                        ? null
                                        : q.AnswerKey.Accepted.Select(a => new
                                        {
                                            single = a.Single,
                                            all = a.All,
                                            pairLeft = a.Pair?.Left,
                                            pairRight = a.Pair?.Right,
                                        }),
                                }),
                        }),
                }),
        });
    }

    private static async Task<IResult> ExamContentEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue, CancellationToken ct)
    {
        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (DeniedRead(principal, version.AuthorId) is { } denial) return denial;

        return Results.Ok(ExamContentSerializer.ToNode(version));
    }

    private static async Task<IResult> SaveExamContentEndpoint(
        string examVersionId, ClaimsPrincipal principal, HttpRequest request,
        IExamCatalogue catalogue, ExamPackageReader reader, IAuditLog audit, IClock clock,
        CancellationToken ct)
    {
        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (DeniedOwn(principal, PermissionKeys.ExamUpdateOwn, version.AuthorId) is { } denial) return denial;

        if (version.Status != ExamVersionStatus.Draft)
            return Conflict("Chỉ bản nháp mới sửa được.");

        using var bodyReader = new StreamReader(request.Body);
        var json = await bodyReader.ReadToEndAsync(ct);

        var result = reader.Read(json, version.DefinitionId, version.VersionNumber);
        if (!result.IsValid || result.Version is null)
            return Results.Ok(new { valid = false, findings = ToWireFindings(result.Findings) });

        version.ReplaceContent(result.Version.Sections, result.Version.Scoring, result.Version.Timing);
        await catalogue.UpsertAsync(version, ct);

        var now = clock.UtcNow;
        await Record(audit, principal, AuditAction.ExamContentSaved, "exam-version",
            version.Id.Value, $"{version.Title} v{version.VersionNumber}", now, ct);

        return Results.Ok(new
        {
            valid = true,
            status = version.Status.ToString().ToLowerInvariant(),
            findings = Array.Empty<object>(),
        });
    }

    private static async Task<IResult> ValidateExamContentEndpoint(
        string examVersionId, ClaimsPrincipal principal, HttpRequest request,
        IExamCatalogue catalogue, ExamPackageReader reader, CancellationToken ct)
    {
        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        if (DeniedRead(principal, version.AuthorId) is { } denial) return denial;

        using var bodyReader = new StreamReader(request.Body);
        var json = await bodyReader.ReadToEndAsync(ct);

        var result = reader.Read(json, version.DefinitionId, version.VersionNumber);

        return Results.Ok(new { valid = result.IsValid, findings = ToWireFindings(result.Findings) });
    }

    private static IEnumerable<object> ToWireFindings(IReadOnlyList<ValidationFinding> findings) =>
        findings.Select(f => new
        {
            stage = "schema",
            code = f.Code,
            pointer = f.Path,
            message = f.Message,
        });

    /// <summary>
    /// Draft-only hard delete. Ownership-scoped; missing or foreign drafts
    /// look like 404 rather than 403 so existence is not disclosed.
    /// </summary>
    private static async Task<IResult> DeleteExamEndpoint(
        string examVersionId, ClaimsPrincipal principal, IExamCatalogue catalogue,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId), ct);
        if (version is null) return Results.NotFound();

        // Prefer ExamDeleteOwn for authors; ExamDeleteAny covers admin override.
        var perms = principal.Permissions();
        var owns = version.AuthorId is { } a && principal.UserId() == a.Value;
        var allowed = (perms.Contains(PermissionKeys.ExamDeleteOwn) && owns)
            || perms.Contains(PermissionKeys.ExamDeleteAny);
        if (!allowed) return Results.NotFound();

        if (version.Status != ExamVersionStatus.Draft)
            return Conflict("Chỉ bản nháp mới xoá được.");

        var label = $"{version.Title} v{version.VersionNumber}";
        await catalogue.DeleteAsync(version.Id, ct);

        await Record(audit, principal, AuditAction.ExamDeleted, "exam-version",
            examVersionId, label, clock.UtcNow, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> UsersEndpoint(
        ClaimsPrincipal principal, IUserRepository users, IRoleRepository roles,
        string? search, string? role, string? status, string? hasEmail, int? page,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserRead) is { } denial) return denial;

        const int PageSize = 25;
        var current = Math.Clamp(page ?? 1, 1, MaxPage);

        RoleId? roleId = null;
        if (!string.IsNullOrWhiteSpace(role))
        {
            var slug = role.Trim();
            if (slug is not (SystemRoles.ExamAuthor or SystemRoles.AcademicLead or SystemRoles.Admin
                or SystemRoles.Learner))
            {
                return Problem(ErrorCodes.ValidationFailed, "role must be a known role slug.",
                    StatusCodes.Status400BadRequest);
            }

            var found = await roles.FindByNameAsync(slug, ct);
            if (found is null)
                return Problem(ErrorCodes.ValidationFailed, "role must be a known role slug.",
                    StatusCodes.Status400BadRequest);
            roleId = found.Id;
        }

        UserStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            parsedStatus = status.Trim().ToLowerInvariant() switch
            {
                "active" => UserStatus.Active,
                "suspended" => UserStatus.Suspended,
                _ => null,
            };
            if (parsedStatus is null)
                return Problem(ErrorCodes.ValidationFailed, "status must be active or suspended.",
                    StatusCodes.Status400BadRequest);
        }

        // hasEmail replaces feature emailVerified — main has no verification flag (ADR-0018).
        bool? parsedHasEmail = null;
        if (!string.IsNullOrWhiteSpace(hasEmail))
        {
            if (!bool.TryParse(hasEmail, out var parsed))
                return Problem(ErrorCodes.ValidationFailed, "hasEmail must be true or false.",
                    StatusCodes.Status400BadRequest);
            parsedHasEmail = parsed;
        }

        var (page1, total) = await users.ListAsync(
            new UserListQuery(search, roleId, parsedStatus, parsedHasEmail, (current - 1) * PageSize, PageSize),
            ct);

        return Results.Ok(new
        {
            total,
            page = current,
            pageSize = PageSize,
            users = page1.Select(u => new
            {
                userId = u.Id.Value,
                displayName = u.DisplayName,
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

    /// <summary>
    /// Insert-only registration of a content-source rights grant.
    ///
    /// A duplicate <c>sourceId</c> is a conflict, not an update. Change rights by
    /// minting a new id. Learner-production proof is required the same way the
    /// seed refuses an unproven grant.
    /// </summary>
    private static async Task<IResult> RegisterContentSourceEndpoint(
        RegisterContentSourceRequest request, ClaimsPrincipal principal,
        IContentRightsRegistry registry, IAuditLog audit, IClock clock,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ContentRightsManage) is { } denial) return denial;

        if (string.IsNullOrWhiteSpace(request.SourceId))
            return Problem(ErrorCodes.ValidationFailed, "sourceId is required.", StatusCodes.Status400BadRequest);

        if (string.IsNullOrWhiteSpace(request.Title))
            return Problem(ErrorCodes.ValidationFailed, "title is required.", StatusCodes.Status400BadRequest);

        if (string.IsNullOrWhiteSpace(request.RootPath))
            return Problem(ErrorCodes.ValidationFailed, "rootPath is required.", StatusCodes.Status400BadRequest);

        ContentSourceId sourceId;
        try
        {
            sourceId = new ContentSourceId(request.SourceId.Trim());
        }
        catch (ArgumentException e)
        {
            return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status400BadRequest);
        }

        var environments = new List<ContentEnvironment>();
        foreach (var raw in request.AllowedEnvironments ?? [])
        {
            if (!TryParseEnvironment(raw, out var environment))
            {
                return Problem(
                    ErrorCodes.ValidationFailed,
                    $"Unknown environment '{raw}'. Use fixture, internal-review, or learner-production.",
                    StatusCodes.Status400BadRequest);
            }

            environments.Add(environment);
        }

        if (environments.Count == 0)
        {
            return Problem(
                ErrorCodes.ValidationFailed,
                "allowedEnvironments must include at least one environment.",
                StatusCodes.Status400BadRequest);
        }

        RightsProof? proof = null;
        if (request.Proof is { } wireProof)
        {
            try
            {
                proof = new RightsProof(wireProof.Reference, wireProof.Reviewer, wireProof.ReviewedAt);
            }
            catch (ArgumentException e)
            {
                return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status400BadRequest);
            }
        }

        if (environments.Contains(ContentEnvironment.LearnerProduction) && proof is null)
        {
            return Problem(
                ErrorCodes.ValidationFailed,
                $"Learner-production for '{sourceId}' requires a rights proof "
                + "(reference, reviewer, reviewedAt).",
                StatusCodes.Status400BadRequest);
        }

        IReadOnlyList<ContentFileRef> files;
        try
        {
            files = (request.Files ?? [])
                .Select(f => new ContentFileRef(f.Path, f.Sha256, f.SizeBytes))
                .ToArray();
        }
        catch (ArgumentException e)
        {
            return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status400BadRequest);
        }

        ContentSource source;
        try
        {
            source = ContentSource.Register(
                sourceId,
                request.Title.Trim(),
                string.IsNullOrWhiteSpace(request.Owner) ? null : request.Owner.Trim(),
                proof,
                environments,
                request.ExpiresAt,
                request.RootPath.Trim(),
                files,
                boundExamVersionIds: [],
                boundExamDefinitionIds: []);
        }
        catch (UnprovenPublishRightException)
        {
            return Problem(
                ErrorCodes.ValidationFailed,
                $"Learner-production for '{sourceId}' requires a rights proof "
                + "(reference, reviewer, reviewedAt).",
                StatusCodes.Status400BadRequest);
        }
        catch (ArgumentException e)
        {
            return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status400BadRequest);
        }

        if (!await registry.RegisterIfAbsentAsync(source, ct))
        {
            return Conflict(
                "Nguồn này đã đăng ký, không ghi đè được — muốn đổi quyền, tạo sourceId mới.",
                ErrorCodes.ValidationFailed);
        }

        var now = clock.UtcNow;
        await Record(
            audit, principal, AuditAction.ContentRightsRegistered,
            "content-source", source.Id.Value, source.Title, now, ct,
            new Dictionary<string, string>
            {
                ["sourceId"] = source.Id.Value,
                ["environments"] = string.Join(",", source.AllowedEnvironments.Select(Describe)),
                ["mayReachLearners"] = ContentRightsPolicy
                    .Evaluate(source, ContentEnvironment.LearnerProduction, now).Allowed
                    ? "true"
                    : "false",
            });

        return Results.Created($"/api/v1/admin/content-sources", new
        {
            sourceId = source.Id.Value,
            title = source.Title,
            owner = source.Owner,
            rootPath = source.RootPath,
            allowedEnvironments = source.AllowedEnvironments.Select(Describe).Order().ToArray(),
            expiresAt = source.ExpiresAt,
            licenceReference = source.Proof?.Reference,
            reviewer = source.Proof?.Reviewer,
            reviewedAt = source.Proof?.ReviewedAt,
            mayReachLearners = ContentRightsPolicy
                .Evaluate(source, ContentEnvironment.LearnerProduction, now).Allowed,
            fileCount = source.Files.Count,
            hashedFileCount = source.Files.Count(f => f.Sha256 is not null),
            examDefinitionIds = source.BoundExamDefinitionIds,
            examVersionIds = source.BoundExamVersionIds,
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
        string userId, ClaimsPrincipal principal, IProtectedAdminMutation protectedAdmin,
        ITokenService tokens, IAuditLog audit, IClock clock, CancellationToken ct) =>
        ChangeStatusSuspend(userId, principal, protectedAdmin, tokens, audit, clock, ct);

    private static Task<IResult> ReinstateEndpoint(
        string userId, ClaimsPrincipal principal, IUserRepository users,
        IAuditLog audit, IClock clock, CancellationToken ct) =>
        ChangeStatusReinstate(userId, principal, users, audit, clock, ct);

    /// <summary>
    /// Suspend goes through <see cref="IProtectedAdminMutation"/> so the
    /// last-admin check and user write share one cross-instance transaction.
    /// Reinstate has no last-admin risk and keeps the plain save.
    /// </summary>
    private static async Task<IResult> ChangeStatusSuspend(
        string userId, ClaimsPrincipal principal, IProtectedAdminMutation protectedAdmin,
        ITokenService tokens, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserSuspend) is { } denial) return denial;

        if (principal.UserId() == userId)
            return Conflict("Không thể tự khoá tài khoản của chính mình.");

        var mutation = await protectedAdmin.TrySuspendAsync(new UserId(userId), ct);
        return mutation.Outcome switch
        {
            ProtectedAdminMutationOutcome.NotFound => Results.NotFound(),
            ProtectedAdminMutationOutcome.WouldLeaveZeroActiveAdmins => Conflict(
                "Không thể khoá quản trị viên cuối cùng đang hoạt động.", ErrorCodes.LastAdminProtected),
            ProtectedAdminMutationOutcome.AlreadyApplied or ProtectedAdminMutationOutcome.Applied =>
                await FinishSuspendAsync(mutation.User!, principal, tokens, audit, clock, ct),
            _ => throw new InvalidOperationException($"Unexpected outcome {mutation.Outcome}."),
        };
    }

    private static async Task<IResult> FinishSuspendAsync(
        Domain.Identity.User user, ClaimsPrincipal principal, ITokenService tokens,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        await tokens.RevokeAllForUserAsync(user.Id, ct);
        await Record(
            audit, principal, AuditAction.UserSuspended,
            "user", user.Id.Value, Label(user), clock.UtcNow, ct);
        return Results.Ok(new { status = user.Status.ToString().ToLowerInvariant() });
    }

    private static async Task<IResult> ChangeStatusReinstate(
        string userId, ClaimsPrincipal principal, IUserRepository users,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.UserSuspend) is { } denial) return denial;

        var user = await users.FindByIdAsync(new UserId(userId), ct);
        if (user is null) return Results.NotFound();

        user.Reinstate();
        await users.SaveAsync(user, ct);

        await Record(
            audit, principal, AuditAction.UserReinstated,
            "user", user.Id.Value, Label(user), clock.UtcNow, ct);

        return Results.Ok(new { status = user.Status.ToString().ToLowerInvariant() });
    }

    private static async Task<IResult> AssignRoleEndpoint(
        string userId, ClaimsPrincipal principal, AssignRoleRequest request,
        IUserRepository users, IRoleRepository roles, IProtectedAdminMutation protectedAdmin,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.RoleAssign) is { } denial) return denial;

        var user = await users.FindByIdAsync(new UserId(userId), ct);
        if (user is null) return Results.NotFound();

        var role = await roles.FindByIdAsync(new RoleId(request.RoleId), ct);
        if (role is null) return Results.NotFound();

        if (principal.UserId() == userId && !request.Grant && role.Name == SystemRoles.Admin)
            return Conflict("Không thể tự gỡ vai admin của chính mình.");

        if (!request.Grant && role.Name == SystemRoles.Admin)
        {
            var mutation = await protectedAdmin.TryRevokeAdminRoleAsync(new UserId(userId), ct);
            return mutation.Outcome switch
            {
                ProtectedAdminMutationOutcome.NotFound => Results.NotFound(),
                ProtectedAdminMutationOutcome.WouldLeaveZeroActiveAdmins => Conflict(
                    "Không thể gỡ vai admin của quản trị viên cuối cùng đang hoạt động.",
                    ErrorCodes.LastAdminProtected),
                ProtectedAdminMutationOutcome.AlreadyApplied or ProtectedAdminMutationOutcome.Applied =>
                    await FinishRoleChangeAsync(mutation.User!, principal, role.Name, grant: false, audit, clock, ct),
                _ => throw new InvalidOperationException($"Unexpected outcome {mutation.Outcome}."),
            };
        }

        if (request.Grant) user.AssignRole(role.Id);
        else user.RemoveRole(role.Id);

        await users.SaveAsync(user, ct);
        return await FinishRoleChangeAsync(user, principal, role.Name, request.Grant, audit, clock, ct);
    }

    private static async Task<IResult> FinishRoleChangeAsync(
        Domain.Identity.User user, ClaimsPrincipal principal, string roleName, bool grant,
        IAuditLog audit, IClock clock, CancellationToken ct)
    {
        await Record(
            audit, principal,
            grant ? AuditAction.RoleAssigned : AuditAction.RoleRemoved,
            "user", user.Id.Value, Label(user), clock.UtcNow, ct,
            new Dictionary<string, string> { ["role"] = roleName });

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

    private static bool TryParseEnvironment(string raw, out ContentEnvironment environment)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "fixture":
                environment = ContentEnvironment.Fixture;
                return true;
            case "internal-review":
            case "internalreview":
                environment = ContentEnvironment.InternalReview;
                return true;
            case "learner-production":
            case "learnerproduction":
                environment = ContentEnvironment.LearnerProduction;
                return true;
            default:
                environment = default;
                return false;
        }
    }

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
        Conflict(detail, ErrorCodes.ValidationFailed);

    private static IResult Conflict(string detail, string code) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static IResult Problem(string code, string detail, int status) =>
        Results.Problem(
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static IResult FromError(Error error) => error.Kind switch
    {
        ErrorKind.NotFound => Results.NotFound(new { code = error.Code, message = error.Detail }),
        ErrorKind.Forbidden or ErrorKind.Unauthorized => Results.Problem(
            detail: error.Detail,
            statusCode: error.Kind == ErrorKind.Unauthorized
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code }),
        ErrorKind.Conflict => Results.Problem(
            detail: error.Detail,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code }),
        ErrorKind.Validation => Results.Problem(
            detail: error.Detail,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code }),
        _ => Results.Problem(
            detail: error.Detail,
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code }),
    };

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

    /// <summary>
    /// Ownership-scoped counterpart to <see cref="Denied"/> for exam updates.
    /// <c>exam.update.any</c> is sufficient on its own (admin override); it must
    /// not require <c>exam.update.own</c>. Holding only <c>.own</c> requires the
    /// caller to be the author. Do not fall through to <see cref="Denied"/> —
    /// a holder of <c>.own</c> who is not the author would otherwise be allowed.
    /// </summary>
    private static IResult? DeniedOwn(ClaimsPrincipal principal, string permission, UserId? authorId)
    {
        if (principal.UserId() is null) return Results.Unauthorized();

        var perms = principal.Permissions();
        var allowed = perms.Contains(PermissionKeys.ExamUpdateAny)
            || (perms.Contains(permission)
                && authorId is not null
                && principal.UserId() == authorId.Value.Value);

        return allowed ? null : Forbidden(permission);
    }

    private static IResult? DeniedRead(ClaimsPrincipal principal, UserId? authorId)
    {
        if (principal.UserId() is null) return Results.Unauthorized();

        var perms = principal.Permissions();
        if (perms.Contains(PermissionKeys.ExamReadAny)) return null;
        if (perms.Contains(PermissionKeys.ExamReadOwn)
            && authorId is not null && principal.UserId() == authorId.Value.Value)
            return null;

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
