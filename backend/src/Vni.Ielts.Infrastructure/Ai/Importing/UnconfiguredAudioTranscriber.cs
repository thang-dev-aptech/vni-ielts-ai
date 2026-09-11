using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Infrastructure.Ai.Importing;

/// <summary>
/// The null implementation of <see cref="IAudioTranscriber"/>, and the one
/// that is wired by default.
///
/// <b>Registered whenever <c>Import:Transcription</c> is unset</b>, which is
/// every deployment today. <see cref="IsConfigured"/> is false, so
/// <see cref="AudioTranscriptionStage"/> returns the package untouched without
/// calling this at all — no findings, no warnings, no change. That is
/// <c>G-11</c>'s shape: an unresolved policy is a configured seam with a null
/// implementation, never an invented default.
///
/// <b>It still refuses properly rather than returning empty text</b>, for the
/// case where some future caller reaches it directly. Absent and empty are
/// different states: an empty transcript reads as "this recording says
/// nothing" and would make <see cref="PassageAnchorCheck"/> report every
/// anchorable answer in the part as missing. → <c>IP-09</c>
///
/// <b>Not <c>NoTranscriptSource</c>.</b> That one is the Speaking-marking
/// seam for a <b>learner's</b> recording, deferred by <c>P-02</c> pending an
/// ASR choice, and it is untouched by this file. This one is about published
/// exam audio, which is third-party material rather than personal data and
/// needs only readable text. → <see cref="IAudioTranscriber"/>, <c>IP-07</c>
/// </summary>
public sealed class UnconfiguredAudioTranscriber : IAudioTranscriber
{
    public bool IsConfigured => false;

    public Task<TranscriptionResult> TranscribeAsync(
        Stream audio, string fileName, CancellationToken ct) =>
        Task.FromResult(TranscriptionResult.Refused(TranscriptionRefusalCodes.NotConfigured));
}
