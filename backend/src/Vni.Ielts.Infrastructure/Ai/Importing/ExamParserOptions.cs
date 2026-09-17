using Vni.Ielts.Infrastructure.Configuration;

namespace Vni.Ielts.Infrastructure.Ai.Importing;

/// <summary>
/// <c>Import:Parser</c> — the gate that decides whether a raw exam document
/// (docx/pdf/txt) may be turned into a package draft by a model, or whether
/// <see cref="Content.Import.UnconfiguredExamSourceParser"/> stays wired and
/// every such upload fails with <c>AI_PARSER_UNAVAILABLE</c>.
///
/// <para>
/// <b>This section selects a provider; it does not hold a second copy of its
/// key.</b> <c>Ai:OpenAi:ApiKey</c> / <c>Ai:Gemini:ApiKey</c> are the one
/// place a provider credential lives — the same section
/// <c>Assessment:WritingMarking</c> already selects from via
/// <c>PrimaryProvider</c>/<c>FallbackProvider</c> without carrying its own
/// key. Giving this section its own <c>ApiKey</c> would mean two places to
/// rotate one secret, and this repository's own history has a credential
/// that was committed, deleted, and never revoked — it does not need a
/// second place to be forgotten. So the completeness this gate checks is
/// <see cref="Provider"/> and <see cref="Model"/> here, <b>and</b> a
/// non-empty key in the <c>Ai</c> section the resolved provider names — see
/// <see cref="Problem"/>.
/// </para>
/// </summary>
public sealed class ExamParserOptions
{
    public const string SectionName = "Import:Parser";

    /// <summary>
    /// <c>OpenAi</c> — the only <c>IStructuredExamAiClient</c> this
    /// deployment wires today, and the section under <c>Ai</c> this gate
    /// reads the key from. Not defaulted: a default here would silently
    /// route a paper somewhere nobody chose. → `G-11`
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Which model transcribes the paper. Independent of
    /// <c>Ai:OpenAi:Model</c> — the model that marks a Writing essay is not
    /// necessarily the model that should transcribe a paper. No default.
    /// → `G-11`
    /// </summary>
    public string? Model { get; set; }

    /// <summary>The API root. Left unset for the vendor's own endpoint.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How many times a transient failure (a truncated stream, a 5xx) is
    /// retried before the import fails. <b>Default 2, clamped 1–3</b> by
    /// <see cref="Problem"/> — the same range
    /// <c>ProviderNeutralExamSourceParser</c>'s own constructor enforces, so
    /// a bad value is caught here rather than at the first parse.
    /// </summary>
    public int MaxAttempts { get; set; } = 2;

    /// <summary>
    /// Stamped on every draft this parser produces, and part of a queued
    /// import job's operation id (<c>ImportJob.OperationIdFor</c>) so a
    /// prompt change can re-parse bytes an older prompt already saw.
    /// Defaults to the prompt this deployment ships with; overriding it is
    /// only meaningful alongside a matching code change.
    /// </summary>
    public string PromptVersion { get; set; } = ExamSourceParsePrompt.Version;

    /// <summary>
    /// The <see cref="AiProviderOptions"/> named by <see cref="Provider"/>,
    /// or null when <see cref="Provider"/> names nothing this deployment
    /// knows — the same set <see cref="AiEgress.Authorise(AiOptions, string, AiDataClassification)"/>
    /// switches on.
    /// </summary>
    private AiProviderOptions? ResolveProvider(AiOptions ai) => Provider switch
    {
        _ when string.Equals(Provider, "OpenAi", StringComparison.OrdinalIgnoreCase) => ai.OpenAi,
        _ when string.Equals(Provider, "Gemini", StringComparison.OrdinalIgnoreCase) => ai.Gemini,
        _ => null,
    };

    /// <summary>
    /// Whether the real parser may be wired: <see cref="Provider"/> and
    /// <see cref="Model"/> both named here, <b>and</b> a key present in the
    /// <c>Ai</c> section <see cref="Provider"/> names. There is no reading of
    /// "some of that" that is safe to run with — see <see cref="Problem"/>.
    /// </summary>
    public bool IsConfigured(AiOptions ai) =>
        !string.IsNullOrWhiteSpace(Provider)
        && !string.IsNullOrWhiteSpace(Model)
        && ResolveProvider(ai) is { IsConfigured: true };

    /// <summary>
    /// What is wrong with this section, or null when it is usable — either
    /// fully configured or entirely unset. One validation for the DI wiring
    /// and the startup gate, so they cannot disagree about what "usable"
    /// means. → <see cref="Vni.Ielts.Api.Common.StartupConfiguration"/>
    ///
    /// <para>
    /// <b>A section that names a provider but is missing a field is refused
    /// outright, in every environment.</b> That is the whole point of this
    /// task: half-configured looks enabled, and the operator who put a
    /// package in and waited is the one who discovers otherwise. There is no
    /// Development reading of "will fail on the first upload" that is worth
    /// tolerating, the same call this codebase already makes for
    /// <c>Assessment:Writing:TaskWeights</c>. The checks intrinsic to this
    /// section run first; the cross-section credential check — the one whose
    /// message names a setting outside <c>Import:Parser</c> — runs last, so
    /// a typo inside this section is never reported as a missing key
    /// somewhere else.
    /// </para>
    /// </summary>
    public string? Problem(AiOptions ai)
    {
        var named = !string.IsNullOrWhiteSpace(Provider) || !string.IsNullOrWhiteSpace(Model);

        if (!named) return null; // Nothing configured: the null implementation, not a fault.

        var missing = new List<string>(2);
        if (string.IsNullOrWhiteSpace(Provider)) missing.Add(nameof(Provider));
        if (string.IsNullOrWhiteSpace(Model)) missing.Add(nameof(Model));

        if (missing.Count > 0)
        {
            return $"{SectionName} names a provider but is missing {string.Join(" and ", missing)}. "
                + "A section that looks enabled and fails on the first upload is worse than one "
                + "that is plainly unset — set Provider and Model together, or leave both empty.";
        }

        if (!string.Equals(Provider, "OpenAi", StringComparison.OrdinalIgnoreCase))
        {
            return $"{SectionName}:Provider is '{Provider}'. The only AI-assisted exam-source "
                + "parser wired into this deployment is 'OpenAi'.";
        }

        // The Claude exclusion (CLAUDE.md rule 6) is enforced again here because this section
        // is not read by AiProviderPolicy's own callers — Ai:OpenAi and Ai:Gemini are — so a
        // Claude model named under Import:Parser:Model would otherwise reach only the runtime
        // check inside AiEgress.Authorise, at the first upload rather than at boot.
        if (AiProviderPolicy.Rejects("OpenAi", new AiProviderOptions { Model = Model }) is { } excluded)
            return excluded;

        if (MaxAttempts is < 1 or > 3)
        {
            return $"{SectionName}:MaxAttempts is {MaxAttempts}. Parser attempts must be between "
                + "1 and 3 — the same range ProviderNeutralExamSourceParser's constructor enforces.";
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            // The value is never echoed: the commonest way this setting is wrong is that a key
            // was pasted into it.
            return $"{SectionName}:BaseUrl is not an absolute URL. It is left unset for the "
                + "vendor's own endpoint; it is not a hostname and not a key.";
        }

        /*
         * <b>The one check whose message names a setting outside this section.</b>
         * Import:Parser:Provider selects OpenAi; the key itself still lives at
         * Ai:OpenAi:ApiKey, the one place this deployment's OpenAI credential
         * is written down. A deployment that filled in everything above but
         * never gave OpenAi a key looks exactly as enabled as one that has —
         * this is what tells them apart before the first upload rather than
         * during it.
         */
        if (ResolveProvider(ai) is not { IsConfigured: true })
        {
            return $"{SectionName}:Provider names '{Provider}' but Ai:{Provider}:ApiKey is not "
                + $"set. {SectionName} selects a provider; the credential still lives with "
                + $"Ai:{Provider}, not with {SectionName}.";
        }

        return null;
    }

    public override string ToString() =>
        $"Provider={Provider ?? "not set"}, Model={Model ?? "not set"}, "
        + $"BaseUrl={SecretRedaction.Url(BaseUrl)}, MaxAttempts={MaxAttempts}, "
        + $"PromptVersion={PromptVersion}";
}
