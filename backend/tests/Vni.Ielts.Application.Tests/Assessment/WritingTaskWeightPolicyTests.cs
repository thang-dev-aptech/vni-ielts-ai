using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Assessment;

/// <summary>
/// S4 / `P-12` — where the Writing Task 1 : Task 2 ratio comes from.
///
/// Three sources in a fixed order: the exam version, then the deployment's
/// configuration, then nothing. The order is the point of the type — a
/// deployment-wide number must not overrule a ratio the exam's author
/// recorded, and "nothing" has to stay a legal answer so an install with no
/// configuration reports no band rather than a guessed one. → `G-11`
/// </summary>
public sealed class WritingTaskWeightPolicyTests
{
    private static readonly WritingTaskWeights Configured = new(1m, 2m);

    private static ScoringProfile Profile(decimal? task1 = null, decimal? task2 = null) =>
        new(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default, task1, task2);

    [Fact]
    public void The_exam_versions_own_ratio_wins_over_the_configured_one()
    {
        var policy = new WritingTaskWeightPolicy(Configured);

        var resolved = policy.Resolve(Profile(1m, 1m));

        Assert.Equal(new WritingTaskWeights(1m, 1m), resolved);
        Assert.NotEqual(Configured, resolved);
    }

    [Fact]
    public void A_version_with_no_ratio_falls_back_to_the_configured_one()
    {
        var policy = new WritingTaskWeightPolicy(Configured);

        Assert.Equal(Configured, policy.Resolve(Profile()));
    }

    [Fact]
    public void No_ratio_anywhere_resolves_to_none_and_require_refuses()
    {
        var policy = WritingTaskWeightPolicy.Unconfigured;

        Assert.Null(policy.Configured);
        Assert.Null(policy.Resolve(Profile()));

        var ex = Assert.Throws<InvalidOperationException>(() => policy.Require(Profile()));
        Assert.Contains("Refusing to invent a weighting", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Require_returns_the_same_answer_resolve_does_when_there_is_one()
    {
        var policy = new WritingTaskWeightPolicy(Configured);

        Assert.Equal(policy.Resolve(Profile()), policy.Require(Profile()));
        Assert.Equal(policy.Resolve(Profile(2m, 3m)), policy.Require(Profile(2m, 3m)));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, -1)]
    public void A_version_declaring_a_non_positive_ratio_is_refused_rather_than_overridden(
        int task1, int task2)
    {
        // Falling through to configuration here would mark the learner on a
        // ratio the exam's author never declared. Corrupt content is a loud
        // fault, and the domain method already says so.
        var policy = new WritingTaskWeightPolicy(Configured);

        Assert.Throws<InvalidOperationException>(() => policy.Resolve(Profile(task1, task2)));
    }

    [Fact]
    public void A_version_declaring_half_a_ratio_is_refused_rather_than_completed_from_configuration()
    {
        var policy = new WritingTaskWeightPolicy(Configured);

        Assert.Throws<InvalidOperationException>(() => policy.Resolve(Profile(task1: 1m)));
        Assert.Throws<InvalidOperationException>(() => policy.Resolve(Profile(task2: 2m)));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(-1, 2)]
    public void Weights_cannot_be_constructed_non_positive(int task1, int task2)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WritingTaskWeights(task1, task2));
    }
}
