using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
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
public sealed class IdempotencyMiddleware(RequestDelegate next, IMongoDatabase db, IClock clock)
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
    /// The save is a <b>whole-sheet replace</b>, so applying it twice leaves
    /// exactly the state applying it once did — there is no second action for
    /// a key to prevent.
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

    private IMongoCollection<BsonDocument> Keys => db.GetCollection<BsonDocument>("idempotency_keys");

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        if (!Guarded.Contains(request.Method)
            || Exempt.Contains(request.Path)
            || IsSsoStart(request.Path)
            || IsSessionRevoke(request)
            || IsAnswerAutosave(request)
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

        var existing = await Keys.Find(Builders<BsonDocument>.Filter.Eq("_id", storageKey))
            .FirstOrDefaultAsync(context.RequestAborted);

        if (existing is not null)
        {
            if (existing["bodyHash"].AsString != bodyHash)
            {
                await WriteProblem(context, StatusCodes.Status409Conflict,
                    ErrorCodes.IdempotencyKeyReused,
                    "This idempotency key was already used for a request with a different body.");
                return;
            }

            // A genuine replay. Return exactly what the first attempt returned.
            context.Response.StatusCode = existing["status"].AsInt32;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Idempotency-Replayed"] = "true";
            await context.Response.WriteAsync(existing["body"].AsString, context.RequestAborted);
            return;
        }

        // Capture the response so it can be replayed.
        var original = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);

            buffer.Position = 0;
            var responseBody = await new StreamReader(buffer).ReadToEndAsync(context.RequestAborted);
            buffer.Position = 0;
            await buffer.CopyToAsync(original, context.RequestAborted);

            // Only successful outcomes are recorded. Storing a failure would
            // make a transient 500 permanent for the lifetime of the key — the
            // client could never retry it successfully.
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                await Keys.InsertOneAsync(new BsonDocument
                {
                    ["_id"] = storageKey,
                    ["bodyHash"] = bodyHash,
                    ["status"] = context.Response.StatusCode,
                    ["body"] = responseBody,
                    // A TTL index on this field expires the record after 24h.
                    ["createdAt"] = clock.UtcNow.UtcDateTime,
                }, cancellationToken: context.RequestAborted);
            }
        }
        catch (MongoWriteException e)
            when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Two identical requests raced and both missed the read. The other
            // one won and its response is already stored, so this is a replay
            // that arrived a moment too early — not an error.
            context.Response.Headers["Idempotency-Replayed"] = "true";
        }
        finally
        {
            context.Response.Body = original;
        }
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
