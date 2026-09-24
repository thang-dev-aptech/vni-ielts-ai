using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Exams;
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
/// Batch endpoints are also here: <c>POST /import/batches</c> starts or resumes
/// a multi-package import via <c>ImportBatchRunner</c>, and <c>GET /import/batches/{id}</c>
/// reports per-item checkpoint state from <c>IImportBatchCheckpointStore</c>.
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
    bool ChecklistComplete,
    IReadOnlyList<ImportDraftGroupView> Groups);

/// <summary>The full set of positions an admin wants a group to have; replaces whatever was there.</summary>
public sealed record SetGroupPositionsRequest(IReadOnlyList<GroupPositionInput> Positions);

/// <summary>
/// The full set of confirmed checklist categories, replacing whatever was
/// confirmed before — same "replace, not patch" contract as positions.
/// Category names are <see cref="ImportReviewCategory"/> members, matched
/// case-insensitively so the wire format an admin client already sends
/// elsewhere (lower-case, no separators) works unchanged.
/// </summary>
public sealed record SetChecklistRequest(IReadOnlyList<string> Confirmed);

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
/// One package-upload attempt, as the history list needs it. Findings stay on
/// the detail route — a list that quoted every message would ship paper
/// content the operator did not open.
/// </summary>
public sealed record PackageImportHistorySummaryView(
    string HistoryId,
    string? OperationId,
    string ActorId,
    string OriginalFileName,
    string? DefinitionId,
    int? VersionNumber,
    string? SourceSha256,
    string? DraftId,
    string? Stage,
    string Result,
    int FindingCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PackageImportHistoryDetailView(
    string HistoryId,
    string? OperationId,
    string ActorId,
    string OriginalFileName,
    string? DefinitionId,
    int? VersionNumber,
    string? SourceSha256,
    string? DraftId,
    string? Stage,
    string Result,
    IReadOnlyList<ImportFindingView> Findings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One item in a batch import, reporting only its checkpoint state — never archive contents.</summary>
public sealed record ImportBatchItemView(
    string ItemId,
    string? DefinitionId,
    int? VersionNumber,
    string State,
    string? DraftId,
    IReadOnlyList<ImportFindingView> Findings,
    int Attempts);

/// <summary>Status of an entire batch import — per-item results and aggregate counts.</summary>
public sealed record ImportBatchStatusView(
    string BatchId,
    IReadOnlyList<ImportBatchItemView> Items,
    int SucceededCount,
    int FailedCount,
    int SkippedCount);

public sealed record PackageImportHistoryPageView(
    IReadOnlyList<PackageImportHistorySummaryView> Items,
    long TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// Structured audit payloads for the package-import lifecycle.
///
/// <b>Identifiers, counts and machine codes — never copyrighted contents.</b>
/// No archive bytes, no package JSON, no finding messages (those can quote a
/// passage), no archive key a caller could turn into a download. Action enum
/// members live here so the HTTP door can emit them; Vietnamese labels on
/// <c>AuditPage</c> stay with admin-shared-contracts.
/// </summary>
public static class PackageImportAudit
{
    public static IReadOnlyDictionary<string, string> UploadAccepted(
        string historyId, string operationId, string definitionId, int versionNumber,
        string sourceSha256, string fileName) =>
        new Dictionary<string, string>
        {
            ["historyId"] = historyId,
            ["operationId"] = operationId,
            ["definitionId"] = definitionId,
            ["versionNumber"] = versionNumber.ToString(),
            ["sourceSha256"] = sourceSha256,
            ["fileName"] = fileName,
        };

    public static IReadOnlyDictionary<string, string> UploadRejected(
        string historyId, string definitionId, int versionNumber, string sourceSha256,
        string fileName, IReadOnlyList<PackageFinding> findings) =>
        new Dictionary<string, string>
        {
            ["historyId"] = historyId,
            ["definitionId"] = definitionId,
            ["versionNumber"] = versionNumber.ToString(),
            ["sourceSha256"] = sourceSha256,
            ["fileName"] = fileName,
            ["findingCount"] = findings.Count.ToString(),
            ["findingCodes"] = Codes(findings),
        };

    public static IReadOnlyDictionary<string, string> WorkerRejected(
        string historyId, string operationId, IReadOnlyList<PackageFinding> findings) =>
        new Dictionary<string, string>
        {
            ["historyId"] = historyId,
            ["operationId"] = operationId,
            ["findingCount"] = findings.Count.ToString(),
            ["findingCodes"] = Codes(findings),
        };

    public static IReadOnlyDictionary<string, string> Approved(
        string draftId, string definitionId, int versionNumber) =>
        new Dictionary<string, string>
        {
            ["draftId"] = draftId,
            ["definitionId"] = definitionId,
            ["versionNumber"] = versionNumber.ToString(),
        };

    public static IReadOnlyDictionary<string, string> WarningOverride(
        string warningId, string category, string reason) =>
        new Dictionary<string, string>
        {
            ["warningId"] = warningId,
            ["category"] = category,
            ["reason"] = reason,
        };

    public static IReadOnlyDictionary<string, string> GroupPositionsSet(
        string draftId, string groupId, int count) =>
        new Dictionary<string, string>
        {
            ["draftId"] = draftId,
            ["groupId"] = groupId,
            ["count"] = count.ToString(),
        };

    public static IReadOnlyDictionary<string, string> ChecklistConfirmed(
        string draftId, int confirmedCount) =>
        new Dictionary<string, string>
        {
            ["draftId"] = draftId,
            ["confirmedCount"] = confirmedCount.ToString(),
        };

    private static string Codes(IReadOnlyList<PackageFinding> findings)
    {
        var joined = string.Join(",", findings.Select(f => f.Code).Where(c => c.Length > 0).Take(20));
        return joined.Length <= 256 ? joined : joined[..256];
    }
}

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

        group.MapGet("/packages", ListPackageHistoryEndpoint)
            .WithName("AdminListImportPackages")
            .WithSummary("Package-upload history, newest first, including door-level refusals");

        group.MapGet("/package-history/{historyId}", GetPackageHistoryEndpoint)
            .WithName("AdminGetImportPackageHistory")
            .WithSummary("One upload attempt: sanitized metadata and the full persisted finding list");

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

        group.MapGet("/packages/{draftId}/assets/{**reference}", GetDraftAssetEndpoint)
            .WithName("AdminGetImportDraftAsset")
            .WithSummary("Stream one privately staged asset for a draft under review — never a promoted exam asset");

        group.MapPost("/packages/{draftId}/warnings/{warningId}/override", OverrideWarningEndpoint)
            .WithName("AdminOverrideImportWarning")
            .WithSummary("Resolve a warning with a mandatory reason; audited as WarningOverridden");

        group.MapPost("/packages/{draftId}/groups/{groupId}/positions", SetGroupPositionsEndpoint)
            .WithName("AdminSetImportGroupPositions")
            .WithSummary("Persist hotspot positions for a group; resets ApprovalState/Checklist like any edit");

        group.MapPost("/packages/{draftId}/checklist", SetChecklistEndpoint)
            .WithName("AdminSetImportChecklist")
            .WithSummary("Replace the confirmed review-checklist categories; approval needs all six confirmed");

        group.MapPost("/packages/{draftId}/approve", ApproveEndpoint)
            .WithName("AdminApproveImportDraft")
            .WithSummary("Approve a draft once every warning is resolved and the checklist is complete");

        group.MapPost("/batches", StartImportBatchEndpoint)
            .WithName("AdminStartImportBatch")
            .WithSummary("Start or resume a batch import with multiple packages")
            .DisableAntiforgery();

        group.MapGet("/batches/{batchId}", GetImportBatchStatusEndpoint)
            .WithName("AdminGetImportBatchStatus")
            .WithSummary("Get the status of a batch import, including per-item checkpoint state");
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
        IImportOutbox outbox, IPackageImportHistoryStore history, IExamSourceParser parser,
        IClock clock, IExamPackageArchiveInspector inspector, IAuditLog audit,
        IOptions<ImportArchiveOptions> archiveOptions,
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
            if (!inspection.IsAcceptable)
            {
                var rejectedId = Guid.NewGuid();
                var rejectedName = PackageImportHistoryBounds.SanitizeFileName(file.FileName);
                await RecordHistoryQuietly(() => history.RecordDoorRejectionAsync(
                    new PackageImportHistory(
                        rejectedId,
                        OperationId: null,
                        ActorId: principal.UserId()!,
                        OriginalFileName: file.FileName,
                        DefinitionId: definitionId,
                        VersionNumber: versionNumber,
                        SourceSha256: sourceSha256,
                        DraftId: null,
                        Stage: null,
                        Result: PackageImportHistoryResult.DoorRejected,
                        Findings: inspection.Findings,
                        CreatedAt: clock.UtcNow,
                        UpdatedAt: clock.UtcNow),
                    ct));

                await RecordAuditQuietly(() => audit.AppendAsync(
                    AuditEntry.Record(
                        new UserId(principal.UserId()!), principal.Email() ?? principal.DisplayName(),
                        AuditAction.PackageUploadRejected, "package-import-history",
                        rejectedId.ToString("D"), rejectedName, clock.UtcNow,
                        PackageImportAudit.UploadRejected(
                            rejectedId.ToString("D"), definitionId.Value, versionNumber,
                            sourceSha256, rejectedName, inspection.Findings)),
                    ct));

                return Rejected(inspection.Findings, http);
            }

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

        /*
         * False means the job was already there — a retried upload of
         * identical bytes under the same definition, version and prompt. The
         * unique index decided that, not this code, and the right answer is
         * the same 202 with the same operation id rather than a conflict: the
         * caller asked for this import and this import is owed.
         *
         * <b>Unless it already failed, in which case "already there" was a
         * silent no-op.</b> An operator whose import failed re-uploads the
         * package; that is the obvious thing to do and it is an unambiguous
         * instruction to try again. Before this, the duplicate id swallowed
         * it: 202, nothing re-runs, and the CMS shows the old error for ever,
         * with a version bump nobody would guess as the only escape.
         *
         * `ReopenAsync` is chosen over answering with a distinct "this import
         * already failed" response because the operator's next move after
         * reading that response is to ask for exactly this, and a door that
         * makes them ask twice is a door they work around. It resets the
         * attempt budget and keeps the recorded stage, so the reopened job
         * resumes rather than re-buying the parse, and its filter refuses
         * anything that is not `Failed` — a `Running` job is never disturbed.
         * The archive was deleted when the job failed and has just been
         * written again above, under the same content-addressed key, so the
         * reopened job has bytes to read.
         */
        if (!await outbox.EnqueueAsync(job, ct))
            await outbox.ReopenAsync(job.OperationId, ct);

        await RecordHistoryQuietly(() => history.RecordQueuedAsync(
            new PackageImportHistory(
                Guid.NewGuid(),
                job.OperationId,
                ActorId: principal.UserId()!,
                OriginalFileName: file.FileName,
                DefinitionId: definitionId,
                VersionNumber: versionNumber,
                SourceSha256: sourceSha256,
                DraftId: null,
                Stage: job.Stage,
                Result: PackageImportHistoryResult.Queued,
                Findings: [],
                CreatedAt: clock.UtcNow,
                UpdatedAt: clock.UtcNow),
            ct));

        var fileName = PackageImportHistoryBounds.SanitizeFileName(file.FileName);
        var queued = await history.FindByOperationAsync(job.OperationId, ct);
        var historyId = queued?.Id.ToString("D") ?? job.OperationId;

        await RecordAuditQuietly(() => audit.AppendAsync(
            AuditEntry.Record(
                new UserId(principal.UserId()!), principal.Email() ?? principal.DisplayName(),
                AuditAction.PackageUploadAccepted, "package-import-history",
                historyId, fileName, clock.UtcNow,
                PackageImportAudit.UploadAccepted(
                    historyId, job.OperationId, definitionId.Value, versionNumber,
                    sourceSha256, fileName)),
            ct));

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
    /// <b>Gated on <c>package.read</c> OR <c>package.upload</c> — fix round 1
    /// on this task.</b> This route was gated on <c>package.read</c> alone,
    /// the same permission that already guards <see cref="GetDraftEndpoint"/>,
    /// on the reasoning that Support holds <c>package.read</c> and not
    /// <c>package.upload</c> and should be able to watch a job without being
    /// able to start one. That reasoning is sound and still holds — but it
    /// only runs one way: an operator who holds <i>only</i>
    /// <c>package.upload</c> (the permission <c>UploadPackageEndpoint</c>
    /// itself requires, and the one the CMS's <c>/import</c> route is gated
    /// on client-side) could start a job and then get a 403 asking how it
    /// went. Silence there is worse than not offering the upload at all: they
    /// cannot tell a running job from a failed one, get no error text, and the
    /// natural response — upload the same package again — collides with the
    /// job already running via the derived operation id and reads as a broken
    /// button. Anyone who may start a job must be able to see how it ended, so
    /// this checks either key.
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
        if (DeniedUnlessAny(principal, PermissionKeys.PackageRead, PermissionKeys.PackageUpload)
            is { } denial)
        {
            return denial;
        }

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

    private static async Task<IResult> ListPackageHistoryEndpoint(
        ClaimsPrincipal principal, IPackageImportHistoryStore history, HttpContext http,
        string? result, string? stage, string? uploader, DateTimeOffset? from, DateTimeOffset? to,
        int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (Denied(principal, PermissionKeys.PackageRead) is { } denial) return denial;

        PackageImportHistoryResult? parsedResult = null;
        if (!string.IsNullOrWhiteSpace(result))
        {
            if (!Enum.TryParse<PackageImportHistoryResult>(result, ignoreCase: true, out var known))
                return Problem(ErrorCodes.ValidationFailed, "Unknown result filter.", 400, http);
            parsedResult = known;
        }

        ImportJobStage? parsedStage = null;
        if (!string.IsNullOrWhiteSpace(stage))
        {
            if (!Enum.TryParse<ImportJobStage>(stage, ignoreCase: true, out var known))
                return Problem(ErrorCodes.ValidationFailed, "Unknown stage filter.", 400, http);
            parsedStage = known;
        }

        var query = new PackageImportHistoryQuery(
            parsedResult, parsedStage, uploader, from, to, page, pageSize);

        try
        {
            query.Validate();
        }
        catch (ArgumentException)
        {
            return Problem(ErrorCodes.ValidationFailed, "The history list filter is invalid.", 400, http);
        }

        var pageResult = await history.QueryAsync(query, ct);
        return Results.Ok(new PackageImportHistoryPageView(
            [.. pageResult.Items.Select(ToSummaryView)],
            pageResult.TotalCount, pageResult.Page, pageResult.PageSize));
    }

    private static async Task<IResult> GetPackageHistoryEndpoint(
        string historyId, ClaimsPrincipal principal, IPackageImportHistoryStore history,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.PackageRead) is { } denial) return denial;
        if (!Guid.TryParse(historyId, out var id)) return Results.NotFound();

        var row = await history.FindByIdAsync(id, ct);
        return row is null ? Results.NotFound() : Results.Ok(ToDetailView(row));
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

    private static PackageImportHistorySummaryView ToSummaryView(PackageImportHistory row) => new(
        row.Id.ToString("D"),
        row.OperationId,
        row.ActorId,
        row.OriginalFileName,
        row.DefinitionId?.Value,
        row.VersionNumber,
        row.SourceSha256,
        row.DraftId?.ToString("D"),
        row.Stage?.ToString(),
        row.Result.ToString(),
        row.Findings.Count,
        row.CreatedAt,
        row.UpdatedAt);

    private static PackageImportHistoryDetailView ToDetailView(PackageImportHistory row) => new(
        row.Id.ToString("D"),
        row.OperationId,
        row.ActorId,
        row.OriginalFileName,
        row.DefinitionId?.Value,
        row.VersionNumber,
        row.SourceSha256,
        row.DraftId?.ToString("D"),
        row.Stage?.ToString(),
        row.Result.ToString(),
        row.Findings.Select(f => new ImportFindingView(f.Severity, f.Code, f.Path, f.Message)).ToArray(),
        row.CreatedAt,
        row.UpdatedAt);

    private static async Task<IResult> GetDraftEndpoint(
        string draftId, ClaimsPrincipal principal, IImportDraftStore drafts, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.PackageRead) is { } denial) return denial;
        if (!Guid.TryParse(draftId, out var id)) return Results.NotFound();

        var draft = await drafts.FindAsync(id, ct);
        return draft is null ? Results.NotFound() : Results.Ok(ToView(draft));
    }

    /// <summary>
    /// Admin-only preview of a draft's privately staged media. The learner
    /// asset route (<c>IExamAssetStore</c>) must keep serving only promoted
    /// content; this door talks only to <see cref="IPrivateImportAssetStore"/>
    /// and scopes every key under the draft's own id so one draft cannot
    /// read another's bytes.
    /// </summary>
    private static async Task<IResult> GetDraftAssetEndpoint(
        string draftId, string reference, ClaimsPrincipal principal,
        IImportDraftStore drafts, IPrivateImportAssetStore privateAssets, CancellationToken ct)
    {
        if (DeniedUnlessAny(principal, PermissionKeys.PackageRead, PermissionKeys.PackageUpload)
            is { } denial)
        {
            return denial;
        }

        if (!Guid.TryParse(draftId, out var id)) return Results.NotFound();

        var draft = await drafts.FindAsync(id, ct);
        if (draft is null) return Results.NotFound();

        if (DraftStagingKey(id, reference) is not { } key) return Results.NotFound();

        if (await privateAssets.OpenPrivateAsync(key, ct) is not { } asset)
            return Results.NotFound();

        return Results.Stream(
            asset.Content,
            asset.ContentType,
            enableRangeProcessing: true);
    }

    /// <summary>
    /// <c>imports/{draftId}/{reference}</c>. Promotion later copies to the
    /// plain <c>assets/…</c> public key; this route never opens that tree.
    /// A traversal segment or empty reference collapses to null → 404.
    /// </summary>
    public static string? DraftStagingKey(Guid draftId, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;

        var normalized = reference.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0) return null;
        if (normalized.Split('/').Any(segment => segment is "" or "." or "..")) return null;

        return $"imports/{draftId:D}/{normalized}";
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
                PackageImportAudit.WarningOverride(
                    warningId, warning.Category.ToString(), request.Reason)),
            ct);

        return Results.Ok(ToView(draft));
    }

    /// <summary>
    /// Mirrors <see cref="OverrideWarningEndpoint"/>'s shape exactly: permission
    /// falls out of <see cref="ImportReviewWorkflow.SetGroupPositionsAsync"/>'s
    /// own <c>actor.CanEdit</c> gate (mapped from <c>package.upload</c> by
    /// <see cref="BuildActor"/>), refusals map through <see cref="RefusedResult"/>,
    /// and a success is audited before the updated view goes back.
    /// </summary>
    private static async Task<IResult> SetGroupPositionsEndpoint(
        string draftId, string groupId, SetGroupPositionsRequest request, ClaimsPrincipal principal,
        IImportDraftStore drafts, ImportReviewWorkflow review, IAuditLog audit, IClock clock,
        HttpContext http, CancellationToken ct)
    {
        if (principal.UserId() is not { } actorId) return Results.Unauthorized();
        if (!Guid.TryParse(draftId, out var id)) return Results.NotFound();

        var current = await drafts.FindAsync(id, ct);
        if (current is null) return Results.NotFound();

        var positions = request.Positions ?? [];
        var actor = BuildActor(principal);
        var result = await review.SetGroupPositionsAsync(id, current.Revision, groupId, positions, actor, ct);
        if (!result.IsSuccess) return RefusedResult(result, http);

        var draft = result.Draft!;
        await audit.AppendAsync(
            AuditEntry.Record(
                new UserId(actorId), principal.Email() ?? principal.DisplayName(),
                AuditAction.GroupPositionsSet, "import-draft", draftId,
                $"{draft.DefinitionId.Value} v{draft.VersionNumber}",
                clock.UtcNow,
                PackageImportAudit.GroupPositionsSet(draftId, groupId, positions.Count)),
            ct);

        return Results.Ok(ToView(draft));
    }

    /// <summary>
    /// Replaces the confirmed checklist categories — the one write
    /// <c>ApproveAsync</c> has always required (<c>draft.Checklist.IsComplete</c>)
    /// with no HTTP door onto it until now. Category names are matched
    /// case-insensitively against <see cref="ImportReviewCategory"/>; an
    /// unrecognised name is a 400, not a silently-dropped confirmation.
    /// </summary>
    private static async Task<IResult> SetChecklistEndpoint(
        string draftId, SetChecklistRequest request, ClaimsPrincipal principal,
        IImportDraftStore drafts, ImportReviewWorkflow review, IAuditLog audit, IClock clock,
        HttpContext http, CancellationToken ct)
    {
        if (principal.UserId() is not { } actorId) return Results.Unauthorized();
        if (!Guid.TryParse(draftId, out var id)) return Results.NotFound();

        var confirmed = new HashSet<ImportReviewCategory>();
        foreach (var name in request.Confirmed ?? [])
        {
            if (!Enum.TryParse<ImportReviewCategory>(name, ignoreCase: true, out var category))
                return Problem(ErrorCodes.ValidationFailed, $"Unknown checklist category '{name}'.", 400, http);
            confirmed.Add(category);
        }

        var current = await drafts.FindAsync(id, ct);
        if (current is null) return Results.NotFound();

        var actor = BuildActor(principal);
        var result = await review.SetChecklistAsync(id, current.Revision, confirmed, actor, ct);
        if (!result.IsSuccess) return RefusedResult(result, http);

        var draft = result.Draft!;
        await audit.AppendAsync(
            AuditEntry.Record(
                new UserId(actorId), principal.Email() ?? principal.DisplayName(),
                AuditAction.ImportChecklistConfirmed, "import-draft", draftId,
                $"{draft.DefinitionId.Value} v{draft.VersionNumber}",
                clock.UtcNow,
                PackageImportAudit.ChecklistConfirmed(draftId, confirmed.Count)),
            ct);

        return Results.Ok(ToView(draft));
    }

    private static async Task<IResult> ApproveEndpoint(
        string draftId, ClaimsPrincipal principal, IImportDraftStore drafts, ImportReviewWorkflow review,
        IAuditLog audit, IClock clock, HttpContext http, CancellationToken ct)
    {
        if (principal.UserId() is not { } actorId) return Results.Unauthorized();
        if (!Guid.TryParse(draftId, out var id)) return Results.NotFound();

        var current = await drafts.FindAsync(id, ct);
        if (current is null) return Results.NotFound();

        var actor = BuildActor(principal);
        var result = await review.ApproveAsync(id, current.Revision, actor, ct);
        if (!result.IsSuccess) return RefusedResult(result, http);

        var draft = result.Draft!;
        await audit.AppendAsync(
            AuditEntry.Record(
                new UserId(actorId), principal.Email() ?? principal.DisplayName(),
                AuditAction.PackageImportApproved, "import-draft", draftId,
                $"{draft.DefinitionId.Value} v{draft.VersionNumber}",
                clock.UtcNow,
                PackageImportAudit.Approved(draftId, draft.DefinitionId.Value, draft.VersionNumber)),
            ct);

        return Results.Ok(ToView(draft));
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
        draft.Checklist.IsComplete,
        draft.Version.ToGroupViews());

    /// <summary>
    /// History is bookkeeping. A write failure here must not change the door's
    /// answer: a refused bomb is still a 422, and an accepted upload is still
    /// a 202 whose job is already in the outbox.
    /// </summary>
    private static async Task RecordHistoryQuietly(Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (Exception)
        {
            // Deliberately empty: the operator's next move is the 422/202, not
            // a 500 that looks as if the package itself failed differently.
        }
    }

    private static async Task RecordAuditQuietly(Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (Exception)
        {
            // Bookkeeping. A refused bomb is still a 422; an accepted upload
            // is still a 202 whose job is already owed.
        }
    }

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
                // Same status as the twin rule on `AdminEndpoints.ApproveEndpoint`
                // for an exam version authored directly in the CMS — one rule,
                // one status, regardless of which door reaches it.
                or ErrorCodes.ReviewerIsAuthor
                => StatusCodes.Status403Forbidden,
            "IMPORT_DRAFT_NOT_FOUND" or "IMPORT_WARNING_NOT_FOUND" or "IMPORT_GROUP_NOT_FOUND"
                => StatusCodes.Status404NotFound,
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

    /// <summary>
    /// Start or resume a multi-package batch import. Accepts multipart form with multiple files,
    /// validates and stores them, then runs ImportBatchRunner to process the batch.
    /// Returns checkpoint state for each item.
    /// </summary>
    private static async Task<IResult> StartImportBatchEndpoint(
        HttpRequest request, ClaimsPrincipal principal, IImportArchiveStore archives,
        ExamImportWorkflow workflow, IImportBatchCheckpointStore checkpoints,
        IExamSourceParser parser, IClock clock, IExamPackageArchiveInspector inspector,
        IAuditLog audit, IOptions<ImportArchiveOptions> archiveOptions,
        HttpContext http, CancellationToken ct)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        if (Denied(principal, PermissionKeys.PackageUpload) is { } denial) return denial;

        if (!request.HasFormContentType)
            return Problem(ErrorCodes.ValidationFailed, "Expected a multipart upload.", 400, http);

        var batchId = Guid.NewGuid().ToString("n");
        var items = new List<ImportBatchItem>();

        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } cap)
            cap.MaxRequestBodySize = archiveOptions.Value.MaxArchiveBytes + MultipartOverheadBytes;

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (InvalidDataException)
        {
            // A multipart body with no parts at all (no file input selected)
            // is malformed enough that Kestrel's own reader refuses it before
            // this code ever sees a file list — same 422 as "zero files",
            // since that is what a caller sending this actually means.
            return Problem(ErrorCodes.ValidationFailed, "At least one file must be provided.", 422, http);
        }

        foreach (var file in form.Files)
        {
            if (file.Length == 0)
                return Problem(ErrorCodes.ValidationFailed, $"File '{file.FileName}' is empty.", 400, http);

            var definitionId = new ExamDefinitionId($"batch-{batchId}-{file.FileName}");
            var versionNumber = 1;

            string sourceSha256;
            string archiveKey;

            await using (var uploadStream = file.OpenReadStream())
            {
                await using var spooled = new FileStream(
                    Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                    81_920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

                await uploadStream.CopyToAsync(spooled, ct);
                spooled.Position = 0;

                sourceSha256 = Convert.ToHexString(await SHA256.HashDataAsync(spooled, ct)).ToLowerInvariant();
                spooled.Position = 0;

                var inspection = await inspector.InspectAsync(spooled, archiveOptions.Value.ToLimits(), ct);
                if (!inspection.IsAcceptable)
                {
                    return Rejected(inspection.Findings, http);
                }

                spooled.Position = 0;
                archiveKey = await archives.SaveAsync(sourceSha256, spooled, ct);
            }

            items.Add(new ImportBatchItem(
                ItemId: file.FileName,
                DefinitionId: definitionId,
                VersionNumber: versionNumber,
                StructuredPackage: archiveKey,
                ExtractedSource: null));
        }

        if (items.Count == 0)
            return Problem(ErrorCodes.ValidationFailed, "At least one file must be provided.", 400, http);

        var runner = new ImportBatchRunner(workflow, checkpoints);
        var result = await runner.RunAsync(batchId, items, ct);

        await RecordAuditQuietly(() => audit.AppendAsync(
            AuditEntry.Record(
                new UserId(principal.UserId()!), principal.Email() ?? principal.DisplayName(),
                AuditAction.PackageUploadAccepted, "batch-import",
                batchId, $"{items.Count} packages", clock.UtcNow,
                new Dictionary<string, string>
                {
                    ["itemCount"] = items.Count.ToString(),
                    ["succeeded"] = result.Succeeded.ToString(),
                    ["failed"] = result.Failed.ToString(),
                    ["skipped"] = result.Skipped.ToString(),
                }),
            ct));

        var statusView = await GetBatchStatusAsync(batchId, checkpoints, items, ct);
        return Results.Accepted($"/api/v1/admin/import/batches/{Uri.EscapeDataString(batchId)}", statusView);
    }

    /// <summary>
    /// Get the status of a batch import, including per-item checkpoint state.
    /// Gated on package.read or package.upload (same as GetJobEndpoint).
    /// </summary>
    private static async Task<IResult> GetImportBatchStatusEndpoint(
        string batchId, ClaimsPrincipal principal, IImportBatchCheckpointStore checkpoints,
        HttpContext http, CancellationToken ct)
    {
        if (DeniedUnlessAny(principal, PermissionKeys.PackageRead, PermissionKeys.PackageUpload)
            is { } denial)
        {
            return denial;
        }

        var allCheckpoints = await checkpoints.ListByBatchIdAsync(batchId, ct);
        if (allCheckpoints.Count == 0)
        {
            return Results.Problem(
                detail: "Batch not found.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["code"] = "BATCH_NOT_FOUND" });
        }

        var items = new List<ImportBatchItemView>();
        int succeeded = 0, failed = 0;

        foreach (var checkpoint in allCheckpoints)
        {
            items.Add(new ImportBatchItemView(
                ItemId: checkpoint.ItemId,
                DefinitionId: null,
                VersionNumber: null,
                State: checkpoint.State.ToString(),
                DraftId: checkpoint.DraftId?.ToString("D"),
                Findings: checkpoint.Findings
                    .Select(f => new ImportFindingView(f.Severity, f.Code, f.Path, f.Message))
                    .ToList(),
                Attempts: checkpoint.Attempts));

            if (checkpoint.State == ImportBatchItemState.Succeeded) succeeded++;
            else failed++;
        }

        var statusView = new ImportBatchStatusView(
            BatchId: batchId,
            Items: items,
            SucceededCount: succeeded,
            FailedCount: failed,
            SkippedCount: 0);

        return Results.Ok(statusView);
    }

    private static async Task<ImportBatchStatusView> GetBatchStatusAsync(
        string batchId, IImportBatchCheckpointStore checkpoints,
        IReadOnlyList<ImportBatchItem> items, CancellationToken ct)
    {
        var statusItems = new List<ImportBatchItemView>();
        int succeeded = 0, failed = 0;

        foreach (var item in items)
        {
            var checkpoint = await checkpoints.FindAsync(batchId, item.ItemId, ct);
            if (checkpoint is null) continue;

            statusItems.Add(new ImportBatchItemView(
                ItemId: checkpoint.ItemId,
                DefinitionId: item.DefinitionId.Value,
                VersionNumber: item.VersionNumber,
                State: checkpoint.State.ToString(),
                DraftId: checkpoint.DraftId?.ToString("D"),
                Findings: checkpoint.Findings
                    .Select(f => new ImportFindingView(f.Severity, f.Code, f.Path, f.Message))
                    .ToList(),
                Attempts: checkpoint.Attempts));

            if (checkpoint.State == ImportBatchItemState.Succeeded) succeeded++;
            else failed++;
        }

        return new ImportBatchStatusView(batchId, statusItems, succeeded, failed, 0);
    }

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

    /// <summary>
    /// Like <see cref="Denied"/>, but for a route any one of several
    /// permissions should open — <see cref="GetJobEndpoint"/>'s "either the
    /// permission that reads or the permission that started the job" rule.
    /// The 403's <c>permission</c> extension names the first of the checked
    /// keys, since a caller refused here holds none of them and the first is
    /// as good a hint as any of what to ask for.
    /// </summary>
    private static IResult? DeniedUnlessAny(ClaimsPrincipal principal, params string[] permissions)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        if (permissions.Any(principal.Permissions().Contains)) return null;

        return Results.Problem(
            detail: $"This account does not hold any of: {string.Join(", ", permissions)}.",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ErrorCodes.PermissionDenied,
                ["permission"] = permissions[0],
            });
    }
}
