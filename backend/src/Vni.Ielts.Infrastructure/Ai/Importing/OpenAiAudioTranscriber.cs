using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Infrastructure.Ai.Importing;

/// <summary>
/// Turns one Listening recording into text by posting it to an
/// OpenAI-compatible <c>audio/transcriptions</c> endpoint.
///
/// ── What it is allowed to carry, and why that is settled ──────────────────
///
/// <b>It goes through <see cref="AiEgress"/> like every other adapter</b>, and
/// classifies its payload <see cref="AiDataClassification.Synthetic"/> — the
/// same call <see cref="OpenAiStructuredExamClient"/> makes for the same
/// reason. The classification asks whether the payload is a real person's
/// data, and a published exam recording has no data subject in it. The rights
/// question — whose copyright is being sent to a third party — is a different
/// question with a different gate (<c>ContentRightsPolicy</c>, <c>P-21</c>),
/// asked before a package reaches import at all.
///
/// <b>That is exactly why this can ship while Speaking marking stays
/// deferred.</b> A learner's recording would be
/// <see cref="AiDataClassification.LearnerPersonal"/>, would have to clear the
/// processor, endpoint and cross-border gates, and is deferred by <c>P-02</c>
/// pending an ASR choice. <c>ITranscriptSource</c> / <c>NoTranscriptSource</c>
/// is that seam and nothing here touches it. → <c>IP-07</c>
///
/// ── What it never does ────────────────────────────────────────────────────
///
/// <b>It never logs a word of what it heard.</b> A transcript is exam content:
/// it goes onto the package and nowhere else. Failures are logged as a status
/// code and a file name, never as a body — a proxy that echoes the request
/// back would otherwise put a recording's text in a log file.
///
/// <b>It never returns an empty transcript.</b> A response carrying no text is
/// a refusal, not a recording that says nothing. → <c>IP-09</c>
/// </summary>
public sealed class OpenAiAudioTranscriber(
    IHttpClientFactory httpFactory,
    IOptions<AiOptions> aiOptions,
    IOptions<AudioTranscriptionOptions> transcriptionOptions,
    ILogger<OpenAiAudioTranscriber> logger) : IAudioTranscriber
{
    /// <summary>
    /// True by construction: this type is only registered when
    /// <see cref="AudioTranscriptionOptions.IsConfigured"/> already held, and
    /// the startup gate has already refused every half-configured shape. The
    /// property exists so the stage can ask one question of whichever
    /// implementation it was handed.
    /// </summary>
    public bool IsConfigured => true;

    public async Task<TranscriptionResult> TranscribeAsync(
        Stream audio, string fileName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(audio);

        /*
         * Authorise first, before a byte is uploaded. A refusal here is a
         * configuration or policy answer — an unset key, an excluded model —
         * and it is cheaper and clearer to discover it before spending the
         * upload than after.
         */
        AiEgressTicket ticket;
        try
        {
            ticket = AiEgress.Authorise(
                aiOptions.Value, "OpenAi", AiDataClassification.Synthetic);
        }
        catch (AiEgressRefusedException e)
        {
            // The refusal enum, never a configured value and never the file's
            // content. `AiEgressRefusedException` carries no secret, but its
            // message names settings, so only the enum is logged.
            logger.LogWarning(
                "Exam audio transcription was refused before any upload: {Refusal}.", e.Refusal);

            return TranscriptionResult.Refused(TranscriptionRefusalCodes.ProviderRefused);
        }

        var model = transcriptionOptions.Value.Model;
        if (string.IsNullOrWhiteSpace(model))
        {
            // Unreachable through the startup gate, which refuses a section
            // naming a provider with no model. Kept because "the gate ran"
            // is an assumption, and the safe answer costs one branch.
            return TranscriptionResult.Refused(TranscriptionRefusalCodes.NotConfigured);
        }

        var baseUrl = string.IsNullOrWhiteSpace(transcriptionOptions.Value.BaseUrl)
            ? "https://api.openai.com/v1/"
            : transcriptionOptions.Value.BaseUrl!.TrimEnd('/') + "/";

        using var content = new MultipartFormDataContent();
        var file = new StreamContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeFor(fileName));

        /*
         * The file name is sent because several hosts infer the container
         * format from it and reject the upload without one. It is a
         * package-relative name the archive inspector already canonicalised
         * (path escape, traversal and non-regular entries were all refused
         * before extraction), and it is used here as a form field only —
         * never as a path, never as a storage key.
         * → docs/security/zip-ingestion-security.md
         */
        content.Add(file, "file", fileName);
        content.Add(new StringContent(model), "model");

        /*
         * `json`, not `verbose_json`. The verbose form returns word- and
         * segment-level timings, which is precisely what Speaking marking
         * would need and precisely what this has no use for: the anchor check
         * does a string search over the text. Asking for data nobody reads is
         * a bigger response to hold in memory and a larger surface to get
         * wrong.
         */
        content.Add(new StringContent("json"), "response_format");

        using var message = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(baseUrl), "audio/transcriptions"))
        {
            Content = content,
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.RevealApiKey());

        var http = httpFactory.CreateClient(nameof(OpenAiAudioTranscriber));

        try
        {
            using var response = await http.SendAsync(message, ct);

            if (!response.IsSuccessStatusCode)
            {
                /*
                 * <b>The status code and the file name; never the body.</b>
                 * The parse client logs a capped excerpt of an error body
                 * because that body is the provider's own diagnostics for a
                 * text request. This request's body is a recording, and a
                 * gateway that echoes a request back would put its content
                 * here. One recording failing is one part without a
                 * transcript and a warning naming it, which is enough to act
                 * on.
                 */
                logger.LogWarning(
                    "Exam audio transcription for {FileName} was rejected with status {Status}.",
                    fileName,
                    (int)response.StatusCode);

                return TranscriptionResult.Refused(TranscriptionRefusalCodes.ProviderRefused);
            }

            var payload = await response.Content.ReadAsStringAsync(ct);
            var text = ReadText(payload);

            // `Transcribed` turns blank into a refusal itself — see its own
            // remarks. Absent and empty are different states. → IP-09
            return TranscriptionResult.Transcribed(text);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller's cancellation is not a verdict on the recording.
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Exam audio transcription for {FileName} timed out.", fileName);
            return TranscriptionResult.Refused(TranscriptionRefusalCodes.ProviderRefused);
        }
        catch (HttpRequestException e)
        {
            // `e.Message` here is a transport failure — a DNS name, a TLS
            // error — and carries none of the payload.
            logger.LogWarning(
                "Exam audio transcription for {FileName} could not reach the provider: {Reason}",
                fileName,
                e.Message);

            return TranscriptionResult.Refused(TranscriptionRefusalCodes.ProviderRefused);
        }
    }

    /// <summary>
    /// The <c>text</c> field of a transcription response, or null when the
    /// response is not that shape.
    ///
    /// <b>A host that ignored <c>response_format</c> and answered in plain
    /// text is accepted too</b> — several OpenAI-compatible resellers do, and
    /// failing a whole recording over a content type when the text is sitting
    /// right there would be a bad trade. Anything else returns null, which the
    /// caller turns into a refusal rather than an empty transcript.
    /// </summary>
    internal static string? ReadText(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        if (!payload.TrimStart().StartsWith('{')) return payload;

        try
        {
            using var document = JsonDocument.Parse(payload);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String
                    ? text.GetString()
                    : null;
        }
        catch (JsonException)
        {
            // Not logged with the payload attached: it may be a partial
            // transcript.
            return null;
        }
    }

    /// <summary>
    /// A container type for the upload, from the file's own extension.
    ///
    /// <b>Best effort, and deliberately so.</b> The endpoint decodes the bytes
    /// rather than trusting this header; it is sent because several hosts
    /// reject a part with no content type at all. An unrecognised extension
    /// gets <c>application/octet-stream</c> rather than a guess.
    /// </summary>
    private static string MediaTypeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".mp4" => "audio/mp4",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".oga" => "audio/ogg",
            ".opus" => "audio/opus",
            ".flac" => "audio/flac",
            ".webm" => "audio/webm",
            _ => "application/octet-stream",
        };
}
