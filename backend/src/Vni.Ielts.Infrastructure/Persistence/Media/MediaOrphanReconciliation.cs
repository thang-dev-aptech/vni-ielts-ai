using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Infrastructure.Persistence.Media;

/// <summary>
/// Test seam for orphan-intent txn mid-points. Production registers
/// <see cref="NoOpMediaOrphanReconciliationHooks"/>.
/// </summary>
public interface IMediaOrphanReconciliationHooks
{
    Task AfterIntentInsertedAsync(CancellationToken ct);
}

public sealed class NoOpMediaOrphanReconciliationHooks : IMediaOrphanReconciliationHooks
{
    public Task AfterIntentInsertedAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Durable intent when upload compensation cannot delete an orphaned object.
/// The intent is the primary recovery pointer and commits before the audit,
/// which is best-effort. Error codes are allow-listed at this boundary.
/// </summary>
internal sealed class MongoMediaOrphanReconciliation(
    MongoContext context,
    IMediaOrphanReconciliationHooks hooks) : IMediaOrphanReconciliation
{
    private static readonly UserId SystemActor = new("system-media-reconcile");

    public async Task RecordFailedCleanupAsync(
        string storageKey,
        string mediaId,
        string fileName,
        MediaCleanupErrorCode errorCode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        _ = ct; // never tied to the request token

        var wire = MediaCleanupErrorCodes.ToWire(errorCode);
        var intentId = Guid.NewGuid().ToString("n");
        var entry = AuditEntry.Record(
            SystemActor,
            "system",
            AuditAction.MediaOrphanCleanupFailed,
            "media-orphan",
            storageKey,
            fileName,
            now,
            new Dictionary<string, string>
            {
                ["mediaId"] = mediaId,
                ["errorCode"] = wire,
            });

        await context.Database.GetCollection<BsonDocument>("media_orphan_intents").InsertOneAsync(
            new BsonDocument
            {
                ["_id"] = intentId,
                ["storageKey"] = storageKey,
                ["mediaId"] = mediaId,
                ["fileName"] = fileName,
                ["errorCode"] = wire,
                ["createdAt"] = now.UtcDateTime,
                ["status"] = "pending",
            },
            cancellationToken: CancellationToken.None);

        // Audit failure must not erase the only durable recovery pointer.
        await hooks.AfterIntentInsertedAsync(CancellationToken.None);
        await context.Database.GetCollection<BsonDocument>("audit_log").InsertOneAsync(
            new BsonDocument
            {
                ["_id"] = entry.Id,
                ["at"] = entry.At.UtcDateTime,
                ["actorId"] = entry.ActorId.Value,
                ["actorEmail"] = entry.ActorEmail,
                ["action"] = entry.Action.ToString(),
                ["targetType"] = entry.TargetType,
                ["targetId"] = entry.TargetId,
                ["targetLabel"] = entry.TargetLabel,
                ["detail"] = new BsonArray(
                    entry.Detail.Select(kv => new BsonDocument { ["k"] = kv.Key, ["v"] = kv.Value })),
            },
            cancellationToken: CancellationToken.None);
    }
}
