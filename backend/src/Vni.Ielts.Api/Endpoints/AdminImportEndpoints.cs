using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Importing;
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

/// <summary>
/// What a caller gets back from an upload now that the work happens elsewhere.
///
/// <b>No draft id, because there is no draft yet.</b> A Cambridge parse costs
/// money and takes minutes; the draft exists when the worker has made one. The
/// <see cref="OperationId"/> is how a caller follows it, and
/// <see cref="DefinitionId"/> is here because the server generates one when the
/// upload did not name it — a caller that never learns it could not find its
/// own exam again.
/// </summary>
public sealed record ImportAcceptedView(
    string OperationId, string DefinitionId, int VersionNumber, string Stage, string State);

/// <summary>
/// One import job, as an operator asking "what happened to my import" needs to
/// see it. <see cref="LastError"/> is a sentence, never a provider response
/// body — see <c>ImportWorker</c> for what is allowed into it.
/// </summary>
public sealed record ImportJobView(
    string OperationId,
    string DefinitionId,
    int VersionNumber,
    string Stage,
    string State,
    int Attempts,
    int MaxAttempts,
    string? DraftId,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? CompletedAt);

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
            .WithSummary("Upload one exam package ZIP; stores it and enqueues an import job")
            .DisableAntiforgery();

        group.MapGet("/template", GetTemplateEndpoint)
            .WithName("AdminGetImportTemplate")
            .WithSummary("Download the empty package skeleton — the folder names the inspector accepts")
            // Task 9 of the 2026-09-11 out-of-band import slice: this route
            // returned a `Results.File` with no response metadata, so the
            // generated OpenAPI document described its 200 as an untyped "OK"
            // — a caller generated from the contract had no way to know this
            // is a binary body rather than JSON. `byte[]` maps to a string
            // schema with `format: byte` (base64), which is the closest this
            // generator gets to "binary file"; the content type is what tells
            // a reader (and a generated client) this is a ZIP.
            .Produces<byte[]>(StatusCodes.Status200OK, "application/zip");

        group.MapGet("/jobs/{operationId}", GetJobEndpoint)
            .WithName("AdminGetImportJob")
            .WithSummary("How far one enqueued import got, and why it stopped if it did");

        group.MapGet("/packages/{draftId}", GetDraftEndpoint)
            .WithName("AdminGetImportDraft")
            .WithSummary("Status and findings for one import draft");

        group.MapPost("/packages/{draftId}/warnings/{warningId}/override", OverrideWarningEndpoint)
            .WithName("AdminOverrideImportWarning")
            .WithSummary("Resolve a warning with a mandatory reason; audited as WarningOverridden");

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
    /// <b>202, not 201 — the door stopped doing the work.</b> Until this
    /// slice the whole import ran inside the request. That was defensible
    /// while the only route was a package already containing a ready
    /// <c>exam.json</c>; it stops being defensible the moment a Cambridge
    /// paper is parsed by a model (minutes, and real money), audio is
    /// transcribed, and forty explanations are generated. An HTTP timeout
    /// halfway through does not refund any of that, and this machine's own
    /// notes record that the API restarts. So the request now does the three
    /// cheap things — hash, store, enqueue — and returns a handle.
    ///
    /// <b>The archive is stored before the job is enqueued, and that order is
    /// the point.</b> The API and the Worker are different processes; the
    /// uploaded ZIP exists only in this request. A job enqueued before its
    /// bytes are safely parked is a job the worker cannot do.
    ///
    /// <b>But inspection still happens here, before a byte is persisted.</b>
    /// CLAUDE.md rule 3 is not "an uploaded ZIP is validated somewhere" — it
    /// is validated <i>before anything is persisted</i>, because a hostile
    /// archive is the one input this product assumes is trying to hurt it.
    /// Moving the expensive half of the pipeline out of band is not a reason
    /// to move the cheap half with it:
    /// <see cref="IExamPackageArchiveInspector.InspectAsync"/> reads the
    /// central directory and writes nothing, so a bomb, a path escape or a
    /// nested archive is refused by this request with the same 422 it always
    /// was — rather than being stored, enqueued, and refused minutes later in
    /// a worker whose failure nobody is watching. The worker inspects again
    /// against the artefact it actually reads, which is what closes the
    /// check-then-use window (`A8`); this is the door, not a substitute for
    /// it.
    /// </summary>
    private static async Task<IResult> UploadPackageEndpoint(
        HttpRequest request, ClaimsPrincipal principal, IImportArchiveStore archives,
        IImportOutbox outbox, IExamSourceParser parser, IClock clock,
        IExamPackageArchiveInspector inspector, IOptions<ImportArchiveOptions> archiveOptions,
        HttpContext http, CancellationToken ct)
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

        string sourceSha256;
        string archiveKey;

        await using (var uploadStream = file.OpenReadStream())
        {
            /*
             * <b>Hashed on the way past, not read twice.</b> The hash is the
             * job's identity — it is what makes a retried upload of identical
             * bytes the same job rather than a second paid parse — so it has
             * to be computed over exactly the bytes that get stored.
             *
             * A buffered form part is seekable in every configuration this
             * deployment runs today, but a forward-only body must never
             * produce a hash of one half and an archive of the other, so the
             * hash is taken from a stream that is then rewound or re-read
             * from the spool rather than from a second pass over a stream
             * that may not support one.
             */
            await using var spooled = new FileStream(
                Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                81_920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

            await uploadStream.CopyToAsync(spooled, ct);
            spooled.Position = 0;

            sourceSha256 = Convert.ToHexString(await SHA256.HashDataAsync(spooled, ct)).ToLowerInvariant();
            spooled.Position = 0;

            // Rule 3: validated before anything is persisted. Reads the
            // central directory, writes nothing, extracts nothing.
            var inspection = await inspector.InspectAsync(spooled, archiveOptions.Value.ToLimits(), ct);
            if (!inspection.IsAcceptable) return Rejected(inspection.Findings, http);

            spooled.Position = 0;

            archiveKey = await archives.SaveAsync(sourceSha256, spooled, ct);
        }

        /*
         * <b>The parse prompt version is read here, before any parser runs.</b>
         * It is a component of the operation id for the same reason a marking
         * job's id carries a rubric version: a parse under an improved prompt
         * is different work, and an id blind to it would make re-uploading
         * identical bytes after a prompt change collide with the job keyed to
         * the old one. With no AI parser wired this is
         * `ImportJob.NoParserConfigured`. → IExamSourceParser.PromptVersion
         */
        var job = ImportJob.New(
            definitionId, versionNumber, sourceSha256, parser.PromptVersion, archiveKey,
            clock.UtcNow,
            /*
             * <b>The upload's own trace, carried to a process that starts
             * minutes later.</b> Without it the worker's span is the root of
             * an unrelated trace and "the operator uploaded and no draft
             * appeared" is two traces nobody can join. Same mechanism, same
             * reason, as `MarkingJob.TraceParent`.
             */
            Activity.Current?.Id);

        // False means the job was already there — a retried upload of
        // identical bytes under the same definition, version and prompt. The
        // unique index decided that, not this code, and the right answer is
        // the same 202 with the same operation id rather than a conflict: the
        // caller asked for this import and this import is owed.
        await outbox.EnqueueAsync(job, ct);

        var location = $"/api/v1/admin/import/jobs/{Uri.EscapeDataString(job.OperationId)}";

        return Results.Accepted(
            location,
            new ImportAcceptedView(
                job.OperationId, definitionId.Value, versionNumber,
                job.Stage.ToString(), job.State.ToString()));
    }

    /// <summary>
    /// The downloadable package skeleton — four skill folders, and inside
    /// <c>reading/</c> and <c>listening/</c> a second level separating the
    /// paper from the answer key, built by <see cref="ImportTemplate.BuildZip"/>
    /// from <see cref="ExamPackageArchiveInspector"/>'s own tables so it can
    /// never ship a folder name the inspector does not accept.
    ///
    /// <b>Gated on <c>package.upload</c>, not <c>package.read</c>.</b> This
    /// route serves a person about to upload a package who needs to know the
    /// folder names before they zip anything — the same audience, and the
    /// same permission, <see cref="UploadPackageEndpoint"/> already checks.
    /// <c>package.read</c> is about reading the status of a package that
    /// already exists, which is a different question this endpoint never
    /// answers.
    /// </summary>
    private static IResult GetTemplateEndpoint(ClaimsPrincipal principal)
    {
        if (Denied(principal, PermissionKeys.PackageUpload) is { } denial) return denial;

        var bytes = ImportTemplate.BuildZip();
        return Results.File(
            bytes,
            "application/zip",
            fileDownloadName: "vni-exam-package-template.zip");
    }

    /// <summary>
    /// "What happened to my import."
    ///
    /// <b>Gated on <c>package.read</c>, not on <c>exam.submit</c>.</b> The
    /// brief named the latter; this is the same permission that already guards
    /// <see cref="GetDraftEndpoint"/>, which returns strictly more about the
    /// same upload. Support holds <c>package.read</c> and not
    /// <c>exam.submit</c>, so gating on the other key would let an operator
    /// read the finished draft while refusing to tell them whether it had been
    /// produced yet.
    ///
    /// <b>An unreadable stage is answered, not thrown.</b>
    /// <c>MongoImportOutbox</c> refuses to map a stored stage this binary does
    /// not define — a deliberate trade of a silent restart (which would re-buy
    /// a parse) for a loud failure. A loud failure nobody can read is only half
    /// that trade, and the window in which it can happen is a rollback, which
    /// is exactly when an operator is asking this question.
    /// </summary>
    private static async Task<IResult> GetJobEndpoint(
        string operationId, ClaimsPrincipal principal, IImportOutbox outbox,
        HttpContext http, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.PackageRead) is { } denial) return denial;

        ImportJob? job;

        try
        {
            job = await outbox.FindAsync(operationId, ct);
        }
        catch (ImportJobStageUnreadableException e)
        {
            return Results.Problem(
                title: "Import job unreadable",
                detail:
                    "This job records a stage this build of the API does not recognise, which "
                    + "happens when a newer deployment wrote it and was then rolled back. It has "
                    + "been stopped rather than restarted, because restarting it would pay for a "
                    + "parse that has already been paid for. Deploy the newer build to read it, "
                    + "or re-upload the package under a fresh version.",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://api.vni-ielts.example/errors/import-job-unreadable",
                instance: http.Request.Path,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "IMPORT_JOB_STAGE_UNREADABLE",
                    ["traceId"] = http.TraceIdentifier,
                    // The stage integer and the id, and nothing else. Both are
                    // identifiers; neither is content.
                    ["operationId"] = e.OperationId,
                    ["stage"] = e.RawStage,
                });
        }

        return job is null ? Results.NotFound() : Results.Ok(ToView(job));
    }

    private static ImportJobView ToView(ImportJob job) => new(
        job.OperationId,
        job.DefinitionId.Value,
        job.VersionNumber,
        job.Stage.ToString(),
        job.State.ToString(),
        job.Attempts,
        ImportJob.MaxAttempts,
        job.DraftId?.ToString("D"),
        job.LastError,
        job.CreatedAt,
        job.NextAttemptAt,
        job.CompletedAt);

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
