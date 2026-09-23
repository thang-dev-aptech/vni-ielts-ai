using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Routing;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Api.Endpoints;

/// <summary>Operator views deliberately separate score metadata from learner/model content.</summary>
public sealed record AdminEvaluationListItem(
    string SessionId, string MarkingId, string Module, int? TaskNumber, string RubricVersion,
    decimal RecomputedBand, decimal? ReportedBand, IReadOnlyList<string> Flags, bool IsCurrent,
    int Version, DateTimeOffset? MarkedAt);

public sealed record AdminEvaluationPage(
    IReadOnlyList<AdminEvaluationListItem> Items, long TotalCount, int Page, int PageSize);

public sealed record AdminCriterionView(
    string Criterion, decimal Band, string Feedback,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Evidence);

public sealed record AdminEvaluationAttemptView(
    string Id, int? TaskNumber, string? Provider, string? Model, string? RequestId,
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Outcome, string? ErrorCode,
    string? ErrorMessage, bool RawOutputTruncated,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RawOutput, string? MarkingId,
    int? MarkingVersion);

public sealed record AdminEvaluationDetail(
    string SessionId, string MarkingId, string Module, int? TaskNumber, string RubricVersion,
    decimal RecomputedBand, decimal? ReportedBand, IReadOnlyList<AdminCriterionView> Criteria,
    IReadOnlyList<string> Flags,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? UngroundedEvidence,
    IReadOnlyList<string> Advisories, WritingMarkingProvenance? Provenance, int Version,
    bool IsCurrent, string? SupersedesId, string? SupersededById, DateTimeOffset? MarkedAt,
    IReadOnlyList<AdminEvaluationAttemptView> Attempts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string?>? LearnerSubmission);

public sealed record FailedMarkingJobView(
    string OperationId, string SessionId, string Module, string RubricVersion, string State,
    int Attempts, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? FailedAt,
    DateTimeOffset? NextAttemptAt, DateTimeOffset? CompletedAt);

public sealed record FailedMarkingJobPage(
    IReadOnlyList<FailedMarkingJobView> Items, long TotalCount, int Page, int PageSize);

/// <summary>
/// A rerun always reaches a provider again. Pricing is deliberately pending: a
/// provider/token-to-VNI policy has not been approved, so this API must say
/// that real cost is incurred without fabricating a figure.
/// </summary>
public sealed record EvaluationRerunView(
    string OperationId, string State, bool Replayed, string CostConsequence,
    string PricingStatus, IReadOnlyList<string> PricingBlockers);

/// <summary>
/// The administrative door for historical AI markings. It has its own endpoint
/// group because this surface is intentionally not composed into the generic
/// admin group: shared Program registration is owned by admin-shared-contracts.
/// </summary>
public static class AdminEvaluationEndpoints
{
    private const int PageSize = 50;
    private const int MaxPage = 10_000;
    private const string EvaluationContentAccessedAction = "EvaluationContentAccessed";
    private const string EvaluationRerunRequestedAction = "EvaluationRerunRequested";

    public static void MapAdminEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/evaluations")
            .WithTags("Admin")
            .RequireAuthorization();

        group.MapGet("", ListEndpoint)
            .WithName("AdminListEvaluations")
            .WithSummary("Paged AI marking history, including current/superseded filters");
        group.MapGet("/failed-jobs", FailedJobsEndpoint)
            .WithName("AdminListFailedMarkingJobs")
            .WithSummary("Paged failed marking queue, newest failure first");
        group.MapGet("/{sessionId}/{markingId}", DetailEndpoint)
            .WithName("AdminGetEvaluation")
            .WithSummary("One marking, its provenance, attempts and supersession links");
        group.MapPost("/failed-jobs/{operationId}/rerun", RerunEndpoint)
            .WithName("AdminRerunEvaluation")
            .WithSummary("Reopen one failed marking job; this can incur a provider charge");
    }

    private static async Task<IResult> ListEndpoint(
        ClaimsPrincipal principal, ISectionMarkingStore markings,
        string? from, string? to, string? module, string? flagged, string? current, int? page,
        HttpContext http, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.EvaluationRead) is { } denial) return denial;
        if (!TryFilters(from, to, module, flagged, current, page, out var query, out var problem))
            return Problem(problem!, http);

        try
        {
            var result = await markings.QueryAsync(query!, ct);
            return Results.Ok(new AdminEvaluationPage(
                result.Items.Select(x => ToListItem(x.SessionId, x.Marking)).ToArray(),
                result.TotalCount, result.Page, result.PageSize));
        }
        catch (ArgumentException exception)
        {
            return Problem(exception.Message, http);
        }
    }

    private static async Task<IResult> DetailEndpoint(
        string sessionId, string markingId, ClaimsPrincipal principal, ISectionMarkingStore markings,
        IEvaluationAttemptStore attempts, IAnswerSheetStore answerSheets,
        IAuditLog audit, IClock clock, bool? includeContent, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.EvaluationRead) is { } denial) return denial;
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(markingId)) return Results.NotFound();

        var id = new ExamSessionId(sessionId);
        // Current-only would 404 the "Thay cho" / "Bị thay bởi" links the
        // detail screen itself renders — a superseded marking is exactly
        // what those links resolve to. → ISectionMarkingStore.ListAllVersionsAsync
        var all = await markings.ListAllVersionsAsync(id, ct);
        var marking = all.FirstOrDefault(x => string.Equals(x.MarkingId, markingId, StringComparison.Ordinal));
        if (marking is null) return Results.NotFound();

        var operationId = MarkingJob.IdFor(id, marking.Module, marking.RubricVersion);
        var history = await attempts.ListByOperationAsync(operationId, ct);
        // The detail screen never expands learner data just because it was
        // opened. An explicit request makes the privacy boundary reviewable in
        // the client and makes one audit row correspond to one content reveal.
        var canReadContent = includeContent == true
            && principal.Permissions().Contains(PermissionKeys.LearnerContentRead);
        IReadOnlyDictionary<string, string?>? submission = null;

        if (canReadContent)
        {
            submission = await answerSheets.LoadAsync(id, marking.Module, ct);
            await RecordContentAccessAsync(audit, principal, sessionId, marking, operationId, clock.UtcNow, ct);
        }

        return Results.Ok(ToDetail(id, marking, history, canReadContent, submission));
    }

    private static async Task<IResult> FailedJobsEndpoint(
        ClaimsPrincipal principal, IMarkingOutbox outbox,
        string? from, string? to, string? module, int? page, HttpContext http, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.EvaluationRead) is { } denial) return denial;
        if (!TryDateModulePage(from, to, module, page, out var parsedFrom, out var parsedTo,
                out var parsedModule, out var current, out var problem))
            return Problem(problem!, http);

        try
        {
            var result = await outbox.QueryAsync(new MarkingJobQuery(
                MarkingJobState.Failed, parsedModule, parsedFrom, parsedTo, current, PageSize), ct);
            return Results.Ok(new FailedMarkingJobPage(
                result.Items.Select(ToFailedJobView).ToArray(), result.TotalCount, result.Page, result.PageSize));
        }
        catch (ArgumentException exception)
        {
            return Problem(exception.Message, http);
        }
    }

    private static async Task<IResult> RerunEndpoint(
        string operationId, ClaimsPrincipal principal, IMarkingOutbox outbox, IAuditLog audit,
        IClock clock, HttpRequest request, HttpContext http, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.EvaluationRerun) is { } denial) return denial;
        if (string.IsNullOrWhiteSpace(operationId)) return Results.NotFound();
        if (!request.Headers.TryGetValue("Idempotency-Key", out var keys)
            || string.IsNullOrWhiteSpace(keys.ToString()))
            return Problem("An Idempotency-Key header is required to rerun a paid evaluation.", http);

        var key = keys.ToString().Trim();
        if (key.Length > 256) return Problem("The Idempotency-Key header is too long.", http);

        var now = clock.UtcNow;
        var result = await outbox.ReopenFailedAsync(operationId, key, now, ct);
        switch (result.Status)
        {
            case MarkingJobReopenStatus.NotFound:
                return Results.NotFound();
            case MarkingJobReopenStatus.Illegal:
                return Conflict("Only a failed marking job can be rerun with a new idempotency key.", http);
            case MarkingJobReopenStatus.Conflict:
                return Conflict("A different rerun request already changed this job.", http);
        }

        // Reopen/replay both return the persisted job. Treat a broken outbox
        // implementation as a conflict rather than dereferencing a nullable
        // result or claiming that a chargeable rerun was accepted.
        if (result.Job is not { } job)
            return Conflict("The marking job could not be reopened.", http);

        // A replay already has a durable audit row from the original transition;
        // writing another would turn a safe retry into misleading activity.
        if (result.Status == MarkingJobReopenStatus.Reopened)
        {
            await audit.AppendAsync(AuditEntry.Record(
                new UserId(principal.UserId()!), ActorLabel(principal),
                Action(EvaluationRerunRequestedAction), "marking-job", operationId, operationId, now,
                EvaluationAudit.Rerun(operationId, job.SessionId.Value, job.Module, job.RubricVersion)), ct);
        }

        return Results.Accepted(
            $"/api/v1/admin/evaluations/failed-jobs/{Uri.EscapeDataString(operationId)}",
            new EvaluationRerunView(operationId, Wire(job.State), result.Status == MarkingJobReopenStatus.Replayed,
                "This schedules another provider evaluation and can incur real provider cost.",
                "pending", ["B-5a", "B-5b"]));
    }

    private static AdminEvaluationListItem ToListItem(ExamSessionId sessionId, SectionMarking marking) => new(
        sessionId.Value, marking.MarkingId ?? string.Empty, Wire(marking.Module), marking.TaskNumber,
        marking.RubricVersion, marking.Band.Value, marking.ReportedBand?.Value,
        marking.Flags.Select(Wire).ToArray(), marking.IsCurrent, marking.Version, marking.MarkedAt);

    private static AdminEvaluationDetail ToDetail(
        ExamSessionId sessionId, SectionMarking marking, IReadOnlyList<EvaluationAttempt> attempts,
        bool includeContent, IReadOnlyDictionary<string, string?>? submission) => new(
        sessionId.Value, marking.MarkingId ?? string.Empty, Wire(marking.Module), marking.TaskNumber,
        marking.RubricVersion, marking.Band.Value, marking.ReportedBand?.Value,
        marking.Criteria.Select(x => new AdminCriterionView(
            x.Criterion, x.Band.Value, x.Feedback, includeContent ? x.Evidence : null)).ToArray(),
        marking.Flags.Select(Wire).ToArray(), includeContent ? marking.UngroundedEvidence : null,
        marking.Advisories ?? [], marking.Provenance, marking.Version, marking.IsCurrent,
        marking.SupersedesId, marking.SupersededById, marking.MarkedAt,
        attempts.Where(x => x.Module == marking.Module && x.TaskNumber == marking.TaskNumber)
            .OrderByDescending(x => x.FinishedAt)
            .Select(x => new AdminEvaluationAttemptView(
                x.Id, x.TaskNumber, x.Provider, x.Model, x.RequestId, x.StartedAt, x.FinishedAt,
                Wire(x.Outcome), x.ErrorCode, x.ErrorMessage, x.RawOutputTruncated,
                includeContent ? x.RawOutput : null, x.MarkingId, x.MarkingVersion)).ToArray(),
        includeContent ? submission : null);

    private static FailedMarkingJobView ToFailedJobView(MarkingJob job) => new(
        job.OperationId, job.SessionId.Value, Wire(job.Module), job.RubricVersion, Wire(job.State),
        job.Attempts, job.LastError, job.CreatedAt, job.FailedAt, job.NextAttemptAt, job.CompletedAt);

    private static async Task RecordContentAccessAsync(
        IAuditLog audit, ClaimsPrincipal principal, string sessionId, SectionMarking marking,
        string operationId, DateTimeOffset at, CancellationToken ct) =>
        await audit.AppendAsync(AuditEntry.Record(
            new UserId(principal.UserId()!), ActorLabel(principal),
            Action(EvaluationContentAccessedAction), "evaluation", marking.MarkingId ?? operationId,
            $"{Wire(marking.Module)} / {sessionId}", at,
            EvaluationAudit.ContentAccess(sessionId, marking.MarkingId, operationId, marking.Module,
                marking.TaskNumber, marking.Version)), ct);

    private static bool TryFilters(
        string? from, string? to, string? module, string? flagged, string? current, int? page,
        out SectionMarkingHistoryQuery? query, out string? problem)
    {
        query = null;
        if (!TryDateModulePage(from, to, module, page, out var parsedFrom, out var parsedTo,
                out var parsedModule, out var parsedPage, out problem)) return false;
        if (!TryOptionalBoolean(flagged, "flagged", out var parsedFlagged, out problem)
            || !TryOptionalBoolean(current, "current", out var parsedCurrent, out problem)) return false;
        query = new SectionMarkingHistoryQuery(parsedFrom, parsedTo, parsedModule, parsedFlagged,
            parsedCurrent, parsedPage, PageSize);
        return true;
    }

    private static bool TryDateModulePage(
        string? from, string? to, string? module, int? page, out DateTimeOffset? parsedFrom,
        out DateTimeOffset? parsedTo, out ExamModule? parsedModule, out int parsedPage, out string? problem)
    {
        parsedFrom = null;
        parsedTo = null;
        parsedModule = null;
        parsedPage = Math.Clamp(page ?? 1, 1, MaxPage);
        problem = null;

        if (!TryDate(from, "from", out parsedFrom, out problem)
            || !TryDate(to, "to", out parsedTo, out problem)) return false;
        if (parsedFrom is { } start && parsedTo is { } end && start > end)
        {
            problem = "The 'from' date must not be after 'to'.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(module)
            && (!Enum.TryParse<ExamModule>(module, true, out var value)
                || !Enum.IsDefined(value)))
        {
            problem = "The module filter is invalid.";
            return false;
        }
        else if (!string.IsNullOrWhiteSpace(module)) parsedModule = Enum.Parse<ExamModule>(module, true);
        return true;
    }

    private static bool TryDate(string? value, string name, out DateTimeOffset? parsed, out string? problem)
    {
        parsed = null;
        problem = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (DateTimeOffset.TryParse(value, out var date))
        {
            parsed = date;
            return true;
        }
        problem = $"The '{name}' filter must be an ISO-8601 date/time.";
        return false;
    }

    private static bool TryOptionalBoolean(string? value, string name, out bool? parsed, out string? problem)
    {
        parsed = null;
        problem = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (bool.TryParse(value, out var answer))
        {
            parsed = answer;
            return true;
        }
        problem = $"The '{name}' filter must be true or false.";
        return false;
    }

    private static IResult Problem(string detail, HttpContext http) => Results.Problem(
        detail: detail, statusCode: StatusCodes.Status400BadRequest, instance: http.Request.Path,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = ErrorCodes.ValidationFailed,
            ["traceId"] = http.TraceIdentifier,
        });

    private static IResult Conflict(string detail, HttpContext http) => Results.Problem(
        detail: detail, statusCode: StatusCodes.Status409Conflict, instance: http.Request.Path,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = ErrorCodes.ValidationFailed,
            ["traceId"] = http.TraceIdentifier,
        });

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

    // Enum values are shared-contract ownership. Parsing by its contract name
    // keeps this endpoint independently compilable until that integration task
    // adds the enum members and the Audit page's Vietnamese labels.
    private static AuditAction Action(string name) => Enum.Parse<AuditAction>(name, ignoreCase: false);

    private static string ActorLabel(ClaimsPrincipal principal) =>
        principal.Email() ?? principal.Phone() ?? principal.UserId() ?? principal.DisplayName();

    private static string Wire<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
}

/// <summary>Audit metadata only: identifiers, state and version; never learner/model text.</summary>
public static class EvaluationAudit
{
    public static IReadOnlyDictionary<string, string> ContentAccess(
        string sessionId, string? markingId, string operationId, ExamModule module, int? taskNumber, int version) =>
        new Dictionary<string, string>
        {
            ["sessionId"] = sessionId,
            ["markingId"] = markingId ?? string.Empty,
            ["operationId"] = operationId,
            ["module"] = module.ToString().ToLowerInvariant(),
            ["taskNumber"] = taskNumber?.ToString() ?? string.Empty,
            ["version"] = version.ToString(),
        };

    public static IReadOnlyDictionary<string, string> Rerun(
        string operationId, string sessionId, ExamModule module, string rubricVersion) =>
        new Dictionary<string, string>
        {
            ["operationId"] = operationId,
            ["sessionId"] = sessionId,
            ["module"] = module.ToString().ToLowerInvariant(),
            ["rubricVersion"] = rubricVersion,
            ["costConsequence"] = "provider-evaluation-may-incur-cost",
            ["pricingStatus"] = "pending",
        };
}
