using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// The front door for an uploaded exam package: everything that happens to a
/// ZIP before a single byte of it is written to disk, and the bounded write
/// that follows.
///
/// <b>Validate before extracting; extract to a sandbox; nothing else.</b>
/// This port knows nothing about manifests, schemas, media or AI — those are
/// downstream stages that operate on files already inside the sandbox
/// (<see cref="ISourceDocumentExtractor"/>, <see cref="IExamPackageValidator"/>).
/// Its only job is to make sure the archive cannot hurt the host and to say
/// which skill each file belongs to. → docs/security/zip-ingestion-security.md
///
/// <para>
/// <b>Pass a seekable stream.</b> Inspection reads the central directory,
/// which lives at the end of the file, and extraction re-reads the same bytes.
/// A non-seekable stream is accepted by either call on its own (it is spooled
/// to a temporary file under the archive byte cap), but it is consumed by the
/// call — so the two-step usage needs the upload persisted once and opened
/// twice. That is also what closes the check-then-use window (A8 in the spec):
/// both calls run against the same fixed artefact.
/// </para>
/// </summary>
public interface IExamPackageArchiveInspector
{
    /// <summary>
    /// Reads the central directory and returns every finding at once. Writes
    /// nothing. An inspection with <see cref="ArchiveInspection.IsAcceptable"/>
    /// false must not be passed to extraction.
    /// </summary>
    Task<ArchiveInspection> InspectAsync(Stream zip, ImportArchiveLimits limits, CancellationToken ct);

    /// <summary>
    /// Extracts the entries the inspection accepted into a fresh, uniquely
    /// named directory under <paramref name="sandboxRoot"/>. Every per-entry
    /// check is re-run against the archive it is given, the byte quota is
    /// enforced on bytes actually written rather than on the declared sizes,
    /// and the directory is removed on any failure, including timeout.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// When <paramref name="ct"/> itself is cancelled. A caller's cancellation
    /// is not a verdict on the package and is never turned into a finding;
    /// the extraction timeout, which is, becomes
    /// <see cref="ArchiveFindingCodes.ExtractionTimeout"/>.
    /// </exception>
    Task<ArchiveExtraction> ExtractToSandboxAsync(
        Stream zip,
        ArchiveInspection inspection,
        string sandboxRoot,
        ImportArchiveLimits limits,
        CancellationToken ct);
}

/// <summary>
/// The caps the inspector enforces. Values come from configuration
/// (<c>Import:Archive</c>); they are a technical decision, not a business one,
/// and they are never echoed in a finding message. → spec § Error reporting
/// </summary>
public sealed record ImportArchiveLimits(
    int MaxEntries,
    long MaxTotalUncompressedBytes,
    long MaxEntryUncompressedBytes,
    int MaxCompressionRatio,
    long MaxArchiveBytes,
    TimeSpan ExtractionTimeout);

/// <summary>
/// Where each accepted file belongs, decided by its top-level folder name
/// alone (<c>P-18</c>). A skill whose folder is absent is simply not present.
/// </summary>
public sealed record PackageLayout(
    IReadOnlyDictionary<ExamModule, IReadOnlyList<string>> EntriesBySkill,
    IReadOnlyList<string> UnknownEntries,
    IReadOnlyList<string> AssetEntries = null!)
{
    public static PackageLayout Empty { get; } =
        new(new Dictionary<ExamModule, IReadOnlyList<string>>(), [], []);

    /// <summary>Skills with at least one file. Order follows <see cref="ExamModule"/>.</summary>
    public IReadOnlyList<ExamModule> PresentSkills =>
        Enum.GetValues<ExamModule>()
            .Where(m => EntriesBySkill.TryGetValue(m, out var e) && e.Count > 0)
            .ToArray();

    /// <summary>Every relative path that would be extracted, across skills and <c>assets/**</c>.</summary>
    public IEnumerable<string> AcceptedEntries =>
        EntriesBySkill.Values.SelectMany(e => e).Concat(AssetEntries ?? []);
}

public sealed record ArchiveInspection(
    bool IsAcceptable,
    IReadOnlyList<PackageFinding> Findings,
    PackageLayout Layout);

public sealed record ArchiveExtraction(
    bool IsSuccess,
    string? SandboxDirectory,
    IReadOnlyList<PackageFinding> Findings,
    long BytesWritten,
    int FilesWritten);

/// <summary>
/// Stable machine-readable codes. Administrators act on the code; the message
/// names the entry and the category, never the threshold.
/// </summary>
public static class ArchiveFindingCodes
{
    public const string NotAnArchive = "ZIP_NOT_AN_ARCHIVE";
    public const string TooLarge = "ZIP_TOO_LARGE";
    public const string TooManyEntries = "ZIP_TOO_MANY_ENTRIES";
    public const string TotalSizeExceeded = "ZIP_TOTAL_SIZE_EXCEEDED";
    public const string EntryTooLarge = "ZIP_ENTRY_TOO_LARGE";
    public const string CompressionRatio = "ZIP_COMPRESSION_RATIO";
    public const string PathEscape = "PATH_ESCAPE";
    public const string PathInvalid = "PATH_INVALID";
    public const string EntryNotRegular = "ENTRY_NOT_REGULAR";
    public const string NestedArchive = "NESTED_ARCHIVE";
    public const string LayoutUnknownEntry = "LAYOUT_UNKNOWN_ENTRY";
    public const string LayoutEmpty = "LAYOUT_EMPTY";
    public const string ExtractionQuotaExceeded = "EXTRACTION_QUOTA_EXCEEDED";
    public const string ExtractionTimeout = "EXTRACTION_TIMEOUT";

    /// <summary>
    /// An entry delivered a different number of bytes than its header
    /// declared, while staying inside the quota. The declared sizes passed
    /// inspection on false pretences, so the package is refused even though
    /// nothing overflowed.
    /// </summary>
    public const string EntrySizeMismatch = "ZIP_ENTRY_SIZE_MISMATCH";
}
