namespace Vni.Ielts.Infrastructure.Persistence;

/// <summary>
/// How long a package's raw upload stays in quarantine storage after it
/// reaches a terminal state (<c>Imported</c>, <c>Rejected</c>, or <c>Failed</c>).
///
/// <b><c>RawUploadRetentionDays</c> defaults to <c>null</c>, deliberately.</b>
/// `phase-2-content-import-plan.md`'s own "needs owner confirmation" list
/// names this exact number as undecided (item 4) — picking one here would be
/// exactly the invented default CLAUDE.md rule G-11 forbids. <c>null</c> means
/// the cleanup job is a configured seam with nothing behind it yet: it runs,
/// finds nothing to do (every package looks "not yet due"), and does nothing,
/// rather than silently adopting a number nobody decided.
///
/// <b>No separate quarantine-copy retention.</b> The plan's own wording
/// assumes an extracted quarantine directory on disk; this pipeline's
/// structural validator (Plan 03) never extracts anything to disk — it reads
/// ZIP entries in memory and never writes a file. The raw upload blob in
/// <see cref="Application.Exams.IPackageUploadStore"/> is the only artefact
/// that exists to retain.
/// </summary>
public sealed class PackageRetentionOptions
{
    public const string SectionName = "PackageRetention";

    public int? RawUploadRetentionDays { get; set; }

    /// <summary>
    /// How long a parsed candidate without a <c>DraftExamVersionId</c> may
    /// stay in the database after it was confirmed or rejected. Candidates
    /// that produced a Draft are never purged by retention — they are the
    /// evidence behind a visible exam version. <b>Null means no purge.</b>
    ///
    /// <para>
    /// <c>PendingReview</c> candidates have no status-change timestamp;
    /// they are skipped until the owner states a rule for aging off
    /// unresolved proposals.
    /// </para>
    /// </summary>
    public int? ParsedCandidateRetentionDays { get; set; }

    /// <summary>
    /// How long a source-document grouping proposal stays after the package
    /// was last updated. <b>Null means no purge.</b>
    /// </summary>
    public int? GroupingProposalRetentionDays { get; set; }

    /// <summary>
    /// How long an extraction run row (metadata only — no source text) stays
    /// after it completed. <b>Null means no purge.</b>
    /// </summary>
    public int? ExtractionRunRetentionDays { get; set; }

    /// <summary>
    /// How often the retention processor runs, in seconds. If null, uses
    /// the default Worker poll cadence. Do not invent a cron expression.
    /// </summary>
    public int? IntervalSeconds { get; set; }
}
