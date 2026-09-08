using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Importing;
using PackageFinding = Vni.Ielts.Application.Importing.PackageFinding;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure.Content.Import;

namespace Vni.Ielts.Api.Endpoints;

/// <summary>
/// The exam-import front door (<c>S6b</c>, <c>P-18</c>/<c>P-19</c>) — the HTTP
/// door onto an import engine that already existed and had no caller. See
/// <see cref="ExamPackageImportPipeline"/> for the ZIP-to-draft sequence and
/// <see cref="ImportReviewWorkflow"/> for everything past that.
///
/// <b>A batch endpoint is deliberately not here.</b> <c>ImportBatchRunner</c>
/// and its Mongo-backed checkpoint store are built and tested; wiring a batch
/// HTTP surface on top is left for a later slice so the single-package path —
/// the one the CMS's <c>ImportPage</c> is already waiting on — lands complete
/// rather than both landing half-done.
/// </summary>
public sealed record OverrideWarningRequest(string Reason);

public sealed record SetChecklistRequest(IReadOnlyList<string> Confirmed);

public sealed record ImportFindingView(string Severity, string Code, string Path, string Message);

public sealed record ImportWarningView(
    string Id, string Category, string Path, string Message, bool Resolved, string? OverrideReason);

public sealed record ImportDraftView(
    string DraftId,
    string DefinitionId,
    int VersionNumber,
    string Route,
    string ApprovalState,
    int Revision,
    string? ReviewedBy,
    IReadOnlyList<string> PresentSkills,
    IReadOnlyList<ImportFindingView> Findings,
    IReadOnlyList<ImportWarningView> Warnings,
    IReadOnlyList<string> ChecklistConfirmed,
    bool ChecklistComplete);

public sealed record ImportRejectionView(bool IsAccepted, IReadOnlyList<ImportFindingView> Findings);

public static class AdminImportEndpoints
{
    /// <summary>
    /// Kestrel's default body cap (1 MB) refuses every real exam package
    /// before application code runs — the same failure `ExamEndpoints`'
    /// Speaking upload documents at length. Raised only for this route, and
    /// only up to the configured archive cap plus a small multipart envelope
    /// allowance, so the endpoint's own findings (not an opaque connection
    /// reset) are what a caller seeing an oversized package actually gets.
    /// </summary>
    private const long MultipartOverheadBytes = 64L * 1024;

    public static void MapAdminImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/import")
            .WithTags("Admin")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.InSessionRead);

        group.MapPost("/packages", UploadPackageEndpoint)
            .WithName("AdminImportPackage")
            .WithSummary("Upload one exam package ZIP; validates and creates a review draft synchronously")
            .DisableAntiforgery();

        group.MapGet("/packages/{draftId}", GetDraftEndpoint)
            .WithName("AdminGetImportDraft")
            .WithSummary("Status and findings for one import draft");

        group.MapPost("/packages/{draftId}/warnings/{warningId}/override", OverrideWarningEndpoint)
            .WithName("AdminOverrideImportWarning")
            .WithSummary("Resolve a warning with a mandatory reason; audited as WarningOverridden");

        group.MapPost("/packages/{draftId}/checklist", SetChecklistEndpoint)
            .WithName("AdminSetImportChecklist")
            .WithSummary("Confirm which review-checklist categories this draft has cleared");

        group.MapPost("/packages/{draftId}/approve", ApproveEndpoint)
            .WithName("AdminApproveImportDraft")
            .WithSummary("Approve a draft once every warning is resolved and the checklist is complete");
    }

    /// <summary>
    /// <c>multipart/form-data</c>: a <c>file</c> part (the ZIP) and two
    /// optional text fields, <c>definitionId</c> and <c>versionNumber</c>
    /// (defaults: a fresh definition id, version 1 — the "new exam" case,
    /// which is what an operator uploading a package for the first time
    /// almost always means).
    ///
    /// <b>Synchronous, by design.</b> The brief is explicit that a background
    /// job queue is out of scope here — the archive caps already bound how
    /// large a single package can be (S6a), so the request completes inside
    /// an ordinary HTTP timeout.
    /// </summary>
    private static async Task<IResult> UploadPackageEndpoint(
        HttpRequest request, ClaimsPrincipal principal, ExamPackageImportPipeline pipeline,
        IOptions<ImportArchiveOptions> archiveOptions, HttpContext http, CancellationToken ct)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        if (Denied(principal, PermissionKeys.PackageUpload) is { } denial) return denial;

        if (!request.HasFormContentType)
            return Problem(ErrorCodes.ValidationFailed, "Expected a multipart upload.", 400, http);

        // Raised before ReadFormAsync reads the body — the feature is
        // read-only once reading starts. See ExamEndpoints.UploadRecordingEndpoint
        // for the identical reasoning and the same "not covered under
        // TestServer" caveat.
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } cap)
            cap.MaxRequestBodySize = archiveOptions.Value.MaxArchiveBytes + MultipartOverheadBytes;

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            return Problem(ErrorCodes.ValidationFailed, "A package needs a non-empty 'file' part.", 400, http);

        var definitionId = form["definitionId"].ToString() is { Length: > 0 } supplied
            ? new ExamDefinitionId(supplied)
            : ExamDefinitionId.New();
        var versionNumber = int.TryParse(form["versionNumber"], out var parsed) && parsed > 0 ? parsed : 1;

        await using var uploadStream = file.OpenReadStream();
        var seekable = uploadStream;
        FileStream? spooled = null;

        try
        {
            if (!seekable.CanSeek)
            {
                // Belt and braces: buffered form parts are seekable in every
                // configuration this deployment runs today, but a caller must
                // never get a wrong read of a truncated archive if that ever
                // stops being true.
                spooled = new FileStream(
                    Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                    81_920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
                await uploadStream.CopyToAsync(spooled, ct);
                spooled.Position = 0;
                seekable = spooled;
            }

            var attempt = await pipeline.ImportAsync(seekable, definitionId, versionNumber, ct);

            if (!attempt.IsAccepted || attempt.Draft is null)
                return Rejected(attempt.Findings, http);

            return Results.Created(
                $"/api/v1/admin/import/packages/{attempt.Draft.Id:D}", ToView(attempt.Draft));
        }
        finally
        {
            if (spooled is not null) await spooled.DisposeAsync();
        }
    }

    private static async Task<IResult> GetDraftEndpoint(
        string draftId, ClaimsPrincipal principal, IImportDraftStore drafts, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.PackageRead) is { } denial) return denial;
        if (!Guid.TryParse(draftId, out var id)) return Results.NotFound();

        var draft = await drafts.FindAsync(id, ct);
        return draft is null ? Results.NotFound() : Results.Ok(ToView(draft));
    }

    /// <summary>
    /// <c>P-19</c>: "admin bỏ qua được nhưng bắt buộc ghi lý do và vào nhật ký".
    /// The blank-reason check here is a fast, friendly 409 before touching the
    /// workflow; <see cref="ImportReviewWorkflow.ResolveWarningAsync"/> makes
    /// the same check its own authoritative gate, so this rule holds for any
    /// future caller of that method too, not only this endpoint.
    /// </summary>
    private static async Task<IResult> OverrideWarningEndpoint(
        string draftId, string warningId, OverrideWarningRequest request, ClaimsPrincipal principal,
        IImportDraftStore drafts, ImportReviewWorkflow review, IAuditLog audit, IClock clock,
        HttpContext http, CancellationToken ct)
    {
        if (principal.UserId() is not { } actorId) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Conflict("Bỏ qua cảnh báo cần kèm lý do.", http);
        if (!Guid.TryParse(draftId, out var id)) return Results.NotFound();

        var current = await drafts.FindAsync(id, ct);
        if (current is null) return Results.NotFound();

        var actor = BuildActor(principal);
        var result = await review.ResolveWarningAsync(id, current.Revision, warningId, request.Reason, actor, ct);
        if (!result.IsSuccess) return RefusedResult(result, http);

        var draft = result.Draft!;
        var warning = draft.Warnings.First(w => w.Id == warningId);

        await audit.AppendAsync(
            AuditEntry.Record(
                new UserId(actorId), principal.Email() ?? principal.DisplayName(),
                AuditAction.WarningOverridden, "import-draft", draftId,
                $"{draft.DefinitionId.Value} v{draft.VersionNumber}",
                clock.UtcNow,
                new Dictionary<string, string>
                {
                    ["warningId"] = warningId,
                    ["category"] = warning.Category.ToString(),
                    ["reason"] = request.Reason,
                }),
            ct);

        return Results.Ok(ToView(draft));
    }

    private static async Task<IResult> SetChecklistEndpoint(
        string draftId, SetChecklistRequest request, ClaimsPrincipal principal,
        IImportDraftStore drafts, ImportReviewWorkflow review, HttpContext http, CancellationToken ct)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        if (!Guid.TryParse(draftId, out var id)) return Results.NotFound();

        var parsed = new HashSet<ImportReviewCategory>();
        foreach (var name in request.Confirmed ?? [])
        {
            if (!Enum.TryParse<ImportReviewCategory>(name, ignoreCase: true, out var category)
                || !Enum.IsDefined(category))
            {
                return Problem(
                    ErrorCodes.ValidationFailed,
                    $"Unknown checklist category '{name}'.",
                    400,
                    http);
            }
            parsed.Add(category);
        }

        var current = await drafts.FindAsync(id, ct);
        if (current is null) return Results.NotFound();

        var actor = BuildActor(principal);
        var result = await review.SetChecklistAsync(id, current.Revision, parsed, actor, ct);
        return result.IsSuccess ? Results.Ok(ToView(result.Draft!)) : RefusedResult(result, http);
    }

    private static async Task<IResult> ApproveEndpoint(
        string draftId, ClaimsPrincipal principal, IImportDraftStore drafts, ImportReviewWorkflow review,
        HttpContext http, CancellationToken ct)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        if (!Guid.TryParse(draftId, out var id)) return Results.NotFound();

        var current = await drafts.FindAsync(id, ct);
        if (current is null) return Results.NotFound();

        var actor = BuildActor(principal);
        var result = await review.ApproveAsync(id, current.Revision, actor, ct);
        return result.IsSuccess ? Results.Ok(ToView(result.Draft!)) : RefusedResult(result, http);
    }

    /// <summary>
    /// Maps the caller's held permissions onto the actor shape
    /// <c>ImportReviewWorkflow</c> already expected but nothing in production
    /// constructed. <c>package.upload</c> stands in for edit authority here —
    /// there is no separate "edit an import draft" permission today, and an
    /// account that may upload a package is the account this slice's editing
    /// surface (none of which is built yet: <c>EditAsync</c> has no endpoint)
    /// would belong to.
    /// </summary>
    private static ImportReviewActor BuildActor(ClaimsPrincipal principal)
    {
        var held = principal.Permissions();
        return new ImportReviewActor(
            principal.UserId() ?? "unknown",
            CanEdit: held.Contains(PermissionKeys.PackageUpload),
            CanReview: held.Contains(PermissionKeys.ExamReview),
            CanPublish: held.Contains(PermissionKeys.ExamPublish));
    }

    private static ImportDraftView ToView(ExamImportDraft draft) => new(
        draft.Id.ToString("D"),
        draft.DefinitionId.Value,
        draft.VersionNumber,
        draft.Route.ToString().ToLowerInvariant(),
        draft.ApprovalState.ToString().ToLowerInvariant(),
        draft.Revision,
        draft.ReviewedBy,
        draft.Version.Sections.Select(s => s.Module.ToString().ToLowerInvariant()).Distinct().ToArray(),
        draft.Findings.Select(f => new ImportFindingView(f.Severity, f.Code, f.Path, f.Message)).ToArray(),
        draft.Warnings.Select(w => new ImportWarningView(
            w.Id, w.Category.ToString().ToLowerInvariant(), w.Path, w.Message, w.Resolved, w.OverrideReason))
            .ToArray(),
        draft.Checklist.Confirmed.Select(c => c.ToString().ToLowerInvariant()).ToArray(),
        draft.Checklist.IsComplete);

    private static IResult Rejected(IReadOnlyList<PackageFinding> findings, HttpContext http) =>
        Results.Problem(
            title: "Package refused",
            detail: findings.Count > 0 ? findings[0].Message : "The package was refused.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: "https://api.vni-ielts.example/errors/package-rejected",
            instance: http.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "PACKAGE_REJECTED",
                ["traceId"] = http.TraceIdentifier,
                ["findings"] = findings.Select(f => new ImportFindingView(f.Severity, f.Code, f.Path, f.Message)),
            });

    private static IResult RefusedResult(ImportReviewResult result, HttpContext http)
    {
        var status = result.ErrorCode switch
        {
            "IMPORT_EDIT_FORBIDDEN" or "IMPORT_REVIEW_FORBIDDEN" or "IMPORT_PUBLISH_FORBIDDEN"
                => StatusCodes.Status403Forbidden,
            "IMPORT_DRAFT_NOT_FOUND" or "IMPORT_WARNING_NOT_FOUND" => StatusCodes.Status404NotFound,
            "PACKAGE_INVALID" => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status409Conflict,
        };

        if (status == StatusCodes.Status404NotFound) return Results.NotFound();

        return Results.Problem(
            detail: result.ErrorCode,
            statusCode: status,
            instance: http.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = result.ErrorCode,
                ["traceId"] = http.TraceIdentifier,
                ["findings"] = result.Findings
                    .Select(f => new ImportFindingView(f.Severity, f.Code, f.Path, f.Message)),
            });
    }

    private static IResult Problem(string code, string detail, int status, HttpContext http) =>
        Results.Problem(
            detail: detail,
            statusCode: status,
            instance: http.Request.Path,
            extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = http.TraceIdentifier });

    private static IResult Conflict(string detail, HttpContext http) =>
        Problem(ErrorCodes.ValidationFailed, detail, StatusCodes.Status409Conflict, http);

    /// <summary>403 with a stable code, not 404 — the caller is a named operator. Same reasoning as <c>AdminEndpoints.Denied</c>.</summary>
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
