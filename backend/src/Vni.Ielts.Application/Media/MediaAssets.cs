namespace Vni.Ielts.Application.Media;

/// <summary>
/// One file in the CMS media library — the audio and images that dictation
/// sets and exam parts point at.
///
/// <para>
/// <b>The shape is the CMS's own contract.</b> <c>apps/admin/src/lib/media.ts</c>
/// has carried this exact record since the screen was built against a browser
/// store, waiting for a server to cut over to; it is reproduced here field for
/// field so the cutover is a URL swap and not a redesign. <c>FileName</c> is
/// what the operator typed — display only, never a storage key. <c>DurationMs</c>
/// stays null unless somebody measured it: the server does not decode audio,
/// and a duration it did not measure would be an invented one.
/// </para>
/// </summary>
public sealed record MediaAsset(
    string MediaId,
    MediaKind Kind,
    string FileName,
    string ContentType,
    long Bytes,
    long? DurationMs,
    string ChecksumSha256,
    string UploadedByName,
    DateTimeOffset UploadedAt,
    bool Retired);

public enum MediaKind
{
    Audio,
    Image,
    File,
}

/// <summary>
/// The bytes behind a <see cref="MediaAsset"/>, opened for streaming.
///
/// Mirrors <see cref="Vni.Ielts.Application.Exams.ExamAsset"/> on purpose: the
/// admin content endpoint serves with the same range-processing and ETag
/// behaviour the learner asset reader already has, so scrubbing a 40-minute
/// listening track does not re-download it from zero.
/// </summary>
public sealed record MediaContent(Stream Content, string ContentType, long? Length, string? ETag);

/// <summary>
/// Object-key rules for media blobs. Every key is <c>media/{mediaId}</c> —
/// content-addressed by the asset's id, so re-uploading the same file twice
/// makes two assets with two keys and nothing collides.
/// </summary>
public static class MediaObjectKey
{
    public const string Prefix = "media/";

    public static string For(string mediaId) => $"{Prefix}{mediaId}";

    /// <summary>
    /// True when <paramref name="objectKey"/> is a key this store may touch.
    /// The check is the same shape the import asset store applies: the right
    /// prefix, and no segment that walks out of it.
    /// </summary>
    public static bool IsValidObjectKey(string objectKey)
    {
        if (!objectKey.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        return objectKey[Prefix.Length..]
            .Split('/')
            .All(segment => segment.Length > 0 && segment != "." && segment != "..");
    }
}

/// <summary>Metadata for the media library. Mongo-backed in Infrastructure.</summary>
public interface IMediaAssetStore
{
    Task InsertAsync(MediaAsset asset, CancellationToken ct);

    Task<MediaAsset?> FindAsync(string mediaId, CancellationToken ct);

    /// <summary>The whole library, newest first. A CMS library is curated — it
    /// stays small enough to page in the client later if it ever needs it.</summary>
    Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken ct);

    /// <summary>Withdraw (or restore) an asset in the pickers. Never touches
    /// the upload timestamp — retirement is not an upload event.</summary>
    Task SetRetiredAsync(string mediaId, bool retired, CancellationToken ct);

    Task DeleteAsync(string mediaId, CancellationToken ct);
}

/// <summary>
/// The bytes for the media library.
///
/// <para>
/// <b>Open, not presigned — a deliberate break from the Speaking pattern.</b>
/// Speaking plays back in the learner exam, cross-origin from the API, which
/// is why it mints presigned URLs. The media library plays back inside the
/// authenticated CMS, so the content endpoint simply streams through the
/// server with range processing — the same shape <c>GET /exams/assets</c> has
/// had since it shipped, and the only shape that also works in Development,
/// where there is no bucket to presign against.
/// </para>
/// </summary>
public interface IMediaBlobStore
{
    /// <summary>
    /// Whether this deployment can store media bytes at all. A runtime fact,
    /// not a build-time one — registered as the null implementation when
    /// object storage is absent, exactly like the Speaking store.
    /// </summary>
    bool IsConfigured { get; }

    Task PutAsync(string objectKey, Stream content, string contentType, string checksumSha256, CancellationToken ct);

    Task<MediaContent?> OpenAsync(string objectKey, CancellationToken ct);

    /// <summary>Removing a key that is already gone is success, not an error.</summary>
    Task DeleteAsync(string objectKey, CancellationToken ct);
}

/// <summary>
/// Thrown when the deployment has nowhere to put media bytes — object storage
/// unconfigured and no local fallback. The endpoint turns it into a stable
/// <c>MEDIA_UPLOAD_UNAVAILABLE</c> problem rather than a 500.
/// </summary>
public sealed class MediaUploadUnavailableException()
    : Exception("Media upload is not configured for this deployment.");
