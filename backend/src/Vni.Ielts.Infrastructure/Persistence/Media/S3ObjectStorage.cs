using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Media;

namespace Vni.Ielts.Infrastructure.Persistence.Media;

/// <summary>
/// <see cref="IObjectStorage"/> over the AWS S3 SDK, pointed at MinIO locally.
/// The only place <c>Amazon.S3</c> is referenced — Architecture.Tests forbids
/// it in Domain and Application, the same rule that keeps a Mongo driver type
/// out of those layers.
/// </summary>
internal sealed class S3ObjectStorage : IObjectStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;

    public S3ObjectStorage(IOptions<MediaObjectStorageOptions> options)
    {
        var config = options.Value;
        _bucket = config.Bucket;
        _client = new AmazonS3Client(
            new BasicAWSCredentials(config.AccessKey, config.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = config.ServiceUrl,
                // MinIO does not support virtual-hosted-style addressing
                // (`bucket.host/key`) the way real S3 does — path style
                // (`host/bucket/key`) is what actually resolves against it.
                ForcePathStyle = true,
            });
    }

    public async Task<string> PutAsync(Stream content, string contentType, CancellationToken ct)
    {
        // Server-generated, and it carries no client input — same rule as
        // GridFsRecordingStore and GridFsPackageUploadStore: never a
        // client-supplied filename, or the name becomes a way to choose where
        // a file lands.
        var key = Guid.NewGuid().ToString("n");

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        }, ct);

        return key;
    }

    public async Task<Stream> OpenAsync(string key, CancellationToken ct)
    {
        var response = await _client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _bucket,
            Key = key,
        }, ct);

        return response.ResponseStream;
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        await _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucket,
            Key = key,
        }, ct);
    }
}
