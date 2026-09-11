using Vni.Ielts.Domain.Media;

namespace Vni.Ielts.Application.Media;

public interface IMediaAssetRepository
{
    Task<MediaAsset?> FindAsync(string mediaId, CancellationToken ct);

    /// <summary>Newest first — the order the Media Library screen renders in.</summary>
    Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken ct);

    Task SaveAsync(MediaAsset asset, CancellationToken ct);

    Task DeleteAsync(string mediaId, CancellationToken ct);
}

/// <summary>
/// Content-addressed object storage — audio, images, and (later) other exam
/// media. An S3-compatible port; MinIO backs it in development,
/// `docs/architecture/system-architecture.md` names the vendor as open
/// (`H-11`, waiting on `B-11`).
///
/// Not <see cref="Vni.Ielts.Application.Exams.IExamAssetStore"/> (existing
/// content, read-only) and not
/// <see cref="Vni.Ielts.Application.Exams.IPackageUploadStore"/> (a raw,
/// not-yet-validated upload) — this is where already-accepted media actually
/// lives.
/// </summary>
public interface IObjectStorage
{
    /// <summary>Returns an internal key, never a public URL — resolving that key to bytes is a separate, permissioned read.</summary>
    Task<string> PutAsync(Stream content, string contentType, CancellationToken ct);

    Task<Stream> OpenAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}

/// <summary>
/// Bounded cleanup failure codes. Persistence stores only the wire form of
/// these values — never a raw exception message. Undefined enum casts
/// normalise to <see cref="MediaCleanupErrorCode.Failed"/>.
/// </summary>
public enum MediaCleanupErrorCode
{
    Canceled = 0,
    Timeout = 1,
    Failed = 2,
}

/// <summary>
/// Wire forms and mapping for <see cref="MediaCleanupErrorCode"/>.
/// Full exception text stays in the protected operational log only.
/// </summary>
public static class MediaCleanupErrorCodes
{
    public const string Canceled = "MEDIA_CLEANUP_CANCELED";
    public const string Timeout = "MEDIA_CLEANUP_TIMEOUT";
    public const string Failed = "MEDIA_CLEANUP_FAILED";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.Ordinal) { Canceled, Timeout, Failed };

    public static MediaCleanupErrorCode From(Exception ex) => ex switch
    {
        OperationCanceledException => MediaCleanupErrorCode.Canceled,
        TimeoutException => MediaCleanupErrorCode.Timeout,
        _ when ex.GetType().Name.Contains("Timeout", StringComparison.Ordinal) =>
            MediaCleanupErrorCode.Timeout,
        _ => MediaCleanupErrorCode.Failed,
    };

    public static MediaCleanupErrorCode Normalize(MediaCleanupErrorCode code) =>
        Enum.IsDefined(code) ? code : MediaCleanupErrorCode.Failed;

    public static string ToWire(MediaCleanupErrorCode code) => Normalize(code) switch
    {
        MediaCleanupErrorCode.Canceled => Canceled,
        MediaCleanupErrorCode.Timeout => Timeout,
        _ => Failed,
    };

    public static string NormalizeWire(string? raw) =>
        raw is not null && Allowed.Contains(raw) ? raw : Failed;
}

/// <summary>
/// Durable record when Put succeeded, metadata Save failed, and compensation
/// Delete also failed — so a reconcile pass can remove the orphaned object.
/// Implementations must persist <see cref="MediaCleanupErrorCodes.ToWire"/> only.
/// </summary>
public interface IMediaOrphanReconciliation
{
    Task RecordFailedCleanupAsync(
        string storageKey,
        string mediaId,
        string fileName,
        MediaCleanupErrorCode errorCode,
        DateTimeOffset now,
        CancellationToken ct);
}
