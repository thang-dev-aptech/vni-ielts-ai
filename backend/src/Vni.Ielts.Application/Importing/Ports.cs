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
