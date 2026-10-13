namespace Vni.Ielts.Application.Assessment;

/// <summary>
/// What speech-to-text returns for one Speaking answer — the text <i>and</i>
/// the word timings.
///
/// <para>
/// <b>This shape is the whole point of the seam.</b> The port used to be
/// <c>Task&lt;string?&gt;</c>: plain text, timings discarded at the boundary.
/// Fluency marking is arithmetic on timings — pauses, speech rate, run length,
/// response latency — and pronunciation assessment needs the words as
/// reference text against the audio. A transcript that has thrown its timings
/// away can never serve either, no matter which provider is chosen later.
/// → the Speaking marking plan (`W9`), and `docs/ai/speaking-pipeline.md`
/// </para>
///
/// <para>
/// <b>Vendor-neutral by construction.</b> Seconds as doubles and a nullable
/// confidence are the intersection of every provider's verbose response —
/// whisper's <c>verbose_json</c> words, Azure's word offsets, anything else —
/// so an adapter maps its vendor's shape once, at the boundary, and nothing
/// downstream knows which ASR produced it. → ADR-0005's reasoning
/// </para>
/// </summary>
/// <param name="QuestionId">Which Speaking question this audio answered.</param>
/// <param name="Text">The plain transcript, as spoken.</param>
/// <param name="Words">
/// One entry per word, in speaking order. Empty — not an error — when the
/// provider returned text only; a fluency implementation must treat that as
/// "timings unavailable" rather than crash.
/// </param>
public sealed record SpeechTranscript(
    string QuestionId,
    string Text,
    IReadOnlyList<TranscriptWord> Words);

/// <summary>One word as the recogniser heard it, and when it was said.</summary>
public sealed record TranscriptWord(
    string Text,
    double StartSeconds,
    double EndSeconds,
    double? Confidence);
