using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Api.Common;

/// <summary>
/// Makes state-changing requests replay-safe.
///
/// <para>
/// Mobile clients on unreliable networks retry aggressively by design. Without
/// this, a retried submission creates a second session, consumes entitlement
/// twice, and triggers a second paid AI evaluation — threat <c>T8</c>, and the
/// spending half of <c>T22</c>.
/// </para>
///
/// <para>
/// <b>Same key, same body</b> returns the stored response without re-executing.
/// <b>Same key, different body</b> is a conflict, not a replay: it means the
/// client reused a key for a genuinely different operation, and silently
/// returning the first response would hide that the second never happened.
/// </para>
/// </summary>
public sealed class IdempotencyMiddleware(
    RequestDelegate next,
    IMongoDatabase db,
    IClock clock,
    ILogger<IdempotencyMiddleware> logger,
    TimeSpan? lease = null)
{
    public const string HeaderName = "Idempotency-Key";

    /// <summary>
    /// <b>Every new state-changing endpoint has to make a decision here.</b>
    ///
    /// The guard denies by default, which is the right way round — an endpoint
    /// that needed protection and silently did not have it would be the worse
    /// failure. But it means a new POST is unreachable until someone either
    /// sends a key from the client or exempts it below, and by 21/08/2026 that
    /// had caught three separate endpoints during testing: the social sign-in
    /// start, signing a device out, and all three password routes.
    ///
    /// Each time the symptom was the same and looked like something else — a
    /// 400 from an endpoint that appeared not to exist.
    /// </summary>

    private static readonly HashSet<string> Guarded =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    /// <summary>
    /// Endpoints exempt because they are safe to repeat and a client cannot
    /// reasonably mint a key for them. Login is here deliberately: requiring a
    /// key on sign-in would break the very first request a new client makes.
    ///
    /// <para>
    /// The two social sign-in endpoints are exempt for the same reason, and
    /// neither loses anything by it. Starting a sign-in twice produces two
    /// independent authorization requests, which is what pressing the button
    /// twice should do. Completing one is single-use by construction — the
    /// handoff code is consumed by an atomic find-and-delete — which is a
    /// stronger guarantee than an idempotency key, not a weaker one.
    /// → ADR-0014
    /// </para>
    /// </summary>
    private static readonly HashSet<string> Exempt =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/v1/auth/login",
            "/api/v1/auth/refresh",
            "/api/v1/auth/logout",
            "/api/v1/auth/sso/complete",

            // Password flows, added 21/08/2026. Each is either naturally
            // idempotent or protected by something stronger:
            //
            //   forgot-password — a retry sends a second mail and changes
            //     nothing else; rate limiting is the control that matters, and
            //     demanding a key from a public form is friction for nobody's
            //     benefit.
            //   reset-password  — the token is single-use, so the second
            //     attempt fails on its own merits.
            //   me/password     — setting the same password twice is a no-op,
            //     and setting a different one is a new action the person
            //     deliberately asked for. A key regenerated per submit would
            //     protect nothing anyway.
            "/api/v1/auth/forgot-password",
            "/api/v1/auth/reset-password",
            "/api/v1/me/password",

            // Setting a phone number twice with the same value is a no-op, and
            // with a different value it is a change the person just asked for.
            "/api/v1/me/phone",

            // Correcting an unverified address. A retry with the same value is
            // a no-op; with a different one it is the correction the person
            // just asked for.
            "/api/v1/me/email",

            // Resending a verification mail is the one case where a retry does
            // cost something — a second email. That is what the registration
            // rate limit is for, and it is a far better fit than asking a
            // button press to carry a key.
            "/api/v1/me/verify-email/resend",

            // Signing out. Naturally idempotent — revoking a family that is
            // already revoked changes nothing — and it is the one operation
            // that must never be refused for want of a header: a client that
            // cannot sign out leaves a live credential behind on a machine
            // somebody else is about to use.
            "/api/v1/auth/logout",
        };

    /// <summary>
    /// <c>/api/v1/auth/sso/{provider}/start</c> carries a provider segment, so
    /// it cannot be matched by exact path.
    /// </summary>
    private static bool IsSsoStart(PathString path) =>
        path.StartsWithSegments("/api/v1/auth/sso", out var rest)
        && rest.Value?.EndsWith("/start", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// <c>DELETE /api/v1/me/sessions/{familyId}</c> — signing another device
    /// out of the account.
    ///
    /// <para>
    /// Exempt because the operation is <b>naturally idempotent</b>: revoking a
    /// family that is already revoked changes nothing and returns the same
    /// answer. An idempotency key exists to stop a retry performing a second
    /// distinct action, and here there is no second action to stop. Demanding
    /// one would only mean the client has to invent a key for a button press.
    /// </para>
    /// </summary>
    private static bool IsSessionRevoke(HttpRequest request) =>
        HttpMethods.IsDelete(request.Method)
        && request.Path.StartsWithSegments("/api/v1/me/sessions");

    /// <summary>
    /// <c>PUT /api/v1/sessions/{id}/answers</c> — the exam autosave.
    ///
    /// <para>
    /// Exempt for the same reason as the revoke above, plus one of its own.
    /// The save is a <b>patch of the questions that changed</b>, so applying it
    /// twice leaves exactly the answers applying it once did — there is no
    /// second action for a key to prevent. (This said "whole-sheet replace"
    /// until 27/08/2026, which was the true reason then and is not now. A
    /// duplicate patch does move the revision twice, which costs the caller one
    /// unnecessary merged sheet in the response and nothing else.)
    /// </para>
    ///
    /// <para>
    /// <b>What it does not defend against, and never did, is order.</b> A
    /// delayed retry of <c>{q7: "cat"}</c> landing after the learner typed
    /// <c>{q7: "dog"}</c> reverts q7, and nothing here dedupes it. That is a
    /// property of retrying a patch at all, not of the exemption — and the
    /// client does not retry autosaves today. → `A5` in
    /// `docs/development/next-actions.md`
    /// </para>
    ///
    /// <para>
    /// The reason of its own is volume. This fires every couple of seconds
    /// while someone is typing, for an hour, per learner. Keying it would
    /// write a stored response per burst and keep each for 24 hours, turning
    /// the idempotency collection into a write-amplified copy of every answer
    /// sheet in the product. The guard is for operations that must not happen
    /// twice; an autosave is one that may happen any number of times.
    /// </para>
    /// </summary>
    /// <summary>
    /// <c>POST /api/v1/dictation/{set}/check</c> — comparing a typed sentence.
    ///
    /// <para>
    /// <b>A read that happens to have a body.</b> It writes nothing, and
    /// running it twice returns the same verdict. It is a POST only because
    /// the sentence a learner typed does not belong in a query string, where
    /// it would land in every access log along the way.
    /// </para>
    /// </summary>
    private static bool IsDictationCheck(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && request.Path.StartsWithSegments("/api/v1/dictation")
        && request.Path.Value?.EndsWith("/check", StringComparison.Ordinal) == true;

    private static bool IsAnswerAutosave(HttpRequest request) =>
        HttpMethods.IsPut(request.Method)
        && request.Path.StartsWithSegments("/api/v1/sessions")
        && request.Path.Value?.EndsWith("/answers", StringComparison.Ordinal) == true;

    /// <summary>
    /// <c>PUT …/stopwatch</c> and <c>PUT …/target-time</c> — the luyện đề clock.
    ///
    /// <para>
    /// <b>Exempt by shape.</b> Both set a value rather than perform an action:
    /// "the clock is running" and "my goal is forty minutes". Applying either
    /// twice leaves exactly what applying it once did, so there is no second
    /// action for a key to prevent — the same argument as the autosave, and it
    /// is the argument that actually holds here rather than the volume one.
    /// </para>
    ///
    /// <para>
    /// The volume argument applies too, and it is not small: a learner tapping
    /// pause and play while they think is a handful of writes a minute, each of
    /// which would otherwise mint a key and keep a stored response for
    /// twenty-four hours.
    /// </para>
    ///
    /// <para>
    /// <b>What this does not excuse is a lost update</b>, and it is not relied
    /// on to. Two tabs pausing together are separated by the compare-and-swap
    /// on the sitting, whose guard carries the stopwatch's own state for
    /// exactly this reason. → <c>SessionState</c>
    /// </para>
    /// </summary>
    private static bool IsStopwatch(HttpRequest request) =>
        HttpMethods.IsPut(request.Method)
        && request.Path.StartsWithSegments("/api/v1/sessions")
        && (request.Path.Value?.EndsWith("/stopwatch", StringComparison.Ordinal) == true
            || request.Path.Value?.EndsWith("/target-time", StringComparison.Ordinal) == true);

    /// <summary>
    /// <c>POST /api/v1/sessions/{id}/recordings</c> — one Speaking answer.
    ///
    /// <para>
    /// <b>Exempt because a key cannot work here, not because the operation is
    /// unimportant.</b> The guard identifies a replay by hashing the request
    /// body, and this is the one route in the product whose body is multipart:
    /// a browser's <c>FormData</c> picks a fresh random boundary on every send,
    /// so the same recording re-sent under the same key hashes differently and
    /// is answered <c>IDEMPOTENCY_KEY_REUSED</c>. That turns the exact case the
    /// key exists for — the first attempt succeeded and its response was lost —
    /// into a hard 409 in the middle of a timed section.
    /// </para>
    ///
    /// <para>
    /// <b>And there is no second action for a key to prevent.</b> The audio is
    /// filed by <c>SubmitSpeakingRecording</c> with <c>SetAnswerAsync</c>, a
    /// single-entry write keyed on the question, so a duplicate upload leaves
    /// the sheet holding one id for that question exactly as one upload does.
    /// A replay costs an unreferenced GridFS blob and nothing a learner or the
    /// ledger can see — no entitlement is spent here, and no AI job is started.
    /// The controls that do apply are the 12 MB cap, the four exam gates in the
    /// handler, and the rate limit already on the route.
    /// </para>
    ///
    /// <para>
    /// <b>This was found by an integration test, and could not have been found
    /// any other way.</b> Without the exemption every Speaking upload was
    /// answered 400 <c>IDEMPOTENCY_KEY_MISSING</c> before reaching the handler,
    /// so the four gates added on 27/08 — in progress, Speaking open, inside the
    /// deadline, question belongs to this exam — were unreachable and Speaking
    /// could not be answered at all. The client cannot fix that from its side:
    /// <c>request()</c> is the only helper that threads a key and it serialises
    /// its body as JSON, so a multipart upload has to use raw <c>fetch</c>.
    /// → the note above on the guard denying by default.
    /// </para>
    /// </summary>
    private static bool IsRecordingUpload(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && request.Path.StartsWithSegments("/api/v1/sessions")
        && request.Path.Value?.EndsWith("/recordings", StringComparison.Ordinal) == true;

    private IMongoCollection<BsonDocument> Keys => db.GetCollection<BsonDocument>("idempotency_keys");

    /// <summary>
    /// How long a claim is honoured before another request may take it over.
    ///
    /// <b>This is a recovery bound, not a correctness one.</b> It only matters
    /// when the process holding a claim dies without releasing it; a lease that
    /// never expired would leave that key permanently unusable and the learner
    /// permanently unable to submit.
    ///
    /// It must be longer than the slowest guarded handler can legitimately
    /// take, or a live request is mistaken for a dead one and the operation
    /// runs a second time while the first is still inside it.
    ///
    /// <b>Five minutes, and it was two.</b> Two was measured against what
    /// <c>/submit</c> and <c>/advance</c> do today, which is a comparison
    /// against an answer key. Both also call the marking runner, and the day an
    /// evaluator is wired that becomes two sequential model calls for Writing
    /// and an ASR pass over up to fourteen minutes of audio for Speaking. Two
    /// minutes would not have been a generous bound for that — it would have
    /// been about the median — and the failure it produces is the expensive
    /// one: a takeover firing while the first request is still inside a paid
    /// provider call, which the runner's already-marked pre-check cannot catch
    /// because the first run has not written anything yet.
    ///
    /// <b>Revisit this when the first evaluator lands</b>, against a measured
    /// p99 rather than an estimate.
    ///
    /// Taking over an expired claim does not make a double execution safe on
    /// its own. That is why the claim carries a token: the original holder's
    /// completion write names it, so a handler that comes back from the dead
    /// cannot overwrite the response of the request that replaced it.
    /// </summary>
    private static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The lease this instance honours. Five minutes unless told otherwise.
    ///
    /// <b>Injectable so the heartbeat can be tested at all.</b> Proving that a
    /// running handler renews its lease means outliving one interval, and at
    /// the production interval that is a minute of wall clock per assertion —
    /// which is the kind of test that gets deleted. It is a constructor
    /// parameter rather than a static so nothing else can be affected by a
    /// test setting it.
    /// </summary>
    private TimeSpan Lease { get; } = lease ?? DefaultLease;

    /// <summary>
    /// How often a running handler pushes its lease forward.
    ///
    /// <b>A lease chosen against an estimate is a guess, and this is what makes
    /// the guess safe to be wrong about.</b> Five minutes was picked against
    /// what <c>/submit</c> and <c>/advance</c> do today; the day an evaluator is
    /// wired they become two sequential model calls plus an ASR pass over up to
    /// fourteen minutes of audio, and nobody knows yet whether that is three
    /// minutes or twelve. A fixed lease that turns out to be short does not
    /// degrade gracefully: a takeover fires while the first request is still
    /// inside a paid provider call, and the runner's already-marked pre-check
    /// cannot catch it because the first run has written nothing yet.
    ///
    /// A heartbeat removes the need to predict. While a handler is running, its
    /// lease keeps moving; when the process dies, the heartbeat dies with it and
    /// the lease expires on the ordinary schedule. So the lease bounds
    /// <i>death</i>, which is what it was always for, rather than bounding
    /// duration, which it was never good at.
    ///
    /// <b>Renewed on the claim's own token.</b> If a takeover has already
    /// happened, this update matches nothing and the heartbeat stops — a
    /// handler that comes back from the dead must not push the lease of a claim
    /// it no longer owns. → `I5.3`
    /// </summary>
    private TimeSpan Heartbeat => TimeSpan.FromMilliseconds(Lease.TotalMilliseconds / 5);

    private const string InFlight = "in-flight";
    private const string Done = "done";

    /// <summary>
    /// The first attempt may or may not have committed, and nothing can tell.
    ///
    /// <b>Written 2026-08-28. The hole it closes: cancellation was being read
    /// as proof that nothing happened.</b>
    ///
    /// The handler runs inside a <c>try</c> whose <c>catch</c> released the
    /// claim, on the reasoning that an exception means the operation failed and
    /// the retry must be able to run. That is true for an exception thrown
    /// *before* anything committed, and it is exactly false for one thrown
    /// after. The common shape is a caller that goes away — a phone changing
    /// network — while the handler is between its compare-and-swap and its
    /// response: the transition landed, `OperationCanceledException` unwound
    /// through the middleware, the claim was deleted, and the retry advanced
    /// the sitting a second time.
    ///
    /// So a claim in this state is neither released nor completed. A retry
    /// inside the lease is told the outcome is undetermined and to read the
    /// current state; after the lease it may take over, which bounds the wait
    /// rather than trapping the key for twenty-four hours.
    /// </summary>
    private const string Unknown = "unknown";

    /// <summary>
    /// Set by a handler that has performed its irreversible side effect.
    ///
    /// <b>The precise half of the rule above.</b> Cancellation is a good
    /// heuristic and it is only a heuristic; a handler can also throw after
    /// committing for reasons that have nothing to do with the caller — a
    /// marking runner that fails after the transition CAS has landed, say. A
    /// handler that knows it has passed the point of no return says so, and the
    /// guard stops guessing.
    ///
    /// Deliberately opt-in. An endpoint that does not set it keeps the old
    /// behaviour, which is right for one that has nothing irreversible to lose:
    /// storing a transient 500 would make it permanent for the life of the key.
    /// </summary>
    public const string CommittedMarker = "Vni.Idempotency.Committed";

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        if (!Guarded.Contains(request.Method)
            || Exempt.Contains(request.Path)
            || IsSsoStart(request.Path)
            || IsSessionRevoke(request)
            || IsAnswerAutosave(request)
            || IsStopwatch(request)
            || IsRecordingUpload(request)
            || IsDictationCheck(request))
        {
            await next(context);
            return;
        }

        if (!request.Headers.TryGetValue(HeaderName, out var header)
            || string.IsNullOrWhiteSpace(header))
        {
            await WriteProblem(context, StatusCodes.Status400BadRequest,
                ErrorCodes.IdempotencyKeyMissing,
                $"This endpoint requires an {HeaderName} header so a retry cannot be "
                + "mistaken for a second request.");
            return;
        }

        var key = header.ToString();
        if (key.Length > 200)
        {
            await WriteProblem(context, StatusCodes.Status400BadRequest,
                ErrorCodes.IdempotencyKeyMissing, "The idempotency key is too long.");
            return;
        }

        // Scope the key to the caller. Otherwise one client's key collides with
        // another's and leaks a stored response across accounts.
        var subject = context.UserId() ?? "anonymous";
        var storageKey = $"{subject}:{request.Method}:{request.Path}:{key}";

        request.EnableBuffering();
        var bodyHash = await HashBodyAsync(request);
        request.Body.Position = 0;

        /*
         * ── Claim before execute ──────────────────────────────────────────
         *
         * <b>This used to be look, execute, then insert, and the order was the
         * bug.</b> Two requests carrying the same key both missed the lookup,
         * both ran the handler, and the second one's insert failed on the
         * duplicate <c>_id</c> — which was caught and reported as "a replay
         * that arrived a moment too early". It was nothing of the kind. The
         * handler had already run twice: two sittings started, two sections
         * marked, two AI evaluations bought, the entitlement spent twice. The
         * only thing the duplicate key prevented was storing the second
         * response, and the two callers were then handed different bodies for
         * what was supposed to be one operation.
         *
         * A key that is claimed first cannot be executed twice, because the
         * claim is the thing the database serialises. Whoever inserts the
         * record owns the operation; everyone else is told to replay or to wait
         * — and neither of those runs a handler.
         */
        var token = Guid.NewGuid().ToString("n");
        var now = clock.UtcNow.UtcDateTime;

        BsonDocument Claim() => new()
        {
            ["_id"] = storageKey,
            ["bodyHash"] = bodyHash,
            ["state"] = InFlight,
            ["token"] = token,
            ["leaseUntil"] = now.Add(Lease),
            // A TTL index on this field expires the record after 24h.
            ["createdAt"] = now,
        };

        /*
         * <b>Two passes, because a record can change between the failed insert
         * and the read that asks who has it.</b>
         *
         * The holder can finish, the TTL can remove it, or a takeover can
         * happen — and answering "come back in a second" to a key whose
         * response is already stored costs the caller a round trip for nothing,
         * while answering it to a key that was reused with a different body
         * hides a client error behind a wait the client will honour for ever.
         * A second pass re-reads and routes properly. Two is enough: a third
         * collision means genuine contention, which <i>is</i> the wait.
         */
        var claimed = false;

        for (var attempt = 0; attempt < 2 && !claimed; attempt++)
        {
            try
            {
                await Keys.InsertOneAsync(Claim(), cancellationToken: context.RequestAborted);
                claimed = true;
                break;
            }
            catch (MongoWriteException e)
                when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Somebody has this key. Below decides which of the three kinds.
            }

            var held = await Keys.Find(Builders<BsonDocument>.Filter.Eq("_id", storageKey))
                .FirstOrDefaultAsync(context.RequestAborted);

            // Gone between the insert and the read. Try to claim it again.
            if (held is null) continue;

            if (held.GetValue("bodyHash", BsonNull.Value).AsString != bodyHash)
            {
                await WriteProblem(context, StatusCodes.Status409Conflict,
                    ErrorCodes.IdempotencyKeyReused,
                    "This idempotency key was already used for a request with a different body.");
                return;
            }

            var state = held.GetValue("state", InFlight).AsString;

            if (state == Done)
            {
                /*
                 * A genuine replay. Return exactly what the first attempt
                 * returned — not something this request computed for itself.
                 *
                 * <b>Including its content type.</b> Replaying everything as
                 * `application/json` was right for every endpoint guarded today
                 * and wrong as a rule: the first guarded endpoint that answers
                 * `204`, or `application/problem+json`, or anything with a
                 * `Location`, would have its replay differ from its original —
                 * and a replay that differs from what it replays is not a
                 * replay. The stored type is used, with JSON as the fallback
                 * for records written before it was stored.
                 */
                context.Response.StatusCode = held["status"].AsInt32;
                context.Response.ContentType =
                    held.GetValue("contentType", BsonNull.Value) is BsonString stored
                        ? stored.AsString
                        : "application/json";

                if (held.GetValue("location", BsonNull.Value) is BsonString location)
                    context.Response.Headers.Location = location.AsString;

                context.Response.Headers["Idempotency-Replayed"] = "true";

                var body = held.GetValue("body", BsonNull.Value);
                if (body is BsonString text && text.AsString.Length > 0)
                    await context.Response.WriteAsync(text.AsString, context.RequestAborted);

                return;
            }

            if (state == Unknown)
            {
                /*
                 * <b>The first attempt may have committed, and nobody can tell.</b>
                 *
                 * Running the handler here would perform an irreversible
                 * transition a second time; answering with a success would
                 * report an outcome nobody observed. Neither is available, so
                 * the caller is told the truth and pointed at the GET that
                 * reports the real state.
                 *
                 * <b>Only inside the lease.</b> Past it the takeover below
                 * applies, which is what stops this becoming a key that can
                 * never be used again.
                 */
                if (held.GetValue("leaseUntil", BsonNull.Value) is BsonDateTime until
                    && until.ToUniversalTime() > now)
                {
                    context.Response.Headers.RetryAfter = "2";
                    await WriteProblem(context, StatusCodes.Status409Conflict,
                        ErrorCodes.OperationOutcomeUnknown,
                        "A previous attempt with this key was interrupted after it may have "
                        + "taken effect. Read the current state rather than retrying.");
                    return;
                }
            }

            /*
             * Still in flight. <b>Taking it over is allowed only once the lease
             * has run out</b>, and the update is what decides — reading the
             * expiry and then writing would let two takeovers through, which is
             * the original bug in a smaller box. The holder's own token is in
             * the filter as well, so two requests that read the same expired
             * claim cannot both succeed.
             */
            var takeover = await Keys.UpdateOneAsync(
                Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("_id", storageKey),
                    Builders<BsonDocument>.Filter.In("state", new[] { InFlight, Unknown }),
                    Builders<BsonDocument>.Filter.Eq("token", held.GetValue("token", BsonNull.Value)),
                    Builders<BsonDocument>.Filter.Lt("leaseUntil", now)),
                Builders<BsonDocument>.Update
                    .Set("state", InFlight)
                    .Set("token", token)
                    .Set("leaseUntil", now.Add(Lease)),
                cancellationToken: context.RequestAborted);

            if (takeover.MatchedCount > 0)
            {
                claimed = true;

                /*
                 * <b>Logged, because a takeover is never routine.</b> It means
                 * a request holding this key stopped without releasing it —
                 * the process died, or it ran past a five-minute lease. Both
                 * are worth knowing about before they are worth debugging, and
                 * the second is how a paid evaluation gets bought twice.
                 * → `I5.2`
                 */
                logger.LogWarning(
                    "Idempotency claim {Key} taken over from a holder that never released it "
                    + "(previous state {State}). A handler outlived its {LeaseMinutes}-minute "
                    + "lease, or the process holding it died.",
                    storageKey, state, Lease.TotalMinutes);
            }

            // Otherwise somebody is genuinely working on it, or it completed
            // while we were looking. The next pass re-reads and finds out
            // which; if there is no next pass, the caller is told to wait.
        }

        if (!claimed)
        {
            // Waiting here would hold a connection open for the length of
            // somebody else's request. The caller is told to come back instead.
            await WriteInFlight(context);
            return;
        }

        // Capture the response so it can be replayed.
        var original = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        var mine = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", storageKey),
            Builders<BsonDocument>.Filter.Eq("token", token));

        /*
         * <b>The lease moves while the handler runs.</b> Without this the
         * five-minute bound is a bet on how long the slowest handler takes, and
         * the losing side of that bet is a second paid evaluation.
         */
        using var beating = new CancellationTokenSource();
        var heartbeat = HeartbeatAsync(mine, beating.Token);

        try
        {
            await next(context);
        }
        catch (Exception failure)
        {
            context.Response.Body = original;

            /*
             * ── Released, or recorded as undetermined ─────────────────────
             *
             * <b>"The handler threw, so nothing was decided" is true for an
             * exception thrown before anything committed, and exactly false for
             * one thrown after.</b> The version this replaces released the
             * claim for both, so the retry re-ran a handler that had already
             * taken effect.
             *
             * Two signals say the side effect may have landed:
             *
             * <b>The caller went away.</b> A phone changing network cancels the
             * request token, and that cancellation unwinds through here — from
             * anywhere, including from after the transition's compare-and-swap.
             * Cancellation says something about the <i>caller</i> and nothing
             * about the <i>handler</i>, so reading it as "nothing happened" is
             * reading the wrong end of the request.
             *
             * <b>The handler said so.</b> `CommittedMarker` is set by a handler
             * that has passed its own point of no return, which covers the
             * exceptions cancellation does not — a marking runner that fails
             * after the transition landed, say.
             *
             * Anything else is released, deliberately. Storing a transient 500
             * would make it permanent for the life of the key, and the retry is
             * the whole point of a key.
             */
            var committed =
                context.Items.ContainsKey(CommittedMarker)
                || (failure is OperationCanceledException && context.RequestAborted.IsCancellationRequested);

            try
            {
                if (committed)
                {
                    logger.LogWarning(
                        failure,
                        "Idempotency claim {Key} left undetermined: the handler may have "
                        + "committed before this failure. A retry is refused until the lease "
                        + "expires rather than repeating an irreversible operation.",
                        storageKey);

                    await Keys.UpdateOneAsync(
                        mine,
                        Builders<BsonDocument>.Update.Set("state", Unknown),
                        cancellationToken: CancellationToken.None);
                }
                else
                {
                    await Keys.DeleteOneAsync(mine, CancellationToken.None);
                }
            }
            catch (MongoException)
            {
                // Nothing useful to do. The lease is the backstop.
            }

            await beating.CancelAsync();
            await heartbeat;

            throw;
        }

        await beating.CancelAsync();
        await heartbeat;

        context.Response.Body = original;

        /*
         * ── From here the handler has run ─────────────────────────────────
         *
         * <b>Everything below uses <c>CancellationToken.None</c>, and that is
         * the correction, not an oversight.</b>
         *
         * It used to run on <c>RequestAborted</c> inside the same try as the
         * handler, with a catch that deleted the claim. So a phone that changed
         * network after its submit succeeded cancelled the token, the read of
         * the buffered body threw, the catch <i>removed the claim</i>, and the
         * retry ran the whole handler again — a second sitting, a second
         * marking, the entitlement spent twice. That is the exact case the
         * header of this file names as the reason it exists, and the guard was
         * undoing itself in it.
         *
         * The caller going away is not a reason to forget what was decided. It
         * is the reason to remember.
         */
        buffer.Position = 0;
        var responseBody = await new StreamReader(buffer).ReadToEndAsync(CancellationToken.None);

        if (context.Response.StatusCode is >= 200 and < 300)
        {
            /*
             * <b>Filtered on this request's own token.</b> If the lease ran out
             * and another request took the claim over, that one now owns the
             * answer; overwriting it here would hand two callers two different
             * stored responses for one key, which is the thing the key exists
             * to prevent.
             */
            var completion = await Keys.UpdateOneAsync(
                mine,
                Builders<BsonDocument>.Update
                    .Set("state", Done)
                    .Set("status", context.Response.StatusCode)
                    .Set("body", responseBody)
                    // Stored so the replay is the same response rather than a
                    // JSON-shaped approximation of it. → the replay branch above
                    .Set("contentType", context.Response.ContentType ?? "application/json")
                    .Set<BsonDocument, BsonValue>(
                        "location",
                        context.Response.Headers.Location.Count > 0
                            ? context.Response.Headers.Location.ToString()
                            : BsonNull.Value),
                cancellationToken: CancellationToken.None);

            /*
             * <b>Nothing matched, so somebody else owns this key now.</b> The
             * lease ran out while this handler was inside it and a takeover
             * replaced the token. This response is real — the side effect
             * happened — and it has nowhere to be stored, which means the
             * operation ran twice and two callers will be handed two different
             * answers for one key.
             *
             * There is nothing to do about it here, and everything to say about
             * it: silently discarding the write is how a lease that is too
             * short stays too short. → `I5.2`
             */
            if (completion.MatchedCount == 0)
            {
                logger.LogError(
                    "Idempotency claim {Key} was taken over while its handler was still "
                    + "running, so a successful response could not be stored. The operation "
                    + "has run more than once. The {LeaseMinutes}-minute lease is shorter "
                    + "than this handler's real duration.",
                    storageKey, Lease.TotalMinutes);
            }
        }
        else
        {
            /*
             * <b>A failure releases the claim rather than recording it.</b>
             * Storing a non-success would make a transient 500 permanent for
             * the lifetime of the key — the client could retry for twenty-four
             * hours and be handed the same 500 without anything ever running
             * again. Deleting it is what makes the retry mean something.
             */
            await Keys.DeleteOneAsync(mine, CancellationToken.None);
        }

        // Last, because the record is what a retry needs and this is only what
        // *this* caller needs — and this caller may already be gone.
        try
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(original, CancellationToken.None);
        }
        catch (IOException)
        {
            // The connection went away. The outcome is stored; the retry will
            // find it.
        }
    }

    /// <summary>
    /// Pushes this claim's lease forward until the handler finishes.
    ///
    /// Failures are swallowed and the loop carries on: a heartbeat that cannot
    /// write is exactly the situation the lease exists for, and turning a
    /// transient database blip into a failed request would be trading a rare,
    /// recoverable problem for a certain one.
    /// </summary>
    private async Task HeartbeatAsync(FilterDefinition<BsonDocument> mine, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(Heartbeat, stop);

                var renewed = await Keys.UpdateOneAsync(
                    mine,
                    Builders<BsonDocument>.Update.Set(
                        "leaseUntil", clock.UtcNow.UtcDateTime.Add(Lease)),
                    cancellationToken: CancellationToken.None);

                /*
                 * Matched nothing: somebody took this claim over, or it was
                 * completed by a takeover's handler. Either way this request no
                 * longer owns the key and must stop pushing its lease.
                 */
                if (renewed.MatchedCount == 0)
                {
                    logger.LogWarning(
                        "Idempotency heartbeat stopped: the claim is no longer owned by this "
                        + "request. A takeover happened while the handler was still running, "
                        + "so the operation is running more than once.");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The handler finished. Ordinary.
        }
        catch (MongoException e)
        {
            logger.LogWarning(e, "Idempotency heartbeat failed; the lease will expire normally.");
        }
    }

    /// <summary>
    /// The same key is being executed right now, by somebody else.
    ///
    /// <b>409 rather than replaying, because there is nothing yet to
    /// replay.</b> The first attempt has not finished, so its response does not
    /// exist; answering with anything this request computed would mean running
    /// the handler twice, which is exactly what the key forbids.
    ///
    /// <c>Retry-After</c> is what makes it actionable: come back and the answer
    /// will either be the stored one or this same wait, and neither of those
    /// costs a second execution.
    /// </summary>
    private static Task WriteInFlight(HttpContext context)
    {
        context.Response.Headers.RetryAfter = "1";

        return WriteProblem(
            context, StatusCodes.Status409Conflict, ErrorCodes.IdempotencyKeyInFlight,
            "A request with this idempotency key is still being processed. "
            + "Retry in a moment to receive its result.");
    }

    private static async Task<string> HashBodyAsync(HttpRequest request)
    {
        using var sha = SHA256.Create();
        var bytes = await sha.ComputeHashAsync(request.Body);
        return Convert.ToHexString(bytes);
    }

    private static async Task WriteProblem(
        HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(
            $$"""
            {"title":"Idempotency","status":{{status}},"code":"{{code}}",
             "detail":{{System.Text.Json.JsonSerializer.Serialize(detail)}},
             "traceId":{{System.Text.Json.JsonSerializer.Serialize(context.TraceIdentifier)}}}
            """.Replace("\n", string.Empty),
            context.RequestAborted);
    }
}

/// <summary>
/// Notified at the instant a guarded handler sets <see cref="IdempotencyMiddleware.CommittedMarker"/>
/// — its irreversible side effect has landed, before the response is
/// serialised or written.
///
/// <b>F0.2 — written 2026-08-28, so a test can stop guessing at a race.</b>
/// <c>IdempotencyContractTests</c> used to prove "a client that cancels after
/// its request committed is not replayed as a fresh execution" by racing a
/// hard-coded one-millisecond client timeout against however long the real
/// HTTP round trip happened to take that run — sometimes landing before the
/// commit, sometimes long after, sometimes never inside the window at all.
/// This gives the test a real signal to wait on instead of a stopwatch guess.
///
/// <b>A no-op in production</b> (<see cref="NoOpCommitSignal"/>), on purpose:
/// the commit itself, via <see cref="IdempotencyMiddleware.CommittedMarker"/>,
/// is what the middleware actually acts on. This interface exists only so a
/// test can observe the same moment from outside the request.
/// </summary>
public interface ICommitSignal
{
    void Signal(HttpContext context);
}

/// <summary>The production implementation: does nothing.</summary>
public sealed class NoOpCommitSignal : ICommitSignal
{
    public void Signal(HttpContext context) { }
}
