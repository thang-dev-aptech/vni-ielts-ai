using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Ai.Extraction;

/// <summary>
/// The GPT side of raw-package extraction.
///
/// ── What it may do ────────────────────────────────────────────────────────
///
/// <b>It produces a JSON string and nothing else.</b> It cannot persist, cannot
/// classify anything by itself, and its answer is not a candidate until
/// <see cref="ExamExtractionValidator"/> has accepted it and a reviewer has
/// looked at it. → CLAUDE.md rule 2
///
/// <b>It goes through <see cref="AiEgress"/> like every other adapter</b>, and
/// classifies its payload <see cref="AiDataClassification.Synthetic"/>. That is
/// not a loophole. The classification asks whether the payload is a real
/// person's data, and an exam paper has no data subject in it. The separate
/// question — whose copyright is being shown to a third party — belongs to
/// <see cref="ConfiguredExamExtractionParser"/>, which asks it before this
/// class is reached.
/// </summary>
public sealed class OpenAiExamExtractionClient(
    IHttpClientFactory httpFactory,
    IOptions<AiOptions> aiOptions,
    ILogger<OpenAiExamExtractionClient> logger) : IExamExtractionClient
{
    public string Provider => "OpenAi";

    /// <summary>
    /// <b>Read from configuration, and not defaulted.</b> An install with no
    /// model configured cannot make a call at all —
    /// <see cref="AiEgress.Authorise"/> refuses it — and the run record for
    /// that refusal should say so rather than name a model nobody chose.
    /// </summary>
    public string Model => aiOptions.Value.OpenAi.Model ?? "not configured";

    public bool IsReseller =>
        !AiProviderPolicy.IsVendorEndpoint("OpenAi", aiOptions.Value.OpenAi.BaseUrl);

    public async Task<ExamExtractionResponse> ExtractAsync(
        ExamExtractionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = AiEgress.Authorise(aiOptions.Value, "OpenAi", AiDataClassification.Synthetic);

        var body = new JsonObject
        {
            ["model"] = ticket.Model,
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = ExamExtractionPrompt.System(ExamExtractionSchema.LoadText()),
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = ExamExtractionPrompt.User(request.Sources),
                }),

            /*
             * <b>`json_object`, not `json_schema`.</b> Strict structured output
             * requires every object to list every property as required, and the
             * extraction contract has optional fields by design — `title`,
             * `answerKey` and `confidence` are absent when the paper does not
             * state them, which is the whole point of them. Sending it strict
             * is a 400; sending it non-strict buys nothing the system prompt
             * does not already do.
             *
             * <b>So the schema is enforced here rather than on the wire</b>, by
             * ExamExtractionValidator. That is the weaker place to enforce
             * shape and also the only place that was ever going to decide: a
             * provider validating its own output is a provider grading its own
             * homework. → docs/ai/output-contracts.md
             */
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["temperature"] = 0,
        };

        var http = httpFactory.CreateClient(nameof(OpenAiExamExtractionClient));
        var baseUrl = string.IsNullOrWhiteSpace(ticket.BaseUrl)
            ? "https://api.openai.com/v1/"
            : ticket.BaseUrl.TrimEnd('/') + "/";

        using var message = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(baseUrl), "chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.RevealApiKey());

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

            var completion = ReadCompletion(payload, ticket.Model);

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
        string payload, string requestedModel)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException e)
        {
            // The envelope, not the extraction: a body that is not JSON at all
            // is a gateway or proxy answering in the model's place.
            throw new TransientExamExtractionException(
                $"The extraction response envelope was not JSON: {e.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? requestedModel
                : requestedModel;

            var requestId = root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString() ?? model
                : model;

            long input = 0, output = 0;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var i) && i.ValueKind == JsonValueKind.Number)
                    input = i.GetInt64();
                if (usage.TryGetProperty("completion_tokens", out var o) && o.ValueKind == JsonValueKind.Number)
                    output = o.GetInt64();
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            {
                throw new ExamExtractionRejectedException(
                    ExamExtractionRejection.NoContent, "The extraction response contained no choices.");
            }

            foreach (var choice in choices.EnumerateArray())
            {
                /*
                 * <b>A truncated completion is transient, not a bad proposal.</b>
                 * Hitting the output ceiling produces JSON that ends mid-object,
                 * which the validator would report as a schema failure — sending
                 * a reviewer to look for a content problem that is really a
                 * length problem.
                 */
                if (choice.TryGetProperty("finish_reason", out var reason)
                    && reason.ValueKind == JsonValueKind.String
                    && reason.GetString() is "length")
                {
                    throw new TransientExamExtractionException(
                        "The extraction response was cut off by the model's output limit.");
                }

                if (choice.TryGetProperty("message", out var msg)
                    && msg.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String
                    && content.GetString() is { Length: > 0 } text)
                {
                    return (Unfence(text), model, requestId, input, output);
                }
            }

            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.NoContent,
                "The extraction response contained no assistant content.");
        }
    }

    /// <summary>
    /// Strips a ```json fence when one arrives anyway. The prompt forbids it;
    /// models produce it regardless, and failing a whole package over three
    /// backticks would be a bad trade.
    /// </summary>
    internal static string Unfence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0) return trimmed;

        var body = trimmed[(firstNewline + 1)..];
        var close = body.LastIndexOf("```", StringComparison.Ordinal);

        return (close < 0 ? body : body[..close]).Trim();
    }
}
