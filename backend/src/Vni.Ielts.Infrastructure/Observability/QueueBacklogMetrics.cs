using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Infrastructure.Observability;

/// <summary>
/// Queue depth and oldest age, as observable gauges. → F4.3
///
/// <b>Observable rather than counters, because backlog is a level, not an
/// event.</b> Nothing "happens" when a queue is deep — it simply is deep, and
/// a value that is only updated when something is enqueued or dequeued goes
/// stale exactly when the queue stops moving, which is the case that matters.
/// An observable gauge asks the database at collection time instead.
///
/// <b>The cost is a database read on the metrics interval, and it is bounded
/// on purpose.</b> Two indexed queries per collection, cached for a few
/// seconds so several readers or a short interval cannot turn observability
/// into load. If the queue is the thing that is struggling, the last thing it
/// needs is a monitor hammering it.
///
/// <b>Registered by the worker, not the API.</b> Both could observe it, and
/// then every value would be reported twice by two services with no way to
/// tell a doubled backlog from two reporters. The worker owns the queue.
/// </summary>
public sealed class QueueBacklogMetrics : IDisposable
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IClock _clock;
    private readonly ILogger<QueueBacklogMetrics> _logger;
    private readonly TimeSpan _cacheFor;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private QueueBacklog _last = QueueBacklog.Empty;
    private DateTimeOffset _lastReadAt = DateTimeOffset.MinValue;

    public QueueBacklogMetrics(
        IServiceScopeFactory scopes,
        IClock clock,
        ILogger<QueueBacklogMetrics> logger,
        TimeSpan? cacheFor = null)
    {
        _scopes = scopes;
        _clock = clock;
        _logger = logger;
        _cacheFor = cacheFor ?? TimeSpan.FromSeconds(10);

        Telemetry.Meter.CreateObservableGauge(
            "vni.queue.depth",
            () => new Measurement<long>(Read().Depth),
            unit: "{job}",
            description: "Marking jobs owed and not currently being worked on.");

        Telemetry.Meter.CreateObservableGauge(
            "vni.queue.oldest_age",
            () => new Measurement<double>(Read().OldestAge.TotalSeconds),
            unit: "s",
            description: "How long the oldest owed marking job has waited.");
    }

    /// <summary>
    /// The current backlog, at most <c>_cacheFor</c> old.
    ///
    /// <b>Blocking, because the metrics callback contract is synchronous.</b>
    /// `CreateObservableGauge` hands back a value, not a task, so the read has
    /// to complete here. It is bounded by the cache and by the queries being
    /// indexed, and it runs on the SDK's collection thread rather than on
    /// anything serving a learner.
    /// </summary>
    private QueueBacklog Read()
    {
        var now = _clock.UtcNow;

        if (now - _lastReadAt < _cacheFor) return _last;

        // Never queue readers behind each other: if a read is already in
        // flight, the slightly-stale value is the right answer. Observability
        // must not become a source of contention.
        if (!_gate.Wait(0)) return _last;

        try
        {
            if (now - _lastReadAt < _cacheFor) return _last;

            using var scope = _scopes.CreateScope();
            var outbox = scope.ServiceProvider.GetRequiredService<IMarkingOutbox>();

            _last = outbox.BacklogAsync(now, CancellationToken.None).GetAwaiter().GetResult();
            _lastReadAt = now;
        }
        catch (Exception e)
        {
            /*
             * <b>A failed read reports the last known value, not zero.</b>
             * Zero is indistinguishable from "the queue is empty", so a
             * database blip would look like the backlog clearing — the single
             * most misleading thing this gauge could say, because it is also
             * exactly what a successful drain looks like.
             */
            _logger.LogWarning(e, "Could not read the marking backlog for metrics.");
        }
        finally
        {
            _gate.Release();
        }

        return _last;
    }

    public void Dispose() => _gate.Dispose();
}
