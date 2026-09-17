using Vni.Ielts.Application.Common;

namespace Vni.Ielts.Application.Media;

/// <summary>
/// The library itself, newest first. Every CMS operator with
/// <c>media.read</c> sees every asset — a media library has no owner scope;
/// it is shared stock, unlike a draft exam.
/// </summary>
public sealed class ListMediaAssets(IMediaAssetStore store)
{
    public async Task<IReadOnlyList<MediaAsset>> HandleAsync(CancellationToken ct) =>
        await store.ListAsync(ct);
}

/// <summary>
/// Open the bytes behind an asset for the CMS content endpoint to stream.
///
/// <para>
/// <b>A retired asset still opens.</b> "Retired" means out of every upload
/// picker; it has never meant unlistenable — a draft that still references a
/// retired file must keep working, which is exactly what the CMS's asset
/// states say (<c>retired</c> resolves, <c>deleted</c> does not).
/// </para>
/// </summary>
public sealed class OpenMediaAssetContent(IMediaAssetStore store, IMediaBlobStore blobs)
{
    public async Task<Result<MediaContent>> HandleAsync(string mediaId, CancellationToken ct)
    {
        if (await store.FindAsync(mediaId, ct) is null)
            return Error.NotFound(ErrorCodes.MediaNotFound, $"No media asset '{mediaId}'.");

        if (!blobs.IsConfigured)
            return Error.Conflict(ErrorCodes.MediaStorageUnavailable,
                "This deployment has no media storage configured.");

        var content = await blobs.OpenAsync(MediaObjectKey.For(mediaId), ct);
        return content is { } found
            ? found
            : Error.NotFound(ErrorCodes.MediaNotFound, $"No stored content for media asset '{mediaId}'.");
    }
}

/// <summary>
/// Withdraw an asset from every upload picker.
///
/// <para>
/// <b>Retire, never hard-remove, is the default exit.</b> The file may still
/// be referenced by a draft or a published version the caller cannot see, and
/// a picker is cheap — the operator who wants it gone from the disk entirely
/// takes the second step (delete) once nothing uses it.
/// </para>
///
/// <para>
/// <b>Idempotent.</b> Retiring a retired asset returns it unchanged; the
/// operation says what is true rather than arguing about how it got there.
/// </para>
/// </summary>
public sealed class RetireMediaAsset(IMediaAssetStore store)
{
    public async Task<Result<MediaAsset>> HandleAsync(string mediaId, CancellationToken ct)
    {
        if (await store.FindAsync(mediaId, ct) is not { } asset)
            return Error.NotFound(ErrorCodes.MediaNotFound, $"No media asset '{mediaId}'.");

        if (asset.Retired)
            return asset;

        await store.SetRetiredAsync(mediaId, retired: true, ct);
        return asset with { Retired = true };
    }
}

/// <summary>
/// Remove an asset and its bytes entirely.
///
/// <para>
/// <b>The blob goes first.</b> If the blob delete throws, the metadata row
/// survives and the operator can retry; if the metadata delete then throws,
/// the store holds an orphan blob nobody references — harmless, invisible,
/// and cheaper to sweep than a metadata row pointing at nothing.
/// </para>
///
/// <para>
/// <b>What this v1 does not check yet.</b> "Only when nothing references it"
/// needs the exam-version ⇄ media mapping, which arrives with the asset
/// mapping slice — today no exam version can name a media asset at all (the
/// package schema's <c>assetRef</c> admits only <c>assets/…</c>), so there is
/// nothing to consult. The mapping slice must add that check here; the
/// comment is the reminder, not a licence.
/// </para>
/// </summary>
public sealed class DeleteMediaAsset(IMediaAssetStore store, IMediaBlobStore blobs)
{
    public async Task<Result<MediaAsset>> HandleAsync(string mediaId, CancellationToken ct)
    {
        if (await store.FindAsync(mediaId, ct) is not { } asset)
            return Error.NotFound(ErrorCodes.MediaNotFound, $"No media asset '{mediaId}'.");

        if (blobs.IsConfigured)
            await blobs.DeleteAsync(MediaObjectKey.For(mediaId), ct);
        await store.DeleteAsync(mediaId, ct);
        return asset;
    }
}
