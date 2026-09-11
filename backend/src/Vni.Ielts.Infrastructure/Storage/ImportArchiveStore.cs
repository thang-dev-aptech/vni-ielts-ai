using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Infrastructure.Storage;

/// <summary>
/// Where an uploaded exam package waits between the request that carried it
/// and the worker that imports it.
///
/// <b>The whole reason <see cref="IImportArchiveStore"/> is not
/// <c>IPrivateImportAssetStore</c>.</b> That port cannot be read back, and its
/// default implementation keeps nothing. See the port's own remarks.
///
/// <b>Keyed on the upload's SHA-256, which is also the job's identity.</b> Two
/// uploads of identical bytes write the same object, so a retried upload costs
/// one stored archive rather than two — matching what
/// <c>ImportJob.OperationIdFor</c> does in the database, and for the same
/// reason.
/// </summary>
internal static class ImportArchiveKeys
{
    /// <summary>
    /// The prefix archives live under, inside the exam-assets bucket.
    ///
    /// <b>A prefix, not a bucket</b> — owner decision 2026-09-04, ADR-0016:
    /// one bucket per environment, one prefix per class of content. A new
    /// bucket would also need its own startup gate, its own readiness probe
    /// entry and its own lifecycle rule; a prefix inherits all three from a
    /// bucket that is already checked.
    ///
    /// <b>Below <c>imports/</c>, beside the staged media</b>
    /// <c>S3PrivateImportAssetStore</c> writes, so a retention or lifecycle
    /// rule written on <c>imports/</c> reaches everything import-shaped at
    /// once. These are third-party copyrighted archives; the point of
    /// deleting one when its job settles is that nothing accumulates here.
    /// </summary>
    public const string Prefix = "imports/archives/";

    /// <summary>
    /// <c>imports/archives/{sha256}.zip</c>, or a refusal.
    ///
    /// The hash reaches here from a request, so it is validated rather than
    /// trusted: 64 lowercase hex characters and nothing else. That is the same
    /// three-check discipline <c>S3ObjectStore.KeyFor</c> applies to an asset
    /// reference, and it is what makes a traversal segment unrepresentable in
    /// a key rather than merely unlikely.
    /// </summary>
    public static string For(string sourceSha256)
    {
        var hash = (sourceSha256 ?? string.Empty).Trim().ToLowerInvariant();

        if (hash.Length != 64 || !hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new ArgumentException(
                "An import archive is keyed by the upload's SHA-256, as 64 hexadecimal characters.",
                nameof(sourceSha256));
        }

        return $"{Prefix}{hash}.zip";
    }

    /// <summary>
    /// Whether a key handed back to <c>OpenAsync</c> or <c>DeleteAsync</c> is
    /// one this store could have written.
    ///
    /// <b>Checked on the way in, not assumed.</b> The key travels through a
    /// database row and back out through a worker, so by the time it returns
    /// it is attacker-influenced in exactly the way an asset reference is. A
    /// key that does not match is treated as "gone" rather than refused
    /// loudly, so a typo and a probe produce the same answer.
    /// </summary>
    public static bool IsWellFormed(string? archiveKey) =>
        archiveKey is not null
        && archiveKey.Length == Prefix.Length + 68
        && archiveKey.StartsWith(Prefix, StringComparison.Ordinal)
        && archiveKey.EndsWith(".zip", StringComparison.Ordinal)
        && archiveKey[Prefix.Length..^4]
            .All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>
/// The archive in the configured S3-compatible bucket — the same one
/// <c>S3SpeakingRecordingStore</c> and <c>S3ExamAssetStore</c> use, under
/// <see cref="ImportArchiveKeys.Prefix"/>.
/// </summary>
internal sealed class S3ImportArchiveStore(
    IAmazonS3 client, ObjectStorageOptions options, ILogger<S3ImportArchiveStore> logger)
    : IImportArchiveStore
{
    public async Task<string> SaveAsync(string sourceSha256, Stream archive, CancellationToken ct)
    {
        var key = ImportArchiveKeys.For(sourceSha256);

        await client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = options.ExamAssetsBucket,
                Key = ObjectStorageOptions.Under(options.ExamAssetsPrefix, key),
                InputStream = archive,
                ContentType = "application/zip",
                CannedACL = S3CannedACL.Private,
                AutoCloseStream = false,

                // R2 refuses an aws-chunked body outright; the same reason
                // S3PrivateImportAssetStore sets it. → AddObjectStorage
                UseChunkEncoding = false,
            },
            ct);

        return key;
    }

    /// <summary>
    /// <b>Forward-only, and that matters.</b> An S3 response body is a network
    /// stream: the caller must spool it before handing it to
    /// <c>ExamPackageImportPipeline</c>, which reads the ZIP central directory
    /// at the end of the file and then re-reads the whole archive.
    /// </summary>
    public async Task<Stream?> OpenAsync(string archiveKey, CancellationToken ct)
    {
        if (!ImportArchiveKeys.IsWellFormed(archiveKey)) return null;

        try
        {
            var response = await client.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = options.ExamAssetsBucket,
                    Key = ObjectStorageOptions.Under(options.ExamAssetsPrefix, archiveKey),
                },
                ct);

            return response.ResponseStream;
        }
        catch (AmazonS3Exception e) when (e.ErrorCode is "NoSuchKey" or "NotFound")
        {
            // Gone, which the port says is a null rather than a throw: a job
            // whose archive expired has to fail with a sentence, not blow a
            // worker loop up.
            return null;
        }
    }

    public async Task DeleteAsync(string archiveKey, CancellationToken ct)
    {
        if (!ImportArchiveKeys.IsWellFormed(archiveKey)) return;

        try
        {
            await client.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = options.ExamAssetsBucket,
                    Key = ObjectStorageOptions.Under(options.ExamAssetsPrefix, archiveKey),
                },
                ct);
        }
        catch (AmazonServiceException e)
        {
            // Never fatal — a stray object is a hygiene problem, a lost import
            // is not. Logged loudly enough that a bucket filling with
            // third-party copyrighted archives is visible before it is a
            // rights problem.
            logger.LogWarning(
                e, "Could not delete the settled import archive from {Bucket}.",
                options.ExamAssetsBucket);
        }
    }
}

/// <summary>
/// The archive on local disk, for a deployment with no object storage
/// configured — which is every Development process and the test host.
///
/// <b>A real implementation, not a null one.</b> The API and the Worker are
/// two processes on one machine in Development, so a file under a shared
/// directory genuinely satisfies the port's contract: it survives the request,
/// another process reads it back, and deleting it means it is gone. `G-11`
/// forbids inventing a business default; it does not forbid a working adapter
/// for the environment that has no bucket. The alternative — refusing every
/// upload without object storage — would make the import door untestable and
/// unusable on a developer's machine, which is where it is worked on.
///
/// <b>What it is not.</b> It is not suitable for more than one machine. A
/// production deployment configures object storage, and
/// <c>AddObjectStorage</c> then registers <see cref="S3ImportArchiveStore"/>
/// over this one; the startup gate already refuses to boot a production
/// process without object storage.
/// </summary>
internal sealed class LocalFileImportArchiveStore(
    string root, ILogger<LocalFileImportArchiveStore> logger) : IImportArchiveStore
{
    /// <summary>
    /// Where archives go when nothing is configured.
    ///
    /// Under the temp directory rather than the repository: an uploaded
    /// package is somebody else's copyright, and a path inside a git worktree
    /// is a path somebody eventually commits.
    /// </summary>
    public static string DefaultRoot { get; } =
        Path.Combine(Path.GetTempPath(), "vni-import-archives");

    private string PathFor(string archiveKey) =>
        Path.Combine(root, archiveKey[ImportArchiveKeys.Prefix.Length..]);

    public async Task<string> SaveAsync(string sourceSha256, Stream archive, CancellationToken ct)
    {
        var key = ImportArchiveKeys.For(sourceSha256);
        Directory.CreateDirectory(root);

        var path = PathFor(key);

        /*
         * <b>Written to a neighbour and moved into place.</b> A worker reading
         * this directory has no lock to take, so a reader that arrives while a
         * copy is still in flight would otherwise open a half-written archive
         * — a truncated ZIP, which reads as a corrupt package rather than as a
         * race. A move within one directory is atomic on every filesystem this
         * runs on.
         */
        var staging = path + "." + Guid.NewGuid().ToString("n") + ".partial";

        await using (var file = new FileStream(
            staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920,
            FileOptions.Asynchronous))
        {
            await archive.CopyToAsync(file, ct);
        }

        // Overwrite: identical bytes produce an identical key, so a second
        // upload of the same package is the same object either way.
        File.Move(staging, path, overwrite: true);

        return key;
    }

    public Task<Stream?> OpenAsync(string archiveKey, CancellationToken ct)
    {
        if (!ImportArchiveKeys.IsWellFormed(archiveKey)) return Task.FromResult<Stream?>(null);

        var path = PathFor(archiveKey);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);

        // Seekable, unlike the S3 one — but callers must not rely on that, or
        // the code works in Development and truncates in production.
        return Task.FromResult<Stream?>(new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, FileOptions.Asynchronous));
    }

    public Task DeleteAsync(string archiveKey, CancellationToken ct)
    {
        if (!ImportArchiveKeys.IsWellFormed(archiveKey)) return Task.CompletedTask;

        try
        {
            File.Delete(PathFor(archiveKey));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not delete the settled import archive from local storage.");
        }

        return Task.CompletedTask;
    }
}
