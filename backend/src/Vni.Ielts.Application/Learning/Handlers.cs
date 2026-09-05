using System.Security.Cryptography;
using System.Text;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Learning;

namespace Vni.Ielts.Application.Learning;

// ── Views ────────────────────────────────────────────────────────────────────

public sealed record LearnerGoalView(decimal TargetBand, DateOnly? ExamDate, DateTimeOffset UpdatedAt);

/// <param name="State"><c>none</c> · <c>met</c> · <c>close</c> · <c>behind</c>. → <see cref="GoalGap.StateOf"/></param>
/// <param name="SessionId">The sitting the current band came from, so "4.0" is one click from its paper.</param>
/// <param name="Detail">
/// For Writing and Speaking, the per-task bands of the latest marked sitting
/// ("Task 1 6.0 · Task 2 6.5") when no module band exists. A module band for
/// Writing needs a Task 1 : Task 2 weighting the exam version carries (`H-8b`);
/// without it the two task bands are reported as what they are and the gap
/// stays null rather than being computed on an average nobody published.
/// </param>
public sealed record CoachingSkillView(
    string Module, decimal? CurrentBand, decimal? Gap, string State, string? SessionId, DateTimeOffset? MeasuredAt,
    string? Detail = null);

/// <param name="Status">
/// <c>ready</c> — validated advice below · <c>unavailable</c> — the advisor
/// refused, failed or is not wired · <c>no-goal</c> / <c>no-data</c> — nothing
/// to advise on yet. The deterministic facts are present in every case.
/// </param>
public sealed record CoachingAiView(string Status, string? Summary, IReadOnlyList<CoachingTip> Tips, string? Model);

public sealed record CoachingView(
    LearnerGoalView? Goal,
    IReadOnlyList<CoachingSkillView> Skills,
    /// <summary>Weakest first. Empty when no skill has a band under the target.</summary>
    IReadOnlyList<string> Focus,
    CoachingAiView Ai);

public sealed record ActivityDayView(string Date, int Count, IReadOnlyList<string> Kinds);

public sealed record ActivityView(
    string TimeZone,
    string Today,
    IReadOnlyList<ActivityDayView> Days,
    int CurrentStreak,
    int LongestStreak,
    bool ActiveToday,
    /// <summary>True once the streak reaches <see cref="StreakCalculator.FlameThreshold"/>.</summary>
    bool Flame,
    int FlameThreshold);

// ── Goal ─────────────────────────────────────────────────────────────────────

public sealed record GetLearnerGoalQuery(UserId UserId);

public sealed class GetLearnerGoal(ILearnerGoalStore goals)
{
    public async Task<LearnerGoalView?> HandleAsync(GetLearnerGoalQuery query, CancellationToken ct)
    {
        var goal = await goals.GetAsync(query.UserId, ct);
        return goal is null ? null : new LearnerGoalView(goal.TargetBand, goal.ExamDate, goal.UpdatedAt);
    }
}

public sealed record SetLearnerGoalCommand(UserId UserId, decimal TargetBand, DateOnly? ExamDate);

public sealed class SetLearnerGoal(ILearnerGoalStore goals, IClock clock)
{
    public async Task<Result<LearnerGoalView>> HandleAsync(SetLearnerGoalCommand command, CancellationToken ct)
    {
        if (!LearnerGoal.IsValidTarget(command.TargetBand))
            return Error.Validation(
                "GOAL_TARGET_INVALID",
                "Mục tiêu là một band từ 4.0 đến 9.0, theo bước 0.5.");

        var goal = LearnerGoal.Create(command.UserId, command.TargetBand, command.ExamDate, clock.UtcNow);
        await goals.SaveAsync(goal, ct);
        return new LearnerGoalView(goal.TargetBand, goal.ExamDate, goal.UpdatedAt);
    }
}

// ── Coaching ─────────────────────────────────────────────────────────────────

/// <param name="IncludeAdvice">
/// False returns the facts alone, with <c>ai.status = pending</c> where advice
/// would apply; the advice is a second, slower call. A dashboard should not
/// wait fifteen seconds of model latency to say which skill is weakest.
/// </param>
public sealed record GetCoachingQuery(UserId UserId, bool IncludeAdvice = false);

/// <summary>
/// Where the learner stands against their goal, and what to do about it.
///
/// <b>The facts are computed here and the model is only asked to phrase
/// them.</b> Current band per skill is the most recent sitting that produced
/// one — the same list the dashboard's "recent sittings" shows, so the two can
/// never disagree. The model receives the target and four numbers, returns
/// text, and that text is validated and cached per set of numbers: the same
/// standing gets the same advice, and a refresh costs nothing.
/// </summary>
public sealed class GetCoaching(
    ILearnerGoalStore goals,
    ListMySittings sittings,
    ISectionMarkingStore markings,
    ICoachingAdvisor advisor,
    ICoachingAdviceCache cache)
{
    public async Task<CoachingView> HandleAsync(GetCoachingQuery query, CancellationToken ct)
    {
        var goal = await goals.GetAsync(query.UserId, ct);
        var recent = await sittings.HandleAsync(new ListMySittingsQuery(query.UserId, ListMySittings.MaxLimit), ct);

        // Newest first, so the first band seen per module is the current one.
        var latest = new Dictionary<ExamModule, (decimal Band, string SessionId, DateTimeOffset At)>();
        foreach (var sitting in recent)
        {
            foreach (var section in sitting.Sections)
            {
                if (section.Band is not { } band) continue;
                if (!Enum.TryParse<ExamModule>(section.Module, ignoreCase: true, out var module)) continue;
                if (latest.ContainsKey(module)) continue;
                latest[module] = (band, sitting.SessionId, sitting.SubmittedAt ?? sitting.StartedAt);
            }
        }

        // Writing and Speaking bands live in markings, not section results.
        // The latest sitting with any marking for the module supplies the
        // per-task detail; a module band is used only when the version could
        // produce one (it appears in `Sections` like the others when it can).
        var detail = new Dictionary<ExamModule, (string Text, string SessionId, DateTimeOffset At)>();
        foreach (var sitting in recent)
        {
            if (detail.ContainsKey(ExamModule.Writing) && detail.ContainsKey(ExamModule.Speaking)) break;
            if (!sitting.Sections.Any(s => s.Module is "writing" or "speaking")) continue;

            var marked = await markings.ListAsync(new Domain.Sessions.ExamSessionId(sitting.SessionId), ct);
            foreach (var group in marked.GroupBy(m => m.Module))
            {
                if (detail.ContainsKey(group.Key) || latest.ContainsKey(group.Key)) continue;
                var text = string.Join(" · ", group
                    .OrderBy(m => m.TaskNumber)
                    .Select(m => m.TaskNumber is { } n ? $"Task {n} {m.Band.Value:0.0}" : $"{m.Band.Value:0.0}"));
                detail[group.Key] = (text, sitting.SessionId, sitting.SubmittedAt ?? sitting.StartedAt);
            }
        }

        var target = goal?.TargetBand;
        var skills = GoalGap.Modules.Select(m =>
        {
            var has = latest.TryGetValue(m, out var found);
            var hasDetail = detail.TryGetValue(m, out var d) && !has;
            decimal? current = has ? found.Band : null;
            decimal? gap = has && target is { } t ? t - found.Band : null;
            return new CoachingSkillView(
                m.ToString().ToLowerInvariant(),
                current,
                gap,
                target is { } tt ? GoalGap.StateOf(tt, current) : "none",
                has ? found.SessionId : hasDetail ? d.SessionId : null,
                has ? found.At : hasDetail ? d.At : null,
                hasDetail ? d.Text : null);
        }).ToList();

        var focus = target is { } tg
            ? GoalGap.Focus(tg, GoalGap.Modules.ToDictionary(
                m => m, m => latest.TryGetValue(m, out var f) ? f.Band : (decimal?)null))
            : [];

        var goalView = goal is null ? null : new LearnerGoalView(goal.TargetBand, goal.ExamDate, goal.UpdatedAt);
        var ai = query.IncludeAdvice
            ? await AdviseAsync(target, skills, ct)
            : new CoachingAiView(AdviceApplies(target, skills) ? "pending" : NoAdviceStatus(target, skills), null, [], null);

        return new CoachingView(goalView, skills, focus, ai);
    }

    private static bool AdviceApplies(decimal? target, IReadOnlyList<CoachingSkillView> skills) =>
        target is not null && skills.Any(s => s.CurrentBand is not null || s.Detail is not null);

    private static string NoAdviceStatus(decimal? target, IReadOnlyList<CoachingSkillView> skills) =>
        target is null ? "no-goal" : "no-data";

    private async Task<CoachingAiView> AdviseAsync(
        decimal? target, IReadOnlyList<CoachingSkillView> skills, CancellationToken ct)
    {
        if (target is not { } t) return new CoachingAiView("no-goal", null, [], null);
        if (skills.All(s => s.CurrentBand is null && s.Detail is null)) return new CoachingAiView("no-data", null, [], null);
        if (!advisor.IsConfigured) return new CoachingAiView("unavailable", null, [], null);

        var facts = new CoachingFacts(
            t, skills.Select(s => new CoachingSkillFact(s.Module, s.CurrentBand, s.Gap, s.Detail)).ToList());

        var key = CacheKey(facts);
        if (await cache.GetAsync(key, ct) is { } cached)
            return new CoachingAiView("ready", cached.Summary, cached.Tips, cached.Model);

        var result = await advisor.AdviseAsync(facts, ct);
        if (!result.Succeeded || result.Advice is null)
            return new CoachingAiView("unavailable", null, [], null);

        await cache.SetAsync(key, result.Advice, ct);
        return new CoachingAiView("ready", result.Advice.Summary, result.Advice.Tips, result.Advice.Model);
    }

    /// <summary>The standing, not the learner: two learners with the same numbers share one cache row.</summary>
    internal static string CacheKey(CoachingFacts facts)
    {
        var text = facts.TargetBand.ToString("0.0") + "|" + string.Join(
            "|", facts.Skills.Select(s => $"{s.Module}={s.CurrentBand?.ToString("0.0") ?? s.Detail ?? "-"}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}

// ── Activity ─────────────────────────────────────────────────────────────────

/// <summary>
/// Records "this learner was here today". Called from the edges that know it
/// — sign-in, token refresh, opening and submitting a paper — and never allowed
/// to fail the request that called it: a ledger hiccup is not a reason to
/// refuse a login.
/// </summary>
public sealed class LearnerPresence(ILearnerActivityLog log, ILearnerCalendar calendar, IClock clock)
{
    public async Task TouchAsync(UserId userId, ActivityKind kind, CancellationToken ct)
    {
        try
        {
            await log.RecordAsync(userId, calendar.DayOf(clock.UtcNow), kind, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Swallowed by design; see the class remarks. The caller's own
            // operation has already succeeded or failed on its own terms.
        }
    }
}

public sealed record GetLearnerActivityQuery(UserId UserId, int Days);

/// <summary>
/// The heatmap and the streak.
///
/// <b>Sittings count even if they predate the ledger.</b> The activity log
/// starts on the day this shipped; a learner's earlier papers are still in
/// <c>exam_sessions</c>, so they are folded in here. That is what makes the
/// first heatmap a learner sees an honest one rather than an empty one.
/// </summary>
public sealed class GetLearnerActivity(
    ILearnerActivityLog log,
    IExamSessionRepository sessions,
    ILearnerCalendar calendar,
    IClock clock)
{
    public const int MaxDays = 371; // 53 weeks, the GitHub grid.

    public async Task<ActivityView> HandleAsync(GetLearnerActivityQuery query, CancellationToken ct)
    {
        var today = calendar.DayOf(clock.UtcNow);
        var days = Math.Clamp(query.Days, 7, MaxDays);
        var from = today.AddDays(-(days - 1));

        var byDay = new Dictionary<DateOnly, (int Count, HashSet<ActivityKind> Kinds)>();
        void Add(DateOnly day, int count, IEnumerable<ActivityKind> kinds)
        {
            if (day < from || day > today) return;
            if (!byDay.TryGetValue(day, out var entry)) entry = (0, []);
            entry.Count += count;
            foreach (var k in kinds) entry.Kinds.Add(k);
            byDay[day] = entry;
        }

        foreach (var day in await log.ListAsync(query.UserId, from, today, ct))
            Add(day.Date, day.Count, day.Kinds);

        foreach (var sitting in await sessions.ListForUserAsync(query.UserId, 500, ct))
        {
            Add(calendar.DayOf(sitting.StartedAt), 1, [ActivityKind.Practice]);
            if (sitting.SubmittedAt is { } submitted) Add(calendar.DayOf(submitted), 1, [ActivityKind.Submit]);
        }

        var active = byDay.Keys.ToHashSet();
        var (current, longest, activeToday) = StreakCalculator.Compute(active, today);

        var list = byDay
            .OrderBy(kv => kv.Key)
            .Select(kv => new ActivityDayView(
                kv.Key.ToString("yyyy-MM-dd"),
                kv.Value.Count,
                kv.Value.Kinds.Select(k => k.ToString().ToLowerInvariant()).OrderBy(k => k).ToList()))
            .ToList();

        return new ActivityView(
            calendar.TimeZoneId,
            today.ToString("yyyy-MM-dd"),
            list,
            current,
            longest,
            activeToday,
            current >= StreakCalculator.FlameThreshold,
            StreakCalculator.FlameThreshold);
    }
}
