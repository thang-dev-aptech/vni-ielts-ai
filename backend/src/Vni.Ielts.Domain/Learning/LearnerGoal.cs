using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Learning;

/// <summary>
/// The band a learner is aiming for, and (optionally) the day they sit the
/// real test.
///
/// <b>Half-band steps only, and inside the scale IELTS actually reports.</b> A
/// target of 6.3 would be a number no result can ever equal, so every gap
/// computed against it would be off by a fraction that looks like progress or
/// its absence. The floor is 4.0 because every advice rule below that would be
/// "start from the beginning", which is not a coaching plan.
/// </summary>
public sealed record LearnerGoal(
    UserId UserId,
    decimal TargetBand,
    DateOnly? ExamDate,
    DateTimeOffset UpdatedAt)
{
    public const decimal MinTarget = 4.0m;
    public const decimal MaxTarget = 9.0m;

    public static bool IsValidTarget(decimal band) =>
        band >= MinTarget && band <= MaxTarget && (band * 2) % 1 == 0;

    public static LearnerGoal Create(UserId userId, decimal targetBand, DateOnly? examDate, DateTimeOffset now)
    {
        if (!IsValidTarget(targetBand))
            throw new ArgumentOutOfRangeException(
                nameof(targetBand), targetBand, "A target band is 4.0–9.0 in half-band steps.");

        return new LearnerGoal(userId, targetBand, examDate, now);
    }
}

/// <summary>What a learner did on a given day, as far as a streak is concerned.</summary>
public enum ActivityKind
{
    /// <summary>Signed in, or kept a session alive.</summary>
    SignIn,

    /// <summary>Opened a sitting — an exam, a skill, or a single part.</summary>
    Practice,

    /// <summary>Handed a paper in.</summary>
    Submit,
}

/// <summary>One calendar day of activity, in the learner's own time zone.</summary>
public sealed record ActivityDay(DateOnly Date, int Count, IReadOnlyCollection<ActivityKind> Kinds);
