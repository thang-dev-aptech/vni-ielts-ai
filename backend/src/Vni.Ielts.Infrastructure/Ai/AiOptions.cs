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
    /// Third-party processors that VNI has a signed data-processing agreement
    /// with. <b>Empty, and that is the current legal position, not an
    /// oversight.</b>
    ///
    /// <para>
    /// The reseller currently used for testing is a company nobody has a
    /// contract with. Until one exists, no third-party host may carry a
    /// learner's essay or voice — and that fact is expressed by this array
    /// being empty rather than by a warning somewhere.
    /// </para>
    ///
    /// <para>
    /// <b>Adding a host here is a code change, on purpose.</b> It is the same
    /// weight as reversing the Claude exclusion: it appears in review, it is
    /// dated by a commit, and it cannot happen by editing a deployment
    /// variable at three in the morning. → <c>G-11</c>: the seam exists, the
    /// implementation is null until somebody decides.
    /// </para>
    /// </summary>
    public static readonly string[] ContractedProcessorHosts = [];

    /// <summary>
    /// Whether this endpoint puts an organisation other than the chosen vendor
    /// in the path. An unset <paramref name="baseUrl"/> means the vendor's own.
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
