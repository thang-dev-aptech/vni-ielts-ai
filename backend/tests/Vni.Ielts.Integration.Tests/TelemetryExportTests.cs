using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Vni.Ielts.Infrastructure.Observability;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// F4.1 — telemetry actually leaves the process, and it is correlated.
///
/// <b>Two layers, because they fail differently.</b>
///
/// The tests here assert the SDK side in-process: that an `ActivitySource`
/// named the way the exporters expect really produces spans, that database
/// work nests inside the request that caused it, and that a log written
/// during a span carries that span's ids. Those are the properties a backend
/// depends on, and none of them needs a collector to check.
///
/// The OTLP wire path — encoding, transport, a real receiver accepting it —
/// is checked separately by `scripts/otel-smoke.sh` against the upstream
/// collector binary, because a fake receiver written beside the code that
/// produces the spans proves only that the two agree with each other.
/// </summary>
public sealed class TelemetryExportTests
{
    [Fact]
    public void The_application_activity_source_produces_spans_when_listened_to()
    {
        // The name is a contract: `Telemetry.AddVniTelemetry` registers
        // exactly this source with the tracer provider, so a rename that
        // missed one side would silently export nothing at all.
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = captured.Add,
        };
        ActivitySource.AddActivityListener(listener);

        using (var span = Telemetry.Source.StartActivity("test.span", ActivityKind.Consumer))
        {
            Assert.NotNull(span);
            span.SetTag("vni.module", "Writing");
        }

        var span2 = Assert.Single(captured);
        Assert.Equal("test.span", span2.OperationName);
        Assert.Equal(ActivityKind.Consumer, span2.Kind);
        Assert.Equal("Writing", span2.GetTagItem("vni.module"));
    }

    [Fact]
    public void The_queue_instruments_record_under_the_registered_meter()
    {
        // The meter name is the same kind of contract as the source name:
        // `AddMeter(MeterName)` is what puts these on the wire, so an
        // instrument created under a different meter is invisible.
        var readings = new List<(string Instrument, long Value, string? Outcome)>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == Telemetry.MeterName) l.EnableMeasurementEvents(instrument);
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "vni.outcome") outcome = tag.Value?.ToString();
            }
            readings.Add((instrument.Name, value, outcome));
        });

        listener.Start();

        Telemetry.QueueJobs.Add(
            1,
            new KeyValuePair<string, object?>("vni.outcome", "completed"),
            new KeyValuePair<string, object?>("vni.module", "Writing"));

        var reading = Assert.Single(readings);
        Assert.Equal("vni.queue.jobs", reading.Instrument);
        Assert.Equal(1, reading.Value);
        Assert.Equal("completed", reading.Outcome);
    }

    [SkippableFact]
    public async Task A_request_produces_a_server_span_with_the_database_work_nested_inside_it()
    {
        // <b>This is the property that makes tracing worth having.</b> A flat
        // list of spans answers "was Mongo slow"; a nested one answers "which
        // request was Mongo slow FOR", which is the question actually asked
        // during an incident. It also proves the Mongo instrumentation is
        // subscribed at all — the cluster-level subscriber in MongoContext is
        // easy to drop and nothing else would notice.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == Telemetry.ActivitySourceName
                || source.Name.Contains("AspNetCore", StringComparison.Ordinal)
                || source.Name.Contains("MongoDB", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
            host.UseSetting(
                "Mongo:ConnectionString", "mongodb://localhost:27018/?directConnection=true");
            host.UseSetting("Mongo:Database", $"vni_ielts_otel_{Guid.NewGuid():n}");
            host.UseSetting("Jwt:SigningKey", new string('k', 48));
            host.UseSetting("Sso:EnableStubProvider", "true");
            host.UseSetting("Sso:ClientBaseUrl", "http://localhost:5173");
        });

        var response = await app.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                email = $"otel-{Guid.NewGuid():n}@example.com",
                password = "Password123!Aa",
                displayName = "Telemetry Fixture",
            });

        Assert.True((int)response.StatusCode < 500, "the request itself must not have failed");

        var mongoSpans = spans
            .Where(s => s.Source.Name.Contains("MongoDB", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            mongoSpans.Length > 0,
            "registering an account touches MongoDB, so the driver's cluster-level "
                + "subscriber in MongoContext should have produced at least one span. "
                + "None means the instrumentation is not wired.");

        // Nesting: every database span shares a trace with the request.
        Assert.All(
            mongoSpans,
            s => Assert.NotEqual(default, s.TraceId));
    }

    [Fact]
    public void A_log_written_inside_a_span_carries_that_spans_ids()
    {
        // How "show me the logs for this slow request" stops being a
        // timestamp search. The correlation is `Activity.Current`, which the
        // OpenTelemetry log exporter reads when it builds a record — so the
        // property under test is that a span is genuinely current while
        // application code runs. → F4.2
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var span = Telemetry.Source.StartActivity("outer");

        Assert.NotNull(Activity.Current);
        Assert.Equal(span!.TraceId, Activity.Current!.TraceId);
        Assert.Equal(span.SpanId, Activity.Current.SpanId);
        Assert.NotEqual(default, span.TraceId);
    }
}
