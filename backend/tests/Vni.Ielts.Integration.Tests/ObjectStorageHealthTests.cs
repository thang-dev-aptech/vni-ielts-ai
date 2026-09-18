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

    /// <summary>
    /// <b>A bucket the probe walks and the environment never created.</b>
    ///
    /// The CMS media library added `vni-media` to the distinct-bucket list
    /// `S3ObjectStorageHealthCheck` walks, and it is the failure mode nobody
    /// sees coming: the process starts perfectly — the startup gate does not
    /// create buckets — and then answers `ServiceUnavailable` for ever. It
    /// cost this repository two red CI runs on 2026-09-18, once because the
    /// local MinIO container predated the compose line that provisions it and
    /// once because the CI job created two of the eight buckets.
    ///
    /// <b>So the probe covering media is asserted rather than assumed.</b>
    /// Without this, the same class passes on any machine that happens to have
    /// the bucket and fails on any that does not, which is how the whole thing
    /// read as flakiness rather than as a missing bucket.
    /// </summary>
    [SkippableFact]
    public async Task Readiness_fails_when_the_media_bucket_does_not_exist()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        await using var app = new ObjectStorageAppFactory
        {
            MediaBucket = $"vni-media-absent-{Guid.NewGuid():n}",
        };

        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("failed", ObjectStorageCheck(body).GetProperty("status").GetString());
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
        var client = app.CreateClient();

        /*
         * <b>The host is built and warmed before the clock starts.</b>
         *
         * `CreateClient` builds the host lazily, so the first request pays for
         * configuration validation, DI construction and JIT — seconds of it on
         * a loaded machine. Measuring that alongside the probe turned this from
         * a test of the probe's deadline into a test of how busy the build
         * agent was, and it went red twice on 2026-09-18 while the probe itself
         * behaved perfectly.
         *
         * `/health/live` touches no dependency, so it costs the startup and
         * nothing else. What the stopwatch then sees is one request.
         */
        (await client.GetAsync("/health/live")).EnsureSuccessStatusCode();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/health/ready");
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

    /// <summary>
    /// <b>Pinned because the readiness probe walks it, and it was not.</b>
    /// The CMS media library added `MediaBucket` to `ObjectStorageOptions`
    /// with a default, and nothing added it here — so this factory stopped
    /// pinning the whole section and started depending on whether a bucket
    /// called `vni-media` happened to exist in whatever MinIO was running.
    /// On a clean stack every test in this class failed; on a developer's
    /// stack it failed intermittently, which is worse.
    /// </summary>
    public string MediaBucket { get; init; } = "vni-media";

    public static bool MinioAvailable => _minioAvailable.Value;

    private static bool MinioRequired =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VNI_REQUIRE_MINIO"));

    /*
     * <b>Probed more than once, for the reason the Mongo probe already is.</b>
     * See `SsoAppFactory._mongoAvailable`, which carries the full account: a
     * `static Lazy<T>` caches the answer for the whole process, so one unlucky
     * moment decides the fate of every test in the assembly.
     *
     * A single attempt was survivable while a missing MinIO merely skipped.
     * `VNI_REQUIRE_MINIO` makes absence fatal, and that changed what a blip
     * costs: a momentary refusal while a neighbouring container takes the
     * port's attention now reads as a failed build rather than as a skip. It
     * fired four times against a healthy `vni-minio` during the FS0 gate work.
     *
     * <b>Retry, not a longer timeout</b> — same reasoning as Mongo. Three
     * seconds is already generous for a loopback connect; the failure mode is
     * refusal, not slowness, and a longer deadline would only make a
     * genuinely-absent MinIO take longer to report. Attempts absorb a blip.
     *
     * <b>Not weaker.</b> A MinIO that is really down still fails all three
     * attempts, so `VNI_REQUIRE_MINIO` still fails the run and a developer
     * without the stack still gets the same skip — just after ~6 seconds of
     * trying rather than after one unlucky moment.
     */
    private const int MinioProbeAttempts = 3;

    private static readonly Lazy<bool> _minioAvailable = new(() =>
    {
        Exception? failure = null;

        for (var attempt = 1; attempt <= MinioProbeAttempts; attempt++)
        {
            try
            {
                using var client = new TcpClient();
                var connected = client
                    .ConnectAsync(IPAddress.Loopback, 9000)
                    .Wait(TimeSpan.FromSeconds(3));
                if (connected && client.Connected) return true;
            }
            catch (Exception e)
            {
                failure = e;
            }

            // A short settle between attempts, long enough for a container
            // claiming the port to finish doing so.
            if (attempt < MinioProbeAttempts) Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        if (MinioRequired)
        {
            throw new InvalidOperationException(
                "VNI_REQUIRE_MINIO is set and no MinIO answered on localhost:9000 after "
                + $"{MinioProbeAttempts} attempts. This is "
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
        builder.UseSetting("ObjectStorage:MediaBucket", MediaBucket);

        /*
         * <b>Every other key of the section, pinned — because Development also
         * loads the developer's `secrets.develop.json`.</b> On 2026-09-04 that
         * file pointed SpeakingRecordingsBucket and three prefixes at a
         * Cloudflare R2 bucket with Region "auto"; this host kept MinIO's URL
         * and credentials from above but inherited the rest, asked MinIO for a
         * bucket that only exists on R2, and readiness failed for a reason that
         * had nothing to do with the test. A test that states half of a
         * section is testing the other half of somebody's laptop.
         */
        builder.UseSetting("ObjectStorage:SpeakingRecordingsBucket", string.Empty);
        builder.UseSetting("ObjectStorage:SpeakingRecordingRetentionDays", string.Empty);
        builder.UseSetting("ObjectStorage:ExamAssetsPrefix", string.Empty);
        builder.UseSetting("ObjectStorage:DictationPrefix", string.Empty);
        builder.UseSetting("ObjectStorage:MediaPrefix", string.Empty);
        builder.UseSetting("ObjectStorage:SpeakingRecordingsPrefix", string.Empty);
        builder.UseSetting("ObjectStorage:Region", "us-east-1");
        builder.UseSetting("ObjectStorage:ForcePathStyle", "true");
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
