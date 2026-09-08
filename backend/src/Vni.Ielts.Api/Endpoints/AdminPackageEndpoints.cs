
using System.Security.Claims;
using System.Security.Cryptography;
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
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Api.Endpoints;

/// <summary>
/// CMS package upload and parsed-candidate review routes, extracted from the
/// feature branch's <c>AdminEndpoints</c> so INT can keep main's admin
/// surface intact and map packages beside <c>MapAdminImportEndpoints</c>.
/// </summary>

public sealed record CorrectCandidateRequest(
    int ExpectedVersion,
    string? Title = null,
    bool UpdateTitle = false,
    string? Classification = null,
    IReadOnlyList<ParsedModuleCandidateDto>? Modules = null);

public sealed record ParsedModuleCandidateDto(
    string? Module,
    string Classification,
    decimal? Confidence,
    IReadOnlyList<ParsedPartCandidateDto> Parts,
    ParsedSourceProvenanceDto Provenance);

public sealed record ParsedPartCandidateDto(
    string Id,
    int Order,
    string? Title,
    string? Body,
    IReadOnlyList<ParsedQuestionCandidateDto> Questions,
    ParsedSourceProvenanceDto Provenance);

public sealed record ParsedQuestionCandidateDto(
    string Id,
    int Order,
    string? Type,
    string? Prompt,
    IReadOnlyList<ParsedQuestionOptionDto> Options,
    ParsedAnswerKeyDto? AnswerKey,
    ParsedSourceProvenanceDto Provenance);

public sealed record ParsedQuestionOptionDto(string Key, string Text);

public sealed record ParsedAnswerKeyDto(IReadOnlyList<string> Accepted, string? MatchingRule);

public sealed record ParsedSourceProvenanceDto(string FileName, int? Page, string? Section, string? Reference);

public sealed record CandidateVersionRequest(int ExpectedVersion);

public sealed record CreateCandidateDraftRequest(
    string Variant,
    CandidateDraftTimingDto TimingProfile,
    CandidateDraftScoringDto ScoringProfile,
    IReadOnlyList<CandidateDraftPartCompletionDto>? PartDetails = null);

public sealed record CandidateDraftTimingDto(
    IReadOnlyDictionary<string, CandidateDraftSectionTimingDto> Sections,
    IReadOnlyList<CandidateDraftSpeakingPartTimingDto>? SpeakingParts = null);

public sealed record CandidateDraftSectionTimingDto(
    int DurationSeconds,
    int? TransferTimeSeconds = null);

public sealed record CandidateDraftSpeakingPartTimingDto(
    int Part,
    int PrepSeconds,
    int ResponseSeconds);

public sealed record CandidateDraftScoringDto(
    IReadOnlyDictionary<string, IReadOnlyList<CandidateDraftBandBoundaryDto>>? RawToBand = null,
    string? ScoringProfileRef = null,
    CandidateDraftCriterionWeightsDto? CriterionWeights = null);

public sealed record CandidateDraftBandBoundaryDto(int MinRaw, decimal Band);

public sealed record CandidateDraftCriterionWeightsDto(decimal Task1, decimal Task2);

public sealed record CandidateDraftPartCompletionDto(
    int PartOrder,
    string? Kind = null,
    int? TaskNumber = null,
    int? PartNumber = null,
    string? AudioAssetRef = null,
    string? ImageAssetRef = null);


public static class AdminPackageEndpoints
{
    public static void MapAdminPackageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.InSessionRead);

        group.MapGet("/packages", PackagesEndpoint)
            .WithName("AdminListExamPackages")
            .WithSummary("Every uploaded content package, newest first");

        group.MapGet("/packages/{packageId}", PackageEndpoint)
            .WithName("AdminGetExamPackage")
            .WithSummary("One package's status, findings, and entries");

        group.MapDelete("/packages/{packageId}", DeletePackageEndpoint)
            .WithName("AdminDeleteExamPackage")
            .WithSummary("Remove a package and cascade-delete any still-Draft versions it created");

        group.MapPost("/packages", UploadPackageEndpoint)
            .WithName("AdminUploadExamPackage")
            .WithSummary("Receive a JSON or ZIP content package for import")
            .DisableAntiforgery();

        group.MapPost("/packages/{packageId}/confirm", ConfirmPackageEndpoint)
            .WithName("AdminConfirmExamPackage")
            .WithSummary("Create a Draft for every exam a multi-exam ZIP found");

        group.MapGet("/packages/{packageId}/candidates", PackageCandidatesEndpoint)
            .WithName("AdminListPackageCandidates")
            .WithSummary("Parsed exam candidates awaiting or completed review for a package");

        group.MapGet("/packages/{packageId}/candidates/{candidateId}", PackageCandidateEndpoint)
            .WithName("AdminGetPackageCandidate")
            .WithSummary("One parsed exam candidate detail including modules, provenance, and corrections");

        group.MapPost("/packages/{packageId}/candidates/{candidateId}/correct", CorrectPackageCandidateEndpoint)
            .WithName("AdminCorrectPackageCandidate")
            .WithSummary("Apply corrections to a parsed candidate with optimistic concurrency");

        group.MapPost("/packages/{packageId}/candidates/{candidateId}/reject", RejectPackageCandidateEndpoint)
            .WithName("AdminRejectPackageCandidate")
            .WithSummary("Reject a parsed candidate with optimistic concurrency");

        group.MapPost("/packages/{packageId}/candidates/{candidateId}/confirm", ConfirmPackageCandidateEndpoint)
            .WithName("AdminConfirmPackageCandidate")
            .WithSummary("Confirm review of a parsed candidate with optimistic concurrency");

        group.MapPost("/packages/{packageId}/candidates/{candidateId}/draft", CreatePackageCandidateDraftEndpoint)
            .WithName("AdminCreatePackageCandidateDraft")
            .WithSummary("Create a Draft exam version from a confirmed parsed candidate");
    }

    /// <summary>The CMS Import screen's history/polling surface — every package, newest first.</summary>
    private static async Task<IResult> PackagesEndpoint(
        ClaimsPrincipal principal, IExamPackageRepository packages, IUserRepository users, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.PackageRead) is { } denied) return denied;

        var all = await packages.ListAllAsync(ct);
        var names = await DisplayNamesOf(users, all.Select(p => p.UploadedBy.Value), ct);

        return Results.Ok(all.Select(p => ToPackageWire(p, names)));
    }

    /// <summary>
    /// One package — what the Import screen polls after an upload, to learn
    /// when structural validation finished and what it found.
    /// </summary>
    private static async Task<IResult> PackageEndpoint(
        string packageId, ClaimsPrincipal principal,
        IExamPackageRepository packages, IUserRepository users, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.PackageRead) is { } denied) return denied;

        var package = await packages.FindAsync(packageId, ct);
        if (package is null) return Results.NotFound();

        var names = await DisplayNamesOf(users, [package.UploadedBy.Value], ct);
        return Results.Ok(ToPackageWire(package, names));
    }

    /// <summary>
    /// Hard-deletes a package and any <see cref="ExamVersionStatus.Draft"/>
    /// versions listed in its <c>createdVersionIds</c>.
    ///
    /// <b>Symmetric with <see cref="DeleteExamEndpoint"/>.</b> A draft cannot
    /// be deleted while a package still names it; deleting the package is what
    /// clears that reference — and it must take the drafts with it, otherwise
    /// the provenance list becomes a set of ids that resolve to nothing.
    ///
    /// <b>Anything that left Draft blocks the whole operation.</b> A submitted
    /// or published version is process evidence (and for published ones, a
    /// sitting foreign key). Refusing keeps both the package row and the
    /// version, rather than orphaning either side.
    /// </summary>
    private static async Task<IResult> DeletePackageEndpoint(
        string packageId, ClaimsPrincipal principal, IExamPackageRepository packages,
        IExamCatalogue catalogue, IPackageCascadeDelete cascade, IClock clock, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.PackageDelete) is { } denial) return denial;

        var package = await packages.FindAsync(packageId, ct);
        if (package is null) return Results.NotFound();

        var drafts = new List<ExamVersion>();
        foreach (var versionId in package.CreatedVersionIds)
        {
            var version = await catalogue.FindAsync(new ExamVersionId(versionId), ct);
            if (version is null) continue;

            if (version.Status != ExamVersionStatus.Draft)
            {
                return Conflict(
                    $"Gói này đã tạo ra đề đang {StatusLabelVi(version.Status)}, không xoá được.");
            }

            drafts.Add(version);
        }

        var now = clock.UtcNow;
        try
        {
            await cascade.CascadeDeleteAsync(
                package,
                drafts,
                new UserId(principal.UserId()!),
                principal.Email() ?? principal.DisplayName() ?? "unknown",
                now,
                ct);
        }
        catch (PackageCascadeConflictException ex)
        {
            return Conflict(ex.Message);
        }

        return Results.NoContent();
    }

    private static string StatusLabelVi(ExamVersionStatus status) => status switch
    {
        ExamVersionStatus.Draft => "bản nháp",
        ExamVersionStatus.InReview => "chờ duyệt",
        ExamVersionStatus.Approved => "đã duyệt",
        ExamVersionStatus.Published => "xuất bản",
        ExamVersionStatus.Unpublished => "đã gỡ xuất bản",
        _ => status.ToWire(),
    };

    private static object ToPackageWire(ExamPackage package, IReadOnlyDictionary<string, string> uploaderNames) => new
    {
        packageId = package.Id,
        sourceKind = package.SourceKind.ToString().ToLowerInvariant(),
        fileName = package.FileName,
        status = package.Status.ToPackageWireStatus(),
        uploadedByName = uploaderNames.GetValueOrDefault(package.UploadedBy.Value, "(tài khoản đã xoá)"),
        findings = package.Findings.Select(f => new
        {
            stage = f.Stage,
            code = f.Code,
            pointer = f.Pointer,
            message = f.Message,
        }),
        entries = package.Entries.Select(e => new
        {
            proposedDefinitionId = e.ProposedDefinitionId,
            title = e.Title,
            module = e.Module.ToString().ToLowerInvariant(),
            questionCount = e.QuestionCount,
        }),
        createdVersionIds = package.CreatedVersionIds,
        createdAt = package.CreatedAt,
        updatedAt = package.UpdatedAt,
    };

    public const long MaxPackageBytes = 2L * 1024 * 1024 * 1024;

    private static async Task<IResult> UploadPackageEndpoint(
        HttpRequest request,
        ClaimsPrincipal principal,
        IExamPackageRepository packages,
        IPackageUploadStore uploads,
        IAuditLog audit,
        IClock clock,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.PackageUpload) is { } deniedUpload) return deniedUpload;
        if (Denied(principal, PermissionKeys.ExamCreate) is { } deniedCreate) return deniedCreate;

        if (!request.HasFormContentType)
            return Problem(
                ErrorCodes.ValidationFailed, "Expected a multipart upload.", StatusCodes.Status400BadRequest);

        var form = await request.ReadFormAsync(
            new Microsoft.AspNetCore.Http.Features.FormOptions { MultipartBodyLengthLimit = MaxPackageBytes },
            ct);
        var file = form.Files.GetFile("package");
        if (file is null || file.Length == 0)
            return Problem(
                ErrorCodes.ValidationFailed,
                "A package needs a file part named \"package\".",
                StatusCodes.Status400BadRequest);

        if (file.Length > MaxPackageBytes)
            return Problem(
                ErrorCodes.PayloadTooLarge,
                "That package is larger than this pipeline accepts.",
                StatusCodes.Status413PayloadTooLarge);

        ExamPackageSourceKind? sourceKind =
            file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? ExamPackageSourceKind.Json
            : file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ExamPackageSourceKind.Zip
            : null;

        if (sourceKind is null)
            return Problem(
                ErrorCodes.ValidationFailed,
                "A package must be a .json or .zip file.",
                StatusCodes.Status400BadRequest);

        var contentType = sourceKind == ExamPackageSourceKind.Json ? "application/json" : "application/zip";

        await using var content = file.OpenReadStream();
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(content, ct)).ToLowerInvariant();
        content.Position = 0;
        var uploadRef = await uploads.SaveAsync(content, file.FileName, contentType, ct);

        var now = clock.UtcNow;
        var package = ExamPackage.Create(
            Guid.NewGuid().ToString("n"), sourceKind.Value, new UserId(principal.UserId()!),
            sha256, file.FileName, uploadRef, now);

        await packages.SaveAsync(package, ct);

        await Record(
            audit, principal, AuditAction.PackageUploaded,
            "package", package.Id, package.FileName, now, ct,
            new Dictionary<string, string> { ["sourceKind"] = package.SourceKind.ToString() });

        return Results.Accepted(
            value: new { packageId = package.Id, status = "uploaded" });
    }

    private static async Task<IResult> ConfirmPackageEndpoint(
        string packageId,
        ClaimsPrincipal principal,
        IExamPackageRepository packages,
        IPackageUploadStore uploads,
        PackageStructuralValidator validator,
        IPackageImportTransaction import,
        IAuditLog audit,
        IClock clock,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.PackageConfirm) is { } deniedConfirm) return deniedConfirm;
        if (Denied(principal, PermissionKeys.ExamCreate) is { } deniedCreate) return deniedCreate;

        var package = await packages.FindAsync(packageId, ct);
        if (package is null) return Results.NotFound();

        if (package.Status != PackageImportStatus.ReadyToImport)
        {
            return Problem(
                ErrorCodes.ValidationFailed,
                $"This package is {package.Status}, not ReadyToImport. It cannot be confirmed.",
                StatusCodes.Status409Conflict);
        }

        var expectedVersion = package.Version;
        var confirmedBy = new UserId(principal.UserId()!);

        await using var gridFsStream = await uploads.OpenAsync(package.UploadRef, ct);
        var outcome = await PackageContentStaging.WithStagedContentAsync(
            gridFsStream,
            staged => Task.FromResult(validator.ValidateZip(staged, confirmedBy)),
            ct);

        if (!outcome.IsValid || outcome.Entries.Count != package.Entries.Count)
        {
            package.MarkFailed(clock.UtcNow);
            await packages.SaveAsync(package, ct);
            return Problem(
                ErrorCodes.ValidationFailed,
                "This package no longer validates the way it did when it was marked ready to import.",
                StatusCodes.Status409Conflict);
        }

        IReadOnlyList<ExamVersionId> createdIds;
        try
        {
            createdIds = await import.ImportAsync(package, expectedVersion, outcome.Versions, ct);
        }
        catch (PackageConcurrencyException)
        {
            return Conflict("Package đã bị thay đổi bởi một yêu cầu khác. Tải lại và thử xác nhận lần nữa.");
        }

        var now = clock.UtcNow;
        await Record(
            audit, principal, AuditAction.PackageImported,
            "package", package.Id, package.FileName, now, ct,
            new Dictionary<string, string>
            {
                ["examCount"] = createdIds.Count.ToString(),
                ["examVersionIds"] = string.Join(",", createdIds.Select(id => id.Value)),
            });

        return Results.Ok(new
        {
            packageId = package.Id,
            createdVersionIds = createdIds.Select(id => id.Value),
        });
    }

    private static async Task<IResult> PackageCandidatesEndpoint(
        string packageId,
        ClaimsPrincipal principal,
        ListParsedExamCandidates listHandler,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.PackageRead) is { } denied) return denied;

        var actor = new ParsedCandidateActor(
            new UserId(principal.UserId()!),
            ActorLabel(principal),
            principal.Permissions());

        var result = await listHandler.HandleAsync(new ListParsedExamCandidatesQuery(actor, packageId), ct);
        if (!result.IsSuccess) return FromError(result.Error);

        return Results.Ok(result.Value!.Select(ToCandidateSummaryWire));
    }

    private static async Task<IResult> PackageCandidateEndpoint(
        string packageId,
        string candidateId,
        ClaimsPrincipal principal,
        GetParsedExamCandidate getHandler,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.PackageRead) is { } denied) return denied;

        var actor = new ParsedCandidateActor(
            new UserId(principal.UserId()!),
            ActorLabel(principal),
            principal.Permissions());

        var result = await getHandler.HandleAsync(new GetParsedExamCandidateQuery(actor, packageId, candidateId), ct);
        if (!result.IsSuccess) return FromError(result.Error);

        return Results.Ok(ToCandidateDetailWire(result.Value!));
    }

    private static async Task<IResult> CorrectPackageCandidateEndpoint(
        string packageId,
        string candidateId,
        CorrectCandidateRequest request,
        ClaimsPrincipal principal,
        ReviewParsedExamCandidate reviewService,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamReview) is { } denied) return denied;

        var actor = new ParsedCandidateActor(
            new UserId(principal.UserId()!),
            ActorLabel(principal),
            principal.Permissions());

        ParsedExamClassification? classification = null;
        if (!string.IsNullOrWhiteSpace(request.Classification))
        {
            classification = ParsedExamClassificationExtensions.ParseWire(request.Classification);
        }

        IReadOnlyList<ParsedModuleCandidate>? modules = null;
        if (request.Modules is not null)
        {
            modules = request.Modules.Select(MapDtoToModuleCandidate).ToList();
        }

        var command = new CorrectParsedCandidateCommand(
            actor,
            packageId,
            candidateId,
            request.ExpectedVersion,
            request.Title,
            request.UpdateTitle,
            classification,
            modules);

        var result = await reviewService.CorrectAsync(command, ct);
        if (!result.IsSuccess) return FromError(result.Error);

        return Results.Ok(ToCandidateDetailWire(result.Value!));
    }

    private static async Task<IResult> RejectPackageCandidateEndpoint(
        string packageId,
        string candidateId,
        CandidateVersionRequest request,
        ClaimsPrincipal principal,
        ReviewParsedExamCandidate reviewService,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamReview) is { } denied) return denied;

        var actor = new ParsedCandidateActor(
            new UserId(principal.UserId()!),
            ActorLabel(principal),
            principal.Permissions());

        var command = new RejectParsedCandidateCommand(actor, packageId, candidateId, request.ExpectedVersion);
        var result = await reviewService.RejectAsync(command, ct);
        if (!result.IsSuccess) return FromError(result.Error);

        return Results.Ok(ToCandidateDetailWire(result.Value!));
    }

    private static async Task<IResult> ConfirmPackageCandidateEndpoint(
        string packageId,
        string candidateId,
        CandidateVersionRequest request,
        ClaimsPrincipal principal,
        ReviewParsedExamCandidate reviewService,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamReview) is { } denied) return denied;

        var actor = new ParsedCandidateActor(
            new UserId(principal.UserId()!),
            ActorLabel(principal),
            principal.Permissions());

        var command = new ConfirmParsedCandidateCommand(actor, packageId, candidateId, request.ExpectedVersion);
        var result = await reviewService.ConfirmAsync(command, ct);
        if (!result.IsSuccess) return FromError(result.Error);

        return Results.Ok(ToCandidateDetailWire(result.Value!));
    }

    private static async Task<IResult> CreatePackageCandidateDraftEndpoint(
        string packageId,
        string candidateId,
        CreateCandidateDraftRequest request,
        ClaimsPrincipal principal,
        CreateDraftFromParsedCandidate draftHandler,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ExamCreate) is { } denied) return denied;

        var actor = new ParsedCandidateActor(
            new UserId(principal.UserId()!),
            ActorLabel(principal),
            principal.Permissions());

        var completionResult = MapDtoToCompletionData(request);
        if (!completionResult.IsSuccess) return FromError(completionResult.Error);

        var command = new CreateDraftFromParsedCandidateCommand(
            actor,
            packageId,
            candidateId,
            completionResult.Value!);

        try
        {
            var result = await draftHandler.HandleAsync(command, ct);
            if (!result.IsSuccess) return FromError(result.Error);

            var draft = result.Value!;
            return Results.Created($"/api/v1/admin/exams/{draft.Id.Value}", new
            {
                examVersionId = draft.Id.Value,
                definitionId = draft.DefinitionId.Value,
                versionNumber = draft.VersionNumber,
                status = draft.Status.ToWire(),
            });
        }
        catch (CandidateCanonicalValidationException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = ErrorCodes.CandidateCompletionInvalid,
                    ["errors"] = ex.Findings.Select(f => new
                    {
                        path = f.Path,
                        code = f.Code,
                        message = f.Message,
                    }).ToArray(),
                    ["findings"] = ex.Findings.Select(f => new
                    {
                        stage = "canonical_validation",
                        code = f.Code,
                        pointer = f.Path,
                        message = f.Message,
                        severity = f.Severity,
                    }).ToArray(),
                });
        }
    }

    private static Result<CandidateCompletionData> MapDtoToCompletionData(CreateCandidateDraftRequest request)
    {
        if (request is null)
        {
            return Error.Validation(ErrorCodes.CandidateCompletionMissing, "Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Variant) ||
            !Enum.TryParse<ExamVariant>(request.Variant, true, out var variant))
        {
            return Error.Validation(ErrorCodes.CandidateCompletionInvalid, "A valid exam variant ('academic' or 'general') is required.");
        }

        if (request.TimingProfile?.Sections is null || request.TimingProfile.Sections.Count == 0)
        {
            return Error.Validation(ErrorCodes.CandidateCompletionMissing, "Timing profile sections are required.");
        }

        var timingSections = new Dictionary<ExamModule, CandidateSectionTiming>();
        foreach (var (modKey, timing) in request.TimingProfile.Sections)
        {
            if (!Enum.TryParse<ExamModule>(modKey, true, out var mod))
            {
                return Error.Validation(ErrorCodes.CandidateCompletionInvalid, $"Invalid module key in timing profile: '{modKey}'.");
            }
            if (timing is null || timing.DurationSeconds <= 0)
            {
                return Error.Validation(ErrorCodes.CandidateCompletionInvalid, $"DurationSeconds must be positive for module '{modKey}'.");
            }
            timingSections[mod] = new CandidateSectionTiming(timing.DurationSeconds, timing.TransferTimeSeconds);
        }

        IReadOnlyList<CandidateSpeakingPartTiming>? speakingParts = null;
        if (request.TimingProfile.SpeakingParts is not null)
        {
            speakingParts = request.TimingProfile.SpeakingParts
                .Select(sp => new CandidateSpeakingPartTiming(sp.Part, sp.PrepSeconds, sp.ResponseSeconds))
                .ToList();
        }

        var timingProfile = new CandidateTimingProfile(timingSections, speakingParts);

        if (request.ScoringProfile is null)
        {
            return Error.Validation(ErrorCodes.CandidateCompletionMissing, "Scoring profile is required.");
        }

        Dictionary<ExamModule, IReadOnlyList<CandidateBandBoundary>>? rawToBand = null;
        if (request.ScoringProfile.RawToBand is not null)
        {
            rawToBand = new Dictionary<ExamModule, IReadOnlyList<CandidateBandBoundary>>();
            foreach (var (modKey, boundaries) in request.ScoringProfile.RawToBand)
            {
                if (!Enum.TryParse<ExamModule>(modKey, true, out var mod))
                {
                    return Error.Validation(ErrorCodes.CandidateCompletionInvalid, $"Invalid module key in scoring profile: '{modKey}'.");
                }
                rawToBand[mod] = boundaries?.Select(b => new CandidateBandBoundary(b.MinRaw, b.Band)).ToList()
                                 ?? (IReadOnlyList<CandidateBandBoundary>)Array.Empty<CandidateBandBoundary>();
            }
        }

        CandidateCriterionWeights? criterionWeights = null;
        if (request.ScoringProfile.CriterionWeights is not null)
        {
            criterionWeights = new CandidateCriterionWeights(
                request.ScoringProfile.CriterionWeights.Task1,
                request.ScoringProfile.CriterionWeights.Task2);
        }

        var scoringProfile = new CandidateScoringProfile(
            rawToBand,
            request.ScoringProfile.ScoringProfileRef,
            criterionWeights);

        IReadOnlyList<CandidatePartCompletion>? partCompletions = null;
        if (request.PartDetails is not null)
        {
            partCompletions = request.PartDetails.Select(p => new CandidatePartCompletion(
                p.PartOrder,
                p.Kind,
                p.TaskNumber,
                p.PartNumber,
                p.AudioAssetRef,
                p.ImageAssetRef)).ToList();
        }

        return new CandidateCompletionData(variant, timingProfile, scoringProfile, partCompletions);
    }

    private static object ToCandidateSummaryWire(ParsedExamCandidate candidate) => new
    {
        candidateId = candidate.Id,
        packageId = candidate.PackageId,
        title = candidate.Title,
        classification = candidate.Classification.ToWire(),
        confidence = candidate.Confidence,
        status = candidate.Status.ToWire(),
        version = candidate.Version,
        draftExamVersionId = candidate.DraftExamVersionId,
        moduleCount = candidate.Modules.Count,
        questionCount = candidate.Modules.Sum(m => m.Parts.Sum(p => p.Questions.Count)),
        unresolvedCount = CalculateUnresolvedCount(candidate),
        sources = candidate.Sources.Select(s => new
        {
            fileName = s.FileName,
            page = s.Page,
            section = s.Section,
            reference = s.Reference,
        }),
    };

    private static object ToCandidateDetailWire(ParsedExamCandidate candidate) => new
    {
        candidateId = candidate.Id,
        packageId = candidate.PackageId,
        title = candidate.Title,
        classification = candidate.Classification.ToWire(),
        confidence = candidate.Confidence,
        status = candidate.Status.ToWire(),
        version = candidate.Version,
        draftExamVersionId = candidate.DraftExamVersionId,
        unresolvedCount = CalculateUnresolvedCount(candidate),
        sources = candidate.Sources.Select(s => new
        {
            fileName = s.FileName,
            page = s.Page,
            section = s.Section,
            reference = s.Reference,
        }),
        modules = candidate.Modules.Select(m => new
        {
            module = m.Module?.ToString().ToLowerInvariant(),
            classification = m.Classification.ToWire(),
            confidence = m.Confidence,
            provenance = new
            {
                fileName = m.Provenance.FileName,
                page = m.Provenance.Page,
                section = m.Provenance.Section,
                reference = m.Provenance.Reference,
            },
            parts = m.Parts.Select(p => new
            {
                id = p.Id,
                order = p.Order,
                title = p.Title,
                body = p.Body,
                provenance = new
                {
                    fileName = p.Provenance.FileName,
                    page = p.Provenance.Page,
                    section = p.Provenance.Section,
                    reference = p.Provenance.Reference,
                },
                questions = p.Questions.Select(q => new
                {
                    id = q.Id,
                    order = q.Order,
                    type = q.Type?.ToPreviewWire(),
                    prompt = q.Prompt,
                    options = q.Options.Select(o => new { key = o.Key, text = o.Text }),
                    answerKey = q.AnswerKey is null ? null : new
                    {
                        accepted = q.AnswerKey.Accepted,
                        matchingRule = q.AnswerKey.MatchingRule,
                    },
                    provenance = new
                    {
                        fileName = q.Provenance.FileName,
                        page = q.Provenance.Page,
                        section = q.Provenance.Section,
                        reference = q.Provenance.Reference,
                    },
                }),
            }),
        }),
        corrections = candidate.Corrections.Select(c => new
        {
            id = c.Id,
            reviewerId = c.ReviewerId.Value,
            field = c.Field.ToString().ToLowerInvariant(),
            targetId = c.TargetId,
            previousValue = c.PreviousValue,
            newValue = c.NewValue,
            at = c.At,
        }),
        confirmedBy = candidate.ConfirmedBy?.Value,
        confirmedAt = candidate.ConfirmedAt,
        rejectedBy = candidate.RejectedBy?.Value,
        rejectedAt = candidate.RejectedAt,
    };

    private static int CalculateUnresolvedCount(ParsedExamCandidate candidate)
    {
        var count = 0;
        if (candidate.Classification is ParsedExamClassification.Unclassified or ParsedExamClassification.NeedsReview)
            count++;
        if (string.IsNullOrWhiteSpace(candidate.Title))
            count++;
        if (candidate.Modules.Count == 0)
            count++;
        foreach (var m in candidate.Modules)
        {
            if (m.Parts.Count == 0) count++;
            foreach (var p in m.Parts)
            {
                if (p.Questions.Count == 0) count++;
                foreach (var q in p.Questions)
                {
                    if (string.IsNullOrWhiteSpace(q.Prompt)) count++;
                    if (q.AnswerKey is null || q.AnswerKey.Accepted.Count == 0) count++;
                }
            }
        }
        return count;
    }

    private static ParsedModuleCandidate MapDtoToModuleCandidate(ParsedModuleCandidateDto dto)
    {
        ExamModule? mod = null;
        if (!string.IsNullOrWhiteSpace(dto.Module) && Enum.TryParse<ExamModule>(dto.Module, true, out var parsedMod))
        {
            mod = parsedMod;
        }

        var parts = dto.Parts.Select(p => new ParsedPartCandidate(
            p.Id,
            p.Order,
            p.Title,
            p.Body,
            p.Questions.Select(q => new ParsedQuestionCandidate(
                q.Id,
                q.Order,
                ParseQuestionType(q.Type),
                q.Prompt,
                q.Options.Select(o => new ParsedQuestionOptionCandidate(o.Key, o.Text)).ToList(),
                q.AnswerKey is null ? null : new ParsedAnswerKeyCandidate(q.AnswerKey.Accepted, q.AnswerKey.MatchingRule),
                new ParsedSourceProvenance(q.Provenance.FileName, q.Provenance.Page, q.Provenance.Section, q.Provenance.Reference)
            )).ToList(),
            new ParsedSourceProvenance(p.Provenance.FileName, p.Provenance.Page, p.Provenance.Section, p.Provenance.Reference)
        )).ToList();

        return new ParsedModuleCandidate(
            mod,
            ParsedExamClassificationExtensions.ParseWire(dto.Classification),
            dto.Confidence,
            parts,
            new ParsedSourceProvenance(dto.Provenance.FileName, dto.Provenance.Page, dto.Provenance.Section, dto.Provenance.Reference));
    }

    private static QuestionType? ParseQuestionType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;
        var normalized = type.Replace("-", "").Replace("_", "");
        return Enum.TryParse<QuestionType>(normalized, true, out var qt) ? qt : null;
    }

    private static IResult FromError(Error error) => error.Kind switch
    {
        ErrorKind.NotFound => Results.NotFound(new { code = error.Code, message = error.Detail }),
        ErrorKind.Forbidden => Results.Problem(
            detail: error.Detail,
            statusCode: StatusCodes.Status403Forbidden,
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

    private static async Task<Dictionary<string, string>> DisplayNamesOf(
        IUserRepository users, IEnumerable<string?> ids, CancellationToken ct)
    {
        var names = new Dictionary<string, string>();

        foreach (var id in ids.Where(id => id is not null).Select(id => id!).Distinct())
        {
            var user = await users.FindByIdAsync(new UserId(id), ct);
            // INT: Email is Email? — prefer address, then phone, then id.
            names[id] = user is null
                ? "(tài khoản đã xoá)"
                : (user.Email?.Value ?? user.Phone?.Value ?? user.Id.Value);
        }

        return names;
    }

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

    private static string ActorLabel(ClaimsPrincipal principal) =>
        principal.Email()
        ?? principal.Phone()
        ?? principal.UserId()
        ?? principal.DisplayName();

    private static IResult Conflict(string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ValidationFailed });

    private static IResult Problem(string code, string detail, int status) =>
        Results.Problem(
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static IResult? Denied(ClaimsPrincipal principal, string permission)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        return principal.Permissions().Contains(permission) ? null : Forbidden(permission);
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

internal static class PackageImportStatusExtensions
{
    public static string ToPackageWireStatus(this PackageImportStatus status) => status switch
    {
        PackageImportStatus.Uploaded => "uploaded",
        PackageImportStatus.Scanning => "scanning",
        PackageImportStatus.Validating => "validating",
        PackageImportStatus.Parsing => "parsing",
        PackageImportStatus.NeedsReview => "needs-review",
        PackageImportStatus.ReadyToImport => "ready-to-import",
        PackageImportStatus.Imported => "imported",
        PackageImportStatus.Rejected => "rejected",
        PackageImportStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped package import status."),
    };
}

/// <summary>
/// The same kebab-case wire spelling as <c>Application.Exams.ExamViewMapping.ToWire</c>,
/// duplicated rather than referenced — that mapping is <c>internal</c> to a
/// different assembly, and this is the only other caller that needs it.
/// </summary>
internal static class QuestionTypeWireExtensions
{
    public static string ToPreviewWire(this QuestionType type) => type switch
    {
        QuestionType.MultipleChoice => "multiple-choice",
        QuestionType.MultipleSelect => "multiple-select",
        QuestionType.TrueFalseNotGiven => "true-false-notgiven",
        QuestionType.YesNoNotGiven => "yes-no-notgiven",
        QuestionType.Matching => "matching",
        QuestionType.Completion => "completion",
        QuestionType.ShortAnswer => "short-answer",
        QuestionType.Labelling => "labelling",
        QuestionType.EssayTask => "essay-task",
        QuestionType.SpeakingResponse => "speaking-response",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unmapped question type."),
    };
}

internal static class ParsedExamClassificationExtensions
{
    public static string ToWire(this ParsedExamClassification c) => c switch
    {
        ParsedExamClassification.Reading => "reading",
        ParsedExamClassification.Listening => "listening",
        ParsedExamClassification.Writing => "writing",
        ParsedExamClassification.Speaking => "speaking",
        ParsedExamClassification.Unclassified => "unclassified",
        ParsedExamClassification.NeedsReview => "needs-review",
        _ => "unclassified",
    };

    public static ParsedExamClassification ParseWire(string? s) => s?.ToLowerInvariant() switch
    {
        "reading" => ParsedExamClassification.Reading,
        "listening" => ParsedExamClassification.Listening,
        "writing" => ParsedExamClassification.Writing,
        "speaking" => ParsedExamClassification.Speaking,
        "unclassified" => ParsedExamClassification.Unclassified,
        "needs-review" or "needs_review" => ParsedExamClassification.NeedsReview,
        _ => ParsedExamClassification.Unclassified,
    };
}

internal static class ParsedCandidateStatusExtensions
{
    public static string ToWire(this ParsedCandidateStatus s) => s switch
    {
        ParsedCandidateStatus.PendingReview => "pending-review",
        ParsedCandidateStatus.Confirmed => "confirmed",
        ParsedCandidateStatus.Rejected => "rejected",
        _ => "pending-review",
    };
}
