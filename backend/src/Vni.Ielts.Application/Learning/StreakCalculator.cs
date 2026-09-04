namespace Vni.Ielts.Application.Learning;

/// <summary>
/// Consecutive active days, counted the way a learner would count them.
///
/// <b>Today does not break the streak until it is over.</b> A learner who was
/// active every day this week and opens the app at breakfast has a streak of
/// seven, not zero; it becomes zero only if the day ends without them. So the
/// current streak is counted back from today when today is active, and from
/// yesterday when it is not.
/// </summary>
public static class StreakCalculator
{
    /// <summary>Three days is where the flame lights. A product choice, and a small one.</summary>
    public const int FlameThreshold = 3;

    public static (int Current, int Longest, bool ActiveToday) Compute(
        IReadOnlySet<DateOnly> activeDays, DateOnly today)
    {
        var activeToday = activeDays.Contains(today);

        var current = 0;
        var cursor = activeToday ? today : today.AddDays(-1);
        while (activeDays.Contains(cursor))
        {
            current++;
            cursor = cursor.AddDays(-1);
        }

        var longest = 0;
        foreach (var day in activeDays)
        {
            // A run starts on a day whose predecessor is not active.
            if (activeDays.Contains(day.AddDays(-1))) continue;
            var length = 0;
            var d = day;
            while (activeDays.Contains(d)) { length++; d = d.AddDays(1); }
            if (length > longest) longest = length;
        }

        return (current, Math.Max(longest, current), activeToday);
    }
}
