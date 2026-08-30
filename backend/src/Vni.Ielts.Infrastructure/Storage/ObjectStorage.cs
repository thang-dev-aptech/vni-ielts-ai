using System.Diagnostics;
using Amazon.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Vni.Ielts.Infrastructure.Configuration;
using Vni.Ielts.Infrastructure.Observability;
using Vni.Ielts.Application.Dictation;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Importing;

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

    /// <summary>
    /// Where a learner's Speaking recording is written. <b>No default, and the
    /// absence is the point.</b>
    ///
    /// <para>
    /// Every other bucket on this type has a default because every other bucket
    /// holds authored content — content VNI made, which VNI may keep. This one
    /// holds a named person's voice, and the buckets in this product are laid
    /// out by <i>retention class</i>: <c>vni-audio-90d</c> says ninety days in
    /// its own name. Defaulting to it would decide, in a property initialiser,
    /// how long a minor's voice recording is kept — a business and legal
    /// decision nobody has made. → <c>G-11</c>, <c>B-2</c>
    /// </para>
    ///
    /// <para>
    /// Unset today, so no Speaking recording can be written. That is the
    /// truthful state until FS5 and the retention decision arrive together.
    /// </para>
    /// </summary>
    public string SpeakingRecordingsBucket { get; set; } = string.Empty;

    /// <summary>
    /// How long a Speaking recording is kept before the lifecycle rule deletes
    /// it. <b>Null, deliberately — this is the retention decision itself.</b>
    ///
    /// <para>
    /// Nothing in this process enforces it: the value exists so that the
    /// bucket's lifecycle rule and this configuration can be checked against
    /// each other, and so that "how long do we keep a recording" has one
    /// answer written down in one place rather than living only in a Cloudflare
    /// console nobody diffs.
    /// </para>
    ///
    /// <para>
    /// PDPL storage limitation makes an unbounded retention a compliance
    /// problem, and the <c>[OPEN QUESTION]</c> about candidates under 18 makes
    /// it a sharper one. A number invented here would look like an answer.
    /// </para>
    /// </summary>
    public int? SpeakingRecordingRetentionDays { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServiceUrl)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>
    /// The host suffix Cloudflare R2 serves its S3-compatible API on.
    ///
    /// <b>FS0.4 — R2 is a configuration profile, not an abstraction.</b> Plan
    /// decision 4: R2 is the production candidate for Speaking recordings, it
    /// speaks the same S3 protocol this adapter already speaks, and no R2 type
    /// may appear in Domain or Application. So the only thing the code knows
    /// about R2 is that one of its documented requirements differs from AWS's
    /// — see <see cref="IsCloudflareR2"/> and the startup gate — and that
    /// knowledge lives in a string constant rather than in a second adapter.
    /// </summary>
    public const string CloudflareR2HostSuffix = ".r2.cloudflarestorage.com";

    /// <summary>
    /// Whether <see cref="ServiceUrl"/> points at Cloudflare R2.
    ///
    /// Used only by the startup gate, to catch the one misconfiguration that
    /// otherwise surfaces as an opaque SDK signature error at the first upload:
    /// R2 requires the region <c>auto</c>, and the AWS SDK's own default is
    /// <c>us-east-1</c>, so the wrong value is the one you get by not thinking
    /// about it. → https://developers.cloudflare.com/r2/api/s3/api/
    /// </summary>
    public bool IsCloudflareR2 =>
        Uri.TryCreate(ServiceUrl, UriKind.Absolute, out var parsed)
        && parsed.Host.EndsWith(CloudflareR2HostSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A description safe to print — see the same override on
    /// <c>AiProviderOptions</c> for why the type owns its own rendering.
    /// </summary>
    public override string ToString() =>
        $"ServiceUrl={SecretRedaction.Url(ServiceUrl)}, "
        + $"AccessKey={SecretRedaction.Identifier(AccessKey)}, "
        + $"SecretKey={SecretRedaction.Describe(SecretKey)}, Region={Region}";
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

        /*
         * <b>F4.1 — object storage is a network dependency and gets a span.</b>
         * Exam audio, Writing charts and Speaking recordings all come through
         * here, so "the Listening audio was slow to start" is answerable only
         * if this call is visible.
         *
         * <b>The bucket, not the key.</b> A key names a specific learner's
         * recording; a bucket names a class of content. Only the second one
         * belongs in telemetry that leaves this machine. → F4.2
         */
        using var span = Telemetry.Source.StartActivity(
            "objectstorage.get", ActivityKind.Client);
        span?.SetTag("vni.bucket", bucket);

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
        catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchKey")
        {
            // <b>Only the object being absent.</b> A missing key and a bad
            // reference are the caller's problem, not the store's, so both
            // become a 404 with nothing to log.
            return null;
        }
        catch (AmazonServiceException e)
        {
            /*
             * <b>Logged and rethrown, not swallowed as "not found".</b> A
             * wrong bucket name, a revoked key or a permissions change all
             * reach here as some other <see cref="AmazonS3Exception.ErrorCode"/>
             * — "NoSuchBucket", "AccessDenied", "InvalidAccessKeyId" — and
             * folding them into the same null this method returns for a
             * missing key used to tell every caller, including the readiness
             * probe, that the store was fine when it was not. The caller now
             * sees a real failure (a 500, not a lying 404) and the log still
             * has the detail an operator needs.
             */
            logger.LogError(
                e, "Object storage refused {Bucket}/{Reference}.", bucket, reference);

            // F4.3 — a degraded store is a paper that cannot be sat, not a
            // background inconvenience. Tagged by error CODE so "the bucket is
            // gone" and "the key is wrong" are not one number; no key and no
            // message, which can carry a signed URL or a credential. → F4.2
            Telemetry.ObjectStorageErrors.Add(
                1,
                new KeyValuePair<string, object?>("vni.bucket", bucket),
                new KeyValuePair<string, object?>(
                    "vni.error",
                    (e as AmazonS3Exception)?.ErrorCode ?? e.GetType().Name));

            throw;
        }
    }
}

/// <summary>
/// Whether the configured buckets are reachable with the configured
/// credentials — the question a readiness probe actually needs answered.
///
/// <b>A separate contract from <see cref="S3ObjectStore.OpenAsync"/>, on
/// purpose.</b> Reusing an object read for readiness means readiness depends
/// on some specific key existing, and a bucket that has never received that
/// key is not the same fact as a bucket that cannot be reached. `HeadBucket`
/// asks the second question directly: it needs no key, and it fails with a
/// distinct, typed exception for "no such bucket" versus "not authorized"
/// versus a network or DNS failure that never reached S3 at all.
/// </summary>
internal sealed class S3ObjectStorageHealthCheck(IAmazonS3 client, ObjectStorageOptions options)
    : IObjectStorageHealthCheck
{
    public async Task CheckAsync(CancellationToken ct)
    {
        await client.HeadBucketAsync(
            new HeadBucketRequest { BucketName = options.ExamAssetsBucket }, ct);
        await client.HeadBucketAsync(
            new HeadBucketRequest { BucketName = options.DictationBucket }, ct);

        if (!string.IsNullOrWhiteSpace(options.SpeakingRecordingsBucket))
        {
            await client.HeadBucketAsync(
                new HeadBucketRequest { BucketName = options.SpeakingRecordingsBucket }, ct);
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

/// <summary>
/// Private staging for media discovered during import. A staged object is not an exam asset and
/// therefore cannot be served by the learner-facing asset reader until review maps it explicitly.
/// </summary>
internal sealed class S3PrivateImportAssetStore(IAmazonS3 client, ObjectStorageOptions options)
    : IPrivateImportAssetStore
{
    public async Task<string> PutPrivateAsync(
        string key, Stream content, string contentType, string sha256, CancellationToken ct)
    {
        if (!key.StartsWith("imports/", StringComparison.Ordinal)
            || key.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new ArgumentException("Import asset key must stay below imports/.", nameof(key));

        var request = new PutObjectRequest
        {
            BucketName = options.ExamAssetsBucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            CannedACL = S3CannedACL.Private,
            AutoCloseStream = false,
        };
        request.Metadata["sha256"] = sha256;
        await client.PutObjectAsync(request, ct);
        return key;
    }
}

/// <summary>
/// The readiness port. Registered only when object storage is configured, the
/// same way <see cref="IExamAssetStore"/> and <see cref="IDictationAssetStore"/>
/// are — a Development process has nothing to probe and nothing to report.
/// </summary>
public interface IObjectStorageHealthCheck
{
    /// <summary>Throws on anything but confirmed access to every configured bucket.</summary>
    Task CheckAsync(CancellationToken ct);
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
                UseHttp = Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out var endpoint)
                    && endpoint.Scheme == Uri.UriSchemeHttp,
            }));

        services.AddSingleton<S3ObjectStore>();
        services.AddSingleton<IExamAssetStore, S3ExamAssetStore>();
        services.AddSingleton<IDictationAssetStore, S3DictationAssetStore>();
        services.AddSingleton<IPrivateImportAssetStore, S3PrivateImportAssetStore>();
        services.AddSingleton<IObjectStorageHealthCheck, S3ObjectStorageHealthCheck>();

        if (!string.IsNullOrWhiteSpace(options.SpeakingRecordingsBucket))
        {
            services.AddSingleton<S3SpeakingRecordingBlobStore>();
            services.AddSingleton<ISpeakingRecordingBlobStore>(sp =>
                sp.GetRequiredService<S3SpeakingRecordingBlobStore>());
            services.AddSingleton(new ObjectStorageSpeakingOptions
            {
                RetentionDays = options.SpeakingRecordingRetentionDays,
            });
        }

        return true;
    }
}
