using System.Diagnostics;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Vni.Ielts.Worker;

/// <summary>
/// The worker's answer to <c>IRequestDevice</c>: there is no request to read
/// one from. See the registration in Program.cs for how this port's absence
/// was found.
/// </summary>
internal sealed class NullRequestDevice : Vni.Ielts.Application.Identity.IRequestDevice
{
    public string? UserAgent => null;
}

/// <summary>
/// What "the marking loop is alive" actually means, tracked from inside the
/// loop rather than inferred from the process still running.
///
/// <b>F2.2 — written because a process can be alive and its loop dead at the
/// same time.</b> A polling loop that throws past its own catch-all, or one
/// that deadlocks inside a single iteration, leaves the .NET process running
/// — nothing crashes, nothing restarts it — while no job has been claimed
/// since. A worker that has stopped working looks, from a process-liveness
/// check alone, identical to one with an empty queue.
///
/// <b>Three facts, not one boolean.</b> "Has the loop ever started",
/// "is the most recent iteration recent enough" and "did the loop exit with
/// an exception it did not itself recover from" are three different ways to
/// be unhealthy, and folding them into a single flag would lose which one to
/// report.
/// </summary>
public sealed class WorkerHealthState
{
    private long _lastPollTicks;
    private volatile bool _started;
    private Exception? _fatal;

    /// <summary>
    /// Which loop this state describes.
    ///
    /// <b>One state per loop, because a shared one lies.</b> Until the import
    /// worker arrived there was exactly one loop, and "the loop is alive" was
    /// a whole answer. With two, a single shared state is refreshed by
    /// whichever loop is still polling — so a marking worker doing its job
    /// every five seconds keeps <see cref="SinceLastPoll"/> fresh while the
    /// import loop is dead, and readiness reports healthy over a queue of
    /// uploads nothing is draining. That is precisely the failure this whole
    /// type was written to catch ("a process can be alive and its loop dead"),
    /// reintroduced one level up.
    /// </summary>
    public string Loop { get; init; } = "loop";

    /// <summary>
    /// Longer than the 40-second heartbeat interval by a comfortable margin,
    /// short enough to still catch a loop that has genuinely stalled within
    /// a couple of minutes rather than needing an operator to notice first.
    ///
    /// <b>A settable property, not a constant.</b> Proving the endpoint
    /// reports "stale" means outliving this threshold, and a test should not
    /// have to wait 90 real seconds to do it — the same reasoning as
    /// <c>IdempotencyMiddleware.Lease</c> being a constructor parameter.
    /// </summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Called once per loop iteration — whether it claimed a job or found the
    /// queue empty — and again on every successful lease-renewal heartbeat
    /// while a job is in progress.
    ///
    /// <b>The heartbeat call matters as much as the loop-top one.</b> Without
    /// it, a worker in the middle of a single long-running marking job (the
    /// day an evaluator makes that minutes rather than milliseconds) would
    /// look stale on every reading between one loop iteration and the next,
    /// which for a job near the lease's own 2-minute bound is long past this
    /// state's staleness threshold. The heartbeat already exists, at a
    /// tighter interval, for exactly the failure mode this guards.
    /// </summary>
    public void RecordPoll()
    {
        _started = true;
        Interlocked.Exchange(ref _lastPollTicks, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// The loop exited its own catch-all — something outside a single
    /// iteration's recoverable-failure handling. Sticky: once fatal, always
    /// fatal for the life of the process, because nothing is going to call
    /// <see cref="RecordPoll"/> again.
    /// </summary>
    public void RecordFatal(Exception e) => _fatal = e;

    public bool Started => _started;

    public bool IsFatal => _fatal is not null;

    public string? FatalReason => _fatal?.GetType().Name;

    public TimeSpan SinceLastPoll => _lastPollTicks == 0
        ? TimeSpan.MaxValue
        : Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastPollTicks));
}

/// <summary>
/// The same two questions the API's own health endpoints ask, answered for a
/// process that has no controllers and nothing else to route.
/// </summary>
public static class WorkerHealthEndpoints
{
    public static void MapWorkerHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .WithSummary("Is this process worth keeping. Touches nothing external");

        app.MapGet("/health/ready", ReadyAsync)
            .WithSummary("Is every worker loop actually claiming work");
    }

    /// <param name="loops">
    /// Every registered <see cref="WorkerHealthState"/>, one per loop.
    ///
    /// <b>Each loop reports for itself and any one of them can fail
    /// readiness.</b> Reporting a single merged answer would let a healthy
    /// marking loop stand in for a dead import loop, which is the masking this
    /// split exists to remove — and a dead import loop is silent by nature:
    /// uploads keep being accepted, and nothing drains them.
    /// </param>
    private static async Task<IResult> ReadyAsync(
        IMongoDatabase database, IEnumerable<WorkerHealthState> loops, CancellationToken ct)
    {
        var checks = new List<object>();
        var ready = true;

        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            var started = Stopwatch.GetTimestamp();

            try
            {
                // The same ping the API uses: the question is whether the
                // queue store answers at all, not whether any particular job
                // exists — an empty queue is not an outage.
                await database.RunCommandAsync<BsonDocument>(
                    new BsonDocument("ping", 1), cancellationToken: deadline.Token);

                checks.Add(new
                {
                    name = "mongo",
                    status = "ok",
                    ms = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                });
            }
            catch (Exception e)
            {
                ready = false;
                checks.Add(new
                {
                    name = "mongo",
                    status = "failed",
                    error = e.GetType().Name,
                    ms = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                });
            }
        }

        foreach (var health in loops)
        {
            object loop;

            if (health.IsFatal)
            {
                ready = false;
                loop = new { name = health.Loop, status = "failed", error = health.FatalReason };
            }
            else if (!health.Started)
            {
                // The window between the process coming up and the loop's first
                // iteration — genuinely not ready yet, not a failure.
                ready = false;
                loop = new { name = health.Loop, status = "starting" };
            }
            else if (health.SinceLastPoll > health.StaleAfter)
            {
                ready = false;
                loop = new
                {
                    name = health.Loop,
                    status = "stale",
                    sinceLastPollMs = (int)health.SinceLastPoll.TotalMilliseconds,
                };
            }
            else
            {
                loop = new
                {
                    name = health.Loop,
                    status = "ok",
                    sinceLastPollMs = (int)health.SinceLastPoll.TotalMilliseconds,
                };
            }

            checks.Add(loop);
        }

        return ready
            ? Results.Ok(new { status = "ready", checks })
            : Results.Json(
                new { status = "not-ready", checks },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
