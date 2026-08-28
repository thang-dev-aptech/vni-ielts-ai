namespace Vni.Ielts.Infrastructure.Observability;

/// <summary>
/// The numbers an alert would fire on.
///
/// <b>F4.3 — configuration, not code, and the distinction is the point.</b>
/// Every value here answers a question nobody has been asked yet: how long may
/// a learner wait for a Writing band before somebody is woken up? That is a
/// product decision about what this service promises, not a technical fact,
/// and hard-coding one would turn a guess into a commitment nobody made.
/// → `G-11`, and the same reasoning as the RPO/RTO targets in
/// `docs/development/backup-and-restore.md`.
///
/// <b>So the defaults below are starting points, deliberately loose.</b> They
/// are chosen to catch a system that is genuinely stuck rather than one that
/// is merely busy — an alert that fires during normal load is an alert people
/// learn to close without reading, which is worse than no alert at all.
///
/// The metrics themselves are emitted unconditionally (see
/// <see cref="Telemetry"/>); these only decide when a value becomes a page.
/// Nothing here chooses an alerting vendor — a threshold is a number, and
/// whatever consumes the OTLP stream applies it.
/// </summary>
public sealed class AlertThresholds
{
    public const string SectionName = "Alerts";

    /// <summary>
    /// How long the oldest owed marking may wait before it is an incident.
    ///
    /// <b>Fifteen minutes, and this is the value most likely to be wrong.</b>
    /// The learner-visible consequence of exceeding it is a results screen
    /// still showing a dash, which for an exam product is the complaint that
    /// actually arrives. `[BUSINESS DECISION]` — the product owner sets what
    /// the service promises; this is only a default that catches "stuck".
    /// </summary>
    public int QueueOldestAgeSeconds { get; set; } = 900;

    /// <summary>
    /// Backlog depth that suggests the workers cannot keep up.
    ///
    /// Depth alone never means much — see `QueueBacklog`. This exists so a
    /// runaway enqueue is visible even while ages stay low.
    /// </summary>
    public long QueueDepth { get; set; } = 200;

    /// <summary>
    /// Share of API responses that may be 5xx over the evaluation window.
    ///
    /// A rate rather than a count: a hundred errors an hour is a catastrophe
    /// on a quiet service and a rounding error on a busy one.
    /// </summary>
    public double ApiServerErrorRate { get; set; } = 0.02;

    /// <summary>p99 latency, in seconds, past which the API is considered slow.</summary>
    public double ApiLatencyP99Seconds { get; set; } = 2.0;

    /// <summary>
    /// Consecutive readiness failures before the dependency is called down.
    ///
    /// More than one, because a single failed probe is usually a timeout under
    /// momentary load, and paging on it teaches people to ignore the page.
    /// </summary>
    public int ReadinessConsecutiveFailures { get; set; } = 3;

    /// <summary>Object-storage errors in the window before it is called degraded.</summary>
    public int ObjectStorageErrors { get; set; } = 5;

    /// <summary>
    /// How stale continuous backup coverage may become. → F3.4
    ///
    /// Mirrors `VNI_PBM_MAX_PITR_LAG_SECONDS` in `scripts/pbm-alert.sh`, which
    /// is what actually evaluates it — backup freshness is measured outside
    /// these processes because a process that has crashed cannot report on its
    /// own backups. Restated here so the whole alert contract is readable in
    /// one place.
    /// </summary>
    public int BackupPitrLagSeconds { get; set; } = 300;

    /// <summary>How old the newest full backup may be. Mirrors `VNI_PBM_MAX_BACKUP_AGE_SECONDS`.</summary>
    public int BackupFullAgeSeconds { get; set; } = 93_600;
}
