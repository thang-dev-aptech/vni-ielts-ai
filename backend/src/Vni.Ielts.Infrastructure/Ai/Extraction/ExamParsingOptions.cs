using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Infrastructure.Ai.Extraction;

/// <summary>
/// Whether exam parsing runs against a production provider (vendor-direct,
/// attested) or a synthetic-test provider (reseller permitted with synthetic
/// rights).
///
/// <para>
/// <b>Two values, and the third is absence.</b> Unset means nobody has decided
/// which environment this install is, and that is a composition problem when
/// <see cref="ExamParsingOptions.Enabled"/> is true.
/// </para>
/// </summary>
public enum ExamParsingEnvironment
{
    /// <summary>
    /// Official vendor endpoints only. Reseller hosts are refused even when
    /// contracted for other routes (e.g. Writing evaluation). The 02/09/2026
    /// Writing reseller override does not cover uploaded exam papers.
    /// </summary>
    Production,

    /// <summary>
    /// Reseller endpoints may be used, but only with
    /// <see cref="ImportDataClassification.Synthetic"/> or
    /// <see cref="ImportDataClassification.RightsCleared"/> material.
    /// Restricted material is still refused before the call — Plan 03.
    /// </summary>
    SyntheticTest,
}

/// <summary>
/// Which provider parses raw exam packages, and under what limits.
///
/// ── Why nearly everything here is nullable ────────────────────────────────
///
/// <b>The values this product's owner has not decided are absent, not
/// defaulted.</b> A retry budget, a fallback provider and a spend cap are
/// decisions about money and about how long a package may sit unreviewed;
/// choosing one here would put a number nobody agreed into production and make
/// it look agreed. So an unset value means "no such policy", and the code
/// behaves as if the policy were absent rather than guessing at it. → <c>G-11</c>
///
/// <b>What is not nullable is not policy.</b> <see cref="Enabled"/>,
/// <see cref="Provider"/> and <see cref="PromptVersion"/> are the three facts
/// without which there is no configured parser at all, and
/// <see cref="TimeoutSeconds"/> is the one limit a background job cannot be run
/// without — a provider call with no deadline in a worker runs until somebody
/// notices the queue. Absent any of them, the product uses the deterministic
/// review-only parser and no provider is called.
/// </summary>
public sealed class ExamParsingOptions
{
    public const string SectionName = "ExamParsing";

    /// <summary>
    /// Whether AI-assisted parsing runs at all. <b>Default false.</b>
    ///
    /// Separate from <see cref="Provider"/> so an install can be configured
    /// ahead of being switched on, and switched off without losing its
    /// configuration.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// <c>Production</c> or <c>SyntheticTest</c>. Required when
    /// <see cref="Enabled"/> is true.
    ///
    /// <para>
    /// <b>Production refuses a reseller BaseUrl for exam parsing.</b> The
    /// 02/09/2026 owner override that permitted a contracted reseller for
    /// Writing evaluation does not extend to uploaded exam papers, which are
    /// somebody's copyright and may contain restricted material. A contracted
    /// reseller host (<c>api.vietapi.tech</c>, <c>apithat.dev</c>) is still a
    /// third-party processor for this route.
    /// </para>
    ///
    /// <para>
    /// <b>SyntheticTest permits a reseller</b> but only when
    /// <see cref="SourceRights"/> resolves to <c>Synthetic</c> or
    /// <c>RightsCleared</c> — restricted material is still refused before the
    /// call, exactly as Plan 03 established.
    /// </para>
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// <c>OpenAi</c> or <c>Gemini</c>. No default: the provider that parses a
    /// paper is a decision with a cost and a calibration consequence, not
    /// whichever adapter happens to be registered first.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Tried when <see cref="Provider"/> fails transiently. Null means there is
    /// no fallback — the attempt fails and the package waits for staff, which
    /// is the honest outcome when nobody has said a second provider is wanted.
    /// </summary>
    public string? FallbackProvider { get; set; }

    /// <summary>
    /// The prompt version this install is pinned to, checked against
    /// <see cref="ExamExtractionPrompt.Version"/> at composition. Stated rather
    /// than read from the code so that upgrading the prompt is a visible
    /// configuration change and a stored run record can be trusted.
    /// </summary>
    public string? PromptVersion { get; set; }

    /// <summary>
    /// How long one provider call may take. Required when enabled.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// How many times one provider may be tried after a transient failure.
    /// Null means exactly one attempt: no retry policy has been agreed, and a
    /// retry budget invented here would multiply an unpriced call by a number
    /// nobody chose.
    /// </summary>
    public int? MaxAttempts { get; set; }

    /// <summary>
    /// The response size cap. Null uses
    /// <see cref="ExamExtractionContract.DefaultMaxResponseBytes"/>, which is a
    /// structural memory bound rather than a product policy.
    /// </summary>
    public int? MaxResponseBytes { get; set; }

    /// <summary>
    /// What class of material this install may show a provider —
    /// <c>Synthetic</c>, <c>RightsCleared</c> or <c>Restricted</c>.
    ///
    /// <para>
    /// <b>Absent means <see cref="ImportDataClassification.Restricted"/>.</b>
    /// An uploaded paper is somebody's copyright until a person who holds the
    /// rights says otherwise, so the permissive value is the one that has to be
    /// typed. A reseller endpoint refuses restricted material outright.
    /// </para>
    /// </summary>
    public string? SourceRights { get; set; }

    /// <summary>
    /// A per-package spend cap.
    ///
    /// <para>
    /// <b>Setting it stops parsing rather than enabling a limit, and that is
    /// deliberate.</b> This route's per-token price is not published and its
    /// usage block has been observed carrying another vendor's fields, so no
    /// call can be priced — which means a cap here could not be enforced, only
    /// displayed. A control that cannot act is worse than an absent one,
    /// because whoever set it stops watching. Enforcing a real cap needs a
    /// price source and belongs to the production-controls plan.
    /// </para>
    /// </summary>
    public decimal? MaxCostUsdPerPackage { get; set; }

    /// <summary>
    /// An operational attestation that the configured providers' data-retention
    /// and no-training policies have been reviewed for production use.
    ///
    /// <para>
    /// <b>Required when <see cref="Environment"/> is <c>Production</c>.</b>
    /// False or missing → composition problem. This is an install-time
    /// declaration by an operator, not a legal close of <c>B-2</c>: it says
    /// "I have checked that the vendor's current terms include a no-training
    /// clause for our tier", not "we have filed a CTIA".
    /// </para>
    /// </summary>
    public bool ProviderRetentionNoTrainingAsserted { get; set; }

    /// <summary>
    /// Maximum number of packages parsed concurrently by the Worker.
    ///
    /// <para>
    /// <b>Null means no extra limiter beyond the existing Worker lease.</b>
    /// This is a seam for a future concurrency control. Do not default it to
    /// a number: a parallelism budget invented here would multiply an unpriced
    /// call by a factor nobody chose. → <c>G-11</c>
    /// </para>
    /// </summary>
    public int? MaxConcurrentPackages { get; set; }

    /// <summary>
    /// The rights classification in force, failing closed when unstated.
    /// </summary>
    public ImportDataClassification ResolvedRights()
    {
        if (string.IsNullOrWhiteSpace(SourceRights)) return ImportDataClassification.Restricted;

        foreach (var name in Enum.GetNames<ImportDataClassification>())
        {
            if (string.Equals(name, SourceRights, StringComparison.OrdinalIgnoreCase))
                return Enum.Parse<ImportDataClassification>(name);
        }

        throw new InvalidOperationException(
            $"ExamParsing:SourceRights is '{SourceRights}', which is not one of "
            + $"{string.Join(", ", Enum.GetNames<ImportDataClassification>())}.");
    }

    public int ResolvedMaxResponseBytes() =>
        MaxResponseBytes ?? ExamExtractionContract.DefaultMaxResponseBytes;

    public int ResolvedMaxAttempts() => MaxAttempts ?? 1;

    /// <summary>
    /// The providers to try, in order. Empty when nothing is configured.
    /// </summary>
    public IReadOnlyList<string> ProviderOrder()
    {
        var order = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(Provider)) order.Add(Provider);

        if (!string.IsNullOrWhiteSpace(FallbackProvider)
            && !order.Contains(FallbackProvider, StringComparer.OrdinalIgnoreCase))
        {
            order.Add(FallbackProvider);
        }

        return order;
    }

    /// <summary>
    /// The resolved environment, or null when not set.
    /// </summary>
    public ExamParsingEnvironment? ResolvedEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment)) return null;

        foreach (var name in Enum.GetNames<ExamParsingEnvironment>())
        {
            if (string.Equals(name, Environment, StringComparison.OrdinalIgnoreCase))
                return Enum.Parse<ExamParsingEnvironment>(name);
        }

        return null;
    }

    /// <summary>
    /// Every reason this configuration cannot run a provider call, or none.
    ///
    /// <para>
    /// <b>All the problems, not the first one.</b> A configuration gate that
    /// stops at the first fault is answered one deployment at a time.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Problems(AiOptions ai)
    {
        ArgumentNullException.ThrowIfNull(ai);

        var problems = new List<string>();

        if (!Enabled) return problems;

        // ── Environment classification ─────────────────────────────────────

        var env = ResolvedEnvironment();

        if (env is null)
        {
            if (!string.IsNullOrWhiteSpace(Environment))
            {
                problems.Add(
                    $"ExamParsing:Environment is '{Environment}', which is not one of "
                    + $"{string.Join(", ", Enum.GetNames<ExamParsingEnvironment>())}.");
            }
            else
            {
                problems.Add(
                    "ExamParsing:Enabled is true but ExamParsing:Environment is not set. "
                    + "Set it to Production or SyntheticTest.");
            }
        }

        // ── Core requirements (all environments) ───────────────────────────

        if (string.IsNullOrWhiteSpace(Provider))
        {
            problems.Add(
                "ExamParsing:Enabled is true but ExamParsing:Provider is not set and has no "
                + "default. → G-11");
        }

        if (string.IsNullOrWhiteSpace(PromptVersion))
        {
            problems.Add(
                "ExamParsing:Enabled is true but ExamParsing:PromptVersion is not set. A stored "
                + "proposal that cannot name the instruction that produced it cannot be told from "
                + "a prompt regression later.");
        }
        else if (!string.Equals(PromptVersion, ExamExtractionPrompt.Version, StringComparison.Ordinal))
        {
            problems.Add(
                $"ExamParsing:PromptVersion pins a prompt this build does not carry. This build "
                + $"has {ExamExtractionPrompt.Version}.");
        }

        if (TimeoutSeconds is null or <= 0)
        {
            problems.Add(
                "ExamParsing:TimeoutSeconds is not set to a positive value. A provider call with "
                + "no deadline inside a worker runs until somebody notices the queue.");
        }

        if (MaxAttempts is not null and (< 1 or > 3))
            problems.Add("ExamParsing:MaxAttempts must be between 1 and 3 when it is set.");

        if (MaxResponseBytes is not null and <= 0)
            problems.Add("ExamParsing:MaxResponseBytes must be positive when it is set.");

        if (MaxCostUsdPerPackage is not null)
        {
            problems.Add(
                "ExamParsing:MaxCostUsdPerPackage is set, and this build cannot enforce it: the "
                + "configured route publishes no per-token price, so no call can be costed. "
                + "Remove the value to run without a spend cap, and treat the cap itself as "
                + "production-controls work.");
        }

        // ── Production-only assertions ──────────────────────────────────────

        if (env is ExamParsingEnvironment.Production)
        {
            if (!ProviderRetentionNoTrainingAsserted)
            {
                problems.Add(
                    "ExamParsing:Environment is Production but "
                    + "ExamParsing:ProviderRetentionNoTrainingAsserted is not true. Production "
                    + "exam parsing requires an operational attestation that the configured "
                    + "providers' data-retention and no-training policies have been reviewed. "
                    + "This is not a legal close of B-2.");
            }
        }

        // ── Rights classification ──────────────────────────────────────────

        try
        {
            ResolvedRights();
        }
        catch (InvalidOperationException e)
        {
            problems.Add(e.Message);
        }

        // ── Provider-specific checks ──────────────────────────────────────

        foreach (var section in ProviderOrder())
        {
            if (section is not ("OpenAi" or "Gemini"))
            {
                problems.Add(
                    $"ExamParsing names provider '{section}'. Only OpenAi and Gemini exist; a "
                    + "third provider is an owner decision, not a configuration value. "
                    + "→ CLAUDE.md rule 6");
                continue;
            }

            // Production requires vendor-direct endpoints. A contracted
            // reseller (api.vietapi.tech, apithat.dev) is permitted for
            // Writing evaluation under the 02/09/2026 decision but is NOT
            // permitted for exam-package copyright material on this route.
            if (env is ExamParsingEnvironment.Production)
            {
                var providerOptions = string.Equals(section, "OpenAi", StringComparison.OrdinalIgnoreCase)
                    ? ai.OpenAi : ai.Gemini;

                if (!AiProviderPolicy.IsVendorEndpoint(section, providerOptions.BaseUrl))
                {
                    problems.Add(
                        $"ExamParsing:Environment is Production and {section} BaseUrl "
                        + $"({providerOptions.BaseUrl ?? "not set"}) is not a vendor-direct "
                        + "endpoint. Production exam parsing requires official vendor endpoints. "
                        + "The 02/09/2026 Writing reseller override does not cover uploaded "
                        + "exam papers.");
                }
            }

            try
            {
                AiEgress.Authorise(ai, section, AiDataClassification.Synthetic);
            }
            catch (AiEgressRefusedException e)
            {
                problems.Add($"ExamParsing cannot use {section}: {e.Message}");
            }
        }

        return problems;
    }

    /// <summary>
    /// Whether a configured provider parser may be composed. False leaves the
    /// deterministic review-only parser in place and calls nobody.
    /// </summary>
    public bool IsConfiguredFor(AiOptions ai) => Enabled && Problems(ai).Count == 0;
}
