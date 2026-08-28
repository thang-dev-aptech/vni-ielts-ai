using System.Net;
using System.Net.Sockets;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Logging.Abstractions;
using Vni.Ielts.Infrastructure.Storage;

namespace Vni.Ielts.Infrastructure.Tests.Storage;

/// <summary>
/// F0.1 — <c>S3ObjectStore.OpenAsync</c> must return null for exactly one
/// reason: the object does not exist. Everything else — a bad bucket name, a
/// revoked key — has to reach the caller as a real, typed failure.
///
/// <b>Written 2026-08-28.</b> Before the fix, the catch clause matched any
/// <c>AmazonServiceException</c> and returned null for all of them, so a
/// missing bucket and a missing key produced the identical answer. That is
/// what let the pre-fix readiness probe (reusing this method) report healthy
/// while the configured bucket did not exist at all — proven directly here,
/// one layer below the HTTP endpoint, against a real local MinIO.
/// </summary>
public sealed class S3ObjectStoreTests
{
    private const string RealBucket = "vni-exam-assets";

    public static bool MinioAvailable => ObjectStorageProbe.MinioAvailable;
    public const string SkipReason = ObjectStorageProbe.SkipReason;

    [SkippableFact]
    public async Task A_missing_key_in_a_real_bucket_returns_null()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        var store = NewStore();

        var result = await store.OpenAsync(
            RealBucket, $"assets/does-not-exist-{Guid.NewGuid():n}.m4a", CancellationToken.None);

        Assert.Null(result);
    }

    [SkippableFact]
    public async Task A_bucket_that_does_not_exist_throws_instead_of_returning_null()
    {
        // <b>The exact regression.</b> Pre-fix, this returned null — the same
        // answer as a missing key — because AmazonServiceException was caught
        // unconditionally. A caller (including the old readiness probe) could
        // not tell "nothing there" from "the bucket is gone".
        Skip.IfNot(MinioAvailable, SkipReason);

        var store = NewStore();

        await Assert.ThrowsAsync<AmazonS3Exception>(() => store.OpenAsync(
            $"vni-bucket-does-not-exist-{Guid.NewGuid():n}", "assets/whatever.m4a", CancellationToken.None));
    }

    [SkippableFact]
    public async Task Wrong_credentials_throw_instead_of_returning_null()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        var store = NewStore(secretKey: "wrong-secret-key-0000000000");

        await Assert.ThrowsAsync<AmazonS3Exception>(() => store.OpenAsync(
            RealBucket, "assets/whatever.m4a", CancellationToken.None));
    }

    private static S3ObjectStore NewStore(string secretKey = "vni-local-dev-only")
    {
        var client = new AmazonS3Client(
            new BasicAWSCredentials("vni-local", secretKey),
            new AmazonS3Config
            {
                ServiceURL = "http://localhost:9000",
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
            });

        return new S3ObjectStore(client, NullLogger<S3ObjectStore>.Instance);
    }
}

/// <summary>Shared MinIO reachability probe, so every suite in this project asks once.</summary>
internal static class ObjectStorageProbe
{
    public static bool MinioAvailable => _available.Value;

    public const string SkipReason =
        "No MinIO on localhost:9000. Start it with "
        + "`docker compose -f infra/docker/compose.yaml up -d minio minio-init`.";

    private static readonly Lazy<bool> _available = new(() =>
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(IPAddress.Loopback, 9000).Wait(TimeSpan.FromSeconds(3))
                && client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    });
}
