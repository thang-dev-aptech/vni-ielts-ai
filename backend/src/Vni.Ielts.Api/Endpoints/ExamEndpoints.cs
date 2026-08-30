using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Application.Practice;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Api.Endpoints;

/// <param name="Timing">
/// <c>"deadline"</c> (thi thử) or <c>"open"</c> (luyện đề). Absent means
/// deadline — the mode that existed before this field did, so an older client
/// keeps sitting the exam it was already sitting rather than silently getting
/// an untimed one.
/// </param>
/// <param name="TargetSeconds">
/// The learner's own goal, for an open-ended sitting only. Ignored for a
/// deadlined one: there the paper decides, and a caller who could set their own
/// exam length is threat `T6`.
/// </param>
public sealed record StartSessionRequest(
    string? PracticeUnitId = null,
    string? ExamVersionId = null, string? Mode = null, string? Module = null,
    string? Timing = null, int? TargetSeconds = null);

/// <param name="Running">
/// True to start the luyện đề clock, false to stop it.
///
/// <b>No timestamp, deliberately.</b> The server reads its own clock. A payload
/// carrying when the learner paused is a payload they can write. → ADR-0007
/// </param>
public sealed record StopwatchRequest(bool Running);

/// <param name="TargetSeconds">Null clears the goal.</param>
public sealed record TargetTimeRequest(int? TargetSeconds);

/// <param name="Changes">
/// The questions this autosave touched — not the whole sheet.
///
/// <b>Absent means untouched; present-and-null means cleared.</b> A whole
/// sheet could not say which, so a client whose copy was seconds old deleted
/// everything another tab had typed and reported it as a successful save.
/// </param>
/// <param name="BaseRevision">
/// The sheet version this patch was composed against, from the previous save's
/// response or from the session view.
///
/// Optional, and it never refuses a write. When it does not match, the response
/// carries the merged sheet so the caller can take in what it missed.
/// </param>
/// <param name="Sequences">
/// A per-question ordering token, one for each entry in <paramref name="Changes"/>.
///
/// <b>Optional, because arrival order was the contract until 27/08/2026.</b> A
/// client that sends none gets the old behaviour — the write that arrives last
/// wins — which is right for a caller that has not been updated and wrong as a
/// guarantee: the write that arrives last is the edit that came last only when
/// nothing in between reordered them, and a retry, a proxy, a stalled request
/// or a second tab all do.
/// → <see cref="Vni.Ielts.Application.Exams.IAnswerSheetStore.PatchAsync"/>
/// </param>
public sealed record SaveAnswersRequest(
    string Module,
    IReadOnlyDictionary<string, string?> Changes,
    int? BaseRevision = null,
    IReadOnlyDictionary<string, long>? Sequences = null);

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

        app.MapGet("/api/v1/practice-units", ListPracticeUnitsEndpoint)
            .WithTags("Exams")
            .WithName("ListPracticeUnits")
            .WithSummary("Practice and mock units projected from published exam versions")
            .Produces<PracticeUnitCatalogueView>()
            .RequireAuthorization();

        exams.MapGet("/assets/{**reference}", AssetEndpoint)
            .WithName("GetExamAsset")
            .WithSummary("Listening audio and question images");

        var sessions = app.MapGroup("/api/v1/sessions").WithTags("Exams").RequireAuthorization();

        // <b>The response types are declared, not inferred.</b> These handlers
        // return `IResult`, so nothing can work out what a 200 carries — and a
        // generated client with no type for the exam it is about to render is a
        // client every screen hand-copies the shape for. That hand-copying is
        // what `A17` was. → `contracts/openapi`, `I7.2`
        sessions.MapPost("/", StartEndpoint)
            .WithName("StartExamSession")
            .WithSummary("Open a sitting")
            .Produces<SessionView>(StatusCodes.Status201Created)
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        sessions.MapGet("/", ListMineEndpoint)
            .WithName("ListMySittings")
            .WithSummary("The caller's own recent sittings, newest first");

        sessions.MapGet("/{sessionId}", GetEndpoint)
            .WithName("GetExamSession")
            .WithSummary("The open section, its questions and the server deadline")
            .Produces<SessionView>();

        sessions.MapPut("/{sessionId}/answers", SaveAnswersEndpoint)
            .WithName("SaveExamAnswers")
            .WithSummary("Autosave the sheet for the section in progress");

        sessions.MapPost("/{sessionId}/recordings", UploadRecordingEndpoint)
            .WithName("UploadSpeakingRecording")
            .WithSummary("Store one Speaking answer and return its id")
            .DisableAntiforgery()
            // <b>The only endpoint that raises the body cap, and it has to.</b>
            // Kestrel enforces a 1 MB limit for the whole application before a
            // byte reaches application code — the right default, because
            // Argon2id makes a multi-megabyte password field expensive to
            // refuse. But a minute of speech is several megabytes, so every
            // real recording was rejected by the server before
            // `MaxRecordingBytes` below ever ran: the endpoint's own 12 MB
            // allowance was unreachable, and the check that enforced it was
            // guarding a door nothing could arrive at.
            //
            // Raised inside the handler, immediately before the body is read
            // — see `UploadRecordingEndpoint`.
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        sessions.MapPost("/{sessionId}/recordings/init", InitRecordingEndpoint)
            .WithName("InitSpeakingRecording")
            .WithSummary("Begin a presigned Speaking upload")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        sessions.MapPost("/{sessionId}/recordings/{uploadId}/complete", CompleteRecordingEndpoint)
            .WithName("CompleteSpeakingRecording")
            .WithSummary("Verify a presigned upload and link it to the answer sheet")
            .RequireRateLimiting(RateLimitPolicies.Authentication);

        sessions.MapPut("/{sessionId}/stopwatch", StopwatchEndpoint)
            .WithName("SetExamStopwatch")
            .WithSummary("Luyện đề only: start or stop the count-up clock");

        sessions.MapPut("/{sessionId}/target-time", TargetTimeEndpoint)
            .WithName("SetExamTargetTime")
            .WithSummary("Luyện đề only: the learner's own goal for this section");

        // The two irreversible transitions, and the two most expensive
        // operations in the product — each closes a section and marks it.
        // Bounded per sitting rather than per user. → `RateLimitPolicies.Transition`
        sessions.MapPost("/{sessionId}/advance", AdvanceEndpoint)
            .WithName("AdvanceExamSection")
            .WithSummary("Full Test only: close this section and open the next")
            .Produces<SessionView>()
            .RequireRateLimiting(RateLimitPolicies.Transition);

        sessions.MapPost("/{sessionId}/submit", SubmitEndpoint)
            .WithName("SubmitExamSession")
            .WithSummary("Submit the sitting and mark what can be marked")
            .Produces<SessionResultsView>()
            .RequireRateLimiting(RateLimitPolicies.Transition);

        sessions.MapGet("/{sessionId}/results", ResultsEndpoint)
            .WithName("GetExamResults")
            .WithSummary("Bands for the sections that have been marked")
            .Produces<SessionResultsView>();

        sessions.MapPost("/{sessionId}/questions/{questionId}/explanation", ExplanationEndpoint)
            .WithName("RequestPersonalizedExplanation")
            .WithSummary("On-demand personalized explanation for one Reading/Listening question")
            .Produces<PersonalizedExplanationView>()
            .RequireRateLimiting(RateLimitPolicies.Transition);
    }

    private static async Task<IResult> ListPracticeUnitsEndpoint(
        string? skill, string? scope, string? variant,
        ListPracticeUnits handler, CancellationToken ct)
    {
        ExamModule? module = null;
        if (skill is not null)
        {
            if (!TryParseModule(skill, out var parsed))
                return Problem("SKILL_INVALID", "Skill must be reading, listening, writing or speaking.", 400);
            module = parsed;
        }

        PracticeScope? parsedScope = scope?.Trim().ToLowerInvariant() switch
        {
            null => null,
            "part" => PracticeScope.Part,
            "skill" => PracticeScope.Skill,
            "full-test" => PracticeScope.FullTest,
            _ => (PracticeScope)(-1),
        };
        if (parsedScope is (PracticeScope)(-1))
            return Problem("SCOPE_INVALID", "Scope must be part, skill or full-test.", 400);

        ExamVariant? parsedVariant = variant?.Trim().ToLowerInvariant() switch
        {
            null => null,
            "academic" => ExamVariant.Academic,
            "general" => ExamVariant.General,
            _ => (ExamVariant)(-1),
        };
        if (parsedVariant is (ExamVariant)(-1))
            return Problem("VARIANT_INVALID", "Variant must be academic or general.", 400);

        return Results.Ok(await handler.HandleAsync(
            new ListPracticeUnitsQuery(module, parsedScope, parsedVariant), ct));
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
    private static async Task<IResult> AssetEndpoint(
        string reference, ClaimsPrincipal principal, IExamAssetStore assets, CancellationToken ct)
    {
        if (principal.UserId() is null) return Results.Unauthorized();

        if (await assets.OpenAsync($"assets/{reference}", ct) is not { } asset)
            return Results.NotFound();

        /*
         * <b>Range requests matter</b>: an audio element seeks by asking for
         * byte ranges, and without this the browser re-downloads from zero
         * every time a learner drags the scrub bar.
         *
         * <b>The entity tag matters for the same reason, one level up.</b> A
         * published exam version is immutable, so its audio never changes — a
         * browser that already has the file should be able to ask "is it still
         * this one" and be told yes in a header rather than fetching megabytes
         * again on every visit to the section.
         */
        return Results.Stream(
            asset.Content,
            asset.ContentType,
            enableRangeProcessing: true,
            entityTag: asset.ETag is { } tag
                ? new Microsoft.Net.Http.Headers.EntityTagHeaderValue(tag)
                : null);
    }

    private static async Task<IResult> StartEndpoint(
        ClaimsPrincipal principal, StartSessionRequest request, HttpContext http,
        StartExamSession legacyHandler, StartPracticeUnitSession practiceHandler,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        if (!string.IsNullOrWhiteSpace(request.PracticeUnitId))
        {
            if (request.ExamVersionId is not null || request.Mode is not null
                || request.Module is not null || request.Timing is not null)
                return Problem(
                    "PRACTICE_UNIT_CONFLICT",
                    "A practiceUnitId resolves exam, scope, module, parts and timing; do not send overrides.",
                    StatusCodes.Status400BadRequest);

            try
            {
                var view = await practiceHandler.HandleAsync(
                    new StartPracticeUnitSessionCommand(
                        new UserId(id), request.PracticeUnitId, request.TargetSeconds), ct);
                return Results.Created($"/api/v1/sessions/{view.SessionId}", view);
            }
            catch (SessionNotFoundException)
            {
                return Problem("PRACTICE_UNIT_NOT_FOUND", "No such available practice unit.", 404);
            }
        }

        // Compatibility window for pre-PracticeUnit clients. This path remains functional but
        // advertises a finite sunset on every response so its removal cannot be silent.
        http.Response.Headers["Deprecation"] = "true";
        http.Response.Headers["Sunset"] = "Wed, 31 Dec 2026 23:59:59 GMT";
        http.Response.Headers.Link = "</api/v1/practice-units>; rel=successor-version";

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

        /*
         * <b>Absent means deadline, and an unknown value is refused.</b>
         *
         * Defaulting an unrecognised string to open-ended would let a typo hand
         * somebody an untimed exam; defaulting it to deadline would let a typo
         * put a stopwatch user under an exam clock they never agreed to. Only
         * *absence* has a defined meaning here, and it means the mode that
         * existed before this field did.
         */
        var timing = SessionTiming.Deadline;

        if (request.Timing is { } asked)
        {
            timing = asked.Trim().ToLowerInvariant() switch
            {
                "deadline" => SessionTiming.Deadline,
                "open" => SessionTiming.OpenEnded,
                _ => (SessionTiming)(-1),
            };

            if (timing is (SessionTiming)(-1))
                return Problem(
                    "TIMING_INVALID",
                    "Timing must be 'deadline' for thi thử or 'open' for luyện đề.",
                    StatusCodes.Status400BadRequest);
        }

        try
        {
            var view = await legacyHandler.HandleAsync(
                new StartExamSessionCommand(
                    new UserId(id), new ExamVersionId(request.ExamVersionId!), mode, module,
                    timing,
                    // Silently dropped for a deadlined sitting rather than
                    // refused: a client sending both is confused, not hostile,
                    // and the paper's own timing is what governs either way.
                    timing == SessionTiming.OpenEnded ? request.TargetSeconds : null),
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

    /// <summary>
    /// <b>Exempt from the idempotency guard by shape, not by omission.</b> Both
    /// of these are `PUT`s that set a value rather than perform an action, so
    /// applying one twice leaves what applying it once did — and the middleware
    /// requires a key on every PUT, which would mean minting one per tap on a
    /// pause button. They are listed in <c>IdempotencyMiddleware</c> for the
    /// same reason the autosave is.
    /// </summary>
    private static async Task<IResult> StopwatchEndpoint(
        string sessionId, ClaimsPrincipal principal, StopwatchRequest request,
        SetStopwatch handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        try
        {
            return Results.Ok(await handler.HandleAsync(
                new SetStopwatchCommand(
                    new UserId(id), new ExamSessionId(sessionId),
                    request.Running ? StopwatchIntent.Resume : StopwatchIntent.Pause),
                ct));
        }
        catch (SessionNotFoundException) { return SessionMissing(); }
        catch (SectionNotOpenException e) { return WrongSection(e); }
        catch (StopwatchNotAvailableException e)
        {
            return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status409Conflict);
        }
        catch (SessionNotInProgressException e)
        {
            return Problem(
                ErrorCodes.SessionNotInProgress,
                $"This sitting is {e.Status} and its clock has stopped for good.",
                StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> TargetTimeEndpoint(
        string sessionId, ClaimsPrincipal principal, TargetTimeRequest request,
        SetTargetTime handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        try
        {
            return Results.Ok(await handler.HandleAsync(
                new SetTargetTimeCommand(
                    new UserId(id), new ExamSessionId(sessionId), request.TargetSeconds),
                ct));
        }
        catch (SessionNotFoundException) { return SessionMissing(); }
        catch (SectionNotOpenException e) { return WrongSection(e); }
        catch (ArgumentOutOfRangeException e)
        {
            return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status400BadRequest);
        }
        catch (StopwatchNotAvailableException e)
        {
            return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status409Conflict);
        }
        catch (SessionNotInProgressException e)
        {
            return Problem(
                ErrorCodes.SessionNotInProgress,
                $"This sitting is {e.Status}.", StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> SaveAnswersEndpoint(
        string sessionId, ClaimsPrincipal principal, SaveAnswersRequest request,
        SaveAnswers handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        if (!TryParseModule(request.Module, out var module))
            return Problem("MODULE_INVALID", "Unknown module.", StatusCodes.Status400BadRequest);

        if (request.Changes is null)
            return Problem(
                ErrorCodes.ValidationFailed,
                "An autosave must carry the questions it changed, in `changes`.",
                StatusCodes.Status400BadRequest);

        try
        {
            var saved = await handler.HandleAsync(
                new SaveAnswersCommand(
                    new UserId(id), new ExamSessionId(sessionId), module, request.Changes,
                    request.BaseRevision, request.Sequences),
                ct);

            /*
             * <b>Was 204, and a body is the point.</b> A client that cannot
             * learn the new revision has to re-read the sheet before every
             * save, or go on writing blind — and writing blind is what this
             * whole mechanism removes.
             *
             * `answers` is omitted on the ordinary path, which is one tab that
             * missed nothing. It appears when the caller's base revision was
             * behind, and then it is the merged sheet: what this patch wrote
             * plus whatever landed in between. There is no 409 any more — two
             * patches never contradict each other, so there is nothing to
             * refuse and nothing for the client to reconcile by hand.
             */
            return saved.Answers is null
                ? Results.Ok(new { revision = saved.Revision })
                : Results.Ok(new
                {
                    revision = saved.Revision,
                    answers = saved.Answers,
                    // Sent with the sheet, never without it. A caller taking in
                    // another writer's answers has to raise its own counters
                    // past theirs, or its next edit to one of those questions
                    // carries a token this endpoint will ignore.
                    sequences = saved.Sequences,
                });
        }
        /*
         * <b>Named per question, because the client has to keep the rest.</b>
         *
         * Both of these refuse the whole patch — one bad entry and nothing is
         * written, which is right: a partially applied autosave is a sheet
         * nobody can reason about. But the refusal used to arrive as prose
         * only, so the client could not tell which entry was the problem, and
         * it did the only thing a caller with no detail can do: it threw the
         * whole batch away. A learner who typed a good answer to question 3
         * and a 60,001-character one into question 4 lost both, silently, and
         * the submit gate then let the paper through.
         *
         * `errors` carries a row per question id, so the client can drop the
         * entries the server named and keep offering the ones it did not.
         * → `useAnswerSheet`, `TERMINAL_REFUSALS`
         */
        catch (UnknownQuestionException e)
        {
            return AnswerProblem(e.QuestionIds, "QUESTION_UNKNOWN", e.Message);
        }
        catch (AnswerTooLongException e)
        {
            return AnswerProblem(e.QuestionIds, "ANSWER_TOO_LONG", e.Message);
        }
        catch (ArgumentException e)
        {
            /*
             * <b>The store's own key-shape guard, mapped rather than escaping
             * as a 500.</b>
             *
             * `GuardKey` refuses a question id containing `.` or beginning with
             * `$`, because either one sends the write to a different place in
             * the document than the one it addressed. The handler checks ids
             * against the exam's questions first, so this is unreachable
             * through a package that came in via the schema — which pins ids to
             * `^[a-z0-9][a-z0-9\-]{0,63}$`. It is *not* unreachable through
             * `IExamCatalogue.UpsertAsync`, which is how the seeder and, later,
             * CMS publishing write a version. One published question id of
             * `1.1` and every autosave in that section becomes a 500: the id
             * passes the handler's check and dies in the store.
             *
             * A 400 is the truthful class. The content is wrong, and no retry
             * of the same request can succeed.
             */
            return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status400BadRequest);
        }
        catch (SessionNotFoundException) { return SessionMissing(); }
        catch (SessionExpiredException) { return Expired(); }
        catch (SectionNotOpenException e) { return WrongSection(e); }
        /*
         * <b>Reported as `SECTION_NOT_OPEN`, and that is the honest code.</b>
         *
         * The freeze is what the section closing looks like from inside a write
         * that was already in flight — the sitting may not have transitioned
         * yet, but this section will take nothing more. The client already
         * treats this code as terminal: it drops the patch rather than
         * re-offering it, and it does not hold the ending shut on it. Inventing
         * a new code would make every client grow a branch for a state it
         * already handles correctly. → `useAnswerSheet`, `TERMINAL_REFUSALS`
         */
        catch (SectionSheetClosedException e)
        {
            return Problem(ErrorCodes.SectionNotOpen, e.Message, StatusCodes.Status409Conflict);
        }
        catch (SpeakingIsNotWrittenException e)
        {
            return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status400BadRequest);
        }
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
    ///
    /// <b>Everything past parsing the multipart belongs to
    /// <see cref="SubmitSpeakingRecording"/>.</b> This used to own the whole
    /// operation, and the rules it did not know about were the ones that
    /// mattered: which section is open, and whether its deadline has passed.
    /// Those are exam rules, and exam rules live where the worker can reach
    /// them too.
    /// </summary>
    private static async Task<IResult> UploadRecordingEndpoint(
        string sessionId,
        ClaimsPrincipal principal,
        HttpRequest request,
        SubmitSpeakingRecording handler,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        if (!request.HasFormContentType) return Problem(
            ErrorCodes.ValidationFailed, "Expected a multipart upload.", StatusCodes.Status400BadRequest);

        /*
         * <b>Lift the body cap for this route, and only for this route.</b>
         *
         * Kestrel refuses a request body over 1 MB for the whole application
         * before a byte reaches application code. That default is right —
         * Argon2id makes a multi-megabyte password field expensive to even
         * refuse — but a minute of speech is several megabytes, so every real
         * recording was rejected by the server and `MaxRecordingBytes` below
         * never ran. The endpoint's own 12 MB allowance was unreachable, and
         * the check enforcing it was guarding a door nothing could arrive at.
         *
         * Set here rather than as endpoint metadata because the limit has to be
         * raised *before* the body is read and this handler reads the form
         * itself. The feature is read-only once reading starts, so ordering is
         * the whole trick: after `ReadFormAsync` it is too late, and there is no
         * error to say so.
         */
        /*
         * <b>Not covered by the integration suite, and that is worth knowing
         * rather than assuming.</b> `WebApplicationFactory` runs on `TestServer`,
         * which does not provide `IHttpMaxRequestBodySizeFeature` at all — it is
         * `null` there, measured. So this block never executes under test, and
         * every upload test in `ExamRunContractTests` is exercising the
         * endpoint's own `file.Length` check and nothing else.
         *
         * That matters because the bug this block was written for — Kestrel's
         * 1 MB default refusing every real Speaking upload before application
         * code ran — is precisely the kind the suite cannot see. It needs a
         * real server. → `I7.4` in `docs/development/infrastructure-gate.md`
         */
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is
            { IsReadOnly: false } cap)
        {
            cap.MaxRequestBodySize = MaxRecordingBytes + MultipartOverheadBytes;
        }

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

        try
        {
            var recordingId = await handler.HandleAsync(
                new SubmitSpeakingRecordingCommand(
                    new UserId(id), new ExamSessionId(sessionId), questionId, content, contentType),
                ct);

            return Results.Ok(new { recordingId });
        }
        catch (SessionNotFoundException) { return SessionMissing(); }
        catch (SessionExpiredException) { return Expired(); }
        catch (SectionNotOpenException e) { return WrongSection(e); }
        /*
         * <b>The section closed while the audio was still streaming.</b>
         *
         * Reported rather than swallowed, and reported as a failure rather than
         * as a success with a caveat: the recording will not be marked, and the
         * client has to be able to say so. A 200 here would tell the learner
         * their spoken answer was filed for a paper that had already been
         * scored without it — which is the failure the closure protocol exists
         * to prevent, arriving through the one door that returns an id.
         * → ADR-0015
         */
        catch (SectionSheetClosedException)
        {
            return Problem(
                ErrorCodes.SectionNotOpen,
                "The Speaking section closed before this recording could be filed, so it "
                + "will not be marked. Nothing already submitted is affected.",
                StatusCodes.Status409Conflict);
        }
        catch (SessionNotInProgressException e)
        {
            return Problem(
                ErrorCodes.SessionNotInProgress,
                $"This sitting is {e.Status}.", StatusCodes.Status409Conflict);
        }
        catch (ArgumentException e)
        {
            return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status400BadRequest);
        }
    }

    private sealed record InitRecordingRequest(
        string QuestionId, string ContentType, long SizeBytes, string ChecksumSha256);

    private sealed record CompleteRecordingRequest(long SizeBytes, string ChecksumSha256);

    private static async Task<IResult> InitRecordingEndpoint(
        string sessionId,
        InitRecordingRequest body,
        ClaimsPrincipal principal,
        InitSpeakingRecording handler,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(body.QuestionId)
            || string.IsNullOrWhiteSpace(body.ContentType)
            || body.SizeBytes <= 0)
        {
            return Problem(
                ErrorCodes.ValidationFailed,
                "Init needs questionId, contentType and sizeBytes.",
                StatusCodes.Status400BadRequest);
        }

        try
        {
            var result = await handler.HandleAsync(
                new InitSpeakingRecordingCommand(
                    new UserId(id),
                    new ExamSessionId(sessionId),
                    body.QuestionId,
                    body.ContentType,
                    body.SizeBytes,
                    body.ChecksumSha256),
                ct);

            return Results.Ok(new
            {
                result.UploadId,
                result.RecordingId,
                uploadUrl = result.UploadUrl?.ToString(),
                expiresAt = result.ExpiresAt,
                contentType = result.ContentType,
                uploadMode = result.UploadMode,
                multipartThresholdBytes = result.MultipartThresholdBytes,
            });
        }
        catch (SessionNotFoundException) { return SessionMissing(); }
        catch (SessionExpiredException) { return Expired(); }
        catch (SectionNotOpenException e) { return WrongSection(e); }
        catch (SessionNotInProgressException e)
        {
            return Problem(
                ErrorCodes.SessionNotInProgress,
                $"This sitting is {e.Status}.",
                StatusCodes.Status409Conflict);
        }
        catch (SpeakingRecordingUploadUnavailableException)
        {
            return Problem(
                ErrorCodes.RecordingUploadUnavailable,
                "Presigned Speaking upload is not configured.",
                StatusCodes.Status503ServiceUnavailable);
        }
        catch (ArgumentException e)
        {
            return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> CompleteRecordingEndpoint(
        string sessionId,
        string uploadId,
        CompleteRecordingRequest body,
        ClaimsPrincipal principal,
        CompleteSpeakingRecording handler,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        if (body.SizeBytes <= 0 || string.IsNullOrWhiteSpace(body.ChecksumSha256))
        {
            return Problem(
                ErrorCodes.ValidationFailed,
                "Complete needs sizeBytes and checksumSha256.",
                StatusCodes.Status400BadRequest);
        }

        try
        {
            var recordingId = await handler.HandleAsync(
                new CompleteSpeakingRecordingCommand(
                    new UserId(id),
                    new ExamSessionId(sessionId),
                    uploadId,
                    body.SizeBytes,
                    body.ChecksumSha256),
                ct);

            return Results.Ok(new { recordingId });
        }
        catch (SessionNotFoundException) { return SessionMissing(); }
        catch (SessionExpiredException) { return Expired(); }
        catch (SectionNotOpenException e) { return WrongSection(e); }
        catch (SectionSheetClosedException)
        {
            return Problem(
                ErrorCodes.SectionNotOpen,
                "The Speaking section closed before this recording could be filed, so it "
                + "will not be marked. Nothing already submitted is affected.",
                StatusCodes.Status409Conflict);
        }
        catch (SessionNotInProgressException e)
        {
            return Problem(
                ErrorCodes.SessionNotInProgress,
                $"This sitting is {e.Status}.",
                StatusCodes.Status409Conflict);
        }
        catch (SpeakingRecordingUploadUnavailableException)
        {
            return Problem(
                ErrorCodes.RecordingUploadUnavailable,
                "Presigned Speaking upload is not configured.",
                StatusCodes.Status503ServiceUnavailable);
        }
        catch (SpeakingRecordingUploadNotFoundException)
        {
            return Problem(
                ErrorCodes.RecordingUploadNotFound,
                "No such Speaking recording upload.",
                StatusCodes.Status404NotFound);
        }
        catch (SpeakingRecordingChecksumMismatchException)
        {
            return Problem(
                ErrorCodes.RecordingChecksumMismatch,
                "The declared checksum or size does not match the upload session.",
                StatusCodes.Status409Conflict);
        }
        catch (SpeakingRecordingVerificationFailedException)
        {
            return Problem(
                ErrorCodes.RecordingVerificationFailed,
                "The uploaded recording could not be verified.",
                StatusCodes.Status409Conflict);
        }
        catch (ArgumentException e)
        {
            return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>
    /// 12 MB. A two-minute Part 2 answer in Opus is well under 2 MB; the
    /// headroom is for a browser that falls back to an uncompressed format.
    /// </summary>
    private const long MaxRecordingBytes = 12L * 1024 * 1024;

    /// <summary>
    /// What the transport allows on top of the file itself.
    ///
    /// <b>The two caps were the same number, and that made the larger one
    /// unreachable.</b> A multipart body is not just the file: it carries a
    /// boundary before and after each part, a `Content-Disposition` and a
    /// `Content-Type` header per part, and the `questionId` field. So a
    /// recording of <i>exactly</i> <see cref="MaxRecordingBytes"/> produces a
    /// body a few hundred bytes larger, Kestrel refuses it before any
    /// application code runs, and the learner is told nothing useful — the
    /// endpoint's own 413, with its own error code and its own sentence, is
    /// never reached. A legal recording, refused, with an unhelpful message.
    ///
    /// 64 KB, and bounded rather than generous: the transport cap is the only
    /// thing standing between this route and an unbounded upload, so the
    /// overhead allowance has to be large enough for any plausible multipart
    /// envelope and small enough that it is not a second limit in disguise.
    /// Real envelopes for this form are a few hundred bytes.
    /// </summary>
    private const long MultipartOverheadBytes = 64L * 1024;


    /// <summary>
    /// Tells the idempotency guard that this operation has taken effect.
    ///
    /// <b>Set after the handler returns and before the response is written,
    /// which is the window that used to lose it.</b> Serialising a result and
    /// writing it to the socket both happen after the transition has committed,
    /// and both can fail — most commonly because the caller went away. Without
    /// this the guard released the claim, and the retry advanced or submitted
    /// the sitting a second time.
    ///
    /// Only for operations that cannot be undone. An endpoint with nothing
    /// irreversible to lose is better off releasing its claim on failure, so
    /// that a transient 500 does not become permanent for the life of the key.
    /// → <see cref="IdempotencyMiddleware.CommittedMarker"/>
    /// </summary>
    private static T Committed<T>(HttpContext http, T result)
    {
        http.Items[IdempotencyMiddleware.CommittedMarker] = true;

        // A no-op outside tests. → ICommitSignal
        http.RequestServices.GetRequiredService<ICommitSignal>().Signal(http);

        return result;
    }

    private static async Task<IResult> AdvanceEndpoint(
        string sessionId, ClaimsPrincipal principal, HttpContext http,
        AdvanceSection handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        try
        {
            var view = await handler.HandleAsync(
                new AdvanceSectionCommand(new UserId(id), new ExamSessionId(sessionId)), ct);

            // The section this call left behind is closed and marked. Nothing
            // after this point may let a retry do it again.
            return Committed(http, Results.Ok(view));
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
        string sessionId, ClaimsPrincipal principal, HttpContext http,
        SubmitExamSession handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        try
        {
            var results = await handler.HandleAsync(
                new SubmitExamSessionCommand(new UserId(id), new ExamSessionId(sessionId)), ct);

            // The paper is in. A retry must never hand it in twice.
            return Committed(http, Results.Ok(results));
        }
        catch (SessionNotFoundException) { return SessionMissing(); }
        catch (SessionExpiredException) { return Expired(); }
        catch (SessionMovedOnException e)
        {
            /*
             * <b>409, and never a 200 with a results view.</b>
             *
             * The submit did not land — another writer kept taking the sitting
             * — so the paper has not been handed in. Answering 200 with the
             * results screen was the old behaviour and it was worse than
             * useless: the learner was shown their results while their sitting
             * was still running, and the idempotency guard then stored that 200
             * as the answer for their key, so no retry could ever get past it.
             *
             * A refusal is not stored, so the retry the client offers actually
             * runs.
             */
            return Problem(
                ErrorCodes.SessionNotInProgress, e.Message, StatusCodes.Status409Conflict);
        }
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

    private static async Task<IResult> ExplanationEndpoint(
        string sessionId,
        string questionId,
        HttpContext http,
        ClaimsPrincipal principal,
        RequestPersonalizedExplanation handler,
        CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();

        var operationId = http.Request.Headers[IdempotencyMiddleware.HeaderName].FirstOrDefault();

        try
        {
            var view = await handler.HandleAsync(
                new RequestPersonalizedExplanationCommand(
                    new UserId(id),
                    new ExamSessionId(sessionId),
                    questionId,
                    operationId ?? string.Empty),
                ct);

            return Results.Ok(Committed(http, view));
        }
        catch (SessionNotFoundException) { return SessionMissing(); }
        catch (InvalidOperationException e) when (e.Message.StartsWith("EXPLANATION_", StringComparison.Ordinal))
        {
            return Problem(e.Message, e.Message, StatusCodes.Status409Conflict);
        }
    }

    /// <summary>404, not 403 — see <c>SessionProjection.LoadOwnedAsync</c>.</summary>
    private static IResult SessionMissing() =>
        Problem(ErrorCodes.SessionNotFound, "No such exam session.", StatusCodes.Status404NotFound);

    /// <summary>
    /// The request named a section the sitting is not on.
    ///
    /// <b>Not <see cref="Expired"/>, which it used to be.</b> A client that
    /// receives SESSION_EXPIRED is meant to stop the exam; a mis-routed
    /// autosave is not a reason to end one that is still running.
    /// </summary>
    private static IResult WrongSection(SectionNotOpenException e) =>
        Problem(ErrorCodes.SectionNotOpen, e.Message, StatusCodes.Status409Conflict);

    private static IResult Expired() =>
        Problem(
            ErrorCodes.SessionExpired,
            "The time for this section has run out. What you saved before the deadline is kept.",
            StatusCodes.Status409Conflict);

    /// <summary>
    /// A refused autosave that names the questions it refused.
    ///
    /// <b>The path is the question id, not a JSON pointer.</b> The client keys
    /// its pending patch by question id and has to remove exactly those
    /// entries; a pointer like <c>/changes/r-4</c> would make it parse the
    /// shape of a request it already knows. Every other <c>errors</c> array in
    /// this API describes a form field, and this one describes an answer —
    /// same envelope, different subject, said plainly rather than disguised.
    /// </summary>
    private static IResult AnswerProblem(
        IReadOnlyList<string> questionIds, string code, string detail) =>
        Results.Problem(
            title: "Validation failed",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ErrorCodes.ValidationFailed,
                ["errors"] = questionIds
                    .Select(id => new FieldError(id, code, detail))
                    .ToArray(),
            });

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
