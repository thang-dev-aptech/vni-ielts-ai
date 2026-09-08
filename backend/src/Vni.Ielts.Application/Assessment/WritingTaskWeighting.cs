using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Assessment;

/// <summary>
/// The Task 1 : Task 2 ratio a Writing band is combined on.
///
/// <b>A pair of positive weights, and nothing else can be one.</b> The
/// constructor refuses a zero or negative half so a ratio read from
/// configuration cannot reach <see cref="BandScore.Weighted"/> as a division
/// by nothing. The numbers are carried, never chosen: `P-12` (owner decision
/// 06/09/2026) settled 1 : 2, and that value lives in configuration and in
/// the exam version's <see cref="ScoringProfile"/> — not here. → `G-11`
/// </summary>
public sealed record WritingTaskWeights
{
    public WritingTaskWeights(decimal task1, decimal task2)
    {
        if (task1 <= 0m || task2 <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(task1), $"{task1}:{task2}",
                "Writing task weights must both be strictly positive.");

        Task1 = task1;
        Task2 = task2;
    }

    public decimal Task1 { get; }
    public decimal Task2 { get; }
}

/// <summary>
/// Where the Writing task weighting comes from, in order.
///
/// <b>The exam version wins; the deployment's configuration is the fallback;
/// none is a supported answer.</b> An exam version that carries its own ratio
/// is content that has already recorded a decision, and a deployment-wide
/// number must not overrule it. A version that carries none used to leave
/// Writing with no band at all (`H-8b` was open); since `P-12` the deployment
/// may supply the ratio instead, from `Assessment:Writing:TaskWeights`. When
/// neither exists, Writing has no combined band and the results screen says
/// why — it never averages, and it never picks a number. → `G-11`
///
/// An interface rather than a static so <c>ToResults</c> can be handed the
/// deployment's answer without Application learning how configuration is
/// bound.
/// </summary>
public interface IWritingTaskWeighting
{
    /// <summary>The deployment-wide ratio, or null when none is configured.</summary>
    WritingTaskWeights? Configured { get; }

    /// <summary>
    /// The ratio in force for this exam version, or null when there is none.
    ///
    /// <b>A version that declares a ratio is validated, not trusted.</b> Both
    /// halves declared goes through
    /// <see cref="ScoringProfile.RequireWritingTaskWeights"/>, which throws
    /// on a non-positive half. Falling through to configuration there would
    /// mark the learner on a ratio the exam's author did not declare, which
    /// is a worse outcome than a loud fault over corrupt content.
    /// </summary>
    WritingTaskWeights? Resolve(ScoringProfile scoring);

    /// <summary>
    /// The ratio in force, or a refusal — for a caller that must have one.
    /// Same semantics as <see cref="ScoringProfile.RequireWritingTaskWeights"/>,
    /// widened to consult configuration before refusing.
    /// </summary>
    WritingTaskWeights Require(ScoringProfile scoring);
}

/// <inheritdoc cref="IWritingTaskWeighting"/>
public sealed class WritingTaskWeightPolicy(WritingTaskWeights? configured = null) : IWritingTaskWeighting
{
    /// <summary>
    /// The null implementation: only what the exam version declares counts.
    /// This is what an install with no `Assessment:Writing:TaskWeights` runs
    /// on, and what <c>ToResults</c> assumes when handed nothing.
    /// </summary>
    public static readonly WritingTaskWeightPolicy Unconfigured = new();

    public WritingTaskWeights? Configured { get; } = configured;

    public WritingTaskWeights? Resolve(ScoringProfile scoring)
    {
        if (scoring.WritingTask1Weight is not null || scoring.WritingTask2Weight is not null)
        {
            // Declared, so it must be whole and positive. A half-stated or
            // non-positive ratio is refused here exactly as `BandFor` refuses
            // a raw score outside its table — see the domain method.
            var (task1, task2) = scoring.RequireWritingTaskWeights();
            return new WritingTaskWeights(task1, task2);
        }

        return Configured;
    }

    public WritingTaskWeights Require(ScoringProfile scoring) =>
        Resolve(scoring)
        ?? throw new InvalidOperationException(
            "This exam version declares no Writing task weighting and none is configured under "
            + "Assessment:Writing:TaskWeights. Set scoring.criterionWeights.writing on the exam "
            + "version, or configure the deployment-wide ratio. Refusing to invent a weighting.");
}
