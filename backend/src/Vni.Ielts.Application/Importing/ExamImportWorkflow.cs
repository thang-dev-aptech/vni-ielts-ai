using System.Security.Cryptography;
using System.Text;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Importing;

public enum ExamImportRoute
{
    StructuredPackage,
    AiParsedSource,
}

public enum ImportApprovalState
{
    ReviewRequired,
    Approved,
}

public sealed record ParserRunMetadata(
    string Provider, string Model, string PromptVersion, string RequestId);

public enum ImportDataClassification { Synthetic, RightsCleared, Restricted }

public sealed record ExtractedImportSource(
    string Name, string MediaType, string Text, string SourceSha256, string TextSha256,
    ImportDataClassification Classification = ImportDataClassification.Restricted);

public sealed record ParsedExamPackage(string PackageJson, ParserRunMetadata Metadata);

public enum ImportReviewCategory
{
    Questions,
    Options,
    WordLimits,
    AcceptedVariants,
    TranscriptAndEvidence,
    AssetMapping,
}

public sealed record ImportReviewChecklist(IReadOnlySet<ImportReviewCategory> Confirmed)
{
    public static ImportReviewChecklist Empty { get; } = new(new HashSet<ImportReviewCategory>());
    public bool IsComplete => Enum.GetValues<ImportReviewCategory>().All(Confirmed.Contains);
}

public sealed record ImportReviewWarning(
    string Id, ImportReviewCategory Category, string Path, string Message, bool Resolved);

public sealed record ExamImportDraft(
    Guid Id,
    ExamDefinitionId DefinitionId,
    int VersionNumber,
    ExamImportRoute Route,
    string SourceHash,
    string PackageHash,
    ExamVersion Version,
    ParserRunMetadata? Parser,
    ImportApprovalState ApprovalState,
    IReadOnlyList<PackageFinding> Findings,
    string SourceText,
    string PackageJson,
    ImportReviewChecklist Checklist,
    IReadOnlyList<ImportReviewWarning> Warnings,
    int Revision,
    string? ReviewedBy);

public sealed record ExamImportAttempt(
    bool IsAccepted, ExamImportDraft? Draft, IReadOnlyList<PackageFinding> Findings)
{
    public static ExamImportAttempt Rejected(IReadOnlyList<PackageFinding> findings) =>
        new(false, null, findings);

    public static ExamImportAttempt Accepted(ExamImportDraft draft) =>
        new(true, draft, draft.Findings);
}

/// <summary>
/// Joins the two ingestion routes at one deterministic validation boundary.
///
/// There is intentionally no publication dependency here. A successful import only writes an
/// unapproved draft; publication is a later, permissioned review action.
/// </summary>
public sealed class ExamImportWorkflow(
    IExamPackageValidator validator,
    IImportDraftStore drafts,
    IExamSourceParser parser)
{
    public Task<ExamImportAttempt> ImportStructuredAsync(
        string packageJson,
        ExamDefinitionId definitionId,
        int versionNumber,
        CancellationToken ct) =>
        ValidateAndSaveAsync(
            packageJson,
            definitionId,
            versionNumber,
            ExamImportRoute.StructuredPackage,
            Hash(packageJson),
            packageJson,
            parserMetadata: null,
            ct);

    public async Task<ExamImportAttempt> ImportExtractedAsync(
        ExtractedImportSource source,
        ExamDefinitionId definitionId,
        int versionNumber,
        CancellationToken ct)
    {
        var observedHash = Hash(source.Text);
        if (!FixedTimeEquals(source.TextSha256, observedHash))
        {
            return ExamImportAttempt.Rejected(
            [
                new PackageFinding(
                    "error", "SOURCE_HASH_MISMATCH", "/source/sha256",
                    "The extracted source bytes do not match the recorded SHA-256 hash."),
            ]);
        }

        var parsed = await parser.ParseAsync(source, ct);
        return await ValidateAndSaveAsync(
            parsed.PackageJson,
            definitionId,
            versionNumber,
            ExamImportRoute.AiParsedSource,
            source.SourceSha256.ToLowerInvariant(),
            source.Text,
            parsed.Metadata,
            ct);
    }

    private async Task<ExamImportAttempt> ValidateAndSaveAsync(
        string packageJson,
        ExamDefinitionId definitionId,
        int versionNumber,
        ExamImportRoute route,
        string sourceHash,
        string sourceText,
        ParserRunMetadata? parserMetadata,
        CancellationToken ct)
    {
        var validation = validator.Validate(packageJson, definitionId, versionNumber);
        if (!validation.IsValid || validation.Version is null)
            return ExamImportAttempt.Rejected(validation.Findings);

        var warnings = route == ExamImportRoute.AiParsedSource
            ? new ImportReviewWarning[]
            {
                new("AI_PARSE_REVIEW", ImportReviewCategory.Questions, "/",
                    "AI-parsed content must be compared with its source before approval.", false),
            }
            : [];
        var packageHash = Hash(packageJson);
        var draft = new ExamImportDraft(
            StableDraftId(definitionId, versionNumber, route, packageHash),
            definitionId, versionNumber, route, sourceHash, packageHash,
            validation.Version, parserMetadata, ImportApprovalState.ReviewRequired,
            validation.Findings, sourceText,
            packageJson, ImportReviewChecklist.Empty, warnings, 0, null);

        await drafts.SaveAsync(draft, ct);
        return ExamImportAttempt.Accepted(draft);
    }

    public static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static Guid StableDraftId(
        ExamDefinitionId definitionId, int versionNumber, ExamImportRoute route, string packageHash)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{definitionId.Value}\n{versionNumber}\n{route}\n{packageHash}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static bool FixedTimeEquals(string claimed, string observed)
    {
        if (claimed.Length != observed.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(claimed.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(observed));
    }
}
