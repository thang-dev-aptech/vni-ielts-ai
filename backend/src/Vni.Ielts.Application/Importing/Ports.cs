using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// The single deterministic gate for every exam-package producer.
/// The implementation belongs to Infrastructure because JSON Schema is an adapter detail.
/// </summary>
public interface IExamPackageValidator
{
    PackageValidationResult Validate(
        string packageJson, ExamDefinitionId definitionId, int versionNumber, UserId? authorId = null);
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
    /// <summary>
    /// The prompt version this parser will run under, or
    /// <see cref="ImportJob.NoParserConfigured"/> when no AI parser is wired.
    ///
    /// <b>Read at enqueue time, not at parse time.</b> It is a component of
    /// <see cref="ImportJob.OperationIdFor"/>, which the HTTP door has to
    /// compute before any parser is called — and it is in that id for the same
    /// reason a marking job's id carries a rubric version: a parse under an
    /// improved prompt is a genuinely different piece of work, and an id that
    /// ignored the prompt would make re-uploading identical bytes after a
    /// prompt change collide with the job already keyed to the old one, with
    /// no way to force a re-parse.
    /// </summary>
    string PromptVersion { get; }

    Task<ParsedExamPackage> ParseAsync(ExtractedImportSource source, CancellationToken ct);
}

public interface IImportDraftStore
{
    /// <summary>Idempotent for an existing draft id with identical import content.</summary>
    Task SaveAsync(ExamImportDraft draft, CancellationToken ct);
    Task<ExamImportDraft?> FindAsync(Guid draftId, CancellationToken ct);

    /// <summary>
    /// The draft an earlier run of this same import already produced, looked up
    /// by what was known <b>before</b> the parser ran.
    ///
    /// <b>Why this cannot be <see cref="FindAsync"/>.</b>
    /// <c>ExamImportWorkflow.StableDraftId</c> derives the draft id from the
    /// <i>package</i> hash — the parser's own output — so a worker that has not
    /// parsed yet cannot compute it. The only identity available before the
    /// paid call is the one the job row already carries: the definition, the
    /// version, the route, and the hash of the extracted source text. That is
    /// what makes a resumed import able to skip the parse it already bought.
    ///
    /// <b><paramref name="parsePromptVersion"/> is the fifth term, and leaving
    /// it out silently undid a decision this system already made.</b>
    /// <see cref="ImportJob.OperationIdFor"/> carries the parse prompt version
    /// for one reason: when an improved prompt ships, re-uploading identical
    /// bytes must be able to re-parse rather than collide with the job keyed to
    /// the old prompt. Two drafts can share the other four terms — the same
    /// source parsed under two prompts — and they are different drafts with
    /// different ids, because the id is derived from the package hash. A lookup
    /// blind to the prompt takes whichever the database returns first and hands
    /// back the parse the new prompt was shipped to supersede.
    ///
    /// <b>Null is a normal answer</b>, not an error: it means no earlier run
    /// under this prompt got as far as saving a draft, so the parse genuinely
    /// has to happen.
    /// </summary>
    Task<ExamImportDraft?> FindBySourceAsync(
        ExamDefinitionId definitionId,
        int versionNumber,
        ExamImportRoute route,
        string sourceHash,
        string parsePromptVersion,
        CancellationToken ct);
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

    /// <summary>
    /// Operator CLI limits for Cambridge books. Cam 20 alone is ~250 MB on
    /// disk; the HTTP import path keeps the tighter <see cref="Default"/>.
    /// </summary>
    public static SourceExtractionLimits Operator { get; } =
        new(300 * 1024 * 1024, 300, 100, 20 * 1024 * 1024, TimeSpan.FromMinutes(2));
}

public sealed record PrivateImportAsset(
    string Reference, string ContentType, long Length, string Sha256);

/// <summary>
/// Bytes of a privately staged import asset. The caller owns the stream and
/// must dispose it. Null from <see cref="IPrivateImportAssetStore.OpenPrivateAsync"/>
/// means the key resolves to nothing — same 404 shape as
/// <c>IExamAssetStore.OpenAsync</c>, and deliberately never thrown.
/// </summary>
public sealed record StagedImportAsset(
    Stream Content, string ContentType, long? ContentLength = null);

/// <summary>
/// Private staging for media discovered during import. A staged object is not
/// an exam asset and therefore cannot be served by the learner-facing
/// <c>IExamAssetStore</c> until review maps it explicitly.
///
/// <b>Write and read, but only for the admin import door.</b> Put is what the
/// import pipeline uses; Open is what the draft-asset preview endpoint uses.
/// Neither path is a substitute for the learner asset reader.
/// </summary>
public interface IPrivateImportAssetStore
{
    Task<string> PutPrivateAsync(
        string key, Stream content, string contentType, string sha256, CancellationToken ct);

    /// <summary>
    /// Null when the key was never staged, or is malformed. Never throws on a
    /// missing object — the admin preview route turns null into 404.
    /// </summary>
    Task<StagedImportAsset?> OpenPrivateAsync(string key, CancellationToken ct);
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

/// <summary>
/// The uploaded archive, parked where another process can fetch it.
///
/// <b>Separate from <see cref="IPrivateImportAssetStore"/> on purpose, and the
/// name of that one is the trap.</b> Its default registration
/// (<c>DiscardedImportAssetStore</c>) accepts Put and keeps nothing — Open
/// always returns null — so it is not a place to park an archive a worker must
/// reopen minutes later. It exists so <c>SafeSourceDocumentExtractor</c> has
/// somewhere to drop embedded media it does not want. Building an out-of-band
/// import on the discarded store would produce a system that accepts every
/// upload, enqueues it, and loses it — with the failure appearing minutes
/// later, in a worker, as a package that cannot be opened.
///
/// <para>
/// An import archive has to survive the request that carried it and be read
/// back by a worker minutes later. That is the same shape as
/// <c>IRecordingStore</c>, and it is backed by the same object storage under
/// its own key prefix (ADR-0016: one bucket, prefixes per class).
/// </para>
///
/// <para>
/// <b>The stream this returns is not required to be seekable, and generally is
/// not.</b> An object-storage response body is forward-only.
/// <c>ExamPackageImportPipeline.ImportAsync</c> demands a seekable stream —
/// inspection reads the ZIP central directory, which lives at the end of the
/// file, and extraction re-reads the same bytes — so a caller must spool this
/// to a seekable stream first. Getting that wrong is a truncated read that
/// looks exactly like a corrupt package.
/// </para>
/// </summary>
public interface IImportArchiveStore
{
    /// <summary>
    /// Stores the archive and returns the key a later process reads it back
    /// by. Keyed on the upload's own SHA-256, so re-uploading identical bytes
    /// writes the same object rather than a second copy — the storage-side
    /// half of what <see cref="ImportJob.OperationIdFor"/> does in the
    /// database.
    /// </summary>
    Task<string> SaveAsync(string sourceSha256, Stream archive, CancellationToken ct);

    /// <summary>
    /// Null when the object is gone — a job whose archive expired, or was
    /// swept, must <b>fail</b>, not throw. "The upload is no longer there" is
    /// a true and final answer an operator can act on; an exception out of a
    /// worker loop is not.
    /// </summary>
    Task<Stream?> OpenAsync(string archiveKey, CancellationToken ct);

    /// <summary>
    /// Removes the stored archive. Called when a job settles, either way: an
    /// uploaded exam package is third-party copyrighted material, and keeping
    /// every upload forever is a rights exposure nobody chose. A failure here
    /// is logged and never fatal — a stray object is a hygiene problem, a lost
    /// import is not.
    /// </summary>
    Task DeleteAsync(string archiveKey, CancellationToken ct);
}
