using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// F0.1 — the readiness probe answers "is object storage reachable with the
/// configured credentials", not "did some arbitrary key happen to exist".
///
/// <b>Written 2026-08-28, replacing a probe that read an arbitrary object.</b>
/// The old <c>/health/ready</c> asked the exam-asset store to open
/// <c>assets/.readiness-probe</c> and treated a null result as healthy — and
/// <c>S3ObjectStore.OpenAsync</c> caught every <c>AmazonServiceException</c>,
/// not only "no such key", and returned null for all of them. A wrong bucket
/// name, a revoked access key, or MinIO refusing every request all produced
/// exactly the "object not found" answer a legitimate empty bucket would have
/// produced. Readiness reported 200 while nothing could actually be served.
///
/// Each fault below is created for real, against a real local MinIO — not
/// mocked — because the bug was in how a real SDK exception got swallowed,
/// and a mock would only prove the mock's opinion of the SDK.
/// </summary>
public sealed class ObjectStorageHealthTests
{
    [SkippableFact]
    public async Task Readiness_is_ok_when_object_storage_is_reachable_with_valid_credentials()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        await using var app = new ObjectStorageAppFactory();
        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var check = ObjectStorageCheck(body);
        Assert.Equal("ok", check.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Readiness_fails_safely_on_a_wrong_access_key()
    {
        // <b>The regression this guards.</b> MinIO answers a bad signature
        // with 403 AccessDenied — an AmazonServiceException the old probe's
        // reused OpenAsync call caught and turned into "not found", i.e.
        // healthy. Wrong credentials must fail readiness, not pass it.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        await using var app = new ObjectStorageAppFactory { SecretKey = "wrong-secret-key-0000000000" };
        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("wrong-secret-key-0000000000", raw, StringComparison.Ordinal);

        var check = ObjectStorageCheck(JsonDocument.Parse(raw).RootElement);
        Assert.Equal("failed", check.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(check.GetProperty("error").GetString()));
    }

    [SkippableFact]
    public async Task Readiness_fails_safely_on_a_bucket_that_does_not_exist()
    {
        // <b>The other half of the same regression.</b> "NoSuchBucket" is
        // also a 404 AmazonS3Exception, and the pre-fix probe could not tell
        // it apart from "no such key" either.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        await using var app = new ObjectStorageAppFactory
        {
            ExamAssetsBucket = $"vni-bucket-does-not-exist-{Guid.NewGuid():n}",
        };
        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var check = ObjectStorageCheck(body);
        Assert.Equal("failed", check.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Readiness_fails_safely_when_object_storage_is_unreachable()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        // A closed local port refuses the TCP connection immediately — no
        // MinIO needs to be running for this one; it is testing "the service
        // is down", not "the credentials are wrong".
        var closedPort = FindClosedLocalPort();

        await using var app = new ObjectStorageAppFactory { ServiceUrl = $"http://127.0.0.1:{closedPort}" };
        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var check = ObjectStorageCheck(body);
        Assert.Equal("failed", check.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Readiness_fails_safely_on_timeout_without_hanging_past_the_probe_deadline()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        // A listener that accepts the TCP connection and then never answers
        // — the client cannot distinguish this from an overloaded MinIO
        // still "connected" but not responding. Only the probe's own 2-second
        // deadline can end this.
        using var blackHole = new TcpListener(IPAddress.Loopback, 0);
        blackHole.Start();
        var port = ((IPEndPoint)blackHole.LocalEndpoint).Port;

        var acceptLoop = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var socket = await blackHole.AcceptTcpClientAsync();
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }
            }
            catch (Exception)
            {
                // Expected once the test disposes the listener.
            }
        });

        await using var app = new ObjectStorageAppFactory { ServiceUrl = $"http://127.0.0.1:{port}" };

        var stopwatch = Stopwatch.StartNew();
        var response = await app.CreateClient().GetAsync("/health/ready");
        stopwatch.Stop();

        blackHole.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(15),
            $"readiness took {stopwatch.Elapsed}, well past its own 2-second probe deadline");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var check = ObjectStorageCheck(body);
        Assert.Equal("failed", check.GetProperty("status").GetString());
    }

    private static JsonElement ObjectStorageCheck(JsonElement body) =>
        body.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "object-storage");

    private static int FindClosedLocalPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}

/// <summary>
/// The API wired to a throwaway Mongo database and configurable object
/// storage — defaults matching the local compose stack's MinIO, so a
/// developer with <c>docker compose -f infra/docker/compose.yaml up -d</c>
/// running exercises the real thing without touching a config file.
/// </summary>
public sealed class ObjectStorageAppFactory : WebApplicationFactory<Program>
{
    private const string MongoConnectionString = "mongodb://localhost:27018/?directConnection=true";
    private readonly string _database = $"vni_ielts_test_{Guid.NewGuid():n}";

    public string ServiceUrl { get; init; } = "http://localhost:9000";
    public string AccessKey { get; init; } = "vni-local";
    public string SecretKey { get; init; } = "vni-local-dev-only";
    public string ExamAssetsBucket { get; init; } = "vni-exam-assets";
    public string DictationBucket { get; init; } = "vni-audio-90d";

    public static bool MinioAvailable => _minioAvailable.Value;

    private static bool MinioRequired =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VNI_REQUIRE_MINIO"));

    private static readonly Lazy<bool> _minioAvailable = new(() =>
    {
        Exception? failure = null;

        try
        {
            using var client = new TcpClient();
            var connected = client.ConnectAsync(IPAddress.Loopback, 9000).Wait(TimeSpan.FromSeconds(3));
            if (connected && client.Connected) return true;
        }
        catch (Exception e)
        {
            failure = e;
        }

        if (MinioRequired)
        {
            throw new InvalidOperationException(
                "VNI_REQUIRE_MINIO is set and no MinIO answered on localhost:9000. This is "
                + "what proves F0.1's readiness fix — start it with "
                + "`docker compose -f infra/docker/compose.yaml up -d minio minio-init`.",
                failure);
        }

        return false;
    });

    public const string MinioSkipReason =
        "No MinIO on localhost:9000. Start it with "
        + "`docker compose -f infra/docker/compose.yaml up -d minio minio-init`.";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);

        builder.UseSetting("Mongo:ConnectionString", MongoConnectionString);
        builder.UseSetting("Mongo:Database", _database);
        builder.UseSetting("Jwt:SigningKey", new string('k', 48));
        builder.UseSetting("Sso:EnableStubProvider", "true");
        builder.UseSetting("Sso:Google:ClientId", string.Empty);
        builder.UseSetting("Sso:Google:ClientSecret", string.Empty);
        builder.UseSetting("Sso:ClientBaseUrl", "http://localhost:5173");
        builder.UseSetting("Sso:Google:RedirectUri", "http://localhost/api/v1/auth/sso/google/callback");

        builder.UseSetting("ObjectStorage:ServiceUrl", ServiceUrl);
        builder.UseSetting("ObjectStorage:AccessKey", AccessKey);
        builder.UseSetting("ObjectStorage:SecretKey", SecretKey);
        builder.UseSetting("ObjectStorage:ExamAssetsBucket", ExamAssetsBucket);
        builder.UseSetting("ObjectStorage:DictationBucket", DictationBucket);
    }

    public override async ValueTask DisposeAsync()
    {
        if (SsoAppFactory.MongoAvailable)
        {
            await new MongoClient(MongoConnectionString).DropDatabaseAsync(_database);
        }

        await base.DisposeAsync();
    }
}
