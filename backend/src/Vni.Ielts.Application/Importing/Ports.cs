using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// The single deterministic gate for every exam-package producer.
/// The implementation belongs to Infrastructure because JSON Schema is an adapter detail.
/// </summary>
public interface IExamPackageValidator
{
    PackageValidationResult Validate(
        string packageJson, ExamDefinitionId definitionId, int versionNumber);
}

public sealed record PackageFinding(string Severity, string Code, string Path, string Message);

public sealed record PackageValidationResult(
    bool IsValid, ExamVersion? Version, IReadOnlyList<PackageFinding> Findings);

/// <summary>
/// Provider-neutral structured parser. Providers may produce a package draft; they cannot
/// persist or publish an exam through this port.
/// </summary>
public interface IExamSourceParser
{
    Task<ParsedExamPackage> ParseAsync(ExtractedImportSource source, CancellationToken ct);
}

public interface IImportDraftStore
{
    /// <summary>Idempotent for an existing draft id with identical import content.</summary>
    Task SaveAsync(ExamImportDraft draft, CancellationToken ct);
    Task<ExamImportDraft?> FindAsync(Guid draftId, CancellationToken ct);
    Task<bool> ReplaceAsync(ExamImportDraft draft, int expectedRevision, CancellationToken ct);

    /// <summary>Newest <see cref="ExamImportDraft.CreatedAt"/> first; drafts with no timestamp sort last.</summary>
    Task<IReadOnlyList<ExamImportDraft>> ListAsync(CancellationToken ct);
}

public enum ImportApprovalCommitStatus
{
    Committed,
    AlreadyCommitted,
    RevisionConflict,
    IdentityConflict,
    AssetMissing,
    AssetConflict,
    Canceled,
}

public sealed record ImportApprovalCommitResult(
    ImportApprovalCommitStatus Status, ExamImportDraft? Draft)
{
    public static ImportApprovalCommitResult Committed(ExamImportDraft draft) =>
        new(ImportApprovalCommitStatus.Committed, draft);

    public static ImportApprovalCommitResult AlreadyCommitted(ExamImportDraft draft) =>
        new(ImportApprovalCommitStatus.AlreadyCommitted, draft);

    public static ImportApprovalCommitResult RevisionConflict() =>
        new(ImportApprovalCommitStatus.RevisionConflict, null);

    public static ImportApprovalCommitResult IdentityConflict() =>
        new(ImportApprovalCommitStatus.IdentityConflict, null);

    public static ImportApprovalCommitResult AssetMissing() =>
        new(ImportApprovalCommitStatus.AssetMissing, null);

    public static ImportApprovalCommitResult AssetConflict() =>
        new(ImportApprovalCommitStatus.AssetConflict, null);

    public static ImportApprovalCommitResult Canceled() =>
        new(ImportApprovalCommitStatus.Canceled, null);
}

/// <summary>
/// Persists the approved import draft and the catalogue Draft in one commit.
/// Implementations must not leak storage types into Application.
/// </summary>
public interface IImportApprovalCommitter
{
    Task<ImportApprovalCommitResult> CommitAsync(
        ExamImportDraft approvedDraft,
        int expectedRevision,
        ExamVersion catalogueDraft,
        CancellationToken ct);
}

/// <summary>
/// Test seam around approval promotion. Production registers a no-op hook;
/// only a test host may replace it. The committer itself is mandatory.
/// </summary>
public interface IImportApprovalCommitHooks
{
    Task AfterCatalogueWriteAsync(ExamVersionId versionId, CancellationToken ct);

    Task AfterAssetPromotionAsync(Guid draftId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Test seam around import draft linkage. Production registers a no-op hook;
/// only a test host may replace it.
/// </summary>
public interface IImportLinkageCommitHooks
{
    Task AfterDraftInsertAsync(Guid draftId, string packageId, CancellationToken ct);
    Task AfterPackageUpdateAsync(Guid draftId, string packageId, CancellationToken ct);
}

public sealed class NoOpImportLinkageCommitHooks : IImportLinkageCommitHooks
{
    public Task AfterDraftInsertAsync(Guid draftId, string packageId, CancellationToken ct) => Task.CompletedTask;
    public Task AfterPackageUpdateAsync(Guid draftId, string packageId, CancellationToken ct) => Task.CompletedTask;
}

public sealed record SourceExtractionLimits(
    long MaxSourceBytes,
    int MaxPages,
    int MaxEmbeddedMedia,
    long MaxEmbeddedMediaBytes,
    TimeSpan Timeout)
{
    public static SourceExtractionLimits Default { get; } =
        new(25 * 1024 * 1024, 250, 100, 20 * 1024 * 1024, TimeSpan.FromSeconds(30));

    /// <summary>
    /// Operator CLI limits for Cambridge books. Cam 20 alone is ~250 MB on
    /// disk; the HTTP import path keeps the tighter <see cref="Default"/>.
    /// </summary>
    public static SourceExtractionLimits Operator { get; } =
        new(300 * 1024 * 1024, 300, 100, 20 * 1024 * 1024, TimeSpan.FromMinutes(2));
}

public sealed record PrivateImportAsset(
    string Reference, string ContentType, long Length, string Sha256);

public interface IPrivateImportAssetStore
{
    Task<string> PutPrivateAsync(
        string key, Stream content, string contentType, string sha256, CancellationToken ct);
}

public interface ISourceDocumentExtractor
{
    Task<SourceExtractionResult> ExtractAsync(
        string sandboxRoot,
        string relativePath,
        SourceExtractionLimits limits,
        CancellationToken ct);
}

public sealed record SourceExtractionResult(
    bool IsSuccess,
    ExtractedImportSource? Source,
    IReadOnlyList<PrivateImportAsset> Assets,
    IReadOnlyList<PackageFinding> Findings);
