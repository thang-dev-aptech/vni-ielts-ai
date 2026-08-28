using System.Diagnostics;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Infrastructure.Observability;

namespace Vni.Ielts.Worker.Tests;

/// <summary>
/// F4.2 — the trace survives the queue.
///
/// <b>The queue is where a trace would otherwise end.</b> A learner submits,
/// the API enqueues a marking job, and a different process picks it up
/// minutes later. Without the trace context travelling on the job, the
/// worker's span is the root of a brand-new trace and "the learner submitted
/// and the result never came back" is two unrelated traces nobody can join.
///
/// There is no message broker here — the job row <i>is</i> the message — so
/// the context rides on the job as a W3C `traceparent` string.
/// </summary>
public sealed class CorrelationTests
{
    private static readonly ActivitySource TestSource = new("Vni.Ielts.Tests.Correlation");

    private static ActivityListener ListenToEverything(List<Activity> sink) =>
        new()
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = sink.Add,
        };

    [Fact]
    public void A_job_enqueued_inside_a_request_carries_that_requests_traceparent()
    {
        var stopped = new List<Activity>();
        using var listener = ListenToEverything(stopped);
        ActivitySource.AddActivityListener(listener);

        using var request = TestSource.StartActivity("POST /api/v1/sessions/submit");
        Assert.NotNull(request);

        // What `MarkingOutbox.EnqueueAsync` captures at the boundary.
        var captured = Activity.Current?.Id;

        Assert.NotNull(captured);
        Assert.Contains(request!.TraceId.ToHexString(), captured!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_worker_span_joins_the_trace_the_job_came_from()
    {
        // <b>The property that makes the whole mechanism worth having.</b> Not
        // "a traceparent was stored" but "the marking span ends up in the same
        // trace as the submit", which is the question asked during an
        // incident.
        var stopped = new List<Activity>();
        using var listener = ListenToEverything(stopped);
        ActivitySource.AddActivityListener(listener);

        string traceParent;
        ActivityTraceId submitTraceId;

        using (var submit = TestSource.StartActivity("POST /api/v1/sessions/submit"))
        {
            Assert.NotNull(submit);
            submitTraceId = submit!.TraceId;
            traceParent = submit.Id!;
        }

        var job = JobWith(traceParent);

        // Exactly what MarkingWorker does with job.TraceParent.
        Assert.True(ActivityContext.TryParse(job.TraceParent, null, out var parent));

        using (var marking = Telemetry.Source.StartActivity(
            "marking.job", ActivityKind.Consumer, parent))
        {
            Assert.NotNull(marking);
            Assert.Equal(
                submitTraceId,
                marking!.TraceId);
        }
    }

    [Fact]
    public void A_job_without_a_traceparent_still_marks_rather_than_failing()
    {
        // Telemetry must never be able to break the work it describes. A job
        // enqueued before this field existed, or by a path with no active
        // span, has to be markable exactly as before.
        var job = JobWith(null);

        var hasParent = job.TraceParent is { Length: > 0 } tp
            && ActivityContext.TryParse(tp, null, out _);

        Assert.False(hasParent);
    }

    [Fact]
    public void A_malformed_traceparent_is_ignored_rather_than_throwing()
    {
        // A corrupted or truncated value in the database must degrade to "no
        // parent", not to an exception on the marking path.
        var job = JobWith("this-is-not-a-traceparent");

        var parsed = ActivityContext.TryParse(job.TraceParent, null, out _);

        Assert.False(parsed);
    }

    private static MarkingJob JobWith(string? traceParent) => new(
        OperationId: "op-1",
        SessionId: ExamSessionId.New(),
        Module: ExamModule.Writing,
        RubricVersion: "v1",
        State: MarkingJobState.Pending,
        Attempts: 0,
        CreatedAt: DateTimeOffset.UnixEpoch,
        NextAttemptAt: null,
        LeaseUntil: null,
        LeaseToken: null,
        LastError: null,
        CompletedAt: null,
        TraceParent: traceParent);
}
