using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Learning;

/// <summary>
/// The deterministic half of coaching: where each skill stands against the
/// target. Everything a learner is told with a number in it comes from here,
/// never from the model — the model only phrases advice around these facts.
/// </summary>
public static class GoalGap
{
    public static readonly IReadOnlyList<ExamModule> Modules =
        [ExamModule.Reading, ExamModule.Listening, ExamModule.Writing, ExamModule.Speaking];

    /// <summary>
    /// <list type="bullet">
    /// <item><c>none</c> — no band on record for this skill.</item>
    /// <item><c>met</c> — at or above target.</item>
    /// <item><c>close</c> — under target by less than a whole band.</item>
    /// <item><c>behind</c> — under target by a band or more.</item>
    /// </list>
    /// A whole band is the threshold because that is the smallest difference
    /// that changes what a learner should do next rather than how much.
    /// </summary>
    public static string StateOf(decimal target, decimal? current)
    {
        if (current is not { } band) return "none";
        var gap = target - band;
        if (gap <= 0) return "met";
        return gap < 1.0m ? "close" : "behind";
    }

    /// <summary>The weakest skills first: largest gap, then the lowest band, then IELTS order.</summary>
    public static IReadOnlyList<string> Focus(decimal target, IReadOnlyDictionary<ExamModule, decimal?> current)
    {
        return Modules
            .Where(m => current.TryGetValue(m, out var band) && band is not null && target - band > 0)
            .OrderByDescending(m => target - current[m]!.Value)
            .ThenBy(m => current[m]!.Value)
            .ThenBy(Modules.IndexOf)
            .Select(m => m.ToString().ToLowerInvariant())
            .ToList();
    }

    private static int IndexOf(this IReadOnlyList<ExamModule> list, ExamModule module)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == module) return i;
        return int.MaxValue;
    }
}
