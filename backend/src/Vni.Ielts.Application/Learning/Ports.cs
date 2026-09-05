using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Learning;

namespace Vni.Ielts.Application.Learning;

public interface ILearnerGoalStore
{
    Task<LearnerGoal?> GetAsync(UserId userId, CancellationToken ct);
    Task SaveAsync(LearnerGoal goal, CancellationToken ct);
}

/// <summary>
/// The per-day activity ledger behind the streak.
///
/// <b>Days, not events.</b> One upsert per (learner, day, kind) is all the
/// heatmap needs, and it is what keeps a learner who refreshes a token every
/// fifteen minutes from writing ninety-six rows a day.
/// </summary>
public interface ILearnerActivityLog
{
    Task RecordAsync(UserId userId, DateOnly day, ActivityKind kind, CancellationToken ct);
    Task<IReadOnlyList<ActivityDay>> ListAsync(UserId userId, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>
/// Which calendar day "now" falls on for a learner.
///
/// <b>A seam, because the answer is a business choice.</b> Every learner is in
/// Vietnam today, so the default is Asia/Ho_Chi_Minh; a per-learner zone is a
/// column and a setting away and changes nothing here.
/// </summary>
public interface ILearnerCalendar
{
    string TimeZoneId { get; }
    DateOnly DayOf(DateTimeOffset instant);
}

/// <summary>The AI half of coaching — advisory text over facts the application computed.</summary>
public interface ICoachingAdvisor
{
    bool IsConfigured { get; }
    Task<CoachingAdviceResult> AdviseAsync(CoachingFacts facts, CancellationToken ct);
}

public interface ICoachingAdviceCache
{
    Task<CoachingAdvice?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, CoachingAdvice advice, CancellationToken ct);
}

/// <summary>
/// Numbers only. The advisor never sees a name, an email, an essay or a
/// transcript — a target band and four current bands are the whole prompt.
/// </summary>
public sealed record CoachingFacts(
    decimal TargetBand,
    IReadOnlyList<CoachingSkillFact> Skills);

/// <param name="Detail">Per-task bands for Writing/Speaking when no module band exists — still only numbers.</param>
public sealed record CoachingSkillFact(string Module, decimal? CurrentBand, decimal? Gap, string? Detail = null);

public sealed record CoachingAdvice(
    string Summary,
    IReadOnlyList<CoachingTip> Tips,
    string Provider,
    string Model,
    string PromptVersion);

public sealed record CoachingTip(string Module, string Text);

public sealed record CoachingAdviceResult(bool Succeeded, CoachingAdvice? Advice, string? FailureCode)
{
    public static CoachingAdviceResult Failed(string code) => new(false, null, code);
    public static CoachingAdviceResult Ok(CoachingAdvice advice) => new(true, advice, null);
}
