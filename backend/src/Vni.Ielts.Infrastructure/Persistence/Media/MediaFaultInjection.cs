using System.Collections.Concurrent;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Domain.Media;

namespace Vni.Ielts.Infrastructure.Persistence.Media;

/// <summary>
/// Test-only fault / tracking seam. Registered by integration test factories —
/// never by production <c>DependencyInjection</c>.
/// </summary>
public sealed class MediaFaultInjection
{
    public int FailNextSaves;
    public int FailNextDeletes;
    public bool ThrowCanceledOnSave;
    public bool FailNextStorageDelete;
    public ConcurrentBag<string> PutKeys { get; } = [];
    public ConcurrentBag<string> DeletedKeys { get; } = [];
    public bool? LastDeleteTokenWasCanceled { get; set; }
    public bool? LastSaveTokenWasCanceled { get; set; }
}

/// <summary>Test decorator — constructed only from test factories.</summary>
internal sealed class FaultInjectingMediaAssetRepository(
    MongoMediaAssetRepository inner,
    MediaFaultInjection fault) : IMediaAssetRepository
{
    public Task<MediaAsset?> FindAsync(string mediaId, CancellationToken ct) =>
        inner.FindAsync(mediaId, ct);

    public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken ct) =>
        inner.ListAsync(ct);

    public Task SaveAsync(MediaAsset asset, CancellationToken ct)
    {
        fault.LastSaveTokenWasCanceled = ct.IsCancellationRequested;

        if (fault.ThrowCanceledOnSave)
            throw new OperationCanceledException(ct);

        if (fault.FailNextSaves > 0)
        {
            fault.FailNextSaves--;
            throw new InvalidOperationException("injected media SaveAsync failure");
        }

        return inner.SaveAsync(asset, ct);
    }

    public Task DeleteAsync(string mediaId, CancellationToken ct)
    {
        if (fault.FailNextDeletes > 0)
        {
            fault.FailNextDeletes--;
            throw new InvalidOperationException("injected media DeleteAsync failure");
        }

        return inner.DeleteAsync(mediaId, ct);
    }
}

/// <summary>Test decorator — constructed only from test factories.</summary>
internal sealed class TrackingObjectStorage(S3ObjectStorage inner, MediaFaultInjection fault) : IObjectStorage
{
    public async Task<string> PutAsync(Stream content, string contentType, CancellationToken ct)
    {
        var key = await inner.PutAsync(content, contentType, ct);
        fault.PutKeys.Add(key);
        return key;
    }

    public Task<Stream> OpenAsync(string key, CancellationToken ct) =>
        inner.OpenAsync(key, ct);

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        fault.LastDeleteTokenWasCanceled = ct.IsCancellationRequested;

        if (fault.FailNextStorageDelete)
        {
            fault.FailNextStorageDelete = false;
            throw new InvalidOperationException("injected storage DeleteAsync failure");
        }

        await inner.DeleteAsync(key, ct);
        fault.DeletedKeys.Add(key);
    }
}
