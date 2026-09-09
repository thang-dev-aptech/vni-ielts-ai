using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Infrastructure.Persistence;

namespace Vni.Ielts.Infrastructure.Persistence.Importing;

[BsonIgnoreExtraElements]
internal sealed class ImportAssetCleanupIntentDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("draftId")]
    public string DraftId { get; set; } = string.Empty;

    [BsonElement("references")]
    public List<string> References { get; set; } = [];

    [BsonElement("reason")]
    public string Reason { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = "pending";

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class ImportAssetCleanupCoordinationDocument
{
    [BsonId]
    public string Id { get; set; } = "import-assets";

    [BsonElement("owner")]
    public string Owner { get; set; } = string.Empty;

    [BsonElement("leaseUntil")]
    public DateTime LeaseUntil { get; set; }

    [BsonElement("fence")]
    public long Fence { get; set; }
}

internal interface IImportAssetCoordinationLease : IAsyncDisposable
{
    string Owner { get; }
    long Fence { get; }
    CancellationToken Token { get; }
    Task EnsureCurrentAsync(CancellationToken ct);
}

internal static class MongoImportAssetCleanup
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LeaseHeartbeat = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AcquirePoll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(15);

    public static async Task RecordAsync(
        MongoContext context,
        Guid draftId,
        IReadOnlyList<string> references,
        ImportAssetCleanupReason reason,
        CancellationToken ct)
    {
        await context.ImportAssetCleanupIntents.InsertOneAsync(
            new ImportAssetCleanupIntentDocument
            {
                Id = Guid.NewGuid().ToString("n"),
                DraftId = draftId.ToString("D"),
                References = [.. references],
                Reason = reason.ToString(),
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
            },
            cancellationToken: ct);
    }

    public static async Task<IReadOnlyList<ImportAssetCleanupIntentDocument>> PendingAsync(
        MongoContext context, CancellationToken ct) =>
        await context.ImportAssetCleanupIntents
            .Find(Builders<ImportAssetCleanupIntentDocument>.Filter.Eq(d => d.Status, "pending"))
            .ToListAsync(ct);

    public static async Task MarkAsync(
        MongoContext context, string id, string status, CancellationToken ct) =>
        await context.ImportAssetCleanupIntents.UpdateOneAsync(
            Builders<ImportAssetCleanupIntentDocument>.Filter.Eq(d => d.Id, id),
            Builders<ImportAssetCleanupIntentDocument>.Update.Set(d => d.Status, status),
            cancellationToken: ct);

    public static async Task<bool> CatalogueReferencesAsync(
        IExamCatalogue catalogue, string reference, CancellationToken ct)
    {
        foreach (var version in await catalogue.ListAllAsync(ct))
        {
            if (ImportAssetPaths.ZipBindableReferences(version)
                .Any(r => string.Equals(r, reference, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<T> WithCoordinationLeaseAsync<T>(
        MongoContext context,
        Func<IImportAssetCoordinationLease, CancellationToken, Task<T>> action,
        CancellationToken ct)
    {
        await BootstrapCoordinationAsync(context, ct);
        await using var lease = await AcquireAsync(context, ct);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.Token);
        return await action(lease, linked.Token);
    }

    private static async Task BootstrapCoordinationAsync(MongoContext context, CancellationToken ct)
    {
        try
        {
            await context.ImportAssetCleanupCoordination.InsertOneAsync(
                new ImportAssetCleanupCoordinationDocument
                {
                    Id = "import-assets",
                    Owner = string.Empty,
                    LeaseUntil = DateTime.UnixEpoch,
                    Fence = 0,
                },
                cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Idempotent singleton bootstrap.
        }
    }

    private static async Task<IImportAssetCoordinationLease> AcquireAsync(
        MongoContext context,
        CancellationToken ct)
    {
        var owner = Guid.NewGuid().ToString("n");
        var options = new FindOneAndUpdateOptions<ImportAssetCleanupCoordinationDocument>
        {
            IsUpsert = false,
            ReturnDocument = ReturnDocument.After,
        };

        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var leaseUntil = now.Add(LeaseDuration);
            var filter = Builders<ImportAssetCleanupCoordinationDocument>.Filter.And(
                Builders<ImportAssetCleanupCoordinationDocument>.Filter.Eq(d => d.Id, "import-assets"),
                Builders<ImportAssetCleanupCoordinationDocument>.Filter.Lte(d => d.LeaseUntil, now));
            var update = Builders<ImportAssetCleanupCoordinationDocument>.Update
                .Set(d => d.Owner, owner)
                .Set(d => d.LeaseUntil, leaseUntil)
                .Inc(d => d.Fence, 1);
            try
            {
                var claimed = await context.ImportAssetCleanupCoordination
                    .FindOneAndUpdateAsync(filter, update, options, ct);
                if (claimed is not null && string.Equals(claimed.Owner, owner, StringComparison.Ordinal))
                    return new Lease(context, owner, claimed.Fence);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Treated as busy lease, never surfaced to callers.
            }

            await Task.Delay(AcquirePoll, ct);
        }

        ct.ThrowIfCancellationRequested();
        throw new OperationCanceledException(ct);
    }

    private sealed class Lease : IImportAssetCoordinationLease
    {
        private readonly MongoContext _context;
        private readonly string _owner;
        private readonly long _fence;
        private readonly CancellationTokenSource _lost = new();
        private readonly Task _heartbeat;
        private Exception? _heartbeatFailure;
        private bool _disposed;

        public string Owner => _owner;
        public long Fence => _fence;
        public CancellationToken Token => _lost.Token;

        public Lease(MongoContext context, string owner, long fence)
        {
            _context = context;
            _owner = owner;
            _fence = fence;
            _heartbeat = HeartbeatLoopAsync();
        }

        public async Task EnsureCurrentAsync(CancellationToken ct)
        {
            var current = await _context.ImportAssetCleanupCoordination
                .Find(Builders<ImportAssetCleanupCoordinationDocument>.Filter.Eq(d => d.Id, "import-assets"))
                .FirstOrDefaultAsync(ct);
            var stale = current is null
                || !string.Equals(current.Owner, _owner, StringComparison.Ordinal)
                || current.Fence != _fence
                || current.LeaseUntil <= DateTime.UtcNow;
            if (stale)
            {
                _lost.Cancel();
                throw new InvalidOperationException("IMPORT_ASSET_LEASE_LOST");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _lost.Cancel();
            try
            {
                await _heartbeat;
            }
            catch (OperationCanceledException) when (_lost.IsCancellationRequested)
            {
                // Expected when the action finishes or loses the lease.
            }

            using var release = new CancellationTokenSource(ReleaseTimeout);
            await _context.ImportAssetCleanupCoordination.UpdateOneAsync(
                Builders<ImportAssetCleanupCoordinationDocument>.Filter.And(
                    Builders<ImportAssetCleanupCoordinationDocument>.Filter.Eq(d => d.Id, "import-assets"),
                    Builders<ImportAssetCleanupCoordinationDocument>.Filter.Eq(d => d.Owner, _owner),
                    Builders<ImportAssetCleanupCoordinationDocument>.Filter.Eq(d => d.Fence, _fence)),
                Builders<ImportAssetCleanupCoordinationDocument>.Update
                    .Set(d => d.Owner, string.Empty)
                    .Set(d => d.LeaseUntil, DateTime.UnixEpoch),
                cancellationToken: release.Token);

            if (_heartbeatFailure is not null)
                throw new InvalidOperationException("IMPORT_ASSET_LEASE_HEARTBEAT_FAILED", _heartbeatFailure);
        }

        private async Task HeartbeatLoopAsync()
        {
            while (!_lost.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(LeaseHeartbeat, _lost.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    using var renew = new CancellationTokenSource(ReleaseTimeout);
                    var now = DateTime.UtcNow;
                    var extended = await _context.ImportAssetCleanupCoordination.UpdateOneAsync(
                        Builders<ImportAssetCleanupCoordinationDocument>.Filter.And(
                            Builders<ImportAssetCleanupCoordinationDocument>.Filter.Eq(d => d.Id, "import-assets"),
                            Builders<ImportAssetCleanupCoordinationDocument>.Filter.Eq(d => d.Owner, _owner),
                            Builders<ImportAssetCleanupCoordinationDocument>.Filter.Eq(d => d.Fence, _fence),
                            Builders<ImportAssetCleanupCoordinationDocument>.Filter.Gt(d => d.LeaseUntil, now)),
                        Builders<ImportAssetCleanupCoordinationDocument>.Update
                            .Set(d => d.LeaseUntil, now.Add(LeaseDuration)),
                        cancellationToken: renew.Token);
                    if (extended.MatchedCount == 0)
                    {
                        _lost.Cancel();
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _heartbeatFailure = ex;
                    _lost.Cancel();
                    break;
                }
            }
        }
    }
}
