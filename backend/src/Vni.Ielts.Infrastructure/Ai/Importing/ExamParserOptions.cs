using Vni.Ielts.Infrastructure.Configuration;

namespace Vni.Ielts.Infrastructure.Ai.Importing;

/// <summary>
/// <c>Import:Parser</c> — the gate that decides whether a raw exam document
/// (docx/pdf/txt) may be turned into a package draft by a model, or whether
/// <see cref="Content.Import.UnconfiguredExamSourceParser"/> stays wired and
/// every such upload fails with <c>AI_PARSER_UNAVAILABLE</c>.
///
/// <para>
/// <b>Deliberately its own section, not a read of <c>Ai:OpenAi</c>.</b> That
/// section already exists and is already configured — it is what Writing
/// marking, Reading/Listening explanations and coaching advice all call
/// through. If this type read it too, configuring Writing marking would
/// silently also turn on AI parsing of copyrighted exam papers nobody asked
/// it to parse — exactly the "a fallback that picks a provider because one
/// happens to be available" this task's brief forbids. So
/// <see cref="OpenAiStructuredExamClient"/> is wired here from <b>this</b>
/// section's own <see cref="Model"/>, <see cref="ApiKey"/> and
/// <see cref="BaseUrl"/> — a deployment turns raw-document parsing on by
/// filling in a value that only raw-document parsing reads, and completeness
/// of this section is exactly what the runtime call needs. There is no way to
/// half-fill this section and have the first upload discover it.
/// </para>
/// </summary>
public sealed class ExamParserOptions
{
    public const string SectionName = "Import:Parser";

    /// <summary>
    /// <c>OpenAi</c> — the only <c>IStructuredExamAiClient</c> this
    /// deployment wires today. Not defaulted: a default here would silently
    /// route a paper somewhere nobody chose. → `G-11`
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>Which model transcribes the paper. No default. → `G-11`</summary>
    public string? Model { get; set; }

    /// <summary>Environment configuration only. Never committed. → CLAUDE.md rule 6</summary>
    public string? ApiKey { get; set; }

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
    /// Whether the real parser may be wired. <b>All three of
    /// <see cref="Provider"/>, <see cref="Model"/> and <see cref="ApiKey"/>,
    /// or none.</b> There is no reading of "some of them" that is safe to run
    /// with — see <see cref="Problem"/>.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Provider)
        && !string.IsNullOrWhiteSpace(Model)
        && !string.IsNullOrWhiteSpace(ApiKey);

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
    /// <c>Assessment:Writing:TaskWeights</c>.
    /// </para>
    /// </summary>
    public string? Problem()
    {
        var named = !string.IsNullOrWhiteSpace(Provider)
            || !string.IsNullOrWhiteSpace(Model)
            || !string.IsNullOrWhiteSpace(ApiKey);

        if (!named) return null; // Nothing configured: the null implementation, not a fault.

        var missing = new List<string>(3);
        if (string.IsNullOrWhiteSpace(Provider)) missing.Add(nameof(Provider));
        if (string.IsNullOrWhiteSpace(Model)) missing.Add(nameof(Model));
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add(nameof(ApiKey));

        if (missing.Count > 0)
        {
            return $"{SectionName} names a provider but is missing {string.Join(" and ", missing)}. "
                + "A section that looks enabled and fails on the first upload is worse than one "
                + "that is plainly unset — set Provider, Model and ApiKey together, or leave all "
                + "three empty.";
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

        return null;
    }

    public override string ToString() =>
        $"Provider={Provider ?? "not set"}, Model={Model ?? "not set"}, "
        + $"BaseUrl={SecretRedaction.Url(BaseUrl)}, ApiKey={SecretRedaction.Describe(ApiKey)}, "
        + $"MaxAttempts={MaxAttempts}, PromptVersion={PromptVersion}";
}
