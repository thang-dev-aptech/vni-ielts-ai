using MongoDB.Driver;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Domain.Media;

namespace Vni.Ielts.Infrastructure.Persistence.Media;

internal sealed class MongoMediaAssetRepository(MongoContext context) : IMediaAssetRepository
{
    public async Task<MediaAsset?> FindAsync(string mediaId, CancellationToken ct)
    {
        var document = await context.MediaAssets
            .Find(asset => asset.Id == mediaId)
            .FirstOrDefaultAsync(ct);

        return document?.ToDomain();
    }

    public async Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken ct)
    {
        var documents = await context.MediaAssets
            .Find(FilterDefinition<MediaAssetDocument>.Empty)
            .SortByDescending(asset => asset.UploadedAt)
            .ToListAsync(ct);

        return [.. documents.Select(d => d.ToDomain())];
    }

    public async Task SaveAsync(MediaAsset asset, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var document = asset.ToDocument();
        await context.MediaAssets.ReplaceOneAsync(
            stored => stored.Id == document.Id,
            document,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }

    public Task DeleteAsync(string mediaId, CancellationToken ct) =>
        context.MediaAssets.DeleteOneAsync(asset => asset.Id == mediaId, ct);
}
