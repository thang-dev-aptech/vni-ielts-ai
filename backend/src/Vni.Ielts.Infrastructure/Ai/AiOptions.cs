namespace Vni.Ielts.Infrastructure.Ai;

/// <summary>
/// Where the AI providers live, and what they are allowed to be sent.
///
/// <para>
/// <b>Keys come from environment configuration and nothing else.</b>
/// <c>Ai__OpenAi__ApiKey</c>, <c>Ai__Gemini__ApiKey</c> — supplied by the
/// deployment, or by <c>dotnet user-secrets</c> on a developer machine. Never
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
