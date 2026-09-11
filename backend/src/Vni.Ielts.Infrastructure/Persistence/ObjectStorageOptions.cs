namespace Vni.Ielts.Infrastructure.Persistence;

/// <summary>
/// MinIO locally; vendor and hosting for production are still open (`H-11`,
/// waiting on `B-11` — data residency). Same shape as <see cref="MongoOptions"/>.
/// </summary>
public sealed class MediaObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public string ServiceUrl { get; set; } = "http://localhost:9000";
    public string AccessKey { get; set; } = "vni-local";
    public string SecretKey { get; set; } = "vni-local-dev-only";

    /// <summary>Already provisioned by <c>infra/docker/compose.yaml</c>'s <c>minio-init</c> service.</summary>
    public string Bucket { get; set; } = "vni-exam-assets";
}
