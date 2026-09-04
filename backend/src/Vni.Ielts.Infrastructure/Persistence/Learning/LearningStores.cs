using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Learning;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Learning;

namespace Vni.Ielts.Infrastructure.Persistence.Learning;

internal sealed class LearnerGoalDocument
{
    /// <summary>The user id. One goal per learner, replaced on every save.</summary>
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("targetBand")]
    public decimal TargetBand { get; set; }

    /// <summary>ISO date, or absent.</summary>
    [BsonElement("examDate")]
    [BsonIgnoreIfNull]
    public string? ExamDate { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// One row per (learner, day). <c>_id</c> is <c>{userId}:{yyyy-MM-dd}</c> so
/// the write is an upsert with no read before it and no duplicate under a
/// race.
/// </summary>
internal sealed class LearnerActivityDayDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("day")]
    public string Day { get; set; } = string.Empty;

    [BsonElement("count")]
    public int Count { get; set; }

    [BsonElement("kinds")]
    public List<string> Kinds { get; set; } = [];

    [BsonElement("lastAt")]
    public DateTime LastAt { get; set; }
}

internal sealed class CoachingAdviceDocument
{
    /// <summary>Hash of the standing (target + four bands); see <c>GetCoaching.CacheKey</c>.</summary>
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("summary")]
    public string Summary { get; set; } = string.Empty;

    [BsonElement("tips")]
    public List<CoachingTipDocument> Tips { get; set; } = [];

    [BsonElement("provider")]
    public string Provider { get; set; } = string.Empty;

    [BsonElement("model")]
    public string Model { get; set; } = string.Empty;

    [BsonElement("promptVersion")]
    public string PromptVersion { get; set; } = string.Empty;

    /// <summary>TTL index expires the row; advice for one standing is not forever.</summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
}

internal sealed class CoachingTipDocument
{
    [BsonElement("module")]
    public string Module { get; set; } = string.Empty;

    [BsonElement("text")]
    public string Text { get; set; } = string.Empty;
}

internal sealed class MongoLearnerGoalStore(MongoContext ctx) : ILearnerGoalStore
{
    public async Task<LearnerGoal?> GetAsync(UserId userId, CancellationToken ct)
    {
        var doc = await ctx.LearnerGoals.Find(g => g.Id == userId.Value).FirstOrDefaultAsync(ct);
        if (doc is null) return null;

        return new LearnerGoal(
            userId,
            doc.TargetBand,
            doc.ExamDate is null ? null : DateOnly.Parse(doc.ExamDate),
            new DateTimeOffset(DateTime.SpecifyKind(doc.UpdatedAt, DateTimeKind.Utc)));
    }

    public Task SaveAsync(LearnerGoal goal, CancellationToken ct) =>
        ctx.LearnerGoals.ReplaceOneAsync(
            g => g.Id == goal.UserId.Value,
            new LearnerGoalDocument
            {
                Id = goal.UserId.Value,
                TargetBand = goal.TargetBand,
                ExamDate = goal.ExamDate?.ToString("yyyy-MM-dd"),
                UpdatedAt = goal.UpdatedAt.UtcDateTime,
            },
            new ReplaceOptions { IsUpsert = true },
            ct);
}

internal sealed class MongoLearnerActivityLog(MongoContext ctx, IClock clock) : ILearnerActivityLog
{
    public Task RecordAsync(UserId userId, DateOnly day, ActivityKind kind, CancellationToken ct)
    {
        var dayText = day.ToString("yyyy-MM-dd");
        var update = Builders<LearnerActivityDayDocument>.Update
            .SetOnInsert(d => d.UserId, userId.Value)
            .SetOnInsert(d => d.Day, dayText)
            .Inc(d => d.Count, 1)
            .AddToSet(d => d.Kinds, kind.ToString().ToLowerInvariant())
            .Set(d => d.LastAt, clock.UtcNow.UtcDateTime);

        return ctx.LearnerActivityDays.UpdateOneAsync(
            d => d.Id == $"{userId.Value}:{dayText}",
            update,
            new UpdateOptions { IsUpsert = true },
            ct);
    }

    public async Task<IReadOnlyList<ActivityDay>> ListAsync(
        UserId userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var lo = from.ToString("yyyy-MM-dd");
        var hi = to.ToString("yyyy-MM-dd");
        var docs = await ctx.LearnerActivityDays
            .Find(d => d.UserId == userId.Value && string.Compare(d.Day, lo) >= 0 && string.Compare(d.Day, hi) <= 0)
            .ToListAsync(ct);

        return docs.Select(d => new ActivityDay(
                DateOnly.Parse(d.Day),
                d.Count,
                d.Kinds.Select(k => Enum.Parse<ActivityKind>(k, ignoreCase: true)).ToList()))
            .ToList();
    }
}

internal sealed class MongoCoachingAdviceCache(MongoContext ctx, IClock clock) : ICoachingAdviceCache
{
    public async Task<CoachingAdvice?> GetAsync(string key, CancellationToken ct)
    {
        var doc = await ctx.CoachingAdvice.Find(a => a.Id == key).FirstOrDefaultAsync(ct);
        return doc is null
            ? null
            : new CoachingAdvice(
                doc.Summary,
                doc.Tips.Select(t => new CoachingTip(t.Module, t.Text)).ToList(),
                doc.Provider, doc.Model, doc.PromptVersion);
    }

    public Task SetAsync(string key, CoachingAdvice advice, CancellationToken ct) =>
        ctx.CoachingAdvice.ReplaceOneAsync(
            a => a.Id == key,
            new CoachingAdviceDocument
            {
                Id = key,
                Summary = advice.Summary,
                Tips = advice.Tips.Select(t => new CoachingTipDocument { Module = t.Module, Text = t.Text }).ToList(),
                Provider = advice.Provider,
                Model = advice.Model,
                PromptVersion = advice.PromptVersion,
                CreatedAt = clock.UtcNow.UtcDateTime,
            },
            new ReplaceOptions { IsUpsert = true },
            ct);
}
