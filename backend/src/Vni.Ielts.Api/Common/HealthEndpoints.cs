using System.Diagnostics;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;

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
        IMongoDatabase database, IExamAssetStore? assets, CancellationToken ct)
    {
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
         * <b>Not a fetch.</b> Reading an object would make readiness depend on
         * a particular file existing, and an empty catalogue is not an outage.
         * Asking whether the store can be reached is the question.
         */
        if (assets is not null)
        {
            var media = await CheckAsync(
                "object-storage",
                async token =>
                {
                    // A reference that resolves to nothing is the expected
                    // answer. What is being tested is that the store answers at
                    // all rather than throwing or hanging.
                    await assets.OpenAsync("assets/.readiness-probe", token);
                },
                ct);

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
