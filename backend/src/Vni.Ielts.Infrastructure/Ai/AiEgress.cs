using Vni.Ielts.Infrastructure.Configuration;

namespace Vni.Ielts.Infrastructure.Ai;

/// <summary>
/// What class of data a request carries. <b>There is no default and no third
/// value.</b>
///
/// <para>
/// The whole guard rests on this being impossible to omit: it is a required
/// argument to <see cref="AiEgress.Authorise"/>, and <see cref="AiEgress"/> is
/// the only way to obtain the endpoint and key an adapter needs. An adapter
/// author who has not thought about which of these applies cannot compile.
/// </para>
///
/// <para>
/// <b>Deliberately not a bool.</b> `bool isSynthetic` reads identically to
/// `bool isReal` at the call site and inverts silently; a named enum member is
/// what a reviewer can check against the caller's intent.
/// </para>
/// </summary>
public enum AiDataClassification
{
    /// <summary>
    /// Invented content — a fixture essay, a canned prompt, a smoke test.
    ///
    /// <b>Not "content with the name removed".</b> A learner's essay with the
    /// candidate's name stripped is still that learner's writing: it is their
    /// personal data, it is what a re-identification attack works on, and
    /// calling it synthetic is how the guard gets defeated by someone who
    /// meant well. Synthetic means nobody wrote it for real.
    /// </summary>
    Synthetic = 0,

    /// <summary>
    /// Anything a real learner produced — essay text, a transcript, a
    /// recording, or a feature vector derived from one.
    ///
    /// <b>Derived features count.</b> Lexical-diversity numbers and pause
    /// timings are computed from a person's speech and describe that person;
    /// they are pseudonymised personal data, not anonymous data.
    /// </summary>
    LearnerPersonal = 1,
}

/// <summary>
/// Why an AI call was refused. Carries no configured value.
/// </summary>
public sealed class AiEgressRefusedException(string reason, AiEgressRefusal refusal)
    : InvalidOperationException(reason)
{
    public AiEgressRefusal Refusal { get; } = refusal;
}

/// <summary>
/// The distinct reasons a call is refused, so a caller can tell an unfinished
/// configuration from a legal boundary without parsing English.
/// </summary>
public enum AiEgressRefusal
{
    /// <summary>No key. The install simply has no provider. → `AwaitingEvaluator`</summary>
    NotConfigured,

    /// <summary>No model was named, and there is no default. → `G-11`</summary>
    NoModel,

    /// <summary>The model is from a family the owner excluded. → CLAUDE.md rule 6</summary>
    ExcludedProvider,

    /// <summary>
    /// The endpoint belongs to an organisation with no data-processing
    /// agreement, and the payload is a real learner's.
    /// </summary>
    UncontractedProcessor,

    /// <summary>This endpoint is marked as permitted to carry invented data only.</summary>
    SyntheticDataOnly,

    /// <summary>Personal data may not leave Vietnam yet. → `B-2`</summary>
    CrossBorderTransferNotPermitted,
}

/// <summary>
/// Permission to make one AI call, and the only route to the values needed to
/// make it.
///
/// <para>
/// <b>The key is behind a method, not a property.</b> A property is what a
/// serializer, a structured-logging destructurer and a debugger watch window
/// all reach for; a method is not. <see cref="ToString"/> is overridden for the
/// same reason. → F4.2
/// </para>
/// </summary>
public sealed class AiEgressTicket
{
    private readonly string _apiKey;

    internal AiEgressTicket(
        string providerSection,
        string model,
        string? baseUrl,
        string apiKey,
        AiDataClassification classification)
    {
        ProviderSection = providerSection;
        Model = model;
        BaseUrl = baseUrl;
        Classification = classification;
        _apiKey = apiKey;
    }

    /// <summary><c>OpenAi</c> or <c>Gemini</c>.</summary>
    public string ProviderSection { get; }

    /// <summary>The model this call is authorised against — never a default.</summary>
    public string Model { get; }

    /// <summary>Null for the vendor's own endpoint.</summary>
    public string? BaseUrl { get; }

    /// <summary>What this ticket was authorised to carry. An adapter must not widen it.</summary>
    public AiDataClassification Classification { get; }

    /// <summary>
    /// The API key, for the one place that builds the request.
    ///
    /// Named as a verb because reading it is an action with consequences, and
    /// because a reviewer grepping for `Reveal` finds every place it happens.
    /// </summary>
    public string RevealApiKey() => _apiKey;

    public override string ToString() =>
        $"{ProviderSection} model={Model} baseUrl={SecretRedaction.Url(BaseUrl)} "
        + $"carrying={Classification} apiKey={SecretRedaction.Describe(_apiKey)}";
}

/// <summary>
/// The synthetic-data guard: the seam every AI adapter passes through, and the
/// contract FS6.3 (OpenAI) and FS6.4 (Gemini) implement against.
///
/// ── Why this is a gate and not a warning ──────────────────────────────────
///
/// <b>The reseller route is a second data processor.</b> Testing currently
/// points <c>Ai:OpenAi:BaseUrl</c> at a third-party endpoint. Every byte of a
/// request through it — a learner's essay, a learner's voice — passes through a
/// company VNI has signed nothing with, on top of the border crossing that
/// reaching OpenAI or Google would already be. <c>docs/development/ai-provider-setup.md</c>
/// has said since it was written that the call site is expected to <b>refuse</b>,
/// because "a warning line in a background job's log is a line nobody reads".
/// Until FS0.4 there was no call site and no refusal — only the sentence.
///
/// ── The shape, and why it is this shape ───────────────────────────────────
///
/// <b>An adapter cannot obtain an endpoint and a key except through
/// <see cref="Authorise"/>, and <see cref="Authorise"/> cannot be called
/// without naming what the payload is.</b> That is the structural part. A
/// guard that an adapter is merely expected to call before doing its own thing
/// is a code-review convention, and code-review conventions do not survive the
/// third adapter or the first hurried afternoon.
///
/// <b>Three independent gates, and personal data must clear all of them.</b>
/// They are separate because they are separate questions with separate owners:
///
/// <list type="number">
/// <item><b>Is a third organisation in the path?</b> Owned by whoever signs a
/// DPA. Not liftable from configuration — see
/// <see cref="AiProviderPolicy.ContractedProcessorHosts"/>.</item>
/// <item><b>Is this endpoint trusted with real work?</b> Owned by the
/// operator, expressed as <c>Ai:{provider}:SyntheticDataOnly = false</c>.</item>
/// <item><b>May personal data leave Vietnam at all?</b> Owned by whoever files
/// the CTIA, expressed as <c>Ai:AllowCrossBorderTransfer = true</c>.
/// → <c>B-2</c></item>
/// </list>
///
/// Collapsing any two of them into one switch would let the person answering
/// the easy question accidentally answer the hard one.
///
/// <b>Synthetic data clears none of them, and needs to clear none of them.</b>
/// Invented text is not personal data, so no processor question and no border
/// question arises. It still has to be a configured, permitted model — a
/// smoke test against an excluded provider is still an excluded provider.
/// </summary>
public static class AiEgress
{
    /// <summary>
    /// Permission to call <paramref name="providerSection"/> with a payload of
    /// class <paramref name="classification"/>, or an exception saying why not.
    /// </summary>
    /// <exception cref="AiEgressRefusedException">
    /// Always thrown rather than returning null. A null would be checked at
    /// three call sites and forgotten at the fourth; an exception's default
    /// behaviour is the safe one.
    /// </exception>
    public static AiEgressTicket Authorise(
        string providerSection,
        AiProviderOptions provider,
        bool allowCrossBorderTransfer,
        AiDataClassification classification)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!provider.IsConfigured)
        {
            throw Refuse(
                AiEgressRefusal.NotConfigured,
                $"Ai:{providerSection}:ApiKey is not set, so this provider cannot be called. "
                + "An install with no provider is a working install — Reading and Listening are "
                + "marked from the answer key (A-11) — so the caller should report "
                + "AwaitingEvaluator rather than treat this as a fault.");
        }

        if (string.IsNullOrWhiteSpace(provider.Model))
        {
            throw Refuse(
                AiEgressRefusal.NoModel,
                $"Ai:{providerSection}:Model is not set and has no default. A default here would "
                + "send work to whichever model the author of the adapter happened to think of, "
                + "and the band it produced could not be reproduced or calibrated. → G-11");
        }

        if (AiProviderPolicy.Rejects(providerSection, provider) is { } excluded)
            throw Refuse(AiEgressRefusal.ExcludedProvider, excluded);

        if (classification == AiDataClassification.LearnerPersonal)
        {
            /*
             * <b>Checked first, and not liftable from configuration.</b> This
             * is the one refusal an operator cannot undo by editing a
             * deployment variable, so it is worth reaching before the two they
             * can — otherwise someone flips both switches, sees a different
             * error, and flips more things.
             */
            if (AiProviderPolicy.IsThirdPartyEndpoint(providerSection, provider.BaseUrl))
            {
                throw Refuse(
                    AiEgressRefusal.UncontractedProcessor,
                    $"Ai:{providerSection}:BaseUrl is not the vendor's own endpoint, so a third "
                    + "organisation is in the path, and this payload is a real learner's. There "
                    + "is no data-processing agreement with that organisation — "
                    + "AiProviderPolicy.ContractedProcessorHosts is empty. Synthetic payloads may "
                    + "use this route; learner work may not, and no configuration value changes "
                    + "that. → CLAUDE.md rule 6");
            }

            if (provider.SyntheticDataOnly)
            {
                throw Refuse(
                    AiEgressRefusal.SyntheticDataOnly,
                    $"Ai:{providerSection}:SyntheticDataOnly is true — its default — so this "
                    + "endpoint may carry invented data only. Setting it to false is a decision "
                    + "about a real person's essay or voice, and having to type it is the record "
                    + "that the decision was made.");
            }

            if (!allowCrossBorderTransfer)
            {
                throw Refuse(
                    AiEgressRefusal.CrossBorderTransferNotPermitted,
                    "Ai:AllowCrossBorderTransfer is false. A learner's essay or voice reaching a "
                    + "provider outside Vietnam is a cross-border transfer of personal data under "
                    + "the PDPL and needs a CTIA filing within 60 days of the first transfer. "
                    + "→ B-2, docs/security/privacy-vietnam-pdpl.md");
            }
        }

        return new AiEgressTicket(
            providerSection,
            provider.Model!,
            string.IsNullOrWhiteSpace(provider.BaseUrl) ? null : provider.BaseUrl,
            provider.ApiKey!,
            classification);
    }

    /// <summary>
    /// Convenience over the whole <see cref="AiOptions"/> section, so a caller
    /// cannot pair one provider's options with the other's section name.
    /// </summary>
    public static AiEgressTicket Authorise(
        AiOptions ai, string providerSection, AiDataClassification classification)
    {
        ArgumentNullException.ThrowIfNull(ai);

        var provider = providerSection switch
        {
            "OpenAi" => ai.OpenAi,
            "Gemini" => ai.Gemini,
            _ => throw new ArgumentOutOfRangeException(
                nameof(providerSection),
                "Only OpenAi and Gemini exist. A third provider is an owner decision and a new "
                + "property on AiOptions, not a string passed in here. → CLAUDE.md rule 6"),
        };

        return Authorise(providerSection, provider, ai.AllowCrossBorderTransfer, classification);
    }

    /// <summary>
    /// Builds the exception, and is the single place that guarantees a refusal
    /// message names <b>settings</b> and never <b>values</b>.
    ///
    /// A refusal is the message most likely to be logged verbatim, pasted into
    /// a ticket, and forwarded — so the reason it gives has to be safe by
    /// construction rather than by each author remembering.
    /// </summary>
    private static AiEgressRefusedException Refuse(AiEgressRefusal refusal, string reason) =>
        new(reason, refusal);
}
