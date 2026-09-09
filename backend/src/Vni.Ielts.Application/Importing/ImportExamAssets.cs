using System.Globalization;
using System.Text;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Importing;

public static class ImportAssetFindingCodes
{
    public const string Missing = "ASSET_MISSING";
    public const string Invalid = "ASSET_INVALID";
    public const string Conflict = "ASSET_CONFLICT";
    public const string Unreferenced = "ASSET_UNREFERENCED";
    public const string PathInvalid = "ASSET_PATH_INVALID";
    public const string ChecksumMismatch = "ASSET_CHECKSUM_MISMATCH";
    public const string StoreUnavailable = "ASSET_STORE_UNAVAILABLE";
}

public sealed record ImportAssetManifestEntry(
    string Reference,
    string StagingKey,
    string ContentType,
    long Length,
    string Sha256);

public sealed record StagedImportAsset(
    string Reference,
    string StagingKey,
    string ContentType,
    long Length,
    string Sha256);

public enum ImportAssetAvailabilityKind
{
    Absent,
    Present,
}

public sealed record ImportAssetAvailability(
    ImportAssetAvailabilityKind Kind,
    string? ContentType,
    long? Length,
    string? Sha256)
{
    public static ImportAssetAvailability Absent { get; } =
        new(ImportAssetAvailabilityKind.Absent, null, null, null);

    public static ImportAssetAvailability Present(string contentType, long length, string sha256) =>
        new(ImportAssetAvailabilityKind.Present, contentType, length, sha256);
}

public enum ImportAssetPromotionStatus
{
    PromotedThisAttempt,
    AlreadyPresent,
    Conflict,
    Missing,
    Canceled,
}

public sealed record ImportAssetPromotionResult(
    ImportAssetPromotionStatus Status, string Reference);

/// <summary>
/// Outcomes of one approval promotion pass. Cleanup after a later failure
/// may only delete <see cref="PromotedThisAttempt"/> — never
/// <see cref="ImportAssetPromotionStatus.AlreadyPresent"/>, empty staging
/// keys, or externally provisioned objects.
/// </summary>
public sealed record ImportAssetPromotionBatch(
    IReadOnlyList<ImportAssetPromotionResult> Outcomes,
    ImportApprovalCommitResult? Blocked)
{
    public IReadOnlyList<string> PromotedThisAttempt =>
        Outcomes
            .Where(o => o.Status == ImportAssetPromotionStatus.PromotedThisAttempt)
            .Select(o => o.Reference)
            .ToArray();
}

public enum ImportAssetCleanupReason
{
    DraftSaveFailed,
    ApprovalCommitFailed,
    StagingAbandoned,
}

/// <summary>
/// Stages ZIP exam media privately, then promotes it to the learner-facing
/// exam-asset namespace. Application must not see AWS or Mongo types here.
/// </summary>
public interface IImportExamAssetStore
{
    Task<StagedImportAsset> StageAsync(
        Guid draftId, string reference, Stream content,
        string contentType, long length, string sha256, CancellationToken ct);

    Task<ImportAssetAvailability> CheckFinalAsync(string reference, CancellationToken ct);

    Task<ImportAssetPromotionResult> PromoteAsync(StagedImportAsset asset, CancellationToken ct);

    Task VerifyFinalAsync(IReadOnlyList<ImportAssetManifestEntry> assets, CancellationToken ct);

    Task RecordCleanupIntentAsync(
        Guid draftId, IReadOnlyList<string> promotedReferences,
        ImportAssetCleanupReason reason, CancellationToken ct);

    Task ProcessPendingCleanupAsync(IExamCatalogue catalogue, CancellationToken ct);
}

public sealed class ImportExamAssetsUnavailableException() : InvalidOperationException(
    "Exam-package ZIP assets cannot be staged because object storage is not configured.");

public static class ImportAssetCompensation
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static CancellationTokenSource Start() => new(Timeout);
}

public static class ImportAssetPaths
{
    public const string Prefix = "assets/";

    public static IReadOnlyList<string> StagedReferences(
        IEnumerable<ImportAssetManifestEntry> manifest) =>
        manifest
            .Where(entry => !string.IsNullOrEmpty(entry.StagingKey))
            .Select(entry => entry.Reference)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static bool IsZipBindable(string reference) =>
        reference.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ZipBindableReferences(ExamVersion version) =>
        ExamAssetInventory.Collect(version)
            .Select(item => item.Ref)
            .Where(IsZipBindable)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static bool TryCanonicalize(string raw, out string canonical, out string? errorCode)
    {
        canonical = string.Empty;
        errorCode = ImportAssetFindingCodes.PathInvalid;

        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (raw.Contains('\\', StringComparison.Ordinal)) return false;

        var name = raw.Replace('\\', '/').Normalize(NormalizationForm.FormC);
        if (name.StartsWith('/')) return false;
        if (name.Contains('\0', StringComparison.Ordinal)) return false;

        if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var rest = name[Prefix.Length..];
        if (rest.Length == 0) return false;

        var segments = rest.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => segment is "" or "." or "..")) return false;

        canonical = Prefix + rest;
        errorCode = null;
        return true;
    }

    public static string StagingKey(Guid draftId, string canonicalReference)
    {
        var relative = canonicalReference.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? canonicalReference[Prefix.Length..]
            : canonicalReference;
        return $"imports/exam-drafts/{draftId:D}/assets/{relative}";
    }

    public static PackageFinding Finding(string code, string path, string message) =>
        new("error", code, Display(path), message);

    /// <summary>Hostile paths never reach a response verbatim.</summary>
    public static string Display(string raw)
    {
        const int max = 160;
        var builder = new StringBuilder(Math.Min(raw.Length, max) + 1);
        foreach (var c in raw)
        {
            if (builder.Length >= max)
            {
                builder.Append('…');
                break;
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            var hidden = category is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned;
            builder.Append(hidden && !char.IsSurrogate(c) ? '?' : c);
        }

        return builder.ToString();
    }
}
