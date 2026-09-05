using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Infrastructure.Configuration;

namespace Vni.Ielts.Infrastructure.Ai.Writing;

/// <summary>
/// OpenAI Responses API adapter with JSON Schema structured output.
/// </summary>
public sealed class OpenAiWritingEvaluationClient(
    IHttpClientFactory httpFactory,
    ILogger<OpenAiWritingEvaluationClient> logger) : IWritingEvaluationClient
{
    public string Provider => "OpenAi";

    public async Task<WritingEvaluationResponse> EvaluateAsync(
        WritingEvaluationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = CurrentTicket.Value
            ?? throw new InvalidOperationException(
                "OpenAiWritingEvaluationClient requires an AiEgressTicket in AsyncLocal scope.");

        var system = WritingEvaluationPromptBuilder.SystemPrompt(
            request.DescriptorText, request.RubricVersion, request.DescriptorSource, request.PromptVersion);
        var user = WritingEvaluationPromptBuilder.UserPrompt(
            request.TaskPrompt, request.LearnerSubmission, request.WordCount, request.MinWords);

        var http = httpFactory.CreateClient(nameof(OpenAiWritingEvaluationClient));
        var baseUrl = string.IsNullOrWhiteSpace(ticket.BaseUrl)
            ? "https://api.openai.com/v1/"
            : ticket.BaseUrl.TrimEnd('/') + "/";

        var useResponsesApi = UsesResponsesApi(ticket);
        using var message = useResponsesApi
            ? BuildResponsesMessage(baseUrl, ticket, request, system, user)
            : BuildChatCompletionsMessage(baseUrl, ticket, system, user);

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.RevealApiKey());

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TransientWritingEvaluationException("OpenAI request timed out.");
        }
        catch (HttpRequestException e)
        {
            throw new TransientWritingEvaluationException(e.Message);
        }

        var payload = await response.Content.ReadAsStringAsync(ct);

        if ((int)response.StatusCode is >= 500 or 429)
            throw new TransientWritingEvaluationException($"OpenAI returned {(int)response.StatusCode}.");

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "OpenAI writing evaluation failed with status {Status}. Response length {Length}.",
                (int)response.StatusCode,
                payload.Length);

            throw new MarkingRejectedException("OpenAI writing evaluation request was rejected.");
        }

        var json = useResponsesApi
            ? ExtractOutputText(payload)
            : ExtractChatMessageContent(payload);
        var (inputTokens, outputTokens, requestId) = useResponsesApi
            ? ExtractResponsesUsage(payload, ticket.Model)
            : ExtractChatUsage(payload, ticket.Model);

        return new WritingEvaluationResponse(json, Provider, ticket.Model, requestId, inputTokens, outputTokens);
    }

    internal static readonly AsyncLocal<AiEgressTicket?> CurrentTicket = new();

    /// <summary>
    /// The Responses API is OpenAI's own, so only OpenAI's own endpoint gets
    /// it; everything else gets <c>chat/completions</c>, which is what an
    /// OpenAI-compatible reseller implements.
    ///
    /// <b>This asks <see cref="AiProviderPolicy.IsVendorEndpoint"/>, not
    /// <c>IsThirdPartyEndpoint</c>, and the difference is load-bearing.</b> The
    /// third-party check answers "is a company with no DPA in the path", and a
    /// host added to <see cref="AiProviderPolicy.ContractedProcessorHosts"/>
    /// stops being third-party the moment somebody signs — or decides. It does
    /// not simultaneously learn a new wire protocol. Reading the contract
    /// answer here would have switched this client to a Responses-API body
    /// against a reseller on that same commit.
    /// </summary>
    private static bool UsesResponsesApi(AiEgressTicket ticket) =>
        AiProviderPolicy.IsVendorEndpoint(ticket.ProviderSection, ticket.BaseUrl);

    private static HttpRequestMessage BuildResponsesMessage(
        string baseUrl,
        AiEgressTicket ticket,
        WritingEvaluationRequest request,
        string system,
        string user)
    {
        var schema = WritingEvaluationSchema.LoadNode();
        var body = new JsonObject
        {
            ["model"] = ticket.Model,
            ["input"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = system,
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = user,
                }),
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "writing_evaluation",
                    ["schema"] = schema,
                    ["strict"] = true,
                },
            },
            ["metadata"] = new JsonObject
            {
                ["idempotency_key"] = request.IdempotencyKey,
                ["attempt"] = request.Attempt.ToString(),
                ["rubric_version"] = request.RubricVersion,
                ["prompt_version"] = request.PromptVersion,
            },
        };

        return new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "responses"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    private static HttpRequestMessage BuildChatCompletionsMessage(
        string baseUrl,
        AiEgressTicket ticket,
        string system,
        string user)
    {
        var schema = WritingEvaluationSchema.LoadText();
        var body = new JsonObject
        {
            ["model"] = ticket.Model,
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = system
                                 + Environment.NewLine
                                 + Environment.NewLine
                                 + "Return only JSON matching this schema:"
                                 + Environment.NewLine
                                 + schema,
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = user,
                }),
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
        };

        return new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    private static string ExtractOutputText(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        return text.GetString() ?? throw new MarkingRejectedException("OpenAI returned empty text.");
                }
            }
        }

        if (root.TryGetProperty("output_text", out var flat) && flat.ValueKind == JsonValueKind.String)
            return flat.GetString() ?? throw new MarkingRejectedException("OpenAI returned empty output_text.");

        throw new MarkingRejectedException("OpenAI response did not contain structured output text.");
    }

    private static string ExtractChatMessageContent(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            throw new MarkingRejectedException("OpenAI chat response had no choices.");

        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out var message)) continue;

            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                return content.GetString() ?? throw new MarkingRejectedException("OpenAI returned empty content.");
        }

        throw new MarkingRejectedException("OpenAI chat response did not contain assistant content.");
    }

    private static (long Input, long Output, string RequestId) ExtractResponsesUsage(
        string payload, string model)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var requestId = root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? model
            : model;

        if (!root.TryGetProperty("usage", out var usage)) return (0, 0, requestId);

        var input = usage.TryGetProperty("input_tokens", out var inTok) ? inTok.GetInt64() : 0L;
        var output = usage.TryGetProperty("output_tokens", out var outTok) ? outTok.GetInt64() : 0L;

        return (input, output, requestId);
    }

    private static (long Input, long Output, string RequestId) ExtractChatUsage(
        string payload, string model)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var requestId = root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? model
            : model;

        if (!root.TryGetProperty("usage", out var usage)) return (0, 0, requestId);

        var input = usage.TryGetProperty("prompt_tokens", out var inTok) ? inTok.GetInt64() : 0L;
        var output = usage.TryGetProperty("completion_tokens", out var outTok) ? outTok.GetInt64() : 0L;

        return (input, output, requestId);
    }
}
