using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Infrastructure.Assessment;

namespace Vni.Ielts.Infrastructure.Tests.Assessment;

/// <summary>
/// S4 / `P-12` — the DI-side reading of <c>Assessment:Writing:TaskWeights</c>.
/// The startup gate reports a bad pair first; this is the backstop that keeps a
/// bad pair from ever becoming a policy.
/// </summary>
public sealed class WritingTaskWeightOptionsTests
{
    [Fact]
    public void Nothing_configured_is_the_unconfigured_policy_not_a_default()
    {
        Assert.Same(WritingTaskWeightPolicy.Unconfigured, WritingTaskWeightOptions.ToPolicy(null));
        Assert.Same(
            WritingTaskWeightPolicy.Unconfigured,
            WritingTaskWeightOptions.ToPolicy(new WritingTaskWeightOptions()));

        Assert.Null(WritingTaskWeightPolicy.Unconfigured.Configured);
    }

    [Fact]
    public void A_configured_pair_becomes_the_configured_policy_as_written()
    {
        var policy = WritingTaskWeightOptions.ToPolicy(
            new WritingTaskWeightOptions { Task1 = 1m, Task2 = 2m });

        Assert.Equal(new WritingTaskWeights(1m, 2m), policy.Configured);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, -1)]
    [InlineData(null, 2)]
    [InlineData(1, null)]
    public void A_bad_pair_is_named_and_never_becomes_a_policy(int? task1, int? task2)
    {
        var options = new WritingTaskWeightOptions { Task1 = task1, Task2 = task2 };

        Assert.NotNull(options.Problem());
        Assert.Contains("Assessment:Writing:TaskWeights", options.Problem());
        Assert.Throws<InvalidOperationException>(() => WritingTaskWeightOptions.ToPolicy(options));
    }
}
