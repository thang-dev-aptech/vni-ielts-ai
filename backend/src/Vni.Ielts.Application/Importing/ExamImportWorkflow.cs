using System.Security.Cryptography;
using System.Text;
using Vni.Ielts.Domain.Common;
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

/// <param name="OverrideReason">
/// Set only when <see cref="Resolved"/> was reached by an admin overriding
/// the warning rather than by the condition genuinely being fixed. `P-19`:
/// "admin bỏ qua được nhưng bắt buộc ghi lý do và vào nhật ký" — the reason
/// travels with the warning so a later reader of the draft sees not just
/// that it was waved through, but why. Trailing and optional so every
/// existing positional construction of this record keeps compiling.
/// </param>
public sealed record ImportReviewWarning(
    string Id, ImportReviewCategory Category, string Path, string Message, bool Resolved,
    string? OverrideReason = null);

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
        CancellationToken ct,
        UserId? authorId = null) =>
        ValidateAndSaveAsync(
            packageJson,
            definitionId,
            versionNumber,
            ExamImportRoute.StructuredPackage,
            Hash(packageJson),
            packageJson,
            parserMetadata: null,
            ct,
            authorId);

    /// <param name="resumeExistingDraft">
    /// <b>True only when the caller has evidence the parse already happened.</b>
    /// The import worker passes it when the job row records a stage strictly
    /// past <c>Parsing</c> — and a stage is reported on <i>entry</i> to its
    /// step, so "past Parsing" means Parsing finished, which means a draft was
    /// saved. Nothing else may set it: a caller that guessed would hand back a
    /// stale draft for a package it never actually read.
    ///
    /// <b>This is the whole of what resuming skips.</b> The parse is the one
    /// step whose completion the recorded stage proves, because it is the only
    /// one that persists its result before the next step begins. Transcription,
    /// keying, the cross-checks and the explanations all write at the end of
    /// the method that performs them, so a recorded stage says they <i>started</i>
    /// and never that they finished — they are re-run, deliberately.
    /// </param>
    public async Task<ExamImportAttempt> ImportExtractedAsync(
        ExtractedImportSource source,
        ExamDefinitionId definitionId,
        int versionNumber,
        CancellationToken ct,
        bool resumeExistingDraft = false,
        UserId? authorId = null)
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

        /*
         * <b>The short-circuit, and it is the point of the whole job record.</b>
         * A Cambridge parse is the single most expensive thing this system
         * buys. Before this existed, `ImportJobStage` was written on every
         * import and read by nothing, so a transient failure, a worker restart
         * or an expired lease paid for the parse again — up to three times for
         * one upload.
         *
         * The lookup is by source hash rather than draft id because the draft
         * id is derived from the parser's output, which is precisely what is
         * not known yet. It is keyed on the prompt version this parser would
         * run under — `parser.PromptVersion`, the same value the job's own id
         * carries — so a draft produced by a superseded prompt is not adopted
         * as though it were this run's work. → IImportDraftStore.FindBySourceAsync
         */
        if (resumeExistingDraft)
        {
            var existing = await drafts.FindBySourceAsync(
                definitionId, versionNumber, ExamImportRoute.AiParsedSource,
                source.SourceSha256.ToLowerInvariant(), parser.PromptVersion, ct);

            if (existing is not null) return ExamImportAttempt.Accepted(existing);
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
            ct,
            authorId);
    }

    private async Task<ExamImportAttempt> ValidateAndSaveAsync(
        string packageJson,
        ExamDefinitionId definitionId,
        int versionNumber,
        ExamImportRoute route,
        string sourceHash,
        string sourceText,
        ParserRunMetadata? parserMetadata,
        CancellationToken ct,
        UserId? authorId = null)
    {
        var validation = validator.Validate(packageJson, definitionId, versionNumber, authorId);
        if (!validation.IsValid || validation.Version is null)
            return ExamImportAttempt.Rejected(validation.Findings);

        var warnings = route == ExamImportRoute.AiParsedSource
            ? new ImportReviewWarning[]
            {
                new(AiParseReviewWarningId, ImportReviewCategory.Questions, "/",
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

    /// <summary>
    /// The one warning this workflow writes, named rather than typed twice.
    ///
    /// <b>It is written here, at save, and never recomputed.</b>
    /// <c>ExamPackageImportPipeline</c> has to be able to tell it apart from
    /// the warnings it regenerates on every run, because a resumed import
    /// replaces its own and must leave everybody else's alone.
    /// </summary>
    public const string AiParseReviewWarningId = "AI_PARSE_REVIEW";

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

/// <summary>
/// How an upload attempt ended, independently of the outbox job row.
///
/// <b>Door rejection is a first-class result.</b> A hostile ZIP is refused
/// before an <see cref="ImportJob"/> exists, so the only durable record of
/// who uploaded what, and why it was refused, lives here. <c>Rejected</c> is
/// the worker's later judgement that an already-parked archive still failed
/// validation; <c>Failed</c> is everything else that settled without a draft.
/// </summary>
public enum PackageImportHistoryResult
{
    DoorRejected,
    Queued,
    Running,
    Completed,
    Rejected,
    Failed,
}

/// <summary>
/// One upload attempt, from the HTTP door through the worker that settles it.
///
/// Append-only at creation: a door rejection is a new row that never shares
/// an operation id, and an accepted upload is one row keyed on the job's
/// operation id so later stage/result writes update the same logical entry
/// rather than growing a second copy of the same attempt.
/// </summary>
public sealed record PackageImportHistory(
    Guid Id,
    string? OperationId,
    string ActorId,
    string OriginalFileName,
    ExamDefinitionId? DefinitionId,
    int? VersionNumber,
    string? SourceSha256,
    Guid? DraftId,
    ImportJobStage? Stage,
    PackageImportHistoryResult Result,
    IReadOnlyList<PackageFinding> Findings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Filters the operator-facing package-import history list.</summary>
public sealed record PackageImportHistoryQuery(
    PackageImportHistoryResult? Result = null,
    ImportJobStage? Stage = null,
    string? ActorId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50)
{
    public const int MaxPageSize = 50;

    public void Validate()
    {
        if (From is { } from && To is { } to && from > to)
            throw new ArgumentException("The history-list start must not be after its end.");
        if (Page < 1) throw new ArgumentOutOfRangeException(nameof(Page));
        if (PageSize < 1 || PageSize > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(PageSize));
    }
}

public sealed record PackageImportHistoryPage(
    IReadOnlyList<PackageImportHistory> Items,
    long TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// Queryable history of every package-upload attempt, including the ones the
/// door refused before an archive was stored.
///
/// <b>The outbox is not this.</b> <see cref="IImportOutbox"/> is the work
/// queue: it stores a job the worker is owed, keyed on the bytes, and it
/// forgets nothing a resume needs. This store is the audit of the attempt
/// itself — who uploaded, under what name, which findings, which stage — so
/// a bomb that never became a job is still visible, and a worker refusal
/// keeps every finding rather than the first sentence of
/// <see cref="ImportJob.LastError"/>.
/// </summary>
public interface IPackageImportHistoryStore
{
    /// <summary>
    /// Always a new row. Door rejections have no operation id, so they cannot
    /// collide with a later accepted upload of different bytes, and they must
    /// not share a row with an archive that was never written.
    /// </summary>
    Task RecordDoorRejectionAsync(PackageImportHistory record, CancellationToken ct);

    /// <summary>
    /// Idempotent on the operation id. A retried upload of identical bytes
    /// refreshes the actor and filename on the existing row; a re-upload of a
    /// <see cref="PackageImportHistoryResult.Failed"/> or
    /// <see cref="PackageImportHistoryResult.Rejected"/> attempt returns that
    /// row to <see cref="PackageImportHistoryResult.Queued"/> the same way
    /// <see cref="IImportOutbox.ReopenAsync"/> returns the job to the queue.
    /// A <c>Completed</c> or <c>Running</c> row is left in that result.
    /// </summary>
    Task RecordQueuedAsync(PackageImportHistory record, CancellationToken ct);

    /// <summary>
    /// Moves the row identified by <paramref name="operationId"/> through a
    /// worker transition. Idempotent: a second write of the same stage and
    /// result is a no-op overwrite, not a second row.
    ///
    /// <paramref name="findings"/> of <c>null</c> means "leave whatever is
    /// already stored" — stage reports must not wipe a previous finding list.
    /// An empty list is a real write of "no findings".
    /// </summary>
    Task ApplyTransitionAsync(
        string operationId,
        PackageImportHistoryResult result,
        ImportJobStage? stage,
        Guid? draftId,
        IReadOnlyList<PackageFinding>? findings,
        CancellationToken ct);

    Task<PackageImportHistory?> FindByOperationAsync(string operationId, CancellationToken ct);

    Task<PackageImportHistory?> FindByIdAsync(Guid id, CancellationToken ct);

    Task<PackageImportHistoryPage> QueryAsync(PackageImportHistoryQuery query, CancellationToken ct);
}

/// <summary>
/// Caps the two untrusted strings an upload carries — the client filename and
/// the finding list — so a hostile archive cannot grow this collection without
/// bound. Applied at the store, not only at the caller, so a missed call site
/// still cannot persist a megabyte-long filename or ten thousand findings.
/// </summary>
public static class PackageImportHistoryBounds
{
    public const int MaxFileNameChars = 255;
    public const int MaxFindings = 50;
    public const int MaxFindingMessageChars = 1000;
    public const int MaxFindingPathChars = 256;
    public const int MaxFindingCodeChars = 64;
    public const int MaxFindingSeverityChars = 32;

    /// <summary>
    /// The leaf name only, control characters stripped, length capped.
    ///
    /// <b>Not <c>Path.GetFileName</c>.</b> That API is platform-defined: a
    /// backslash-separated traversal that a Windows client sent is a single
    /// name on Linux, and storing it would put the traversal in the history
    /// row. Both separators are treated as separators here, so
    /// <c>..\\..\\evil.zip</c> and <c>../../evil.zip</c> both become
    /// <c>evil.zip</c>.
    /// </summary>
    public static string SanitizeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "unnamed.zip";

        var unified = raw.Replace('\\', '/');
        var slash = unified.LastIndexOf('/');
        var name = slash >= 0 ? unified[(slash + 1)..] : unified;

        Span<char> buffer = name.Length <= 512 ? stackalloc char[name.Length] : new char[name.Length];
        var written = 0;
        foreach (var c in name)
        {
            buffer[written++] = char.IsControl(c) ? '_' : c;
        }

        var cleaned = new string(buffer[..written]).Trim().Trim('.');
        if (cleaned.Length == 0) return "unnamed.zip";
        return cleaned.Length <= MaxFileNameChars ? cleaned : cleaned[..MaxFileNameChars];
    }

    public static IReadOnlyList<PackageFinding> BoundFindings(IReadOnlyList<PackageFinding>? findings)
    {
        if (findings is null || findings.Count == 0) return [];

        var take = Math.Min(findings.Count, MaxFindings);
        var bounded = new PackageFinding[take];
        for (var i = 0; i < take; i++)
        {
            var f = findings[i];
            bounded[i] = new PackageFinding(
                Bound(f.Severity, MaxFindingSeverityChars),
                Bound(f.Code, MaxFindingCodeChars),
                Bound(f.Path, MaxFindingPathChars),
                Bound(f.Message, MaxFindingMessageChars));
        }

        return bounded;
    }

    private static string Bound(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}
