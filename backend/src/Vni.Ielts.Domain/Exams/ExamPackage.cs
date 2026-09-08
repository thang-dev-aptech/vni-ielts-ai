using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Exams;

public enum ExamPackageSourceKind { Json, Zip }

/// <summary>
/// One exam found inside a package, surfaced so an operator can confirm before
/// N drafts are created at once from a multi-exam ZIP — not because the content
/// is uncertain (it is schema-valid by definition, unlike Phase 6's AI
/// candidates), but because creating N drafts by mistake is not a cheap undo.
/// → `docs/development/phase-2-content-import-plan.md` "Vì sao tách nội dung
/// nhập ra khỏi review-candidate"
/// </summary>
public sealed record ExamPackageEntry(
    string ProposedDefinitionId,
    string Title,
    ExamModule Module,
    int QuestionCount);

/// <summary>
/// One upload, from receipt through to the drafts it produced or the reasons it
/// did not. Deliberately reuses <see cref="PackageImportStatus"/> and
/// <see cref="PackageFinding"/> from <c>ParsedExamCandidate.cs</c> rather than
/// declaring a second copy — both content sources (deterministic JSON/ZIP here,
/// AI-proposed content in Phase 6) converge on the same status vocabulary
/// (`C-23`), even though this aggregate never reaches <c>Parsing</c> or
/// <c>NeedsReview</c>: this content is schema-valid by definition, not a
/// proposal a reviewer has to judge.
///
/// No persistence attributes — same boundary as every other domain type.
/// </summary>
public sealed class ExamPackage
{
    private readonly List<PackageFinding> _findings;
    private readonly List<ExamPackageEntry> _entries;
    private readonly List<string> _createdVersionIds;

    private ExamPackage(
        string id,
        ExamPackageSourceKind sourceKind,
        UserId uploadedBy,
        string sha256,
        string fileName,
        string uploadRef,
        PackageImportStatus status,
        IEnumerable<PackageFinding> findings,
        IEnumerable<ExamPackageEntry> entries,
        IEnumerable<string> createdVersionIds,
        int version,
        bool uploadPurged,
        string? uploadPurgeState,
        string? uploadPurgeClaimId,
        DateTimeOffset? uploadPurgeClaimedAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A package needs an id.", nameof(id));
        if (string.IsNullOrWhiteSpace(sha256)) throw new ArgumentException("A package needs a content hash.", nameof(sha256));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A package needs a file name.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(uploadRef)) throw new ArgumentException("A package needs a storage reference.", nameof(uploadRef));
        if (version < 0) throw new ArgumentOutOfRangeException(nameof(version));

        Id = id;
        SourceKind = sourceKind;
        UploadedBy = uploadedBy;
        Sha256 = sha256;
        FileName = fileName;
        UploadRef = uploadRef;
        Status = status;
        _findings = [.. findings];
        _entries = [.. entries];
        _createdVersionIds = [.. createdVersionIds];
        Version = version;
        UploadPurged = uploadPurged;
        UploadPurgeState = uploadPurgeState;
        UploadPurgeClaimId = uploadPurgeClaimId;
        UploadPurgeClaimedAt = uploadPurgeClaimedAt;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public string Id { get; }
    public ExamPackageSourceKind SourceKind { get; }
    public UserId UploadedBy { get; }
    public string Sha256 { get; }

    /// <summary>Display only — never used to resolve a path. → `zip-ingestion-security.md` A1/A4</summary>
    public string FileName { get; }

    /// <summary>Opaque key into <c>IPackageUploadStore</c> — the immutable artefact every later stage validates against, never re-read from a mutable source (`A8`).</summary>
    public string UploadRef { get; }

    public PackageImportStatus Status { get; private set; }
    public IReadOnlyList<PackageFinding> Findings => _findings;
    public IReadOnlyList<ExamPackageEntry> Entries => _entries;

    /// <summary>Populated only once, by <see cref="MarkImported"/> — what a repeat <c>GET</c> or a double-confirm answers with instead of redoing anything.</summary>
    public IReadOnlyList<string> CreatedVersionIds => _createdVersionIds;

    /// <summary>Bumped on every mutation. What a confirm endpoint's optimistic-concurrency check compares against.</summary>
    public int Version { get; private set; }

    /// <summary>Plan 07's retention cleanup already ran on this package's raw upload — <see cref="UploadRef"/> no longer resolves.</summary>
    public bool UploadPurged { get; private set; }
    public string? UploadPurgeState { get; private set; }
    public string? UploadPurgeClaimId { get; private set; }
    public DateTimeOffset? UploadPurgeClaimedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ExamPackage Create(
        string id,
        ExamPackageSourceKind sourceKind,
        UserId uploadedBy,
        string sha256,
        string fileName,
        string uploadRef,
        DateTimeOffset now) =>
        new(id, sourceKind, uploadedBy, sha256, fileName, uploadRef,
            PackageImportStatus.Uploaded, [], [], [], 0, uploadPurged: false,
            uploadPurgeState: null, uploadPurgeClaimId: null, uploadPurgeClaimedAt: null, now, now);

    public static ExamPackage Rehydrate(
        string id,
        ExamPackageSourceKind sourceKind,
        UserId uploadedBy,
        string sha256,
        string fileName,
        string uploadRef,
        PackageImportStatus status,
        IEnumerable<PackageFinding> findings,
        IEnumerable<ExamPackageEntry> entries,
        IEnumerable<string> createdVersionIds,
        int version,
        bool uploadPurged,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string? uploadPurgeState = null,
        string? uploadPurgeClaimId = null,
        DateTimeOffset? uploadPurgeClaimedAt = null) =>
        new(id, sourceKind, uploadedBy, sha256, fileName, uploadRef,
            status, findings, entries, createdVersionIds, version, uploadPurged,
            uploadPurgeState, uploadPurgeClaimId, uploadPurgeClaimedAt, createdAt, updatedAt);

    /// <summary>Uploaded → Scanning. Malware/AV scanning, ahead of structural validation. → Plan 03</summary>
    public void MarkScanning(DateTimeOffset now)
    {
        RequireStatus(PackageImportStatus.Uploaded, "moved to scanning");
        Status = PackageImportStatus.Scanning;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Scanning or Uploaded → Validating. JSON with no media has nothing to scan, so it may skip straight here.</summary>
    public void MarkValidating(DateTimeOffset now)
    {
        if (Status is not (PackageImportStatus.Uploaded or PackageImportStatus.Scanning))
        {
            throw new InvalidOperationException(
                $"Cannot be moved to validating from {Status} — this package must be Uploaded or Scanning.");
        }

        Status = PackageImportStatus.Validating;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>
    /// Scanning or Validating → Rejected. Findings are required — a rejection
    /// with nothing to show the uploader is not actionable.
    /// </summary>
    public void Reject(IReadOnlyList<PackageFinding> findings, DateTimeOffset now)
    {
        if (Status is not (PackageImportStatus.Scanning or PackageImportStatus.Validating))
        {
            throw new InvalidOperationException(
                $"Cannot be rejected from {Status} — this package must be Scanning or Validating.");
        }

        if (findings.Count == 0)
            throw new ArgumentException("A rejection requires at least one finding.", nameof(findings));

        Status = PackageImportStatus.Rejected;
        _findings.AddRange(findings);
        UpdatedAt = now;
        Version++;
    }

    public void MarkParsing(DateTimeOffset now)
    {
        RequireStatus(PackageImportStatus.Validating, "moved to parsing");
        Status = PackageImportStatus.Parsing;
        UpdatedAt = now;
        Version++;
    }

    public void MarkNeedsReview(DateTimeOffset now, IReadOnlyList<PackageFinding>? findings = null)
    {
        RequireStatus(PackageImportStatus.Parsing, "marked as needing review");
        if (findings is not null) _findings.AddRange(findings);
        Status = PackageImportStatus.NeedsReview;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Validating → ReadyToImport. A multi-exam ZIP, waiting for the operator to confirm before N drafts are created.</summary>
    public void MarkReadyToImport(IReadOnlyList<ExamPackageEntry> entries, DateTimeOffset now)
    {
        RequireStatus(PackageImportStatus.Validating, "marked ready to import");
        if (entries.Count == 0)
            throw new ArgumentException("Ready-to-import requires at least one entry.", nameof(entries));

        Status = PackageImportStatus.ReadyToImport;
        _entries.AddRange(entries);
        UpdatedAt = now;
        Version++;
    }

    /// <summary>
    /// Validating or ReadyToImport → Imported. Single-source content goes
    /// straight from Validating; a confirmed multi-exam ZIP goes from
    /// ReadyToImport. Either way this package never creates a second Draft.
    /// Records the ids it produced — what a double-confirm reads back instead
    /// of creating a second batch.
    /// </summary>
    public void MarkImported(IReadOnlyList<string> createdVersionIds, DateTimeOffset now)
    {
        if (Status is not (PackageImportStatus.Validating or PackageImportStatus.ReadyToImport or PackageImportStatus.NeedsReview))
        {
            throw new InvalidOperationException(
                $"Cannot be imported from {Status} — this package must be Validating, ReadyToImport, or NeedsReview.");
        }

        if (createdVersionIds.Count == 0)
            throw new ArgumentException("Importing requires at least one created version id.", nameof(createdVersionIds));

        Status = PackageImportStatus.Imported;
        foreach (var id in createdVersionIds)
        {
            if (!_createdVersionIds.Contains(id))
                _createdVersionIds.Add(id);
        }
        UpdatedAt = now;
        Version++;
    }

    public void AddCreatedVersion(string createdVersionId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(createdVersionId))
            throw new ArgumentException("Created version id cannot be empty.", nameof(createdVersionId));

        if (!_createdVersionIds.Contains(createdVersionId))
        {
            _createdVersionIds.Add(createdVersionId);
            UpdatedAt = now;
            Version++;
        }
    }

    /// <summary>Any non-terminal state → Failed. An unexpected processing error, not a validation rejection.</summary>
    public void MarkFailed(DateTimeOffset now)
    {
        if (Status is PackageImportStatus.Imported or PackageImportStatus.Rejected or PackageImportStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Cannot be marked failed from {Status} — this package has already reached a terminal state.");
        }

        Status = PackageImportStatus.Failed;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>
    /// Any terminal state → still that state, with the raw upload gone. Plan
    /// 07's retention cleanup — only valid once a package can no longer
    /// change (a package still <c>Validating</c> needs its upload to
    /// actually validate), and only once.
    /// </summary>
    public void MarkUploadPurged(DateTimeOffset now)
    {
        if (Status is not (PackageImportStatus.Imported or PackageImportStatus.Rejected or PackageImportStatus.Failed))
        {
            throw new InvalidOperationException(
                $"Cannot purge the upload from {Status} — this package must have reached a terminal state.");
        }

        if (UploadPurged)
            throw new InvalidOperationException("This package's upload has already been purged.");

        UploadPurged = true;
        UploadPurgeState = "Purged";
        UploadPurgeClaimId = null;
        UploadPurgeClaimedAt = null;
        UpdatedAt = now;
        Version++;
    }

    private void RequireStatus(PackageImportStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"Cannot be {action} from {Status} — this package must be {expected}.");
        }
    }
}
