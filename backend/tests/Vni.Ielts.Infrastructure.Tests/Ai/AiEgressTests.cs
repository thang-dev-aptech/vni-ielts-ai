using Vni.Ielts.Infrastructure.Ai;

namespace Vni.Ielts.Infrastructure.Tests.Ai;

/// <summary>
/// FS0.4 — the synthetic-data guard, which is the contract FS6.3 (OpenAI) and
/// FS6.4 (Gemini) implement against.
///
/// <b>The property under test is a refusal, not a warning.</b>
/// `docs/development/ai-provider-setup.md` has said since it was written that
/// the call site is expected to refuse a real learner's work on a reseller
/// endpoint, "because a warning line in a background job's log is a line nobody
/// reads". Until now there was no call site, so the sentence was the whole
/// control. These tests are what turns it into one.
/// </summary>
public sealed class AiEgressTests
{
    private const string Key = "FAKE-NOT-A-REAL-KEY-for-tests-only-0000";
    private const string Reseller = "https://reseller.example/v1";
    private const string VendorOpenAi = "https://api.openai.com/v1";

    private static AiProviderOptions Provider(
        string? baseUrl = null, bool syntheticOnly = true, string model = "gpt-5.5") =>
        new() { ApiKey = Key, BaseUrl = baseUrl, Model = model, SyntheticDataOnly = syntheticOnly };

    /// <summary>
    /// <b>The headline requirement.</b> A reseller base URL plus a real
    /// learner's essay is refused at runtime, not warned about.
    /// </summary>
    [Fact]
    public void Learner_data_is_refused_on_a_reseller_endpoint()
    {
        var refused = Assert.Throws<AiEgressRefusedException>(() => AiEgress.Authorise(
            "OpenAi",
            Provider(baseUrl: Reseller),
            allowCrossBorderTransfer: true,
            AiDataClassification.LearnerPersonal));

        Assert.Equal(AiEgressRefusal.UncontractedProcessor, refused.Refusal);
    }

    /// <summary>
    /// <b>And it stays refused with every configuration switch turned the
    /// permissive way.</b> This is what "structurally impossible" has to mean:
    /// an operator who flips both flags — the one that says this endpoint is
    /// trusted with real work, and the one that says the border may be crossed
    /// — still cannot route a learner's essay through a company with no
    /// data-processing agreement. Lifting it is a code change to
    /// `AiProviderPolicy.ContractedProcessorHosts`, which appears in review.
    /// </summary>
    [Fact]
    public void No_configuration_value_lifts_the_reseller_refusal_for_learner_data()
    {
        var refused = Assert.Throws<AiEgressRefusedException>(() => AiEgress.Authorise(
            "OpenAi",
            Provider(baseUrl: Reseller, syntheticOnly: false),
            allowCrossBorderTransfer: true,
            AiDataClassification.LearnerPersonal));

        Assert.Equal(AiEgressRefusal.UncontractedProcessor, refused.Refusal);
        Assert.Empty(AiProviderPolicy.ContractedProcessorHosts);
    }

    /// <summary>
    /// <b>The route still works for what it is for.</b> A guard that also
    /// blocked synthetic traffic would block the only thing this endpoint was
    /// approved for, and the first person to hit it would be tempted to weaken
    /// the guard rather than reclassify their payload.
    /// </summary>
    [Fact]
    public void Synthetic_data_is_permitted_on_a_reseller_endpoint()
    {
        var ticket = AiEgress.Authorise(
            "OpenAi",
            Provider(baseUrl: Reseller),
            allowCrossBorderTransfer: false,
            AiDataClassification.Synthetic);

        Assert.Equal("gpt-5.5", ticket.Model);
        Assert.Equal(AiDataClassification.Synthetic, ticket.Classification);
        Assert.Equal(Key, ticket.RevealApiKey());
    }

    /// <summary>
    /// The three gates are independent, so each one alone is enough to refuse.
    /// Reaching the vendor's own endpoint isolates the other two.
    /// </summary>
    [Fact]
    public void Learner_data_on_the_vendor_endpoint_still_needs_the_synthetic_flag_lifted()
    {
        var refused = Assert.Throws<AiEgressRefusedException>(() => AiEgress.Authorise(
            "OpenAi",
            Provider(baseUrl: VendorOpenAi),
            allowCrossBorderTransfer: true,
            AiDataClassification.LearnerPersonal));

        Assert.Equal(AiEgressRefusal.SyntheticDataOnly, refused.Refusal);
    }

    [Fact]
    public void Learner_data_on_the_vendor_endpoint_still_needs_the_border_opened()
    {
        var refused = Assert.Throws<AiEgressRefusedException>(() => AiEgress.Authorise(
            "OpenAi",
            Provider(baseUrl: VendorOpenAi, syntheticOnly: false),
            allowCrossBorderTransfer: false,
            AiDataClassification.LearnerPersonal));

        Assert.Equal(AiEgressRefusal.CrossBorderTransferNotPermitted, refused.Refusal);
    }

    /// <summary>
    /// <b>An unset base URL is the vendor's own endpoint, not "unknown".</b>
    /// Getting this wrong in either direction is a real bug: treating it as
    /// third-party blocks the eventual production path, and treating a
    /// third-party as the vendor is the leak this file exists to stop.
    /// </summary>
    [Fact]
    public void All_three_gates_open_permits_the_call()
    {
        var ticket = AiEgress.Authorise(
            "OpenAi",
            Provider(baseUrl: null, syntheticOnly: false),
            allowCrossBorderTransfer: true,
            AiDataClassification.LearnerPersonal);

        Assert.Null(ticket.BaseUrl);
        Assert.Equal(AiDataClassification.LearnerPersonal, ticket.Classification);
    }

    /// <summary>
    /// <b>A base URL that will not parse counts as third-party.</b> "I could
    /// not tell" must not resolve to "it is fine" when the false negative is a
    /// learner's essay leaving the country to an unknown company.
    /// </summary>
    [Theory]
    [InlineData("not-a-url")]
    [InlineData("api.openai.com")]
    [InlineData("https://api.openai.com.evil.example/v1")]
    public void An_endpoint_that_is_not_provably_the_vendor_is_third_party(string baseUrl)
    {
        Assert.True(AiProviderPolicy.IsThirdPartyEndpoint("OpenAi", baseUrl));
    }

    [Theory]
    [InlineData("OpenAi", "https://api.openai.com/v1")]
    [InlineData("Gemini", "https://generativelanguage.googleapis.com/v1beta")]
    public void A_vendor_host_is_not_third_party(string section, string baseUrl)
    {
        Assert.False(AiProviderPolicy.IsThirdPartyEndpoint(section, baseUrl));
    }

    /// <summary>
    /// <b>No key is a supported state, and it is not the same refusal as a
    /// blocked one.</b> Reading and Listening are marked from the answer key
    /// and never reach a model (`A-11`), so the caller has to be able to tell
    /// "no evaluator configured" — which is `AwaitingEvaluator` on a results
    /// screen — from "this call is not allowed".
    /// </summary>
    [Fact]
    public void An_unconfigured_provider_refuses_with_its_own_distinct_reason()
    {
        var refused = Assert.Throws<AiEgressRefusedException>(() => AiEgress.Authorise(
            "OpenAi",
            new AiProviderOptions { Model = "gpt-5.5" },
            allowCrossBorderTransfer: true,
            AiDataClassification.Synthetic));

        Assert.Equal(AiEgressRefusal.NotConfigured, refused.Refusal);
    }

    /// <summary>
    /// <b>No model, and no default to fall back on.</b> `G-11`. A default here
    /// would decide which model marks a learner's work, and the resulting band
    /// could not be reproduced or calibrated.
    /// </summary>
    [Fact]
    public void A_configured_provider_with_no_model_is_refused_rather_than_defaulted()
    {
        var refused = Assert.Throws<AiEgressRefusedException>(() => AiEgress.Authorise(
            "OpenAi",
            new AiProviderOptions { ApiKey = Key },
            allowCrossBorderTransfer: true,
            AiDataClassification.Synthetic));

        Assert.Equal(AiEgressRefusal.NoModel, refused.Refusal);
    }

    /// <summary>
    /// The owner's exclusion holds at the call site too, not only at boot — a
    /// process could be handed options that never passed the startup gate.
    /// </summary>
    [Fact]
    public void An_excluded_model_is_refused_even_for_synthetic_data()
    {
        var refused = Assert.Throws<AiEgressRefusedException>(() => AiEgress.Authorise(
            "OpenAi",
            Provider(model: "cc-max-claude-sonnet-5"),
            allowCrossBorderTransfer: false,
            AiDataClassification.Synthetic));

        Assert.Equal(AiEgressRefusal.ExcludedProvider, refused.Refusal);
    }

    /// <summary>
    /// <b>A refusal is the message most likely to be logged verbatim and
    /// pasted into a ticket.</b> So it names settings and never values.
    /// </summary>
    [Fact]
    public void A_refusal_message_never_contains_the_configured_key_or_endpoint()
    {
        var refused = Assert.Throws<AiEgressRefusedException>(() => AiEgress.Authorise(
            "OpenAi",
            Provider(baseUrl: Reseller, syntheticOnly: false),
            allowCrossBorderTransfer: true,
            AiDataClassification.LearnerPersonal));

        Assert.DoesNotContain(Key, refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("reseller.example", refused.Message, StringComparison.Ordinal);

        // And it still names the setting, so the message is actionable.
        Assert.Contains("Ai:OpenAi:BaseUrl", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The ticket is the object an adapter holds while building a
    /// request</b> — which makes it the object most likely to end up in a log
    /// line, an exception, or a debugger dump.
    /// </summary>
    [Fact]
    public void A_ticket_does_not_print_its_key()
    {
        var ticket = AiEgress.Authorise(
            "OpenAi",
            Provider(baseUrl: Reseller),
            allowCrossBorderTransfer: false,
            AiDataClassification.Synthetic);

        Assert.DoesNotContain(Key, ticket.ToString(), StringComparison.Ordinal);
        Assert.Contains("gpt-5.5", ticket.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole-section overload cannot mispair a provider with the wrong
    /// section name, and a third provider is not expressible at all.
    /// </summary>
    [Fact]
    public void Only_the_two_selected_providers_exist()
    {
        var ai = new AiOptions { OpenAi = Provider(), AllowCrossBorderTransfer = false };

        var ticket = AiEgress.Authorise(ai, "OpenAi", AiDataClassification.Synthetic);
        Assert.Equal("OpenAi", ticket.ProviderSection);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => AiEgress.Authorise(ai, "Anthropic", AiDataClassification.Synthetic));
    }

    /// <summary>
    /// <b>The default, stated as a test.</b> `Ai:AllowCrossBorderTransfer`
    /// existed only in `CLAUDE.md` until FS0.4; a switch that no class declares
    /// is not a switch.
    /// </summary>
    [Fact]
    public void Cross_border_transfer_is_off_unless_somebody_turns_it_on()
    {
        Assert.False(new AiOptions().AllowCrossBorderTransfer);
    }
}
