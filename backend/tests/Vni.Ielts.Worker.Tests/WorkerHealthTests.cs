using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Vni.Ielts.Worker;

namespace Vni.Ielts.Worker.Tests;

/// <summary>
/// F2.2 — the worker's own health contract: a real HTTP port, readiness that
/// depends on the polling loop actually having run recently rather than only
/// on the process being alive.
/// </summary>
public sealed class WorkerHealthTests
{
    [SkippableFact]
    public async Task Liveness_answers_without_touching_anything_external()
    {
        Skip.IfNot(WorkerAppFactory.MongoAvailable, WorkerAppFactory.SkipReason);

        await using var app = new WorkerAppFactory();
        var response = await app.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("live", body.GetProperty("status").GetString());
    }

    /// <summary>
    /// <b>An empty queue must not read as unhealthy.</b> The real
    /// <c>MarkingWorker</c> runs against a real, genuinely empty database —
    /// nothing seeded, nothing claimed — so the only way this passes is if
    /// readiness depends on the loop having iterated at all, not on it
    /// having found work.
    /// </summary>
    [SkippableFact]
    public async Task Readiness_is_ok_once_the_loop_has_polled_an_empty_queue()
    {
        Skip.IfNot(WorkerAppFactory.MongoAvailable, WorkerAppFactory.SkipReason);

        await using var app = new WorkerAppFactory();
        var client = app.CreateClient();

        // The loop polls every 5 seconds when idle; give it room for a first
        // iteration without hard-coding a sleep the assertion doesn't need —
        // poll readiness itself until it reports the loop has started.
        JsonElement body = default;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync("/health/ready");
            body = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (response.StatusCode == HttpStatusCode.OK) break;
            await Task.Delay(250);
        }

        var checks = body.GetProperty("checks").EnumerateArray().ToArray();
        var mongo = Assert.Single(checks, c => c.GetProperty("name").GetString() == "mongo");

        Assert.Equal("ok", mongo.GetProperty("status").GetString());

        // Both loops report for themselves. A single merged answer would let
        // one healthy loop stand in for the other.
        foreach (var name in new[] { MarkingLoop, ImportLoop })
        {
            var loop = Assert.Single(checks, c => c.GetProperty("name").GetString() == name);
            Assert.Equal("ok", loop.GetProperty("status").GetString());
        }
    }

    [SkippableFact]
    public async Task Readiness_is_not_ready_before_the_loop_has_started()
    {
        Skip.IfNot(WorkerAppFactory.MongoAvailable, WorkerAppFactory.SkipReason);

        await using var app = new HealthOnlyWorkerAppFactory();
        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("starting", LoopCheck(body, MarkingLoop).GetProperty("status").GetString());
        Assert.Equal("starting", LoopCheck(body, ImportLoop).GetProperty("status").GetString());
    }

    /// <summary>
    /// <b>The scenario this whole item exists for.</b> The process is up —
    /// this HTTP request is answering — and the loop is not: it recorded a
    /// poll once, a long time ago, and nothing since. A liveness probe alone
    /// would call this container healthy indefinitely.
    /// </summary>
    [SkippableFact]
    public async Task Readiness_fails_when_the_loop_has_gone_stale()
    {
        Skip.IfNot(WorkerAppFactory.MongoAvailable, WorkerAppFactory.SkipReason);

        // A short threshold, injected the same way IdempotencyMiddleware's
        // Lease is — so this proves the boundary in milliseconds rather than
        // waiting out the real 90-second production value.
        await using var app = new HealthOnlyWorkerAppFactory
        {
            StaleAfter = TimeSpan.FromMilliseconds(50),
        };

        foreach (var health in app.Services.GetRequiredService<IEnumerable<WorkerHealthState>>())
            health.RecordPoll();

        await Task.Delay(TimeSpan.FromMilliseconds(200));

        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("stale", LoopCheck(body, MarkingLoop).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Readiness_fails_when_the_loop_recorded_a_fatal_exception()
    {
        Skip.IfNot(WorkerAppFactory.MongoAvailable, WorkerAppFactory.SkipReason);

        await using var app = new HealthOnlyWorkerAppFactory();
        var health = app.Loop(MarkingLoop);

        health.RecordPoll();
        health.RecordFatal(new InvalidOperationException("the loop exited unexpectedly"));

        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var loop = LoopCheck(body, MarkingLoop);
        Assert.Equal("failed", loop.GetProperty("status").GetString());
        Assert.Equal("InvalidOperationException", loop.GetProperty("error").GetString());
    }

    /// <summary>
    /// F2.3 — 150s, comfortably longer than <c>MarkingWorker.Lease</c>'s 2
    /// minutes, not the generic host's undocumented 30-second default. A
    /// claimed job surviving a deploy depends on this being long enough to
    /// let it finish rather than being torn away mid-heartbeat.
    /// </summary>
    [SkippableFact]
    public async Task The_shutdown_window_gives_a_claimed_job_room_to_finish()
    {
        Skip.IfNot(WorkerAppFactory.MongoAvailable, WorkerAppFactory.SkipReason);

        await using var app = new WorkerAppFactory();
        app.CreateClient(); // builds and starts the host

        var options = app.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<HostOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(150), options.ShutdownTimeout);
    }

    /// <summary>
    /// <b>The masking this split exists to remove.</b> One loop is doing its
    /// job — polling right now — and the other has not polled since well
    /// before the staleness threshold. With a single shared
    /// <c>WorkerHealthState</c>, the healthy loop's poll refreshes the very
    /// field the dead one is judged by, so readiness answers 200 over a queue
    /// of uploaded packages nothing is draining. And a dead import loop is
    /// silent by nature: the API keeps accepting uploads and returning 202,
    /// and the only symptom is drafts that never appear.
    ///
    /// <b>The marking loop is polled last, deliberately.</b> Under a shared
    /// state that ordering is what makes the stale reading disappear; if this
    /// test polled the healthy loop first it would pass either way.
    /// </summary>
    [SkippableFact]
    public async Task A_healthy_marking_loop_does_not_mask_a_dead_import_loop()
    {
        Skip.IfNot(WorkerAppFactory.MongoAvailable, WorkerAppFactory.SkipReason);

        await using var app = new HealthOnlyWorkerAppFactory
        {
            StaleAfter = TimeSpan.FromMilliseconds(50),
        };

        // The import loop's last sign of life, long ago.
        app.Loop(ImportLoop).RecordPoll();
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // The marking loop, alive and well, right now.
        app.Loop(MarkingLoop).RecordPoll();

        var response = await app.CreateClient().GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("stale", LoopCheck(body, ImportLoop).GetProperty("status").GetString());
        Assert.Equal("ok", LoopCheck(body, MarkingLoop).GetProperty("status").GetString());
    }

    /// <summary>
    /// <b>The wiring the masking test cannot see.</b>
    /// <see cref="HealthOnlyWorkerAppFactory"/> replaces both states to get a
    /// short staleness threshold, so it proves the <i>endpoint</i> reports
    /// each loop separately and says nothing about what the real
    /// <c>Program.cs</c> handed the two workers. This asks the real host: two
    /// distinct instances, one per loop. Handing both workers one object is
    /// the whole failure — whichever loop is still polling refreshes the
    /// field the other is judged by — and it is a single character's
    /// difference in the composition root.
    /// </summary>
    [SkippableFact]
    public void Each_worker_loop_is_wired_to_its_own_health_state()
    {
        Skip.IfNot(WorkerAppFactory.MongoAvailable, WorkerAppFactory.SkipReason);

        using var app = new WorkerAppFactory();
        app.CreateClient(); // builds and starts the host

        var loops = app.Services.GetRequiredService<IEnumerable<WorkerHealthState>>().ToArray();

        var marking = Assert.Single(loops, state => state.Loop == MarkingLoop);
        var importing = Assert.Single(loops, state => state.Loop == ImportLoop);

        Assert.NotSame(marking, importing);
    }

    internal const string MarkingLoop = "marking-loop";
    internal const string ImportLoop = "import-loop";

    private static JsonElement LoopCheck(JsonElement body, string loop) =>
        body.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == loop);
}

/// <summary>
/// The real worker — <c>MarkingWorker</c> and <c>ReconciliationWorker</c>
/// both running, against a throwaway database.
/// </summary>
public sealed class WorkerAppFactory : WebApplicationFactory<Program>
{
    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";
    private readonly string _database = $"vni_ielts_worker_test_{Guid.NewGuid():n}";

    public static bool MongoAvailable => SsoAvailabilityProbe.Value;

    public const string SkipReason =
        "No MongoDB replica set on localhost:27018. Start it with "
        + "`docker compose -f infra/docker/compose.yaml up -d`.";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
        builder.UseSetting("Mongo:ConnectionString", ConnectionString);
        builder.UseSetting("Mongo:Database", _database);
        builder.UseSetting("Recordings:SweepEnabled", "false");
    }

    public override async ValueTask DisposeAsync()
    {
        if (MongoAvailable)
        {
            await new MongoClient(ConnectionString).DropDatabaseAsync(_database);
        }

        await base.DisposeAsync();
    }
}

/// <summary>
/// The health surface alone — both hosted services removed, so a test can
/// drive <see cref="WorkerHealthState"/> directly instead of racing a real
/// loop for a branch that only a real failure would otherwise reach.
/// </summary>
public sealed class HealthOnlyWorkerAppFactory : WebApplicationFactory<Program>
{
    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";
    private readonly string _database = $"vni_ielts_worker_health_test_{Guid.NewGuid():n}";

    public TimeSpan? StaleAfter { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
        builder.UseSetting("Mongo:ConnectionString", ConnectionString);
        builder.UseSetting("Mongo:Database", _database);
        builder.UseSetting("Recordings:SweepEnabled", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            if (StaleAfter is { } threshold)
            {
                // Both loops, not one: the endpoint reports every registered
                // state, and replacing only one would leave a 90-second
                // threshold on the other and a test that waits for it.
                services.RemoveAll<WorkerHealthState>();
                services.AddSingleton(new WorkerHealthState
                {
                    Loop = WorkerHealthTests.MarkingLoop,
                    StaleAfter = threshold,
                });
                services.AddSingleton(new WorkerHealthState
                {
                    Loop = WorkerHealthTests.ImportLoop,
                    StaleAfter = threshold,
                });
            }
        });
    }

    /// <summary>
    /// The state for one named loop. <b>Named rather than resolved as a single
    /// service</b>, because two singletons of one type mean
    /// <c>GetRequiredService</c> silently hands back whichever was registered
    /// last — which is exactly how a test ends up driving the loop it is not
    /// asserting about.
    /// </summary>
    public WorkerHealthState Loop(string loop) =>
        Services.GetRequiredService<IEnumerable<WorkerHealthState>>()
            .Single(state => state.Loop == loop);

    public override async ValueTask DisposeAsync()
    {
        if (WorkerAppFactory.MongoAvailable)
        {
            await new MongoClient(ConnectionString).DropDatabaseAsync(_database);
        }

        await base.DisposeAsync();
    }
}

internal static class SsoAvailabilityProbe
{
    public static readonly bool Value = Probe();

    private static bool Probe()
    {
        try
        {
            var client = new MongoClient(
                new MongoClientSettings
                {
                    Server = new MongoServerAddress("localhost", 27018),
                    DirectConnection = true,
                    ServerSelectionTimeout = TimeSpan.FromSeconds(3),
                    ConnectTimeout = TimeSpan.FromSeconds(3),
                });

            client.ListDatabaseNames().MoveNext();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
