using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Usage;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Domain.Usage;
using Vni.Ielts.Infrastructure.Persistence.Identity;

namespace Vni.Ielts.Infrastructure.Persistence.Usage;

/// <summary>
/// The ledger row, stored. Enum stored as its name, never its ordinal —
/// PersistenceRepresentationTests explains why an ordinal is a landmine here.
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class UsageEntryDocument
{
    /// <summary>
    /// The deterministic-or-random id from the domain, not a Mongo
    /// <c>ObjectId</c>. A unique-key collision on a deterministic id is what
    /// makes an earn idempotent — see <see cref="MongoUsageLedger.AppendAsync"/>.
    /// </summary>
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("at")]
    public DateTime At { get; set; }

    [BsonElement("action")]
    public string Action { get; set; } = string.Empty;

    [BsonElement("kind")]
    public string Kind { get; set; } = string.Empty;

    [BsonElement("turns")]
    public decimal Turns { get; set; }

    [BsonElement("sessionId")]
    [BsonIgnoreIfNull]
    public string? SessionId { get; set; }

    [BsonElement("provider")]
    [BsonIgnoreIfNull]
    public string? Provider { get; set; }

    [BsonElement("model")]
    [BsonIgnoreIfNull]
    public string? Model { get; set; }

    [BsonElement("tokensIn")]
    [BsonIgnoreIfNull]
    public long? TokensIn { get; set; }

    [BsonElement("tokensOut")]
    [BsonIgnoreIfNull]
    public long? TokensOut { get; set; }

    [BsonElement("costEstimate")]
    [BsonIgnoreIfNull]
    public decimal? CostEstimate { get; set; }
}

internal static class UsageMappers
{
    public static UsageEntryDocument ToDocument(this UsageEntry entry) => new()
    {
        Id = entry.Id,
        UserId = entry.UserId.Value,
        At = entry.At.UtcDateTime,
        Action = entry.Action,
        Kind = entry.Kind.ToString(),
        Turns = entry.Turns,
        SessionId = entry.SessionId?.Value,
        Provider = entry.Provider,
        Model = entry.Model,
        TokensIn = entry.TokensIn,
        TokensOut = entry.TokensOut,
        CostEstimate = entry.CostEstimate,
    };

    public static UsageEntry ToDomain(this UsageEntryDocument doc) => UsageEntry.Rehydrate(
        doc.Id,
        new UserId(doc.UserId),
        new DateTimeOffset(DateTime.SpecifyKind(doc.At, DateTimeKind.Utc)),
        doc.Action,
        Enum.Parse<UsageKind>(doc.Kind),
        doc.Turns,
        doc.SessionId is null ? null : new ExamSessionId(doc.SessionId),
        doc.Provider,
        doc.Model,
        doc.TokensIn,
        doc.TokensOut,
        doc.CostEstimate);
}

/// <summary>
/// Append and read against the <c>usage_ledger</c> collection. Nothing here
/// updates or deletes a row — see <see cref="IUsageLedger"/>.
/// </summary>
internal sealed class MongoUsageLedger(MongoContext ctx) : IUsageLedger
{
    public async Task<bool> AppendAsync(UsageEntry entry, CancellationToken ct)
    {
        try
        {
            await ctx.UsageLedger.InsertOneAsync(entry.ToDocument(), cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The row with this id already exists — the earn or grant it
            // represents already happened. Not an error; the caller's
            // action still succeeded.
            return false;
        }
    }

    public async Task<IReadOnlyList<UsageEntry>> ListAsync(UserId userId, int take, CancellationToken ct)
    {
        var docs = await ctx.UsageLedger
            .Find(e => e.UserId == userId.Value)
            .SortByDescending(e => e.At)
            .Limit(take)
            .ToListAsync(ct);

        return [.. docs.Select(d => d.ToDomain())];
    }

    public async Task<decimal> BalanceAsync(UserId userId, CancellationToken ct)
    {
        // A learner's whole history is at most a few thousand rows even after
        // years of daily use, so summing client-side over a single-field
        // projection is simpler than a server-side aggregation and costs
        // nothing an index doesn't already make cheap.
        var turns = await ctx.UsageLedger
            .Find(e => e.UserId == userId.Value)
            .Project(e => e.Turns)
            .ToListAsync(ct);

        return turns.Sum();
    }
}

/// <summary>
/// Looks a referrer up by the code on their share link — a scan of the
/// unique, sparse <c>ux_users_referral_code</c> index, not a table scan.
/// </summary>
internal sealed class MongoReferralDirectory(MongoContext ctx) : IReferralDirectory
{
    public async Task<User?> FindByReferralCodeAsync(string code, CancellationToken ct)
    {
        var doc = await ctx.Users.Find(u => u.ReferralCode == code).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }
}
