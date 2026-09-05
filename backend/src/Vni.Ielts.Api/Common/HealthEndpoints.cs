using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Infrastructure.Observability;
using Vni.Ielts.Infrastructure.Storage;

namespace Vni.Ielts.Api.Common;

/// <summary>
/// Two questions an orchestrator asks, and they are not the same question.
///
/// <b>Written 2026-08-28, replacing a single `/health` that returned `ok`
/// unconditionally.</b> That endpoint answered the same way whether the
/// database was reachable or not, which makes it worse than having none: a load
/// balancer routes traffic to it, a deployment goes green on it, and an
/// operator watching it sees a healthy system serving 500s.
///
/// ── Why two, and why the difference matters ───────────────────────────────
///
/// <b>`/health/live` asks "is this process worth keeping".</b> It touches
/// nothing external and it is what a restart policy reads. If it consulted the
/// database, then a database outage would restart every API process — turning
/// one failure into two, and losing the in-flight requests that would have
/// survived it. A liveness probe that fails on a dependency is how a brief
/// outage becomes a restart loop.
///
/// <b>`/health/ready` asks "should this process be sent traffic".</b> It checks
/// what a request actually needs, so a node that cannot serve is taken out of
/// rotation without being killed — and comes back on its own when the
/// dependency does.
///
/// <b>An optional dependency must not fail readiness.</b> No AI evaluator is
/// wired (`B-2`), and Reading and Listening are marked from the answer key and
/// never touch one. A readiness check that failed on it would take the whole
/// product out of rotation to report a module that was never available.
/// → `A-11`, `I6.4`
/// </summary>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        /*
         * <b>Nothing external, deliberately.</b> This is what a restart policy
         * reads, and a liveness probe that fails on a dependency turns one
         * outage into a restart loop across every process.
         */
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .WithTags("Ops")
            .WithSummary("Is this process worth keeping. Touches nothing external")
            .AllowAnonymous();

        app.MapGet("/health/ready", ReadyAsync)
            .WithTags("Ops")
            .WithSummary("Should this process be sent traffic")
            .AllowAnonymous();

        /*
         * <b>Kept, because something is already pointed at it.</b> Removing a
         * health endpoint is the kind of change that is discovered by a load
         * balancer at three in the morning. It now answers the readiness
         * question rather than lying, so anything still using it gets better
         * information than it did.
         */
        app.MapGet("/health", ReadyAsync).WithTags("Ops").AllowAnonymous();
    }

    private static async Task<IResult> ReadyAsync(
        IMongoDatabase database, HttpContext context, CancellationToken ct)
    {
        /*
         * <b>Resolved explicitly, not as a nullable service parameter.</b> In
         * Development, with no object storage configured,
         * <c>IObjectStorageHealthCheck</c> is never registered at all — and
         * minimal-API parameter binding decides *how* to bind a parameter once,
         * at route-build time, from whether the type is a known service. An
         * unregistered type doesn't reliably fall back to "inject null"; it can
         * be inferred as a body parameter instead, which is the wrong binding
         * for a GET and a startup-time trap for exactly the environment this
         * endpoint most needs to keep working in. Asking the container
         * directly is unambiguous either way.
         */
        var objectStorage = context.RequestServices.GetService<IObjectStorageHealthCheck>();

        var checks = new List<object>();
        var ready = true;

        var mongo = await CheckAsync(
            "mongo",
            async token =>
            {
                /*
                 * <b>A ping, not a query.</b> Reading a collection would make
                 * readiness depend on the data being right as well as the
                 * database being up, and an empty catalogue is not an outage.
                 */
                await database.RunCommandAsync<BsonDocument>(
                    new BsonDocument("ping", 1), cancellationToken: token);
            },
            ct);

        checks.Add(mongo.Report);
        ready &= mongo.Ok;

        /*
         * <b>Media, when there is a store to ask.</b> A Development process
         * reads from the fixtures directory and has nothing to probe; a
         * production one has a bucket, and a bucket that has stopped answering
         * is a node that should not be sent a Listening section.
         *
         * <b>`HeadBucket`, never a fetch.</b> Reading an arbitrary object key
         * would make readiness depend on that key's absence being handled the
         * same way as an auth failure or a wrong bucket name — which used to
         * be true and is the bug this replaced. `IObjectStorageHealthCheck`
         * asks the access question directly and throws a distinct, typed
         * exception for "no such bucket", "not authorized" and "unreachable".
         */
        if (objectStorage is not null)
        {
            var media = await CheckAsync("object-storage", objectStorage.CheckAsync, ct);

            checks.Add(media.Report);
            ready &= media.Ok;
        }

        return ready
            ? Results.Ok(new { status = "ready", checks })
            : Results.Json(
                new { status = "not-ready", checks },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// Runs one probe with its own deadline, and reports rather than throws.
    ///
    /// <b>A readiness endpoint that hangs is a readiness endpoint that fails
    /// open.</b> An orchestrator waiting on a probe with no answer eventually
    /// times out and treats the node as unhealthy — which is the right outcome
    /// reached the slowest possible way, after holding a connection for the
    /// whole timeout. Two seconds is far longer than a healthy ping and far
    /// shorter than any sensible probe interval.
    /// </summary>
    private static async Task<(bool Ok, object Report)> CheckAsync(
        string name, Func<CancellationToken, Task> probe, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));

        var started = Stopwatch.GetTimestamp();

        try
        {
            await probe(deadline.Token);

            return (true, new
            {
                name,
                status = "ok",
                ms = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            });
        }
        catch (Exception e)
        {
            /*
             * <b>The type, not the message.</b> An exception message from a
             * driver can carry a connection string, and this endpoint is
             * anonymous — it is reachable by anything that can reach the
             * process. The type is enough to tell a timeout from a refusal, and
             * the log has the rest.
             */
            // F4.3 — the 503 is seen only by whoever probed, and an
            // orchestrator that reacts by restarting keeps no memory of it.
            // The counter is what makes "MongoDB failed readiness three times
            // in ten minutes" a question anyone can answer. The dependency
            // NAME and the exception TYPE only — never the message, which can
            // carry a connection string. → F4.2
            Telemetry.ReadinessFailures.Add(
                1,
                new KeyValuePair<string, object?>("vni.dependency", name),
                new KeyValuePair<string, object?>("vni.error", e.GetType().Name));

            return (false, new
            {
                name,
                status = "failed",
                error = e.GetType().Name,
                ms = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            });
        }
    }
}
