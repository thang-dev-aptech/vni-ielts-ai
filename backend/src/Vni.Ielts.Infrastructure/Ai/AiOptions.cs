using Vni.Ielts.Infrastructure.Configuration;

namespace Vni.Ielts.Infrastructure.Ai;

/// <summary>
/// Where the AI providers live, and what they are allowed to be sent.
///
/// <para>
/// <b>Keys come from environment configuration and nothing else.</b>
/// <c>Ai__OpenAi__ApiKey</c>, <c>Ai__Gemini__ApiKey</c> — supplied by the
/// deployment, or by <c>secrets.develop.json</c> / environment variables. Never
/// an <c>appsettings</c> file, never a committed file of any kind: .gitignore
/// and a PreToolUse hook both block <c>.env*</c>, and CI scans for
/// credential-shaped strings. → CLAUDE.md rule 6
/// </para>
///
/// <para>
/// <b>Two providers, and Claude is deliberately absent.</b> GPT (OpenAI) and
/// Gemini (Google) were selected by the owner on 2026-08-20; the Claude API is
/// excluded. A third provider is a decision, not a config value, so adding one
/// means adding a property here rather than passing a name through.
/// </para>
///
/// <para>
/// See <c>docs/development/ai-provider-setup.md</c> for how to supply these.
/// </para>
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public AiProviderOptions OpenAi { get; set; } = new();
    public AiProviderOptions Gemini { get; set; } = new();

    /// <summary>
    /// Whether personal data may leave Vietnam at all. <b>Default false.</b>
    ///
    /// <para>
    /// Named in <c>CLAUDE.md</c> since 2026-08-28 and, until FS0.4, existing
    /// nowhere in the code — the whole AI pipeline was to be built behind a
    /// switch that no class declared and no test could set. A gate that is
    /// only written down is not a gate.
    /// </para>
    ///
    /// <para>
    /// Both selected providers are US companies, so any learner essay or
    /// recording reaching one is a cross-border transfer of personal data
    /// under Vietnam's PDPL and needs a CTIA filing within 60 days of the
    /// first transfer. That filing is <c>B-2</c>, and it is unresolved — so
    /// the value that permits the transfer is the one somebody has to type,
    /// and typing it is the record that the filing was considered.
    /// </para>
    ///
    /// <para>
    /// <b>It is a separate question from <see cref="AiProviderOptions.SyntheticDataOnly"/>,
    /// and both must be answered.</b> This one asks whether the border may be
    /// crossed; that one asks whether a particular endpoint is trusted with
    /// real work. A provider whose endpoint is the vendor's own can be trusted
    /// and still be on the wrong side of an unfiled CTIA.
    /// </para>
    ///
    /// <para>Deterministic Reading and Listening marking never reaches a
    /// provider, so it is not gated by this at all. → <c>A-11</c></para>
    /// </summary>
    public bool AllowCrossBorderTransfer { get; set; }
}

/// <summary>
/// One provider's endpoint, key, and the class of data it may carry.
/// </summary>
public sealed class AiProviderOptions
{
    /// <summary>
    /// The API root. Left unset for the provider's own endpoint.
    ///
    /// <para>
    /// <b>A base URL that is not the vendor's own is a second data processor.</b>
    /// Testing routes through a third-party reseller, which means the request —
    /// and everything in it — passes through a company nobody has a contract
    /// with. That is why <see cref="SyntheticDataOnly"/> exists and why it
    /// defaults the way it does.
    /// </para>
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Environment configuration only. See the note on <see cref="AiOptions"/>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Which model to call — <c>gpt-5.5</c>, and so on.
    ///
    /// <para>
    /// <b>Configuration, not a constant in the adapter.</b> A reseller offers
    /// several tiers of the same family and they differ in cost and in
    /// latency; the one that suits marking a Writing task is a decision
    /// somebody makes and revisits, not a string compiled into a class. It is
    /// also the field that has to change first when a provider retires a
    /// model, which is not an event that should require a deployment of new
    /// code.
    /// </para>
    ///
    /// <para>
    /// Not defaulted. An invented default here would send a learner's essay to
    /// whichever model the author of this file happened to think of. → `G-11`
    /// </para>
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Whether this endpoint may only be sent invented data.
    /// <b>Defaults to true, and that default is the safety property.</b>
    ///
    /// <para>
    /// A learner's essay and a learner's voice are personal data. Sending them
    /// to a US endpoint is a cross-border transfer under Vietnam's PDPL and
    /// needs a CTIA filing (<c>B-2</c>, unresolved); sending them through a
    /// reseller adds a processor on top of that. Both are decisions somebody
    /// has to make and record — so the permissive value is the one that has to
    /// be typed, and typing it is the record.
    /// </para>
    ///
    /// <para>
    /// The flag is not advice. Callers are expected to refuse rather than warn:
    /// a warning on a batch job is a line in a log nobody reads.
    /// </para>
    /// </summary>
    public bool SyntheticDataOnly { get; set; } = true;

    /// <summary>
    /// Whether a key was supplied at all.
    ///
    /// <para>
    /// <b>Absent is a state, not a failure.</b> No provider is configured today
    /// and the product has to run anyway — Reading and Listening are marked
    /// from the answer key and never touch a model (<c>A-11</c>). A caller that
    /// treats "no key" as an error turns an unconfigured install into a broken
    /// one.
    /// </para>
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// A description safe to print. <b>Overridden so that the default is not.</b>
    ///
    /// <para>
    /// The inherited <see cref="object.ToString"/> prints a type name, which is
    /// harmless — but this type reaches places that reflect over it. A
    /// structured-logging destructurer, a diagnostic endpoint, an
    /// <c>IOptions</c> dump written in a hurry: each of those turns "the object
    /// was in scope" into "the key is in the log". Giving the type its own safe
    /// rendering means the lazy thing and the correct thing are the same thing.
    /// → F4.2
    /// </para>
    /// </summary>
    public override string ToString() =>
        $"BaseUrl={SecretRedaction.Url(BaseUrl)}, Model={Model ?? "not set"}, "
        + $"ApiKey={SecretRedaction.Describe(ApiKey)}, SyntheticDataOnly={SyntheticDataOnly}";
}

/// <summary>
/// The owner's provider exclusion, enforced rather than remembered.
/// </summary>
public static class AiProviderPolicy
{
    /// <summary>
    /// Model families this product may not call, whatever route reaches them.
    ///
    /// <para>
    /// <b>The Claude API is excluded by owner decision, 2026-08-20.</b> That
    /// was easy to honour while it meant "do not sign up for it". It stopped
    /// being easy the moment a reseller appeared whose OpenAI-compatible
    /// endpoint serves fifteen Claude models beside six GPT ones: the
    /// exclusion is now one typo in a config value away from being broken,
    /// silently, in a section called <c>OpenAi</c>.
    /// </para>
    ///
    /// <para>
    /// So the decision is checked at startup rather than trusted. Reversing it
    /// means editing this list — a code change that appears in review, which
    /// is the right weight for an owner decision.
    /// </para>
    /// </summary>
    public static readonly string[] ExcludedModelMarkers = ["claude"];

    /// <summary>
    /// The hosts that are the vendor itself, per provider section.
    ///
    /// <para>
    /// <b>An allowlist, because the question is "is this the company we chose"
    /// and there is no other way to ask it.</b> A denylist of known resellers
    /// would be obsolete the day someone stands up a new one, and the failure
    /// mode of a stale denylist is silent permission.
    /// </para>
    ///
    /// <para>
    /// Reaching a host on this list is <i>not</i> permission to send learner
    /// work. Both companies are in the United States, so the border question
    /// (<see cref="AiOptions.AllowCrossBorderTransfer"/>) is still open and
    /// still separate. All this list decides is whether a <b>third</b>
    /// organisation is in the path.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> VendorHosts(string section) => section switch
    {
        "OpenAi" => ["api.openai.com"],
        "Gemini" => ["generativelanguage.googleapis.com", "aiplatform.googleapis.com"],
        _ => [],
    };

    /// <summary>
    /// Third-party processors permitted to carry a learner's work.
    ///
    /// <para>
    /// <b><c>api.vietapi.tech</c> — <c>[QUYẾT ĐỊNH]</c> chủ sản phẩm,
    /// 2026-09-02: <i>"cho chạy thật luôn"</i>.</b> The owner was shown what
    /// this entry costs and chose it anyway, which is the decision this list
    /// exists to record. Until that commit the array was empty and every
    /// learner essay stopped here with
    /// <see cref="AiEgressRefusal.UncontractedProcessor"/>.
    /// </para>
    ///
    /// <para>
    /// <b><c>apithat.dev</c> — <c>[QUYẾT ĐỊNH]</c> chủ sản phẩm,
    /// 2026-09-03: switch the live OpenAI-compatible route to this host and
    /// call the configured OpenAI-compatible model (currently
    /// <c>deepseek-v4-pro</c> in operator secrets).</b> Without this line, boot
    /// refuses <c>SyntheticDataOnly = false</c> against that BaseUrl, and so
    /// does every learner-essay call. Probes on 2026-09-03 returned
    /// <c>deepseek-ai/deepseek-v4-flash-0731</c> then
    /// <c>deepseek-ai/deepseek-v4-pro-0813</c> with no Anthropic usage
    /// fields — unlike the 2026-08-27 vietapi measurements.
    /// </para>
    ///
    /// <para>
    /// <b>Three things this entry does not settle</b>, written down because
    /// whoever reads this next will need them:
    /// </para>
    /// <list type="number">
    /// <item><b>No data-processing agreement is on file.</b> This list is named
    /// for one and today carries a decision instead. If a DPA is signed later,
    /// nothing here changes; if the answer comes back "no", this is the line to
    /// delete.</item>
    /// <item><b>The real backend is unverified, and the evidence points at
    /// Claude.</b> On 2026-08-27 five model names from five different families
    /// all returned <c>claude_cache_creation_*</c> in <c>usage</c> — a field
    /// only Anthropic emits. <see cref="ExcludedModelMarkers"/> matches the
    /// <i>requested</i> model name, so <c>gpt-5.5</c> passes it while possibly
    /// reaching the API the 2026-08-20 decision excluded.
    /// → <c>docs/development/ai-provider-setup.md</c></item>
    /// <item><b>A band from this route cannot name the model that produced
    /// it</b> — which calibration (<c>M-28</c>), reproduction and an audit
    /// trail all need.</item>
    /// </list>
    ///
    /// <para>
    /// The border question stays separate and stays open: this list decides
    /// only whether a <b>third organisation</b> is in the path.
    /// <see cref="AiOptions.AllowCrossBorderTransfer"/> decides whether
    /// personal data may leave Vietnam, and <c>B-2</c> is unanswered.
    /// </para>
    /// </summary>
    public static readonly string[] ContractedProcessorHosts =
        ["api.vietapi.tech", "apithat.dev"];

    /// <summary>
    /// Whether this endpoint is the chosen vendor's own. Nothing about
    /// contracts.
    ///
    /// <para>
    /// <b>Split out of <see cref="IsThirdPartyEndpoint"/> on the day
    /// <see cref="ContractedProcessorHosts"/> stopped being empty.</b> While it
    /// was empty the two questions had the same answer, so one method served
    /// both and the conflation was invisible. They are different questions:
    /// "is a third organisation in the path" is about a data-processing
    /// agreement; "does this endpoint speak the vendor's newest API" is about
    /// wire format. Contracting with a reseller does not teach it the OpenAI
    /// Responses API.
    /// </para>
    ///
    /// <para>
    /// Getting this wrong is not subtle. <c>OpenAiWritingEvaluationClient</c>
    /// picks its request shape from this answer, so the conflated version would
    /// have posted a Responses-API body to a reseller that only speaks
    /// <c>chat/completions</c> — and every Writing evaluation would have broken
    /// at the moment the host was contracted, which is also the moment learner
    /// essays started flowing.
    /// </para>
    /// </summary>
    public static bool IsVendorEndpoint(string section, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return true;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed)) return false;

        foreach (var vendor in VendorHosts(section))
        {
            if (string.Equals(parsed.Host, vendor, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether this endpoint puts an organisation other than the chosen vendor
    /// — or a contracted processor — in the path. An unset
    /// <paramref name="baseUrl"/> means the vendor's own.
    /// </summary>
    /// <remarks>
    /// A base URL that cannot be parsed counts as third-party. The alternative
    /// — treating "I could not tell" as "it is fine" — is the wrong way round
    /// for a check whose false negative is a learner's essay leaving the
    /// country to a company with no contract.
    /// </remarks>
    public static bool IsThirdPartyEndpoint(string section, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed)) return true;

        var host = parsed.Host;

        foreach (var vendor in VendorHosts(section))
        {
            if (string.Equals(host, vendor, StringComparison.OrdinalIgnoreCase)) return false;
        }

        foreach (var contracted in ContractedProcessorHosts)
        {
            if (string.Equals(host, contracted, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    /// <summary>
    /// The reason this configuration cannot be used, or null if it can.
    /// </summary>
    public static string? Rejects(string section, AiProviderOptions provider)
    {
        if (provider.Model is not { } model || string.IsNullOrWhiteSpace(model)) return null;

        foreach (var marker in ExcludedModelMarkers)
        {
            if (model.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return $"Ai:{section}:Model is '{model}'. The Claude API is excluded by owner "
                    + "decision (2026-08-20, CLAUDE.md rule 6). A reseller offering it under an "
                    + "OpenAI-compatible endpoint does not change that.";
            }
        }

        return null;
    }
}
