namespace Vni.Ielts.Application.Importing;

/// <summary>
/// Turns one Listening recording into readable text, at import time.
///
/// ── This is NOT the Speaking seam, and the difference is the whole reason
/// this port exists ────────────────────────────────────────────────────────
///
/// <b><see cref="Vni.Ielts.Application.Assessment.ITranscriptSource"/> is a
/// different port and stays exactly as it is.</b> That one transcribes a
/// <b>learner's</b> speech, and every property that makes it hard applies
/// there and nowhere here:
///
/// <list type="bullet">
/// <item>a learner's recording is <b>personal data</b> under Vietnam's PDPL,
/// so sending it abroad is a cross-border transfer with a CTIA behind it
/// (<c>B-2</c>); published exam audio has no data subject in it at all;</item>
/// <item>Speaking marking needs <b>word-level timings</b> to score
/// pronunciation and fluency, which is what narrows the provider field to
/// almost nothing; an exam transcript needs only readable text, because the
/// only thing that reads it is a string search
/// (<see cref="PassageAnchorCheck"/>) and, later, an explanation pass;</item>
/// <item><c>P-02</c> defers Speaking marking until a provider is chosen.
/// <see cref="Vni.Ielts.Application.Assessment.ITranscriptSource"/> therefore
/// stays on <c>NoTranscriptSource</c> and this task does not touch it.</item>
/// </list>
///
/// <b>Merging the two would re-couple this to that deferral</b>, and the
/// measured cost of leaving Listening without transcripts is concrete: 0 of
/// 24 Listening parts in this repository's Cambridge packages carry one, so
/// <see cref="PassageAnchorCheck"/> — the strongest deterministic check in
/// the import pipeline — skips every Listening part and protects nothing
/// there. Keeping the ports apart is what lets that be fixed while Speaking
/// stays deferred. → <c>IP-07</c>
/// </summary>
public interface IAudioTranscriber
{
    /// <summary>
    /// Whether a provider is actually wired. False is the supported "off"
    /// state, not a fault: <see cref="AudioTranscriptionStage"/> then does
    /// nothing at all — no call, no warning, no change to the package.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Text for one recording, or a refusal saying why there is none.
    /// </summary>
    /// <param name="audio">
    /// The recording's bytes. The caller owns the stream and disposes it.
    /// </param>
    /// <param name="fileName">
    /// The file's own name, sent so a provider can infer the container format.
    /// It is a package-relative name the archive inspector already
    /// canonicalised; it is never used as a path or a storage key.
    /// </param>
    Task<TranscriptionResult> TranscribeAsync(Stream audio, string fileName, CancellationToken ct);
}

/// <summary>
/// What one transcription attempt produced.
///
/// <b><see cref="Text"/> is null on refusal and never <see cref="string.Empty"/>.</b>
/// Absent and empty are different states downstream and must stay different:
/// an empty transcript reads as "this recording says nothing", and
/// <see cref="PassageAnchorCheck"/> would then report every anchorable answer
/// in the part as absent — a wave of false warnings against a perfectly good
/// paper. A check that refuses correct papers is a check that gets switched
/// off. → <c>IP-09</c>
/// </summary>
/// <param name="RefusalCode">
/// A stable, machine-readable code — never the provider's prose, and never
/// anything that was heard. A transcript is exam content; a refusal names the
/// part and the category, nothing else.
/// </param>
public sealed record TranscriptionResult(bool IsSuccess, string? Text, string? RefusalCode)
{
    /// <summary>
    /// A refusal is produced rather than a blank success, deliberately: see
    /// the type's own remarks. A caller that hands blank text to
    /// <see cref="Transcribed"/> gets a refusal back rather than an empty
    /// transcript on the package.
    /// </summary>
    public static TranscriptionResult Transcribed(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Refused(TranscriptionRefusalCodes.EmptyResponse)
            : new TranscriptionResult(true, text, null);

    public static TranscriptionResult Refused(string code) => new(false, null, code);
}

/// <summary>
/// Why no text came back. Stable codes, because an operator acts on the code;
/// none of them carries a configured value, a provider message, or a word of
/// what the recording said.
/// </summary>
public static class TranscriptionRefusalCodes
{
    /// <summary>No transcription provider is wired in this deployment.</summary>
    public const string NotConfigured = "TRANSCRIBER_NOT_CONFIGURED";

    /// <summary>The provider answered, and the answer held no text.</summary>
    public const string EmptyResponse = "TRANSCRIBER_EMPTY_RESPONSE";

    /// <summary>The provider refused, errored, or could not be reached.</summary>
    public const string ProviderRefused = "TRANSCRIBER_PROVIDER_REFUSED";

    /// <summary>The audio file itself could not be opened or read.</summary>
    public const string AudioUnreadable = "TRANSCRIBER_AUDIO_UNREADABLE";
}

/// <summary>
/// One recording inside the extracted package, named by its package-relative
/// path and opened on demand.
///
/// <b>Opened lazily, through a delegate, rather than handed over as a
/// stream.</b> A Listening package holds four recordings of tens of megabytes
/// each, and the commonest outcome of
/// <see cref="AudioTranscriptionStage.RunAsync"/> is that none of them is read
/// at all — the package brought its own transcripts (<c>IP-08</c>), or the
/// count gate refused to guess (<c>IP-09</c>). Materialising every stream up
/// front would pay for all of that every time.
/// </summary>
public sealed record ImportAudioFile(
    string RelativePath, Func<CancellationToken, Task<Stream>> OpenAsync)
{
    /// <summary>The last path segment — what is sent to a provider as a file name.</summary>
    public string FileName => Path.GetFileName(RelativePath);
}

/// <summary>
/// Something an administrator has to look at before this package is approved.
///
/// <b>Deliberately not a <see cref="PackageFinding"/>, and deliberately not a
/// bare string.</b> A finding carries only a severity, and the codebase has
/// already been bitten twice by a <c>"warning"</c>-severity finding falling
/// between both gates — <c>ImportReviewWorkflow.ApproveAsync</c> blocks only
/// on <c>"error"</c>, and there is no resolve path for a finding at all. A
/// bare string, meanwhile, has no path to file it against and no code to key a
/// resolution on. Its caller turns it into an <see cref="ImportReviewWarning"/>
/// — blocking, clearable with a recorded and audited reason (<c>P-19</c>) —
/// which is the same shape every other import judgement already uses.
/// </summary>
public sealed record TranscriptionWarning(string Code, string Path, string Message);

/// <summary>Stable codes for <see cref="TranscriptionWarning"/>.</summary>
public static class TranscriptionWarningCodes
{
    /// <summary>A recording was left with no transcript because the attempt was refused.</summary>
    public const string Refused = "TRANSCRIPT_REFUSED";

    /// <summary>
    /// The number of recordings needing text does not match the number of
    /// audio files, so nothing was transcribed. → <c>IP-09</c>
    /// </summary>
    public const string AudioCountMismatch = "TRANSCRIPT_AUDIO_COUNT_MISMATCH";

    /// <summary>
    /// A part named its own audio and that name resolves to no file, or to
    /// more than one. Nothing was transcribed. → <c>IP-09</c>
    /// </summary>
    public const string AudioReferenceUnresolved = "TRANSCRIPT_AUDIO_REFERENCE_UNRESOLVED";

    /// <summary>
    /// Some parts name their own audio and some do not, so neither the
    /// reference route nor the positional one can be trusted for the whole
    /// set. Nothing was transcribed. → <c>IP-09</c>
    /// </summary>
    public const string AudioReferencesMixed = "TRANSCRIPT_AUDIO_REFERENCES_MIXED";
}

/// <param name="PackageJson">
/// The package, with <c>transcript</c> written onto every part a recording was
/// successfully transcribed for. Byte-identical to the input when nothing was
/// transcribed, so a caller can use <see cref="Changed"/> to decide whether a
/// revalidation and a write are owed.
/// </param>
/// <param name="Supplied">
/// How many parts already carried a transcript and were therefore left alone
/// with no model called. → <c>IP-08</c>
/// </param>
/// <param name="Transcribed">How many parts gained a transcript from a provider.</param>
public sealed record AudioTranscriptionResult(
    string PackageJson,
    IReadOnlyList<TranscriptionWarning> Warnings,
    int Supplied,
    int Transcribed)
{
    public bool Changed => Transcribed > 0;
}
