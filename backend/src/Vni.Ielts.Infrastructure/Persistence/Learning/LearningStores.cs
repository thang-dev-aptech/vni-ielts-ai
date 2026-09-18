using System.Globalization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Learning;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Learning;

namespace Vni.Ielts.Infrastructure.Persistence.Learning;

/// <summary>
/// The one spelling of a day in this collection, written and read the same way.
///
/// <b>A stored date is a contract, not a display string.</b> <c>learner_goals.examDate</c>
/// and <c>learner_activity_days.day</c> hold <c>yyyy-MM-dd</c> text, and that text is
/// also a <i>key</i>: the activity range query compares <c>day</c> with
/// <c>string.Compare</c>, and the upsert addresses the row by
/// <c>{userId}:{day}</c>. So the format is load-bearing twice over and must not
/// depend on the machine.
///
/// <b>The pattern alone is not enough.</b> <c>"yyyy-MM-dd"</c> pins the separator but
/// not the <i>calendar</i>, which is cultural too. Measured on .NET 10:
///
/// <list type="bullet">
/// <item><c>th-TH</c> — <c>2026-09-18</c> is written <c>2569-09-18</c> (Buddhist era)
/// and <c>DateOnly.Parse("2026-09-18")</c> reads back <c>1483-09-18</c>. A learner's
/// exam date silently becomes a different date, and their activity rows sort into a
/// range nothing queries.</item>
/// <item><c>ar-SA</c> — <c>DateOnly.Parse("2026-09-18")</c> throws
/// <see cref="FormatException"/> against the Umm al-Qura calendar, so reading a goal
/// that was written on a Gregorian host is a 500.</item>
/// </list>
///
/// <see cref="ParseExact(string)"/> rather than <c>Parse</c> on the way in: the stored
/// shape is known exactly, so anything else is corrupt data and should say so here
/// rather than become a plausible wrong date.
/// </summary>
internal static class LearningDays
{
    internal const string Format = "yyyy-MM-dd";

    internal static string ToText(DateOnly day) => day.ToString(Format, CultureInfo.InvariantCulture);

    internal static DateOnly Parse(string text) =>
        DateOnly.ParseExact(text, Format, CultureInfo.InvariantCulture);
}

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
            doc.ExamDate is null ? null : LearningDays.Parse(doc.ExamDate),
            new DateTimeOffset(DateTime.SpecifyKind(doc.UpdatedAt, DateTimeKind.Utc)));
    }

    public Task SaveAsync(LearnerGoal goal, CancellationToken ct) =>
        ctx.LearnerGoals.ReplaceOneAsync(
            g => g.Id == goal.UserId.Value,
            new LearnerGoalDocument
            {
                Id = goal.UserId.Value,
                TargetBand = goal.TargetBand,
                ExamDate = goal.ExamDate is { } examDate ? LearningDays.ToText(examDate) : null,
                UpdatedAt = goal.UpdatedAt.UtcDateTime,
            },
            new ReplaceOptions { IsUpsert = true },
            ct);
}

internal sealed class MongoLearnerActivityLog(MongoContext ctx, IClock clock) : ILearnerActivityLog
{
    public Task RecordAsync(UserId userId, DateOnly day, ActivityKind kind, CancellationToken ct)
    {
        var dayText = LearningDays.ToText(day);
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
        var lo = LearningDays.ToText(from);
        var hi = LearningDays.ToText(to);
        var docs = await ctx.LearnerActivityDays
            .Find(d => d.UserId == userId.Value && string.Compare(d.Day, lo) >= 0 && string.Compare(d.Day, hi) <= 0)
            .ToListAsync(ct);

        return docs.Select(d => new ActivityDay(
                LearningDays.Parse(d.Day),
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
