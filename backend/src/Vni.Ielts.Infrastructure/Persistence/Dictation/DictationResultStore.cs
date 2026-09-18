using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Dictation;
using Vni.Ielts.Domain.Dictation;

namespace Vni.Ielts.Infrastructure.Persistence.Dictation;

/// <summary>
/// One attempt, stored. Nothing here is a key into content: the set id and
/// the order together identify the sentence, and the sentence text lives in
/// the catalogue where it can be corrected without rewriting history.
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class DictationAttemptDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("setId")]
    public string SetId { get; set; } = string.Empty;

    [BsonElement("order")]
    public int Order { get; set; }

    [BsonElement("at")]
    public DateTime At { get; set; }

    [BsonElement("correct")]
    public int Correct { get; set; }

    [BsonElement("total")]
    public int Total { get; set; }

    /// <summary>
    /// Stored rather than derived from <c>correct == total</c>.
    ///
    /// They agree today. They would stop agreeing the moment a comparison
    /// rule changes — a tolerated accent, an ignored punctuation mark — and
    /// the row would then report a verdict the learner never saw. A row is a
    /// record of what happened, not a re-computation of it.
    /// </summary>
    [BsonElement("isPerfect")]
    public bool IsPerfect { get; set; }
}

internal static class DictationAttemptMapping
{
    public static DictationAttemptDocument ToDocument(this DictationAttempt a) => new()
    {
        Id = a.Id,
        UserId = a.UserId,
        SetId = a.SetId,
        Order = a.Order,
        At = a.At.UtcDateTime,
        Correct = a.Correct,
        Total = a.Total,
        IsPerfect = a.IsPerfect,
    };

    public static DictationAttempt ToDomain(this DictationAttemptDocument d) => new(
        d.Id,
        d.UserId,
        d.SetId,
        d.Order,
        new DateTimeOffset(DateTime.SpecifyKind(d.At, DateTimeKind.Utc)),
        d.Correct,
        d.Total,
        d.IsPerfect);
}

/// <summary>
/// Append and read against <c>dictation_attempts</c>. No update, no delete —
/// see <see cref="IDictationResults"/>.
/// </summary>
internal sealed class MongoDictationResults(MongoContext ctx) : IDictationResults
{
    public Task AppendAsync(DictationAttempt attempt, CancellationToken ct) =>
        ctx.DictationAttempts.InsertOneAsync(attempt.ToDocument(), cancellationToken: ct);

    public async Task<IReadOnlyList<DictationAttempt>> ListAsync(
        string userId, string setId, CancellationToken ct)
    {
        var docs = await ctx.DictationAttempts
            .Find(a => a.UserId == userId && a.SetId == setId)
            .ToListAsync(ct);

        return [.. docs.Select(d => d.ToDomain())];
    }

    public async Task<IReadOnlyDictionary<string, int>> PerfectCountsAsync(
        string userId, CancellationToken ct)
    {
        /*
         * Distinct sentences, not rows. A learner who gets sentence 3 right
         * on four separate evenings has finished one sentence, and a count
         * of rows would tell the library screen they had finished four —
         * eventually more than the set contains.
         *
         * Projected to (setId, order) and reduced here rather than in an
         * aggregation pipeline: the filter is already indexed, a learner's
         * perfect rows are bounded by the corpus rather than by how long
         * they have used the product, and a pipeline would be harder to read
         * for no measurable gain at this size.
         */
        var docs = await ctx.DictationAttempts
            .Find(a => a.UserId == userId && a.IsPerfect)
            .Project(a => new { a.SetId, a.Order })
            .ToListAsync(ct);

        return docs
            .GroupBy(a => a.SetId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Order).Distinct().Count());
    }
}
