using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Assessment;

namespace Vni.Ielts.Infrastructure.Tests.Ai.Writing;

public sealed class WritingSectionEvaluatorConfigurationTests
{
    [Fact]
    public void IsConfigured_is_false_when_marking_disabled()
    {
        var assessment = new AssessmentOptions
        {
            Writing = new RubricOptions { Version = "v1", DescriptorSource = "fixture" },
            WritingMarking = new WritingMarkingOptions { Enabled = false, PrimaryProvider = "OpenAi" },
        };

        var ai = new AiOptions
        {
            AllowCrossBorderTransfer = true,
            OpenAi = new AiProviderOptions
            {
                ApiKey = "key",
                Model = "gpt-test",
                SyntheticDataOnly = false,
            },
        };

        Assert.False(WritingSectionEvaluator.IsConfiguredFor(assessment, ai));
    }

    [Fact]
    public void IsConfigured_is_false_when_cross_border_not_permitted()
    {
        var assessment = EnabledAssessment("OpenAi");
        var ai = new AiOptions
        {
            AllowCrossBorderTransfer = false,
            OpenAi = ConfiguredProvider(),
        };

        Assert.False(WritingSectionEvaluator.IsConfiguredFor(assessment, ai));
    }

    [Fact]
    public void IsConfigured_is_true_when_all_gates_clear()
    {
        var assessment = EnabledAssessment("OpenAi");
        var ai = new AiOptions
        {
            AllowCrossBorderTransfer = true,
            OpenAi = ConfiguredProvider(),
        };

        Assert.True(WritingSectionEvaluator.IsConfiguredFor(assessment, ai));
    }

    [Fact]
    public void IsConfigured_is_false_for_synthetic_only_reseller_test_profile()
    {
        var assessment = EnabledAssessment("OpenAi");
        var ai = new AiOptions
        {
            AllowCrossBorderTransfer = false,
            OpenAi = new AiProviderOptions
            {
                ApiKey = "key",
                BaseUrl = "https://reseller.example/v1",
                Model = "gpt-test",
                SyntheticDataOnly = true,
            },
        };

        Assert.False(WritingSectionEvaluator.IsConfiguredFor(assessment, ai));
    }

    [Fact]
    public void IsConfigured_is_false_for_synthetic_only_official_provider()
    {
        var assessment = EnabledAssessment("OpenAi");
        var ai = new AiOptions
        {
            AllowCrossBorderTransfer = true,
            OpenAi = new AiProviderOptions
            {
                ApiKey = "key",
                Model = "gpt-test",
                SyntheticDataOnly = true,
            },
        };

        Assert.False(WritingSectionEvaluator.IsConfiguredFor(assessment, ai));
    }

    private static AssessmentOptions EnabledAssessment(string provider) => new()
    {
        Writing = new RubricOptions { Version = "v1", DescriptorSource = "fixture" },
        WritingMarking = new WritingMarkingOptions
        {
            Enabled = true,
            PrimaryProvider = provider,
            PromptVersion = "writing-eval-prompt-v1",
        },
    };

    private static AiProviderOptions ConfiguredProvider() => new()
    {
        ApiKey = "key",
        Model = "gpt-test",
        SyntheticDataOnly = false,
    };
}
