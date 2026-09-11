using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Ai.Extraction;

/// <summary>
/// The Gemini side of raw-package extraction.
///
/// <para>
/// <b>The same contract, the same validator, a different envelope.</b> Gemini
/// takes its system instruction in its own field and returns candidate parts
/// rather than choices; everything after the JSON string is extracted is
/// shared with the GPT adapter, which is what makes the shared contract suite
/// meaningful rather than two suites that happen to agree today.
/// </para>
///
/// <para>
/// <b><c>responseSchema</c> is deliberately not sent.</b> Gemini accepts a
/// subset of JSON Schema — no <c>$defs</c>, no <c>$ref</c>, and nullability
/// expressed as its own keyword rather than a type union — and the extraction
/// contract is built from all three. Rewriting it into that subset would create
/// a second, drifting copy of the contract whose disagreements with the real
/// one would show up as rejected papers. <c>responseMimeType</c> asks for JSON;
/// the shape is enforced where it has to be enforced anyway, since
/// provider-side validation is a convenience the server must assume failed.
/// → <c>docs/ai/output-contracts.md</c>
/// </para>
/// </summary>
public sealed class GeminiExamExtractionClient(
    IHttpClientFactory httpFactory,
    IOptions<AiOptions> aiOptions,
    ILogger<GeminiExamExtractionClient> logger) : IExamExtractionClient
{
    public string Provider => "Gemini";

    /// <inheritdoc cref="OpenAiExamExtractionClient.Model"/>
    public string Model => aiOptions.Value.Gemini.Model ?? "not configured";

    public bool IsReseller =>
        !AiProviderPolicy.IsVendorEndpoint("Gemini", aiOptions.Value.Gemini.BaseUrl);

    public async Task<ExamExtractionResponse> ExtractAsync(
        ExamExtractionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = AiEgress.Authorise(aiOptions.Value, "Gemini", AiDataClassification.Synthetic);

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject
                {
                    ["text"] = ExamExtractionPrompt.System(ExamExtractionSchema.LoadText()),
                }),
            },
            ["contents"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray(new JsonObject
                    {
                        ["text"] = ExamExtractionPrompt.User(request.Sources),
                    }),
                }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["temperature"] = 0,
            },
        };

        var http = httpFactory.CreateClient(nameof(GeminiExamExtractionClient));
        var baseUrl = string.IsNullOrWhiteSpace(ticket.BaseUrl)
            ? "https://generativelanguage.googleapis.com/v1beta/"
            : ticket.BaseUrl.TrimEnd('/') + "/";

        var url = new Uri(
            new Uri(baseUrl),
            $"models/{Uri.EscapeDataString(ticket.Model)}:generateContent"
            + $"?key={Uri.EscapeDataString(ticket.RevealApiKey())}");

        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        message.Headers.Add("X-Goog-Request-Id", request.CorrelationId);

        using var deadline = ExamExtractionHttp.Deadline(ct, request.TimeoutSeconds);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TransientExamExtractionException("The extraction request timed out.");
        }
        catch (HttpRequestException e)
        {
            throw new TransientExamExtractionException(e.Message);
        }

        using (response)
        {
            string payload;
            try
            {
                payload = await ExamExtractionHttp.ReadBoundedAsync(
                    response, request.MaxResponseBytes, deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TransientExamExtractionException(
                    "The extraction response stalled before it finished.");
            }

            ExamExtractionHttp.EnsureSuccess(
                response, Provider, request.CorrelationId, payload.Length, logger);

            var completion = ReadCompletion(payload, ticket.Model, request.CorrelationId);

            logger.LogInformation(
                "Exam extraction answered by {Provider} model {Model}, request {RequestId}, "
                + "correlation {CorrelationId}, prompt {PromptVersion}, attempt {Attempt}, "
                + "{InputTokens} in / {OutputTokens} out.",
                Provider,
                completion.Model,
                completion.RequestId,
                request.CorrelationId,
                request.PromptVersion,
                request.Attempt,
                completion.Input,
                completion.Output);

            return new ExamExtractionResponse(
                completion.Json, Provider, completion.Model, completion.RequestId,
                completion.Input, completion.Output);
        }
    }

    private static (string Json, string Model, string RequestId, long Input, long Output) ReadCompletion(
        string payload, string requestedModel, string correlationId)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException e)
        {
            throw new TransientExamExtractionException(
                $"The extraction response envelope was not JSON: {e.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            var model = root.TryGetProperty("modelVersion", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? requestedModel
                : requestedModel;

            /*
             * <b>Falls back to the correlation id this product generated.</b>
             * Gemini does not always return `responseId`, and a run record whose
             * request identifier is the model name cannot be used to find the
             * call — which is the only thing a request identifier is for.
             */
            var requestId = root.TryGetProperty("responseId", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString() ?? correlationId
                : correlationId;

            long input = 0, output = 0;
            if (root.TryGetProperty("usageMetadata", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("promptTokenCount", out var i) && i.ValueKind == JsonValueKind.Number)
                    input = i.GetInt64();
                if (usage.TryGetProperty("candidatesTokenCount", out var o) && o.ValueKind == JsonValueKind.Number)
                    output = o.GetInt64();
            }

            if (!root.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array)
            {
                throw new ExamExtractionRejectedException(
                    ExamExtractionRejection.NoContent, "The extraction response contained no candidates.");
            }

            foreach (var candidate in candidates.EnumerateArray())
            {
                if (candidate.TryGetProperty("finishReason", out var reason)
                    && reason.ValueKind == JsonValueKind.String
                    && reason.GetString() is "MAX_TOKENS")
                {
                    throw new TransientExamExtractionException(
                        "The extraction response was cut off by the model's output limit.");
                }

                if (!candidate.TryGetProperty("content", out var content)) continue;
                if (!content.TryGetProperty("parts", out var parts)
                    || parts.ValueKind != JsonValueKind.Array) continue;

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text)
                        && text.ValueKind == JsonValueKind.String
                        && text.GetString() is { Length: > 0 } json)
                    {
                        return (OpenAiExamExtractionClient.Unfence(json), model, requestId, input, output);
                    }
                }
            }

            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.NoContent,
                "The extraction response contained no structured output text.");
        }
    }
}
