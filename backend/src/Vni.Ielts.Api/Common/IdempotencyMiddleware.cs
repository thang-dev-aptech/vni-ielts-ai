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

    private static readonly HashSet<string> Guarded =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    /// <summary>
    /// Endpoints exempt because they are safe to repeat and a client cannot
    /// reasonably mint a key for them. Login is here deliberately: requiring a
    /// key on sign-in would break the very first request a new client makes.
    /// </summary>
    private static readonly HashSet<string> Exempt =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/v1/auth/login",
            "/api/v1/auth/refresh",
            "/api/v1/auth/logout",
        };

    private IMongoCollection<BsonDocument> Keys => db.GetCollection<BsonDocument>("idempotency_keys");

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        if (!Guarded.Contains(request.Method) || Exempt.Contains(request.Path))
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
