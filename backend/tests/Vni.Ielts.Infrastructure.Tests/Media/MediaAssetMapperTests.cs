using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Media;
using Vni.Ielts.Infrastructure.Persistence.Media;

namespace Vni.Ielts.Infrastructure.Tests.Media;

public sealed class MediaAssetMapperTests
{
    private static readonly UserId Uploader = UserId.New();
    private static readonly DateTimeOffset T0 = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_audio_asset_round_trips_including_duration()
    {
        var asset = MediaAsset.Create(
            "media-1", MediaKind.Audio, "listening-part-1.m4a", "audio/mp4",
            bytes: 2_411_520, durationMs: 301_000, checksum: "abc123", storageKey: "storage-key-1",
            Uploader, T0);

        var restored = asset.ToDocument().ToDomain();

        Assert.Equal(asset.Id, restored.Id);
        Assert.Equal(MediaKind.Audio, restored.Kind);
        Assert.Equal(301_000, restored.DurationMs);
        Assert.Equal(Uploader, restored.UploadedBy);
        Assert.Equal(T0, restored.UploadedAt);
        Assert.False(restored.Retired);
    }

    [Fact]
    public void An_image_asset_with_no_duration_round_trips_as_null()
    {
        var asset = MediaAsset.Create(
            "media-2", MediaKind.Image, "chart.png", "image/png",
            bytes: 51_200, durationMs: null, checksum: "def456", storageKey: "storage-key-2",
            Uploader, T0);

        var restored = asset.ToDocument().ToDomain();

        Assert.Null(restored.DurationMs);
    }

    [Fact]
    public void Retired_state_round_trips()
    {
        var asset = MediaAsset.Create(
            "media-3", MediaKind.File, "rubric.pdf", "application/pdf",
            bytes: 1024, durationMs: null, checksum: "ghi789", storageKey: "storage-key-3",
            Uploader, T0);
        asset.Retire();

        var restored = asset.ToDocument().ToDomain();

        Assert.True(restored.Retired);
    }
}
