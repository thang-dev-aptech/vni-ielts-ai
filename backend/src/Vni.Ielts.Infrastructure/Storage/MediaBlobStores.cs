using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Infrastructure.Observability;

namespace Vni.Ielts.Infrastructure.Storage;

/// <summary>
/// The library's bytes against a real bucket.
///
/// <para>
/// <b>Direct against the S3 client, not through <see cref="S3ObjectStore"/>.</b>
/// That reader validates references shaped <c>assets/…</c> — the package
/// schema's contract. Media keys are <c>media/{id}</c>, a different class with
/// a different validator, and <see cref="MediaObjectKey"/> is it. The rules
/// that <i>do</i> carry over are copied, not skipped: a private ACL, the
/// checksum as object metadata, no chunked bodies for R2, a span tagged with
/// the bucket (never the key — see F4.2), and "absent object" returning null
/// rather than throwing.
/// </para>
/// </summary>
internal sealed class S3MediaBlobStore(IAmazonS3 client, ObjectStorageOptions options)
    : IMediaBlobStore
{
    public bool IsConfigured => true; // registered only when a bucket is configured

    public async Task PutAsync(
        string objectKey, Stream content, string contentType, string checksumSha256, CancellationToken ct)
    {
        EnsureKey(objectKey);
        var request = new PutObjectRequest
        {
            BucketName = options.MediaBucket,
            Key = ObjectStorageOptions.Under(options.MediaPrefix, objectKey),
            InputStream = content,
            ContentType = contentType,
            CannedACL = S3CannedACL.Private,
            AutoCloseStream = false,
            UseChunkEncoding = false, // R2: no aws-chunked bodies — see AddObjectStorage
        };
        request.Metadata["sha256"] = checksumSha256;
        await client.PutObjectAsync(request, ct);
    }

    public async Task<MediaContent?> OpenAsync(string objectKey, CancellationToken ct)
    {
        EnsureKey(objectKey);

        using var span = Telemetry.Source.StartActivity("objectstorage.get", ActivityKind.Client);
        span?.SetTag("vni.bucket", options.MediaBucket);

        try
        {
            var response = await client.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = options.MediaBucket,
                    Key = ObjectStorageOptions.Under(options.MediaPrefix, objectKey),
                }, ct);

            var contentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? "application/octet-stream"
                : response.Headers.ContentType;

            return new MediaContent(response.ResponseStream, contentType, response.ContentLength, response.ETag);
        }
        catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchKey")
        {
            return null;
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        EnsureKey(objectKey);
        try
        {
            await client.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = options.MediaBucket,
                    Key = ObjectStorageOptions.Under(options.MediaPrefix, objectKey),
                }, ct);
        }
        catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchKey")
        {
            // Idempotent delete — same rule the Speaking store keeps.
        }
    }

    private static void EnsureKey(string objectKey)
    {
        if (!MediaObjectKey.IsValidObjectKey(objectKey))
            throw new ArgumentException("Media object keys must stay below media/.", nameof(objectKey));
    }
}

/// <summary>
/// The Development answer: the same port, against a folder on disk.
///
/// <para>
/// <b>Why it exists.</b> The local Docker stack runs MinIO, but the
/// Development process deliberately reads exam and dictation assets from the
/// fixtures directory — pointing the whole process at MinIO would move every
/// seeded asset into an empty bucket and break practice out of the box. Media
/// gets a smaller answer: a folder next to the API's working directory, so
/// <i>upload, list, play, retire, delete</i> all work on a fresh clone with no
/// credentials, while production uses the S3 store through the same port.
/// </para>
///
/// <para>
/// The content type travels in a sidecar file next to the blob — a directory
/// listing is not a database, and serving bytes under the wrong type is the
/// exact mistake the sniff exists to prevent. The ETag is the checksum the
/// upload already computed, so a browser that cached the file revalidates for
/// free.
/// </para>
/// </summary>
internal sealed class LocalMediaBlobStore(string root) : IMediaBlobStore
{
    private static readonly JsonSerializerOptions SidecarOptions =
        new(JsonSerializerDefaults.General);

    public bool IsConfigured => true;

    public async Task PutAsync(
        string objectKey, Stream content, string contentType, string checksumSha256, CancellationToken ct)
    {
        var (blobPath, metaPath) = PathsFor(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(blobPath)!); // root + media/
        await using (var file = File.Create(blobPath))
        {
            await content.CopyToAsync(file, ct);
        }

        await File.WriteAllTextAsync(
            metaPath,
            JsonSerializer.Serialize(new Sidecar(contentType, checksumSha256), SidecarOptions),
            ct);
    }

    public async Task<MediaContent?> OpenAsync(string objectKey, CancellationToken ct)
    {
        var (blobPath, metaPath) = PathsFor(objectKey);
        if (!File.Exists(blobPath)) return null;

        var sidecar = await File.ReadAllTextAsync(metaPath, ct);
        var parsed = JsonSerializer.Deserialize<Sidecar>(sidecar, SidecarOptions)
            ?? new Sidecar("application/octet-stream", null);

        var stream = File.Open(blobPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new MediaContent(
            stream,
            parsed.ContentType,
            stream.Length,
            parsed.Checksum is { } checksum ? $"\"{checksum}\"" : null);
    }

    public Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        var (blobPath, metaPath) = PathsFor(objectKey);
        if (File.Exists(blobPath)) File.Delete(blobPath);
        if (File.Exists(metaPath)) File.Delete(metaPath);
        return Task.CompletedTask;
    }

    private (string Blob, string Meta) PathsFor(string objectKey)
    {
        if (!MediaObjectKey.IsValidObjectKey(objectKey))
            throw new ArgumentException("Media object keys must stay below media/.", nameof(objectKey));

        var blobPath = Path.GetFullPath(Path.Combine(root, objectKey));
        var rootPath = Path.GetFullPath(root);
        if (!blobPath.StartsWith(rootPath, StringComparison.Ordinal))
            throw new ArgumentException("Media object keys must stay under the local root.", nameof(objectKey));

        return (blobPath, blobPath + ".meta.json");
    }

    private sealed record Sidecar(
        [property: JsonPropertyName("contentType")] string ContentType,
        [property: JsonPropertyName("checksum")] string? Checksum);
}

/// <summary>
/// Registered when no bucket and no local root are available. Every operation
/// refuses with the same typed exception the Speaking store's null
/// implementation throws — the endpoint turns it into a stable problem code
/// instead of a 500.
/// </summary>
internal sealed class UnconfiguredMediaBlobStore : IMediaBlobStore
{
    public bool IsConfigured => false;

    public Task PutAsync(
        string objectKey, Stream content, string contentType, string checksumSha256, CancellationToken ct) =>
        throw new MediaUploadUnavailableException();

    public Task<MediaContent?> OpenAsync(string objectKey, CancellationToken ct) =>
        throw new MediaUploadUnavailableException();

    public Task DeleteAsync(string objectKey, CancellationToken ct) =>
        throw new MediaUploadUnavailableException();
}
