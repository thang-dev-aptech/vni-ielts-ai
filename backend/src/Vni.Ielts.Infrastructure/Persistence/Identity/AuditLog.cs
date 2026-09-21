using System.Text;
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
/// An opaque cursor for audit log pagination, encoding the last seen row's
/// timestamp and id. Cursors are base64-encoded strings that the client
/// passes back unchanged; they are never parsed on the client.
/// </summary>
internal sealed record AuditCursor
{
    private const string Scheme = "audit:v1:";

    public DateTime At { get; init; }
    public string Id { get; init; } = string.Empty;

    public static AuditCursor Create(DateTime at, string id) => new() { At = at, Id = id };

    public static AuditCursor? TryDecode(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return null;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            if (!decoded.StartsWith(Scheme, StringComparison.Ordinal))
                return null;

            var parts = decoded[Scheme.Length..].Split(':', 2);
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks))
                return null;

            return new AuditCursor { At = new DateTime(ticks, DateTimeKind.Utc), Id = parts[1] };
        }
        catch
        {
            return null;
        }
    }

    public string Encode()
    {
        var decoded = $"{Scheme}{At.Ticks}:{Id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(decoded));
    }
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

    /// <summary>
    /// One page of the audit log, keyed by cursor rather than offset.
    ///
    /// <para>
    /// <b>Why not offset:</b> entries insert at the head as they are made.
    /// Offset paging counts from the top, so every insert above the current
    /// position shifts the offset down, causing the next page to either skip
    /// rows or show one again. A cursor names a position in the sort order,
    /// not a count of rows to skip, so an insert above it changes nothing.
    /// </para>
    /// </summary>
    public async Task<(IReadOnlyList<AuditEntry> Entries, string? NextCursor)> ListCursorAsync(
        string? actorId, string? action, int take, CancellationToken ct, string? cursor = null)
    {
        var filters = new List<FilterDefinition<AuditDocument>>();

        if (!string.IsNullOrWhiteSpace(actorId))
            filters.Add(Builders<AuditDocument>.Filter.Eq(e => e.ActorId, actorId));

        if (!string.IsNullOrWhiteSpace(action))
            filters.Add(Builders<AuditDocument>.Filter.Eq(e => e.Action, action));

        var baseFilter = filters.Count == 0
            ? Builders<AuditDocument>.Filter.Empty
            : Builders<AuditDocument>.Filter.And(filters);

        var filter = baseFilter;

        // Decode the cursor to apply position filtering
        if (AuditCursor.TryDecode(cursor) is { } after)
        {
            var cursorFilter = Builders<AuditDocument>.Filter.Or(
                Builders<AuditDocument>.Filter.Lt(e => e.At, after.At),
                Builders<AuditDocument>.Filter.And(
                    Builders<AuditDocument>.Filter.Eq(e => e.At, after.At),
                    Builders<AuditDocument>.Filter.Lt(e => e.Id, after.Id)
                )
            );
            filter = Builders<AuditDocument>.Filter.And(baseFilter, cursorFilter);
        }

        var docs = await Entries
            .Find(filter)
            .SortByDescending(e => e.At)
            .ThenByDescending(e => e.Id)
            .Limit(take + 1)  // Fetch one extra to see if there are more
            .ToListAsync(ct);

        string? nextCursor = null;
        if (docs.Count > take)
        {
            // There are more rows; provide a cursor pointing to the last row we're returning
            docs = docs.Take(take).ToList();
            if (docs.Count > 0)
            {
                var lastDoc = docs[^1];
                nextCursor = AuditCursor.Create(lastDoc.At, lastDoc.Id).Encode();
            }
        }

        return ([.. docs.Select(ToDomain)], nextCursor);
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
