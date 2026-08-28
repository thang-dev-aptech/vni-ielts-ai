using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Vni.Ielts.Infrastructure.Observability;

/// <summary>
/// Traces, metrics and structured logs — one wiring, both processes.
///
/// <b>F4.1 — before this there was no telemetry of any kind.</b> No
/// `ActivitySource`, no `Meter`, no exporter: `grep -rn "OpenTelemetry\|Otlp\|
/// ActivitySource\|Meter" backend/src` returned nothing at all. A learner
/// reporting "the exam was slow" or "my result never arrived" left nobody
/// anything to look at beyond console logs with no request to tie them to.
///
/// <b>OTLP, and no vendor.</b> The protocol is the commitment; the backend is
/// not. Anything speaking OTLP — a local collector, a self-hosted stack, or a
/// SaaS chosen later — receives this unchanged. Choosing that backend is a
/// Production Ready decision and is deliberately not made here.
///
/// <b>Off unless an endpoint is configured.</b> `Otel:Endpoint` empty means no
/// exporter is registered at all, which is the honest default for a laptop and
/// for CI: an SDK that retries a dead collector every few seconds turns "no
/// observability configured" into log noise and startup latency. The
/// instrumentation still runs, so a test can read the spans in-process without
/// any collector at all. → `G-11`
/// </summary>
public static class Telemetry
{
    /// <summary>
    /// The name every span this application creates itself belongs to.
    ///
    /// Public because tests assert against it and because the worker's queue
    /// spans come from the same source as the API's — one service name per
    /// process, one activity source across both.
    /// </summary>
    public const string ActivitySourceName = "Vni.Ielts";

    public const string MeterName = "Vni.Ielts";

    /// <summary>Spans this application starts by hand. Instrumentation libraries bring their own.</summary>
    public static readonly ActivitySource Source = new(ActivitySourceName);

    public static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// Queue work actually processed, by outcome.
    ///
    /// <b>A counter rather than a log line to be grepped.</b> "How many
    /// markings failed in the last hour" is the question an alert asks, and
    /// answering it by counting log lines makes the alert depend on log
    /// retention and on nobody changing the message text. → F4.3
    /// </summary>
    public static readonly Counter<long> QueueJobs =
        Meter.CreateCounter<long>(
            "vni.queue.jobs",
            unit: "{job}",
            description: "Marking jobs processed, tagged by outcome.");

    public static readonly Histogram<double> QueueJobDuration =
        Meter.CreateHistogram<double>(
            "vni.queue.job.duration",
            unit: "s",
            description: "How long one marking job took.");

    /// <summary>
    /// Readiness probe failures, by dependency. → F4.3
    ///
    /// <b>A counter, even though `/health/ready` already returns 503.</b> The
    /// status code is visible only to whoever calls the probe — an
    /// orchestrator, which reacts by restarting or removing the instance and
    /// then has no memory of it. The counter is what makes "MongoDB has failed
    /// readiness three times in ten minutes" answerable at all, which is the
    /// difference between a restart loop nobody notices and a named
    /// dependency problem.
    /// </summary>
    public static readonly Counter<long> ReadinessFailures =
        Meter.CreateCounter<long>(
            "vni.readiness.failures",
            unit: "{failure}",
            description: "Readiness checks that failed, tagged by dependency.");

    /// <summary>
    /// Object-storage operations that failed, by kind. → F4.3
    ///
    /// Exam audio, Writing charts and Speaking recordings all live there, so a
    /// degraded store is a paper that cannot be sat rather than a background
    /// inconvenience. Tagged by error kind so "the bucket is gone" and "one
    /// object is missing" are not one number.
    /// </summary>
    public static readonly Counter<long> ObjectStorageErrors =
        Meter.CreateCounter<long>(
            "vni.objectstorage.errors",
            unit: "{error}",
            description: "Object-storage operations that failed, tagged by kind.");

    /// <summary>
    /// Registers tracing, metrics and log export for whichever process calls it.
    /// </summary>
    /// <param name="serviceName">
    /// `vni-api` or `vni-worker`. They scale and fail differently, so they are
    /// two services in the telemetry model even though they share this wiring.
    /// </param>
    /// <param name="configureTracing">
    /// The API adds ASP.NET Core instrumentation here. Infrastructure must not
    /// reference ASP.NET Core, so the web-specific part is supplied by the web
    /// process rather than pulled down into this layer.
    /// </param>
    public static IServiceCollection AddVniTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<TracerProviderBuilder>? configureTracing = null)
    {
        var endpoint = configuration["Otel:Endpoint"] ?? string.Empty;
        var exporting = !string.IsNullOrWhiteSpace(endpoint);

        var resource = ResourceBuilder
            .CreateDefault()
            .AddService(
                serviceName: serviceName,
                serviceVersion: typeof(Telemetry).Assembly.GetName().Version?.ToString() ?? "0.0.0")
            .AddAttributes(
            [
                new KeyValuePair<string, object>(
                    "deployment.environment",
                    configuration["Otel:Environment"] ?? "development"),
            ]);

        var builder = services.AddOpenTelemetry().ConfigureResource(_ => { });

        builder.WithTracing(tracing =>
        {
            tracing
                .SetResourceBuilder(resource)
                .AddSource(ActivitySourceName)
                // Outbound HTTP: every external call this process makes,
                // including the AI providers once they are wired.
                .AddHttpClientInstrumentation();

            configureTracing?.Invoke(tracing);

            if (exporting) tracing.AddOtlpExporter(o => o.Endpoint = new Uri(endpoint));
        });

        builder.WithMetrics(metrics =>
        {
            metrics
                .SetResourceBuilder(resource)
                .AddMeter(MeterName)
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation();

            if (exporting) metrics.AddOtlpExporter(o => o.Endpoint = new Uri(endpoint));
        });

        /*
         * <b>Logs go through the same pipeline as spans, not a second one.</b>
         * The point of exporting logs over OTLP is that a log line carries the
         * trace and span id of whatever was running when it was written — so
         * "show me the logs for this slow request" is a lookup rather than a
         * timestamp search. Two separate pipelines cannot do that.
         */
        services.AddLogging(logging =>
        {
            logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resource);
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;

                if (exporting) options.AddOtlpExporter(o => o.Endpoint = new Uri(endpoint));
            });
        });

        return services;
    }
}
