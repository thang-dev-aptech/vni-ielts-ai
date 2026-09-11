using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Media;

/// <summary>
/// Matches <c>apps/admin/src/lib/media.ts</c>'s <c>MediaKind</c> exactly — the
/// already-shipped, already-tested client contract this entity exists to
/// satisfy server-side, not a taxonomy invented here.
/// </summary>
public enum MediaKind { Audio, Image, File }

/// <summary>
/// One uploaded file in the Media Library, independent of the package
/// pipeline (`docs/development/phase-2-content-import-plan.md` Plan 04).
///
/// <b>Content is immutable once stored.</b> Replacing a file is not an
/// operation this type offers — a new recording is a new asset with a new id,
/// never an edit to an existing one. That is what makes "a published version's
/// media never changes under it" true structurally rather than by convention:
/// <c>apps/admin/src/lib/media.ts</c>'s <c>assetState</c> calls a published
/// reference "locked" for the same reason.
///
/// <b>Usage tracking is real but currently vacuous.</b> Nothing produces a
/// <c>media/&lt;id&gt;</c>-shaped <c>assetRef</c> yet — that format is
/// explicitly Phase 3's (in-place authoring), per the plan's own scope note.
/// A media asset can be created and retired today; whether anything
/// references it is answered by the caller cross-referencing
/// <c>ExamVersion</c> content, not by anything this type tracks itself.
/// </summary>
public sealed class MediaAsset
{
    private MediaAsset(
        string id, MediaKind kind, string fileName, string contentType, long bytes,
        int? durationMs, string checksum, string storageKey, UserId uploadedBy,
        DateTimeOffset uploadedAt, bool retired)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A media asset needs an id.", nameof(id));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A media asset needs a file name.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(contentType)) throw new ArgumentException("A media asset needs a content type.", nameof(contentType));
        if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes), "A media asset cannot be empty.");
        if (string.IsNullOrWhiteSpace(checksum)) throw new ArgumentException("A media asset needs a checksum.", nameof(checksum));
        if (string.IsNullOrWhiteSpace(storageKey)) throw new ArgumentException("A media asset needs a storage key.", nameof(storageKey));
        if (durationMs is < 0) throw new ArgumentOutOfRangeException(nameof(durationMs));

        Id = id;
        Kind = kind;
        FileName = fileName;
        ContentType = contentType;
        Bytes = bytes;
        DurationMs = durationMs;
        Checksum = checksum;
        StorageKey = storageKey;
        UploadedBy = uploadedBy;
        UploadedAt = uploadedAt;
        Retired = retired;
    }

    public string Id { get; }
    public MediaKind Kind { get; }

    /// <summary>Display only, and never a storage key — the same rule the package format states for <c>assetRef</c>.</summary>
    public string FileName { get; }

    public string ContentType { get; }
    public long Bytes { get; }
    public int? DurationMs { get; }

    /// <summary>SHA-256, hex. What makes "the same file" answerable — same reasoning as `media.ts`'s <c>checksumOf</c>.</summary>
    public string Checksum { get; }

    /// <summary>Opaque key into <c>IObjectStorage</c>. Never sent to a client.</summary>
    public string StorageKey { get; }

    public UserId UploadedBy { get; }
    public DateTimeOffset UploadedAt { get; }
    public bool Retired { get; private set; }

    public static MediaAsset Create(
        string id, MediaKind kind, string fileName, string contentType, long bytes,
        int? durationMs, string checksum, string storageKey, UserId uploadedBy, DateTimeOffset uploadedAt) =>
        new(id, kind, fileName, contentType, bytes, durationMs, checksum, storageKey, uploadedBy, uploadedAt, retired: false);

    public static MediaAsset Rehydrate(
        string id, MediaKind kind, string fileName, string contentType, long bytes,
        int? durationMs, string checksum, string storageKey, UserId uploadedBy, DateTimeOffset uploadedAt, bool retired) =>
        new(id, kind, fileName, contentType, bytes, durationMs, checksum, storageKey, uploadedBy, uploadedAt, retired);

    /// <summary>Withdrawn from the picker. Still resolves for everything already using it — `mayRetire`/`assetState` in `media.ts`.</summary>
    public void Retire()
    {
        if (Retired) throw new InvalidOperationException("This asset is already retired.");
        Retired = true;
    }
}
