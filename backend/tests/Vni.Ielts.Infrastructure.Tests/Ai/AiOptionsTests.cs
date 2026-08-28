using Microsoft.Extensions.Configuration;
using Vni.Ielts.Infrastructure.Ai;

namespace Vni.Ielts.Infrastructure.Tests.Ai;

/// <summary>
/// The two properties of AI configuration that are not preferences.
/// </summary>
public sealed class AiOptionsTests
{
    private static AiOptions Bind(params (string Key, string Value)[] pairs)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

        return configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
    }

    /// <summary>
    /// <b>No key is a state the product runs in, not an error.</b> Reading and
    /// Listening are marked from the answer key and never reach a model
    /// (<c>A-11</c>), so an install with no provider is a working install. A
    /// binder that threw here would turn every fresh clone into a broken one.
    /// </summary>
    [Fact]
    public void An_absent_provider_binds_to_unconfigured_rather_than_failing()
    {
        var options = Bind();

        Assert.False(options.OpenAi.IsConfigured);
        Assert.False(options.Gemini.IsConfigured);
        Assert.Null(options.OpenAi.ApiKey);
    }

    /// <summary>
    /// <b>The restrictive value is the default, so permission has to be typed.</b>
    ///
    /// A learner's essay and a learner's voice are personal data; a US endpoint
    /// is a cross-border transfer under Vietnam's PDPL (<c>B-2</c>, unresolved)
    /// and a reseller base URL is a second processor on top of that. If this
    /// defaulted the other way, the first adapter anyone writes would ship real
    /// learner work to a third party and nothing would have said so.
    /// </summary>
    [Fact]
    public void A_provider_may_carry_only_synthetic_data_until_someone_says_otherwise()
    {
        var configured = Bind(
            ("Ai:OpenAi:ApiKey", "test-key"),
            ("Ai:OpenAi:BaseUrl", "https://reseller.example/v1"));

        Assert.True(configured.OpenAi.IsConfigured);
        Assert.True(configured.OpenAi.SyntheticDataOnly);

        // And it can be lifted — deliberately, one provider at a time.
        var lifted = Bind(
            ("Ai:Gemini:ApiKey", "test-key"),
            ("Ai:Gemini:SyntheticDataOnly", "false"));

        Assert.False(lifted.Gemini.SyntheticDataOnly);
        // Lifting one says nothing about the other.
        Assert.True(lifted.OpenAi.SyntheticDataOnly);
    }

    /// <summary>
    /// <b>The exclusion survives a reseller.</b>
    ///
    /// The Claude API is excluded by owner decision. That was easy to honour
    /// while it meant "do not sign up"; it stopped being easy when a reseller
    /// turned up serving fifteen Claude models beside six GPT ones on one
    /// OpenAI-compatible endpoint. The exclusion is now one typo away from
    /// being broken inside a section literally called OpenAi — so it is
    /// checked, not remembered.
    /// </summary>
    [Theory]
    [InlineData("claude-opus-5")]
    [InlineData("cc-max-claude-sonnet-5")]
    [InlineData("bedrock-claude-fable-5")]
    [InlineData("CLAUDE-OPUS-4.8-thinking")]
    public void An_excluded_model_is_refused_whatever_route_offers_it(string model)
    {
        var provider = new AiProviderOptions { ApiKey = "k", Model = model };

        var reason = AiProviderPolicy.Rejects("OpenAi", provider);

        Assert.NotNull(reason);
        Assert.Contains("excluded by owner decision", reason);
    }

    [Theory]
    [InlineData("gpt-5.5")]
    [InlineData("gpt-5.5-xhigh")]
    [InlineData(null)]
    [InlineData("")]
    public void Everything_else_passes(string? model)
    {
        Assert.Null(AiProviderPolicy.Rejects("OpenAi", new AiProviderOptions { Model = model }));
    }
}
