using Vni.Ielts.Infrastructure.Configuration;

namespace Vni.Ielts.Infrastructure.Ai.Importing;

/// <summary>
/// <c>Import:Transcription</c> — the gate that decides whether a Listening
/// recording inside an uploaded package may be turned into text by a model, or
/// whether <see cref="UnconfiguredAudioTranscriber"/> stays wired and the
/// transcription stage does nothing at all.
///
/// <para>
/// <b>This section selects a provider; it does not hold a second copy of its
/// key.</b> Exactly the shape <see cref="ExamParserOptions"/> settled for
/// <c>Import:Parser</c>, and for the same reason: <c>Ai:OpenAi:ApiKey</c> /
/// <c>Ai:Gemini:ApiKey</c> is the one place a provider credential lives, and
/// this repository's own history contains a credential that was committed,
/// deleted, and never revoked. It does not need a second place to be
/// forgotten. So completeness here is <see cref="Provider"/> and
/// <see cref="Model"/>, <b>and</b> a non-empty key in the <c>Ai</c> section the
/// resolved provider names — see <see cref="Problem"/>.
/// </para>
///
/// <para>
/// <b>What this is not.</b> Nothing here has anything to do with transcribing
/// a <b>learner's</b> speech. That is Speaking marking, it is deferred by
/// <c>P-02</c> until an ASR provider is chosen, and its seam
/// (<c>ITranscriptSource</c> / <c>NoTranscriptSource</c>) is untouched.
/// Published exam audio is third-party material with no data subject in it;
/// a learner's recording is personal data under Vietnam's PDPL. → <c>IP-07</c>
/// </para>
/// </summary>
public sealed class AudioTranscriptionOptions
{
    public const string SectionName = "Import:Transcription";

    /// <summary>
    /// <c>OpenAi</c> — the only transcription client this deployment wires
    /// today, and the section under <c>Ai</c> this gate reads the key from.
    /// Not defaulted: a default here would silently send a publisher's audio
    /// somewhere nobody chose. → <c>G-11</c>
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Which model transcribes the recording. Independent of
    /// <c>Ai:OpenAi:Model</c> — the model that marks a Writing essay is a
    /// chat model and cannot transcribe audio at all, so sharing that setting
    /// would guarantee a 400 on the first import. No default. → <c>G-11</c>
    /// </summary>
    public string? Model { get; set; }

    /// <summary>The API root. Left unset for the vendor's own endpoint.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How long one recording's transcription may take. <b>Default 300 s.</b>
    ///
    /// <para>
    /// <b><c>[QUYẾT ĐỊNH kỹ thuật]</c></b> — a thirty-minute Listening
    /// recording is tens of megabytes uploaded before a single byte comes
    /// back, and <see cref="HttpClient"/>'s 100 s default is neither chosen
    /// nor visible. Five minutes is well past the measured upload-plus-decode
    /// time for a Cambridge Listening part on a normal connection and short
    /// enough that a hung socket fails the part rather than the import. The
    /// cost of being wrong is bounded either way: too low and a slow-but-
    /// honest call becomes one part with no transcript and a warning naming
    /// it; too high and one import stage runs longer. Clamped to 30..1800 s
    /// at wiring time so a typo cannot produce a zero-second client.
    /// </para>
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    private AiProviderOptions? ResolveProvider(AiOptions ai) => Provider switch
    {
        _ when string.Equals(Provider, "OpenAi", StringComparison.OrdinalIgnoreCase) => ai.OpenAi,
        _ when string.Equals(Provider, "Gemini", StringComparison.OrdinalIgnoreCase) => ai.Gemini,
        _ => null,
    };

    /// <summary>
    /// Whether the real transcriber may be wired: <see cref="Provider"/> and
    /// <see cref="Model"/> both named here, <b>and</b> a key present in the
    /// <c>Ai</c> section <see cref="Provider"/> names.
    /// </summary>
    public bool IsConfigured(AiOptions ai) =>
        !string.IsNullOrWhiteSpace(Provider)
        && !string.IsNullOrWhiteSpace(Model)
        && ResolveProvider(ai) is { IsConfigured: true };

    /// <summary>
    /// What is wrong with this section, or null when it is usable — either
    /// fully configured or entirely unset. One validation for the DI wiring
    /// and the startup gate, so they cannot disagree about what "usable"
    /// means.
    ///
    /// <para>
    /// <b>A section that names a provider but is missing a field is refused
    /// outright, in every environment</b>, exactly as <c>Import:Parser</c> is:
    /// half-configured looks enabled, and the operator who uploaded a package
    /// and waited is the one who discovers otherwise. Section-intrinsic checks
    /// run first; the cross-section credential check — the one whose message
    /// names a setting outside <c>Import:Transcription</c> — runs last, so a
    /// typo inside this section is never reported as a missing key somewhere
    /// else.
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
                + "A section that looks enabled and does nothing on the first import is worse than "
                + "one that is plainly unset — set Provider and Model together, or leave both "
                + "empty.";
        }

        if (!string.Equals(Provider, "OpenAi", StringComparison.OrdinalIgnoreCase))
        {
            return $"{SectionName}:Provider is '{Provider}'. The only exam-audio transcription "
                + "client wired into this deployment is 'OpenAi'.";
        }

        // The Claude exclusion (CLAUDE.md rule 6) is checked here for the same reason
        // ExamParserOptions checks it: this section is not read by AiProviderPolicy's own
        // callers, so a Claude model named here would otherwise reach only the runtime check
        // inside AiEgress.Authorise — at the first import rather than at boot.
        if (AiProviderPolicy.Rejects("OpenAi", new AiProviderOptions { Model = Model }) is { } excluded)
            return excluded;

        if (TimeoutSeconds is < 30 or > 1800)
        {
            return $"{SectionName}:TimeoutSeconds is {TimeoutSeconds}. One recording's "
                + "transcription must be allowed between 30 and 1800 seconds — below that a "
                + "normal Listening part cannot finish uploading, above it a hung socket holds an "
                + "import stage for half an hour.";
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
         * Import:Transcription:Provider selects OpenAi; the key itself still
         * lives at Ai:OpenAi:ApiKey, the one place this deployment's OpenAI
         * credential is written down. A deployment that filled in everything
         * above but never gave OpenAi a key looks exactly as enabled as one
         * that has.
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
        + $"BaseUrl={SecretRedaction.Url(BaseUrl)}, TimeoutSeconds={TimeoutSeconds}";
}
