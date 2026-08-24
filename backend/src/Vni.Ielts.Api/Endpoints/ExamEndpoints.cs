using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Api.Endpoints;

public sealed record StartSessionRequest(string ExamVersionId, string Mode, string? Module);

public sealed record SaveAnswersRequest(
    string Module, IReadOnlyDictionary<string, string?> Answers);

/// <summary>
/// The exam catalogue and one learner's sittings.
///
/// <b>Every route here is authenticated.</b> Exam content is not public: a
/// passage handed to an anonymous caller is a passage that can be scraped
/// alongside the answers a signed-in caller later submits, which turns the
/// catalogue into a public answer key by correlation.
/// </summary>
public static class ExamEndpoints
{
    public static void MapExamEndpoints(this IEndpointRouteBuilder app)
    {
        var exams = app.MapGroup("/api/v1/exams").WithTags("Exams").RequireAuthorization();

        exams.MapGet("/", ListExamsEndpoint)
            .WithName("ListExams")
            .WithSummary("The exams this learner can sit");

        exams.MapGet("/assets/{**reference}", AssetEndpoint)
            .WithName("GetExamAsset")
            .WithSummary("Listening audio and question images");

        var sessions = app.MapGroup("/api/v1/sessions").WithTags("Exams").RequireAuthorization();

        sessions.MapPost("/", StartEndpoint)
            .WithName("StartExamSession")
            .WithSummary("Open a sitting")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        sessions.MapGet("/", ListMineEndpoint)
            .WithName("ListMySittings")
            .WithSummary("The caller's own recent sittings, newest first");

        sessions.MapGet("/{sessionId}", GetEndpoint)
            .WithName("GetExamSession")
            .WithSummary("The open section, its questions and the server deadline");

        sessions.MapPut("/{sessionId}/answers", SaveAnswersEndpoint)
            .WithName("SaveExamAnswers")
            .WithSummary("Autosave the sheet for the section in progress");

        sessions.MapPost("/{sessionId}/recordings", UploadRecordingEndpoint)
            .WithName("UploadSpeakingRecording")
            .WithSummary("Store one Speaking answer and return its id")
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        sessions.MapPost("/{sessionId}/advance", AdvanceEndpoint)
            .WithName("AdvanceExamSection")
            .WithSummary("Full Test only: close this section and open the next");

        sessions.MapPost("/{sessionId}/submit", SubmitEndpoint)
            .WithName("SubmitExamSession")
            .WithSummary("Submit the sitting and mark what can be marked");

        sessions.MapGet("/{sessionId}/results", ResultsEndpoint)
            .WithName("GetExamResults")
            .WithSummary("Bands for the sections that have been marked");
    }

    /// <summary>
    /// The learner's own history.
    ///
    /// <b>No user parameter, by design.</b> The identity comes from the token,
    /// so there is no route or query through which one learner could ask for
    /// another's sittings — the same reasoning as every other endpoint in this
    /// group, applied to a list rather than to one record.
    /// </summary>
    private static async Task<IResult> ListMineEndpoint(
        ClaimsPrincipal principal, ListMySittings handler, int? limit, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        var sittings = await handler.HandleAsync(
            new ListMySittingsQuery(new UserId(id), limit ?? 10), ct);

        return Results.Ok(new { sittings });
    }

    private static async Task<IResult> ListExamsEndpoint(
        ClaimsPrincipal principal, ListExams handler, CancellationToken ct)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        return Results.Ok(new { exams = await handler.HandleAsync(new ListExamsQuery(), ct) });
    }

    /// <summary>
    /// Exam media.
    ///
    /// <b>Authenticated like everything else here.</b> Listening audio is exam
    /// content: served anonymously it can be collected, transcribed and
    /// published as an answer key by anyone who can guess a filename.
    ///
    /// The store treats the reference as hostile and returns null rather than
    /// throwing on anything it does not like, so a traversal attempt and a
    /// typo produce the same 404 — an attacker learns nothing from the
    /// difference.
    /// </summary>
    private static IResult AssetEndpoint(
        string reference, ClaimsPrincipal principal, IExamAssetStore assets)
    {
        if (principal.UserId() is null) return Results.Unauthorized();

        if (assets.Open($"assets/{reference}") is not { } asset)
            return Results.NotFound();

        // Range requests matter: an audio element seeks by asking for byte
        // ranges, and without this the browser re-downloads from zero.
        return Results.Stream(asset.Content, asset.ContentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> StartEndpoint(
        ClaimsPrincipal principal, StartSessionRequest request, StartExamSession handler,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        if (!TryParseMode(request.Mode, out var mode))
            return Problem("MODE_INVALID", "Mode must be 'full' or 'single'.", StatusCodes.Status400BadRequest);

        ExamModule? module = null;
        if (mode == SessionMode.Single)
        {
            if (!TryParseModule(request.Module, out var parsed))
                return Problem(
                    "MODULE_INVALID",
                    "A single-skill session must name reading, listening, writing or speaking.",
                    StatusCodes.Status400BadRequest);
            module = parsed;
        }

        try
        {
            var view = await handler.HandleAsync(
                new StartExamSessionCommand(new UserId(id), new ExamVersionId(request.ExamVersionId), mode, module),
                ct);

            return Results.Created($"/api/v1/sessions/{view.SessionId}", view);
        }
        catch (SessionNotFoundException)
        {
            return Problem(ErrorCodes.ExamNotFound, "No such exam.", StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException e)
        {
            return Problem(ErrorCodes.ExamNotSittable, e.Message, StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> GetEndpoint(
        string sessionId, ClaimsPrincipal principal, GetExamSession handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        try
        {
            return Results.Ok(await handler.HandleAsync(
                new GetExamSessionQuery(new UserId(id), new ExamSessionId(sessionId)), ct));
        }
        catch (SessionNotFoundException)
        {
            return SessionMissing();
        }
    }

    private static async Task<IResult> SaveAnswersEndpoint(
        string sessionId, ClaimsPrincipal principal, SaveAnswersRequest request,
        SaveAnswers handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        if (!TryParseModule(request.Module, out var module))
            return Problem("MODULE_INVALID", "Unknown module.", StatusCodes.Status400BadRequest);

        try
        {
            await handler.HandleAsync(
                new SaveAnswersCommand(
                    new UserId(id), new ExamSessionId(sessionId), module, request.Answers),
                ct);

            return Results.NoContent();
        }
        catch (SessionNotFoundException) { return SessionMissing(); }
        catch (SessionExpiredException) { return Expired(); }
        catch (SessionNotInProgressException e)
        {
            return Problem(
                ErrorCodes.SessionNotInProgress,
                $"This sitting is {e.Status} and no longer accepts answers.",
                StatusCodes.Status409Conflict);
        }
    }

    /// <summary>
    /// One Speaking answer.
    ///
    /// <b>Returns an id, and the id is what goes on the answer sheet.</b> The
    /// audio never travels through the autosave, which rewrites the whole
    /// sheet every few seconds — a two-minute recording riding along on each
    /// of those would be megabytes per keystroke burst.
    ///
    /// <b>Capped, and the cap is enforced here rather than trusted from the
    /// client.</b> A `Content-Length` is a claim; a stream that keeps arriving
    /// after the cap is a denial-of-service with a polite header on it.
    /// </summary>
    private static async Task<IResult> UploadRecordingEndpoint(
        string sessionId,
        ClaimsPrincipal principal,
        HttpRequest request,
        IExamCatalogue catalogue,
        IExamSessionRepository sessions,
        IRecordingStore recordings,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        var session = await sessions.FindAsync(new ExamSessionId(sessionId), ct);
        if (session is null || session.UserId != new UserId(id)) return SessionMissing();

        if (session.Status != SessionStatus.InProgress)
            return Problem(
                ErrorCodes.SessionNotInProgress,
                $"This sitting is {session.Status}.", StatusCodes.Status409Conflict);

        if (!request.HasFormContentType) return Problem(
            ErrorCodes.ValidationFailed, "Expected a multipart upload.", StatusCodes.Status400BadRequest);

        var form = await request.ReadFormAsync(ct);
        var questionId = form["questionId"].ToString();
        var file = form.Files.GetFile("audio");

        if (string.IsNullOrWhiteSpace(questionId) || file is null)
            return Problem(
                ErrorCodes.ValidationFailed,
                "A recording needs a questionId and an audio part.",
                StatusCodes.Status400BadRequest);

        if (file.Length > MaxRecordingBytes)
            return Problem(
                ErrorCodes.RecordingTooLarge,
                "That recording is longer than a Speaking answer can be.",
                StatusCodes.Status413PayloadTooLarge);

        // The declared type is not trusted for anything but a stored label; it
        // decides no code path and is never echoed into a response header
        // without being mapped first.
        var contentType = file.ContentType is { Length: > 0 } declared && declared.StartsWith("audio/")
            ? declared
            : "application/octet-stream";

        await using var content = file.OpenReadStream();
        var recordingId = await recordings.SaveAsync(
            new ExamSessionId(sessionId), questionId, content, contentType, ct);

        return Results.Ok(new { recordingId });
    }

    /// <summary>
    /// 12 MB. A two-minute Part 2 answer in Opus is well under 2 MB; the
    /// headroom is for a browser that falls back to an uncompressed format.
    /// </summary>
    private const long MaxRecordingBytes = 12L * 1024 * 1024;

    private static async Task<IResult> AdvanceEndpoint(
        string sessionId, ClaimsPrincipal principal, AdvanceSection handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        try
        {
            return Results.Ok(await handler.HandleAsync(
                new AdvanceSectionCommand(new UserId(id), new ExamSessionId(sessionId)), ct));
        }
        catch (SessionNotFoundException) { return SessionMissing(); }
        catch (SessionNotInProgressException e)
        {
            return Problem(
                ErrorCodes.SessionNotInProgress, $"This sitting is {e.Status}.",
                StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException e)
        {
            // A single-skill session asked to advance. Its next step is a new
            // test, which is a different operation. → CLAUDE.md rule 10
            return Problem(ErrorCodes.NotAFullTest, e.Message, StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> SubmitEndpoint(
        string sessionId, ClaimsPrincipal principal, SubmitExamSession handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        try
        {
            return Results.Ok(await handler.HandleAsync(
                new SubmitExamSessionCommand(new UserId(id), new ExamSessionId(sessionId)), ct));
        }
        catch (SessionNotFoundException) { return SessionMissing(); }
        catch (SessionExpiredException) { return Expired(); }
    }

    private static async Task<IResult> ResultsEndpoint(
        string sessionId, ClaimsPrincipal principal, GetSessionResults handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        try
        {
            return Results.Ok(await handler.HandleAsync(
                new GetSessionResultsQuery(new UserId(id), new ExamSessionId(sessionId)), ct));
        }
        catch (SessionNotFoundException) { return SessionMissing(); }
    }

    /// <summary>404, not 403 — see <c>SessionProjection.LoadOwnedAsync</c>.</summary>
    private static IResult SessionMissing() =>
        Problem(ErrorCodes.SessionNotFound, "No such exam session.", StatusCodes.Status404NotFound);

    private static IResult Expired() =>
        Problem(
            ErrorCodes.SessionExpired,
            "The time for this section has run out. What you saved before the deadline is kept.",
            StatusCodes.Status409Conflict);

    private static IResult Problem(string code, string detail, int status) =>
        Results.Problem(
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static bool TryParseMode(string? raw, out SessionMode mode)
    {
        mode = SessionMode.Single;
        return raw?.ToLowerInvariant() switch
        {
            "full" => (mode = SessionMode.Full) == SessionMode.Full,
            "single" => true,
            _ => false,
        };
    }

    private static bool TryParseModule(string? raw, out ExamModule module) =>
        Enum.TryParse(raw, ignoreCase: true, out module)
        && Enum.IsDefined(module);
}
