using Amazon.S3;
using Amazon.S3.Model;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Infrastructure.Storage;

/// <summary>
/// Private Speaking recordings in the configured S3-compatible bucket.
///
/// Presigned PUT/HEAD for the init/complete contract; direct Put for the legacy
/// multipart path when object storage is configured.
/// </summary>
internal sealed class S3SpeakingRecordingBlobStore(IAmazonS3 client, ObjectStorageOptions options)
    : ISpeakingRecordingBlobStore
{
    public bool IsConfigured =>
        options.IsConfigured
        && !string.IsNullOrWhiteSpace(options.SpeakingRecordingsBucket);

    public Uri CreatePresignedPutUrl(
        string objectKey, string contentType, string checksumSha256, TimeSpan ttl)
    {
        EnsureKey(objectKey);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = options.SpeakingRecordingsBucket,
            Key = ObjectStorageOptions.Under(options.SpeakingRecordingsPrefix, objectKey),
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(ttl),
            ContentType = contentType,
        };
        request.Headers["x-amz-meta-sha256"] = checksumSha256;

        var url = client.GetPreSignedURL(request);
        if (options.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps)
        {
            url = new UriBuilder(parsed) { Scheme = Uri.UriSchemeHttp, Port = parsed.Port == 443 ? 9000 : parsed.Port }
                .Uri.ToString();
        }

        return new Uri(url);
    }

    public async Task<SpeakingRecordingObjectHead?> HeadAsync(string objectKey, CancellationToken ct)
    {
        EnsureKey(objectKey);

        try
        {
            var response = await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = options.SpeakingRecordingsBucket,
                    Key = ObjectStorageOptions.Under(options.SpeakingRecordingsPrefix, objectKey),
                }, ct);

            var checksum = response.Metadata.Keys.Contains("x-amz-meta-sha256")
                ? response.Metadata["x-amz-meta-sha256"]
                : response.Metadata.Keys.Contains("sha256")
                    ? response.Metadata["sha256"]
                    : null;

            var contentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? "application/octet-stream"
                : response.Headers.ContentType;

            return new SpeakingRecordingObjectHead(response.ContentLength, contentType, checksum);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task PutAsync(
        string objectKey, Stream content, string contentType, string checksumSha256,
        CancellationToken ct)
    {
        EnsureKey(objectKey);

        var request = new PutObjectRequest
        {
            BucketName = options.SpeakingRecordingsBucket,
            Key = ObjectStorageOptions.Under(options.SpeakingRecordingsPrefix, objectKey),
            InputStream = content,
            ContentType = contentType,
            CannedACL = S3CannedACL.Private,
            AutoCloseStream = false,
            UseChunkEncoding = false, // R2: no aws-chunked bodies — see AddObjectStorage
        };
        request.Metadata["sha256"] = checksumSha256;
        await client.PutObjectAsync(request, ct);
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        EnsureKey(objectKey);

        try
        {
            await client.DeleteObjectAsync(
                options.SpeakingRecordingsBucket,
                ObjectStorageOptions.Under(options.SpeakingRecordingsPrefix, objectKey), ct);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Idempotent delete.
        }
    }

    private static void EnsureKey(string objectKey)
    {
        if (!SpeakingRecordingKey.IsValidObjectKey(objectKey))
            throw new ArgumentException("Object key must stay under recordings/.", nameof(objectKey));
    }
}

/// <summary>
/// <see cref="IRecordingStore"/> backed by object storage and Mongo metadata.
/// </summary>
internal sealed class S3SpeakingRecordingStore(
    S3SpeakingRecordingBlobStore blobs,
    ISpeakingRecordingMetadataStore metadata) : IRecordingStore
{
    public async Task<string> SaveAsync(
        ExamSessionId sessionId, string questionId, Stream content, string contentType,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var checksum = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(bytes));

        var objectKey = SpeakingRecordingKey.For(sessionId, questionId);
        await using var upload = new MemoryStream(bytes, writable: false);
        await blobs.PutAsync(objectKey, upload, contentType, checksum, ct);

        return SpeakingRecordingKey.RecordingIdFor(sessionId, questionId);
    }

    public async Task DeleteAsync(string recordingId, CancellationToken ct)
    {
        var objectKey = SpeakingRecordingKey.Prefix + recordingId;
        if (!SpeakingRecordingKey.IsValidObjectKey(objectKey)) return;

        await blobs.DeleteAsync(objectKey, ct);
    }

    public async Task DeleteForSessionAsync(ExamSessionId sessionId, CancellationToken ct)
    {
        var rows = await metadata.ListBySessionAsync(sessionId, ct);
        foreach (var row in rows.Select(r => r.RecordingId).Distinct())
            await DeleteAsync(row, ct);
    }

    public async Task<IReadOnlyList<StoredRecording>> ListOlderThanAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct)
    {
        var rows = await metadata.ListOlderThanAsync(olderThan, limit, ct);

        return rows
            .Where(r => r.Status == SpeakingRecordingStatus.Linked)
            .Select(r => new StoredRecording(
                r.RecordingId, r.SessionId, r.QuestionId, r.CreatedAt))
            .ToArray();
    }
}
