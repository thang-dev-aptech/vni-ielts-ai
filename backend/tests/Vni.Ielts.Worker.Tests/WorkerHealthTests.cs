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
        var loop = Assert.Single(checks, c => c.GetProperty("name").GetString() == "loop");
        var mongo = Assert.Single(checks, c => c.GetProperty("name").GetString() == "mongo");

        Assert.Equal("ok", loop.GetProperty("status").GetString());
        Assert.Equal("ok", mongo.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Readiness_is_not_ready_before_the_loop_has_started()
    {
        Skip.IfNot(WorkerAppFactory.MongoAvailable, WorkerAppFactory.SkipReason);

        await using var app = new HealthOnlyWorkerAppFactory();
        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var loop = LoopCheck(body);
        Assert.Equal("starting", loop.GetProperty("status").GetString());
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

        var health = app.Services.GetRequiredService<WorkerHealthState>();
        health.RecordPoll();

        await Task.Delay(TimeSpan.FromMilliseconds(200));

        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("stale", LoopCheck(body).GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Readiness_fails_when_the_loop_recorded_a_fatal_exception()
    {
        Skip.IfNot(WorkerAppFactory.MongoAvailable, WorkerAppFactory.SkipReason);

        await using var app = new HealthOnlyWorkerAppFactory();
        var health = app.Services.GetRequiredService<WorkerHealthState>();

        health.RecordPoll();
        health.RecordFatal(new InvalidOperationException("the loop exited unexpectedly"));

        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var loop = LoopCheck(body);
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

    private static JsonElement LoopCheck(JsonElement body) =>
        body.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "loop");
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
                services.RemoveAll<WorkerHealthState>();
                services.AddSingleton(new WorkerHealthState { StaleAfter = threshold });
            }
        });
    }

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
