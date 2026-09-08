using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Media;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Resolves a `media/&lt;id&gt;`-shaped `assetRef` — the form in-place CMS
/// authoring produces (Phase 3), as opposed to the `assets/&lt;path&gt;` form
/// a ZIP import produces. Looks up the `MediaAsset` by id, then reads its
/// bytes through the same <see cref="IObjectStorage"/> the Media Library
/// upload endpoint already writes to.
///
/// <b>A retired asset still resolves.</b> Retirement removes an id from the
/// upload picker; it does not revoke access for content that already
/// references it — an already-approved or published exam version's audio
/// must keep playing. → `docs/ux/cms-content-operations.md` §2.4
/// </summary>
public sealed class MediaBackedAssetStore(IMediaAssetRepository media, IObjectStorage storage) : IExamAssetStore
{
    public async Task<ExamAsset?> OpenAsync(string reference, CancellationToken ct)
    {
        if (!reference.StartsWith("media/", StringComparison.Ordinal)) return null;

        var mediaId = reference["media/".Length..];
        if (mediaId.Length == 0) return null;

        var asset = await media.FindAsync(mediaId, ct);
        if (asset is null) return null;

        var content = await storage.OpenAsync(asset.StorageKey, ct);
        return new ExamAsset(content, asset.ContentType);
    }
}

/// <summary>
/// Tries each store in order and returns the first hit — the seam that lets
/// `assets/` (package-relative, Development-only fixture today) and
/// `media/&lt;id&gt;` (Media Library, both environments) resolve through one
/// <see cref="IExamAssetStore"/> without either implementation knowing about
/// the other's namespace.
/// </summary>
public sealed class CompositeAssetStore(IEnumerable<IExamAssetStore> stores) : IExamAssetStore
{
    public async Task<ExamAsset?> OpenAsync(string reference, CancellationToken ct)
    {
        foreach (var store in stores)
        {
            if (await store.OpenAsync(reference, ct) is { } asset) return asset;
        }

        return null;
    }
}
