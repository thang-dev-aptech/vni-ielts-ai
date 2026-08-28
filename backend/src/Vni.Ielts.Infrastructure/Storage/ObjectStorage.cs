using Amazon.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Vni.Ielts.Application.Dictation;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Storage;

/// <summary>
/// Where media lives outside Development.
///
/// <b>Written 2026-08-28. Before it, the only asset store was a Development-only
/// reader of the fixtures directory</b> — so a production process had no exam
/// audio, no exam images and no dictation audio at all. Listening would have
/// played nothing, and the failure would have looked like a broken player.
///
/// <b>S3-compatible, not S3.</b> One adapter covers MinIO — which is already in
/// the local stack — and AWS S3, and any of the several providers that speak the
/// same protocol. That matters more than usual here: `B-2` (the PDPL
/// cross-border position) may yet require that learner media stays in Vietnam,
/// and a provider swap should be a connection string rather than a rewrite.
/// → ADR-0004's reasoning, applied to storage
///
/// <b>Buckets are laid out by retention class, not by content type.</b> PDPL
/// storage limitation and data-subject deletion both have to reach object
/// storage, and a retention-shaped layout makes deletion a policy on a prefix
/// rather than a scan. That decision is already made in the local compose file;
/// this follows it.
/// </summary>
public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    /// <summary>
    /// The S3 endpoint. Empty means object storage is not configured.
    ///
    /// <b>Empty is a supported state, not a fault.</b> A Development process
    /// reads media from the fixtures directory and never touches this; the
    /// startup gate is what refuses to boot a production process without it.
    /// </summary>
    public string ServiceUrl { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    /// <summary>
    /// From environment configuration only. Never committed — the `.gitignore`
    /// and a PreToolUse hook both block `.env*`, and CI scans for
    /// credential-shaped strings. → CLAUDE.md rule 6
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// <b>True for MinIO, false for AWS.</b> MinIO addresses buckets by path
    /// (`https://host/bucket/key`); AWS prefers the virtual-host style
    /// (`https://bucket.host/key`) and only the first works against a plain
    /// hostname with no wildcard DNS behind it.
    /// </summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>Region name. Ignored by MinIO, required by the AWS SDK.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>Exam packages' media — passages' images, Listening audio.</summary>
    public string ExamAssetsBucket { get; set; } = "vni-exam-assets";

    /// <summary>Dictation audio, which is a separate catalogue with its own lifecycle.</summary>
    public string DictationBucket { get; set; } = "vni-audio-90d";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServiceUrl)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(SecretKey);
}

/// <summary>
/// A shared client, and the two things every read through it has to get right.
///
/// <b>One client for the process.</b> The AWS SDK's client holds a connection
/// pool; constructing one per request is the classic way to exhaust sockets
/// under load, and it is not obvious from the call site that it would.
///
/// <b>A reference is never trusted as a key.</b> An asset reference travels
/// through an authored package, a database and an HTTP route before it reaches
/// here, so it is attacker-influenced by the time it arrives. It is validated
/// against the schema's own shape and refused on anything containing a
/// traversal segment — the same three checks the fixture store makes, for the
/// same reason. → `zip-ingestion-security.md`, threat `T9`
/// </summary>
internal sealed class S3ObjectStore(IAmazonS3 client, ILogger<S3ObjectStore> logger)
{
    /// <summary>
    /// Turns `assets/listening-part-1.m4a` into an object key, or refuses.
    ///
    /// <b>Returns null rather than throwing, and a bad reference and a missing
    /// object produce the same answer.</b> A traversal attempt and a typo both
    /// become a 404, so an attacker learns nothing from the difference.
    /// </summary>
    private static string? KeyFor(string reference)
    {
        if (!reference.StartsWith("assets/", StringComparison.Ordinal)) return null;

        var relative = reference["assets/".Length..];
        if (relative.Length == 0) return null;

        var segments = relative.Split('/', StringSplitOptions.None);

        // `.` and `..` mean nothing to S3 — a key is an opaque string — but a
        // key containing them is one nothing legitimate ever wrote, and
        // accepting it would make this the one place a package's own path
        // conventions could be smuggled through.
        if (segments.Any(s => s is "" or "." or "..")) return null;

        return relative;
    }

    public async Task<(Stream Content, string ContentType, long? Length, string? ETag)?> OpenAsync(
        string bucket, string reference, CancellationToken ct)
    {
        if (KeyFor(reference) is not { } key) return null;

        try
        {
            var response = await client.GetObjectAsync(
                new GetObjectRequest { BucketName = bucket, Key = key }, ct);

            /*
             * <b>The stored content type, never one guessed from the key.</b>
             * A key's extension is content that came in with a package; a
             * browser told the wrong type either refuses to play or sniffs, and
             * sniffing is exactly what a content type exists to prevent.
             */
            var contentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? "application/octet-stream"
                : response.Headers.ContentType;

            return (response.ResponseStream, contentType, response.ContentLength, response.ETag);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonServiceException e)
        {
            /*
             * <b>Logged and reported as absent.</b> A media request that cannot
             * be served is a 404 to the caller either way, and returning null
             * keeps one code path for "no such asset". The log is what tells an
             * operator the difference between a missing object and a bucket
             * that has stopped answering — which the learner must not be able
             * to tell apart, and an operator must.
             */
            logger.LogError(
                e, "Object storage refused {Bucket}/{Reference}.", bucket, reference);

            return null;
        }
    }
}

internal sealed class S3ExamAssetStore(S3ObjectStore store, ObjectStorageOptions options)
    : IExamAssetStore
{
    public async Task<ExamAsset?> OpenAsync(string reference, CancellationToken ct) =>
        await store.OpenAsync(options.ExamAssetsBucket, reference, ct) is { } found
            ? new ExamAsset(found.Content, found.ContentType, found.Length, found.ETag)
            : null;
}

internal sealed class S3DictationAssetStore(S3ObjectStore store, ObjectStorageOptions options)
    : IDictationAssetStore
{
    public async Task<DictationAsset?> OpenAsync(string reference, CancellationToken ct) =>
        await store.OpenAsync(options.DictationBucket, reference, ct) is { } found
            ? new DictationAsset(found.Content, found.ContentType, found.Length, found.ETag)
            : null;
}

internal static class ObjectStorageRegistration
{
    /// <summary>
    /// Registers the S3 client and the two stores, when configured.
    ///
    /// <b>Configured or not is a runtime fact, not a build-time one</b> — the
    /// same binary runs in Development against the fixtures and in production
    /// against a bucket. The startup gate is what refuses to boot a production
    /// process with neither.
    /// </summary>
    public static bool AddObjectStorage(
        this IServiceCollection services, ObjectStorageOptions options)
    {
        if (!options.IsConfigured) return false;

        services.AddSingleton(options);

        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                ForcePathStyle = options.ForcePathStyle,
                AuthenticationRegion = options.Region,
            }));

        services.AddSingleton<S3ObjectStore>();
        services.AddSingleton<IExamAssetStore, S3ExamAssetStore>();
        services.AddSingleton<IDictationAssetStore, S3DictationAssetStore>();

        return true;
    }
}
