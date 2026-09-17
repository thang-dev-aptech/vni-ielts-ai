using System.Security.Cryptography;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Application.Tests.Exams;

namespace Vni.Ielts.Application.Tests.Media;

/// <summary>
/// The media upload and administration handlers, against fake ports.
///
/// <para>
/// <b>What these tests hold still.</b> The checks are ordered — empty, then
/// recognisable, then within the kind's ceiling, each refused before any byte
/// is stored — and the WEBP-before-RIFF sniff order, which decides whether a
/// WebP image is stored as an image or mis-filed as a wav. Reverting either
/// turns a test below red.
/// </para>
/// </summary>
public sealed class MediaAssetUploadTests
{
    private static readonly DateTimeOffset T0 = new(2026, 09, 11, 10, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Mp3Bytes =
        [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x11, 0x22, 0x33];

    private readonly FakeMediaStore store = new();
    private readonly FakeBlobs blobs = new();
    private readonly UploadMediaAsset handler;

    public MediaAssetUploadTests() =>
        handler = new UploadMediaAsset(store, blobs, new MovableClock(T0));

    [Fact]
    public async Task A_real_mp3_is_stored_as_audio_with_the_checksum_of_its_own_bytes()
    {
        var result = await handler.HandleAsync(new UploadMediaAsset.Command(
            new MemoryStream(Mp3Bytes), "listening-part-1.mp3", 12_345, "op"), CancellationToken.None);

        var asset = result.OkValue();
        Assert.Equal(MediaKind.Audio, asset.Kind);
        Assert.Equal("audio/mpeg", asset.ContentType);
        Assert.Equal(Mp3Bytes.Length, asset.Bytes);
        Assert.Equal(12_345, asset.DurationMs);
        Assert.Equal(T0, asset.UploadedAt);
        Assert.False(asset.Retired);
        Assert.Equal("op", asset.UploadedByName);

        // The checksum is over the whole content, not the probe.
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Mp3Bytes)).ToLowerInvariant(),
            asset.ChecksumSha256);

        // The blob was filed under the asset's own id, with the sniffed type.
        var put = Assert.Single(blobs.Puts);
        Assert.Equal(MediaObjectKey.For(asset.MediaId), put.Key);
        Assert.Equal("audio/mpeg", put.ContentType);
        Assert.Equal(asset.ChecksumSha256, put.Checksum);

        // Metadata and blob exist together.
        Assert.NotNull(await store.FindAsync(asset.MediaId, CancellationToken.None));
    }

    [Fact]
    public async Task A_webp_is_sniffed_as_an_image_even_though_it_opens_with_riff()
    {
        var webp = new byte[16];
        "RIFF"u8.CopyTo(webp);
        webp[8] = 0x57; // W
        webp[9] = 0x45; // E
        webp[10] = 0x42; // B
        webp[11] = 0x50; // P

        var result = await handler.HandleAsync(new UploadMediaAsset.Command(
            new MemoryStream(webp), "cover.webp", null, "op"), CancellationToken.None);

        var asset = result.OkValue();
        Assert.Equal(MediaKind.Image, asset.Kind);
        Assert.Equal("image/webp", asset.ContentType);
    }

    [Fact]
    public async Task Bytes_that_match_no_signature_are_refused_and_nothing_is_stored()
    {
        var exe = new byte[] { 0x4d, 0x5a, 0x90, 0x00, 0x01, 0x02, 0x03, 0x04 };

        var result = await handler.HandleAsync(new UploadMediaAsset.Command(
            new MemoryStream(exe), "payload.exe", null, "op"), CancellationToken.None);

        result.AssertError(ErrorCodes.MediaUnrecognisedFormat);
        Assert.Empty(store.Rows);
        Assert.Empty(blobs.Puts);
    }

    [Fact]
    public async Task An_empty_upload_is_refused_before_anything_else()
    {
        var result = await handler.HandleAsync(new UploadMediaAsset.Command(
            new MemoryStream(), "empty.mp3", null, "op"), CancellationToken.None);

        result.AssertError(ErrorCodes.MediaEmptyFile);
        Assert.Empty(blobs.Puts);
    }

    [Fact]
    public async Task An_upload_past_its_own_kinds_ceiling_is_refused()
    {
        // An image's ceiling is 5 MB — small enough to build here, unlike the
        // audio one. The header is a real PNG; the size is what fails it.
        var png = new byte[5 * 1024 * 1024 + 1];
        png[0] = 0x89;
        png[1] = 0x50;
        png[2] = 0x4e;
        png[3] = 0x47;

        var result = await handler.HandleAsync(new UploadMediaAsset.Command(
            new MemoryStream(png), "huge.png", null, "op"), CancellationToken.None);

        result.AssertError(ErrorCodes.MediaTooLarge);
        Assert.Empty(blobs.Puts);
    }

    [Fact]
    public async Task A_deployment_with_nowhere_to_put_bytes_refuses_upfront()
    {
        blobs.Configured = false;

        var result = await handler.HandleAsync(new UploadMediaAsset.Command(
            new MemoryStream(Mp3Bytes), "song.mp3", null, "op"), CancellationToken.None);

        result.AssertError(ErrorCodes.MediaUploadUnavailable);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public async Task A_negative_duration_is_refused_rather_than_stored()
    {
        var result = await handler.HandleAsync(new UploadMediaAsset.Command(
            new MemoryStream(Mp3Bytes), "song.mp3", -1, "op"), CancellationToken.None);

        result.AssertError(ErrorCodes.MediaInvalidDuration);
    }

    [Fact]
    public async Task A_blank_file_name_is_refused()
    {
        var result = await handler.HandleAsync(new UploadMediaAsset.Command(
            new MemoryStream(Mp3Bytes), "  ", null, "op"), CancellationToken.None);

        result.AssertError(ErrorCodes.MediaUnrecognisedFormat);
    }
}

public sealed class MediaAssetAdminTests
{
    private static readonly MediaAsset Asset = new(
        "abc123", MediaKind.Audio, "part1.mp3", "audio/mpeg", 1024, 5_000,
        "deadbeef", "op", new DateTimeOffset(2026, 09, 11, 9, 0, 0, TimeSpan.Zero), false);

    [Fact]
    public async Task Retire_marks_the_row_and_a_second_call_writes_nothing()
    {
        var store = new FakeMediaStore();
        await store.InsertAsync(Asset, CancellationToken.None);
        var handler = new RetireMediaAsset(store);

        var first = (await handler.HandleAsync(Asset.MediaId, CancellationToken.None)).OkValue();
        Assert.True(first.Retired);

        var second = (await handler.HandleAsync(Asset.MediaId, CancellationToken.None)).OkValue();
        Assert.True(second.Retired);
        Assert.Equal(1, store.RetireCalls); // the second call was read-only
    }

    [Fact]
    public async Task Retiring_an_unknown_id_is_a_not_found()
    {
        var result = await new RetireMediaAsset(new FakeMediaStore())
            .HandleAsync("missing", CancellationToken.None);

        result.AssertError(ErrorCodes.MediaNotFound);
    }

    [Fact]
    public async Task Delete_removes_the_bytes_first_then_the_row()
    {
        var store = new FakeMediaStore();
        var blobs = new FakeBlobs();
        await store.InsertAsync(Asset, CancellationToken.None);

        var result = await new DeleteMediaAsset(store, blobs)
            .HandleAsync(Asset.MediaId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([MediaObjectKey.For(Asset.MediaId)], blobs.Deletes);
        Assert.Null(await store.FindAsync(Asset.MediaId, CancellationToken.None));
    }

    [Fact]
    public async Task A_retired_asset_still_opens()
    {
        var store = new FakeMediaStore();
        var blobs = new FakeBlobs();
        await store.InsertAsync(Asset with { Retired = true }, CancellationToken.None);

        var content = (await new OpenMediaAssetContent(store, blobs)
            .HandleAsync(Asset.MediaId, CancellationToken.None)).OkValue();

        Assert.Equal("audio/mpeg", content.ContentType);
    }

    [Fact]
    public async Task Opening_an_unknown_id_is_a_not_found()
    {
        var result = await new OpenMediaAssetContent(new FakeMediaStore(), new FakeBlobs())
            .HandleAsync("missing", CancellationToken.None);

        result.AssertError(ErrorCodes.MediaNotFound);
    }
}

internal sealed class FakeMediaStore : IMediaAssetStore
{
    public List<MediaAsset> Rows { get; } = [];
    public int RetireCalls { get; private set; }

    public Task InsertAsync(MediaAsset asset, CancellationToken ct)
    {
        Rows.Add(asset);
        return Task.CompletedTask;
    }

    public Task<MediaAsset?> FindAsync(string mediaId, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.MediaId == mediaId));

    public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MediaAsset>>(Rows.OrderByDescending(r => r.UploadedAt).ToList());

    public Task SetRetiredAsync(string mediaId, bool retired, CancellationToken ct)
    {
        RetireCalls++;
        var index = Rows.FindIndex(r => r.MediaId == mediaId);
        if (index >= 0) Rows[index] = Rows[index] with { Retired = retired };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string mediaId, CancellationToken ct)
    {
        Rows.RemoveAll(r => r.MediaId == mediaId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeBlobs : IMediaBlobStore
{
    public bool Configured { get; set; } = true;
    public List<(string Key, string ContentType, string Checksum)> Puts { get; } = [];
    public List<string> Deletes { get; } = [];

    public bool IsConfigured => Configured;

    public Task PutAsync(
        string objectKey, Stream content, string contentType, string checksumSha256, CancellationToken ct)
    {
        Puts.Add((objectKey, contentType, checksumSha256));
        return Task.CompletedTask;
    }

    public Task<MediaContent?> OpenAsync(string objectKey, CancellationToken ct) =>
        Task.FromResult<MediaContent?>(
            new MediaContent(Stream.Null, "audio/mpeg", null, "\"deadbeef\""));

    public Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        Deletes.Add(objectKey);
        return Task.CompletedTask;
    }
}

internal static class MediaResultAssert
{
    public static MediaAsset OkValue(this Result<MediaAsset> result) =>
        result.IsSuccess ? result.Value!
            : throw new Xunit.Sdk.XunitException(
                $"Expected success, got {result.Error.Code}: {result.Error.Detail}");

    public static MediaContent OkValue(this Result<MediaContent> result) =>
        result.IsSuccess ? result.Value!
            : throw new Xunit.Sdk.XunitException(
                $"Expected success, got {result.Error.Code}: {result.Error.Detail}");

    public static void AssertError(this Result<MediaAsset> result, string code) =>
        Xunit.Assert.True(!result.IsSuccess && result.Error.Code == code,
            $"Expected error {code}, got {(result.IsSuccess ? "success" : result.Error.Code)}");

    public static void AssertError(this Result<MediaContent> result, string code) =>
        Xunit.Assert.True(!result.IsSuccess && result.Error.Code == code,
            $"Expected error {code}, got {(result.IsSuccess ? "success" : result.Error.Code)}");
}
