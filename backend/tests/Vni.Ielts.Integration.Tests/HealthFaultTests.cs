using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// F2.1 — the readiness contract's Mongo half, proven against a real outage
/// rather than only against the generic probe wrapper it shares with
/// <c>ObjectStorageHealthTests</c>.
///
/// <b>Why a dedicated, throwaway replica set rather than stopping the shared
/// dev Mongo.</b> Every other integration suite in this project runs
/// concurrently against `localhost:27018`; taking it down mid-suite would
/// fail unrelated tests for a reason that has nothing to do with them. This
/// spins up a second, disposable single-node replica set on its own port —
/// the same recipe <c>.github/workflows/e2e.yml</c> already uses to give the
/// browser suite an isolated Mongo — points a dedicated app factory at it,
/// and stops <i>that</i> container.
///
/// <b>Why this could not simply reuse the "point at a closed port" trick from
/// the object-storage fault tests.</b> The API asserts the replica set and
/// creates indexes against Mongo *at startup*
/// (<c>InitialiseInfrastructureAsync</c>), before the process ever starts
/// listening — so a Mongo that was never reachable fails the boot, not the
/// readiness probe. What this proves is the other failure mode: a Mongo that
/// was reachable when the process started and later goes away.
/// </summary>
public sealed class HealthFaultTests
{
    public static bool DockerAvailable => _dockerAvailable.Value;

    public const string SkipReason = "Docker is not available on PATH.";

    private static readonly Lazy<bool> _dockerAvailable = new(() =>
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo(
                "docker", "version --format {{.Server.Version}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            probe?.WaitForExit(5000);
            return probe is { ExitCode: 0 };
        }
        catch (Exception)
        {
            return false;
        }
    });

    [SkippableFact]
    public async Task Readiness_fails_safely_when_mongo_becomes_unreachable_after_boot()
    {
        Skip.IfNot(DockerAvailable, SkipReason);

        var port = FindFreeTcpPort();
        var container = $"vni-fault-mongo-{Guid.NewGuid():n}";

        Docker($"run -d --name {container} -p {port}:27017 mongo:7 "
            + "--replSet rs0 --bind_ip_all --port 27017");

        try
        {
            await WaitForMongoPingAsync(container, timeoutSeconds: 60);

            Docker($"exec {container} mongosh --quiet --eval "
                + "\"rs.initiate({_id:'rs0',members:[{_id:0,host:'localhost:27017'}]})\"");

            await WaitForPrimaryAsync(container, timeoutSeconds: 60);

            await using var app = new FaultInjectedMongoAppFactory(port);

            // 1 · Boots and reports ready against a Mongo that is genuinely up.
            var healthy = await app.CreateClient().GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);

            var healthyBody = await healthy.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                "ok",
                MongoCheck(healthyBody).GetProperty("status").GetString());

            // 2 · The dedicated container goes away — not the shared dev one.
            Docker($"stop -t 1 {container}");

            var afterOutage = await app.CreateClient().GetAsync("/health/ready");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, afterOutage.StatusCode);

            var outageBody = await afterOutage.Content.ReadFromJsonAsync<JsonElement>();
            var mongo = MongoCheck(outageBody);

            Assert.Equal("failed", mongo.GetProperty("status").GetString());
            // The type, never a message — same contract as the object-storage
            // checks, and the same reason: this endpoint is anonymous.
            Assert.False(string.IsNullOrWhiteSpace(mongo.GetProperty("error").GetString()));

            /*
             * Bounded by the probe's own deadline, not by however long the
             * driver would otherwise spend rediscovering a dead topology.
             *
             * <b>The bound is set against the failure it exists to catch, not
             * against the deadline itself.</b> `MongoClientSettings` defaults
             * — measured, not recalled — are `ServerSelectionTimeout = 30s`
             * and `ConnectTimeout = 30s`, so a probe with no deadline answers
             * in about thirty seconds and an orchestrator gives up on
             * /health/ready instead of reading the 503 it returns. The
             * deadline is two seconds. Anything between those two numbers
             * separates "the deadline governed" from "the driver did".
             *
             * It was `< 3000`, and that was measuring the machine rather than
             * the code: `CancelAfter` is cooperative, so the reported figure
             * includes timer latency, the driver noticing the token, and the
             * continuation being scheduled — and with only 1000ms of headroom
             * it failed once inside a full sequential solution run while
             * passing 3/3 in isolation. Ten seconds keeps a 5x margin over
             * the deadline under load and still fails loudly if the deadline
             * is ever removed, which is the regression that matters.
             */
            Assert.True(
                mongo.GetProperty("ms").GetInt32() < 10_000,
                "the mongo check ran long enough that the driver's own 30-second "
                    + "server-selection timeout, not the probe's 2-second deadline, "
                    + "looks to be what stopped it");
        }
        finally
        {
            Docker($"rm -f {container}");
        }
    }

    private static JsonElement MongoCheck(JsonElement body) =>
        body.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "mongo");

    private static async Task WaitForMongoPingAsync(string container, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var (exitCode, _) = Docker(
                $"exec {container} mongosh --quiet --eval \"db.adminCommand({{ping:1}}).ok\"",
                throwOnFailure: false);
            if (exitCode == 0) return;
            await Task.Delay(1000);
        }

        throw new TimeoutException($"Mongo container {container} never answered a ping.");
    }

    private static async Task WaitForPrimaryAsync(string container, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var (exitCode, output) = Docker(
                $"exec {container} mongosh --quiet --eval \"db.hello().isWritablePrimary\"",
                throwOnFailure: false);
            if (exitCode == 0 && output.Contains("true", StringComparison.Ordinal)) return;
            await Task.Delay(1000);
        }

        throw new TimeoutException($"Mongo container {container} never elected a PRIMARY.");
    }

    private static int FindFreeTcpPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static (int ExitCode, string Output) Docker(string arguments, bool throwOnFailure = true)
    {
        using var process = Process.Start(new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);

        if (throwOnFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"docker {arguments} failed ({process.ExitCode}): {stderr}");
        }

        return (process.ExitCode, stdout + stderr);
    }
}

/// <summary>
/// Development, no object storage, Mongo pointed at whatever port the caller
/// gives it — built for <see cref="HealthFaultTests"/> alone, so its own
/// disposal never touches the shared dev database other suites use.
/// </summary>
internal sealed class FaultInjectedMongoAppFactory(int mongoPort) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);

        builder.UseSetting(
            "Mongo:ConnectionString", $"mongodb://localhost:{mongoPort}/?directConnection=true");
        builder.UseSetting("Mongo:Database", $"vni_ielts_fault_test_{Guid.NewGuid():n}");
        builder.UseSetting("Jwt:SigningKey", new string('k', 48));
        builder.UseSetting("Sso:EnableStubProvider", "true");
        builder.UseSetting("Sso:Google:ClientId", string.Empty);
        builder.UseSetting("Sso:Google:ClientSecret", string.Empty);
        builder.UseSetting("Sso:ClientBaseUrl", "http://localhost:5173");
        builder.UseSetting("Sso:Google:RedirectUri", "http://localhost/api/v1/auth/sso/google/callback");
    }
}
