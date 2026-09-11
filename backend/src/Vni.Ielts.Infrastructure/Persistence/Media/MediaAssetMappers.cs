using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Media;

namespace Vni.Ielts.Infrastructure.Persistence.Media;

internal static class MediaAssetMappers
{
    private static DateTime Utc(DateTimeOffset value) => value.UtcDateTime;

    private static DateTimeOffset Offset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static MediaAssetDocument ToDocument(this MediaAsset asset) => new()
    {
        Id = asset.Id,
        Kind = asset.Kind.ToString(),
        FileName = asset.FileName,
        ContentType = asset.ContentType,
        Bytes = asset.Bytes,
        DurationMs = asset.DurationMs,
        Checksum = asset.Checksum,
        StorageKey = asset.StorageKey,
        UploadedBy = asset.UploadedBy.Value,
        UploadedAt = Utc(asset.UploadedAt),
        Retired = asset.Retired,
    };

    public static MediaAsset ToDomain(this MediaAssetDocument document) => MediaAsset.Rehydrate(
        document.Id,
        Enum.Parse<MediaKind>(document.Kind),
        document.FileName,
        document.ContentType,
        document.Bytes,
        document.DurationMs,
        document.Checksum,
        document.StorageKey,
        new UserId(document.UploadedBy),
        Offset(document.UploadedAt),
        document.Retired);
}
