using Vni.Ielts.Infrastructure;
using Vni.Ielts.Infrastructure.Observability;
using Vni.Ielts.Worker;

/*
 * ── `--healthcheck`, before anything is built ─────────────────────────────
 *
 * Same reasoning as the API's own copy of this block: the `aspnet` runtime
 * image ships no `curl`, and a `HEALTHCHECK` that booted a second copy of the
 * process would be a probe that fails under the memory pressure it caused.
 * → Vni.Ielts.Api/Program.cs
 */
if (args.Contains("--healthcheck"))
{
    var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8081";
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

    try
    {
        var answer = await probe.GetAsync($"http://127.0.0.1:{port}/health/ready");
        return answer.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception)
    {
        return 1;
    }
}

/*
 * <b>The worker is a real process now, and it was a template.</b>
 *
 * What stood here registered a `BackgroundService` that logged the time once a
 * second against no dependencies at all — so the marking queue had nothing
 * draining it, and the process looked healthy while doing nothing. Worse than
 * an empty file, because a running service is evidence to whoever checks.
 *
 * <b>`AddInfrastructure`, which is the whole point.</b> The worker needs the
 * same stores, the same evaluator ports and the same rubric source the API
 * uses; a second, worker-shaped composition root would be a second definition
 * of what marking means, and the one that drifts is always the one nobody has
 * filed a bug against.
 */
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());

/*
 * <b>F4.1 — the same telemetry wiring as the API, under a different service
 * name.</b> `vni-api` and `vni-worker` scale on different signals and fail
 * differently, so they are two services in the telemetry model even though
 * they share one composition root — the same reason they are two images.
 *
 * No ASP.NET Core instrumentation: the worker's HTTP surface is its own
 * health port, and tracing a liveness probe every few seconds is noise.
 */
builder.Services.AddVniTelemetry(builder.Configuration, serviceName: "vni-worker");

/*
 * <b>F4.3 — queue depth and oldest age, observed here and nowhere else.</b>
 * The API could report them too, and then every value would arrive twice from
 * two services with no way to tell a doubled backlog from two reporters. The
 * worker owns the queue, so the worker reports on it.
 *
 * Singleton and resolved eagerly: an observable gauge that is never
 * constructed is a metric that silently does not exist.
 */
builder.Services.AddSingleton<QueueBacklogMetrics>();

// Thresholds are configuration, not code — see AlertThresholds for why.
builder.Services.Configure<AlertThresholds>(
    builder.Configuration.GetSection(AlertThresholds.SectionName));

/*
 * <b>F2.5 — checked here, not discovered inside the host's own shutdown
 * machinery.</b> A bad value below is wrong in two different ways, neither
 * caught at the point it is set. Negative throws
 * `ArgumentOutOfRangeException` out of `CancellationTokenSource.CancelAfter`
 * — reproduced directly, from inside `Host.StopAsync` — but only once
 * shutdown actually begins, the one moment a deployment can least afford a
 * surprise. Zero does not throw at all: the process boots and runs
 * normally, and a claimed job gets no grace whatsoever the day it matters,
 * with no error anywhere to say why — silently undoing F2.3.
 */
var workerShutdownTimeoutSeconds = builder.Configuration.GetValue("Worker:ShutdownTimeoutSeconds", 150);
if (workerShutdownTimeoutSeconds <= 0)
{
    throw new InvalidOperationException(
        $"Worker:ShutdownTimeoutSeconds is {workerShutdownTimeoutSeconds}. A negative value "
        + "crashes the host during shutdown instead of at startup; zero boots fine but silently "
        + "gives a claimed job no grace at all.");
}

/*
 * <b>F2.3 — long enough to let a claimed job actually finish.</b> The
 * .NET generic host's own default is 30 seconds, chosen for services with
 * nothing worth waiting on. This one has a job that took the trouble to
 * claim a lease and start a heartbeat: cutting it off at 30 seconds is
 * indistinguishable, from the learner's side, from the evaluation never
 * having been attempted at all — except it also spent whatever the provider
 * charged for the call that got abandoned.
 *
 * `[QUYẾT ĐỊNH kỹ thuật]` — the default here tracks `MarkingWorker.Lease`
 * (2 minutes) plus a margin, not a number invented independently of it:
 * a job's own lease is already the bound the rest of the system trusts for
 * "how long can one attempt reasonably take", so the shutdown window has no
 * reason to disagree with it. Configurable rather than hard-coded because
 * that lease itself is a placeholder ahead of a real evaluator's measured
 * p99 (see the class docstring) and this should move with it, not separately.
 */
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(workerShutdownTimeoutSeconds));

/*
 * <b>F2.2 — found by the worker's own health test suite, not by inspection.</b>
 * `AddInfrastructure` registers `JwtTokenService` behind `ITokenService`,
 * which constructor-injects `IRequestDevice` — a port the API supplies with
 * `RequestDevice`, reading the real HTTP request's User-Agent. The worker
 * never issues a token and never will, but the DI graph does not know that:
 * with nothing registered for the port, a container built in Development
 * (which validates the whole graph eagerly) refused to boot with "Unable to
 * resolve service for type IRequestDevice". Production's lazy validation
 * would not have caught this until the day the worker did try to construct
 * `ITokenService` — a null reference deep inside identity code, from a
 * process nowhere near identity. A worker genuinely has no request to read a
 * device from, so `null` is the honest answer, not a placeholder for one.
 */
builder.Services.AddSingleton<Vni.Ielts.Application.Identity.IRequestDevice, NullRequestDevice>();

// F2.2 — one instance, shared between the loop that writes to it and the
// health endpoint that reads it. Registered before the hosted services so
// both resolve the same singleton.
builder.Services.AddSingleton<WorkerHealthState>();

builder.Services.AddHostedService<MarkingWorker>();

/*
 * <b>Off unless switched on</b> — see the class. A background process that
 * deletes audio is not something to enable by default in an environment nobody
 * has looked at, and the moment somebody enables it is the moment they decide
 * what the retention window is. → `I2.5`
 */
builder.Services.AddHostedService<ReconciliationWorker>();

var app = builder.Build();

// F4.3 — resolved eagerly, because the gauges are registered in the
// constructor: a singleton nobody asks for is never built, and a metric that
// is never built reports nothing while looking perfectly configured.
_ = app.Services.GetRequiredService<QueueBacklogMetrics>();

app.MapWorkerHealthEndpoints();

/*
 * <b>Indexes, then work.</b> The worker's claim is a filtered update over
 * state and due time, and without the index behind it every poll is a
 * collection scan. It shares the initialisation the API runs for the same
 * reason it shares the composition root: two of them would drift.
 */
await app.Services.InitialiseInfrastructureAsync(CancellationToken.None);

await app.RunAsync();

// `--healthcheck` above returns 1 on a failed probe, so this file has to have
// a return value on every path. `app.RunAsync()` blocks until shutdown;
// reaching here means the process was asked to stop, which is a success.
return 0;

/// <summary>Exposed so the integration tests can spin the real worker up.</summary>
public partial class Program;
