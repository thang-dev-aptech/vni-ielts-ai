using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Infrastructure.Persistence.Identity;

[BsonIgnoreExtraElements]
internal sealed class AuditDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("at")]
    public DateTime At { get; set; }

    [BsonElement("actorId")]
    public string ActorId { get; set; } = string.Empty;

    [BsonElement("actorEmail")]
    public string ActorEmail { get; set; } = string.Empty;

    [BsonElement("action")]
    public string Action { get; set; } = string.Empty;

    [BsonElement("targetType")]
    public string TargetType { get; set; } = string.Empty;

    [BsonElement("targetId")]
    public string TargetId { get; set; } = string.Empty;

    [BsonElement("targetLabel")]
    public string TargetLabel { get; set; } = string.Empty;

    [BsonElement("detail")]
    public List<DetailPair> Detail { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class DetailPair
{
    [BsonElement("k")]
    public string Key { get; set; } = string.Empty;

    [BsonElement("v")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// The audit trail, in Mongo.
///
/// <b>Insert and read. No update method, no delete method, no TTL index.</b>
/// The TTL absence is deliberate and worth stating: every other collection in
/// this file that grows without bound has one, and this is the collection
/// where "it cleans itself up" would mean evidence disappearing on a schedule.
/// Retention becomes a decision someone makes explicitly, not a default
/// nobody remembers setting. → `cms-spec.md` ràng buộc 6, threat `T21`
/// </summary>
internal sealed class MongoAuditLog(MongoContext context) : IAuditLog
{
    private IMongoCollection<AuditDocument> Entries =>
        context.Database.GetCollection<AuditDocument>("audit_log");

    public Task AppendAsync(AuditEntry entry, CancellationToken ct) =>
        Entries.InsertOneAsync(
            new AuditDocument
            {
                Id = entry.Id,
                At = entry.At.UtcDateTime,
                ActorId = entry.ActorId.Value,
                ActorEmail = entry.ActorEmail,
                Action = entry.Action.ToString(),
                TargetType = entry.TargetType,
                TargetId = entry.TargetId,
                TargetLabel = entry.TargetLabel,
                Detail = [.. entry.Detail.Select(kv => new DetailPair { Key = kv.Key, Value = kv.Value })],
            },
            cancellationToken: ct);

    public async Task<(IReadOnlyList<AuditEntry> Entries, long Total)> ListAsync(
        string? actorId, string? action, int skip, int take, CancellationToken ct)
    {
        var filters = new List<FilterDefinition<AuditDocument>>();

        if (!string.IsNullOrWhiteSpace(actorId))
            filters.Add(Builders<AuditDocument>.Filter.Eq(e => e.ActorId, actorId));

        if (!string.IsNullOrWhiteSpace(action))
            filters.Add(Builders<AuditDocument>.Filter.Eq(e => e.Action, action));

        var filter = filters.Count == 0
            ? Builders<AuditDocument>.Filter.Empty
            : Builders<AuditDocument>.Filter.And(filters);

        var total = await Entries.CountDocumentsAsync(filter, cancellationToken: ct);

        var docs = await Entries
            .Find(filter)
            .SortByDescending(e => e.At)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(ct);

        return ([.. docs.Select(ToDomain)], total);
    }

    public static Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken ct) =>
        database.GetCollection<AuditDocument>("audit_log").Indexes.CreateOneAsync(
            new CreateIndexModel<AuditDocument>(
                Builders<AuditDocument>.IndexKeys
                    .Descending(e => e.At)
                    .Ascending(e => e.Action),
                new CreateIndexOptions { Name = "ix_audit_at_action" }),
            cancellationToken: ct);

    private static AuditEntry ToDomain(AuditDocument doc) =>
        new(
            doc.Id,
            new DateTimeOffset(DateTime.SpecifyKind(doc.At, DateTimeKind.Utc)),
            new UserId(doc.ActorId),
            doc.ActorEmail,
            Enum.Parse<AuditAction>(doc.Action),
            doc.TargetType,
            doc.TargetId,
            doc.TargetLabel,
            doc.Detail.ToDictionary(d => d.Key, d => d.Value));
}
