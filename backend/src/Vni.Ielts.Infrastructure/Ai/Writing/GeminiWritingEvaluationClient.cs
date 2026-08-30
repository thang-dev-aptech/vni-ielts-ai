using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Vni.Ielts.Domain.Assessment;

namespace Vni.Ielts.Infrastructure.Ai.Writing;

/// <summary>
/// Gemini generateContent adapter with JSON Schema response enforcement.
/// </summary>
public sealed class GeminiWritingEvaluationClient(
    IHttpClientFactory httpFactory,
    ILogger<GeminiWritingEvaluationClient> logger) : IWritingEvaluationClient
{
    public string Provider => "Gemini";

    public async Task<WritingEvaluationResponse> EvaluateAsync(
        WritingEvaluationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = CurrentTicket.Value
            ?? throw new InvalidOperationException(
                "GeminiWritingEvaluationClient requires an AiEgressTicket in AsyncLocal scope.");

        var schema = WritingEvaluationSchema.LoadNode();
        var system = WritingEvaluationPromptBuilder.SystemPrompt(
            request.DescriptorText, request.RubricVersion, request.DescriptorSource, request.PromptVersion);
        var user = WritingEvaluationPromptBuilder.UserPrompt(
            request.TaskPrompt, request.LearnerSubmission, request.WordCount, request.MinWords);

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = system }),
            },
            ["contents"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = user }),
                }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = schema,
            },
        };

        var http = httpFactory.CreateClient(nameof(GeminiWritingEvaluationClient));
        var baseUrl = string.IsNullOrWhiteSpace(ticket.BaseUrl)
            ? "https://generativelanguage.googleapis.com/v1beta/"
            : ticket.BaseUrl.TrimEnd('/') + "/";

        var url = new Uri(
            new Uri(baseUrl),
            $"models/{Uri.EscapeDataString(ticket.Model)}:generateContent?key={Uri.EscapeDataString(ticket.RevealApiKey())}");

        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        message.Headers.Add("X-Goog-Request-Id", request.IdempotencyKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TransientWritingEvaluationException("Gemini request timed out.");
        }
        catch (HttpRequestException e)
        {
            throw new TransientWritingEvaluationException(e.Message);
        }

        var payload = await response.Content.ReadAsStringAsync(ct);

        if ((int)response.StatusCode is >= 500 or 429)
            throw new TransientWritingEvaluationException($"Gemini returned {(int)response.StatusCode}.");

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Gemini writing evaluation failed with status {Status}. Response length {Length}.",
                (int)response.StatusCode,
                payload.Length);

            throw new MarkingRejectedException("Gemini writing evaluation request was rejected.");
        }

        var json = ExtractCandidateText(payload);
        var (inputTokens, outputTokens, requestId) = ExtractUsage(payload, ticket.Model);

        return new WritingEvaluationResponse(json, Provider, ticket.Model, requestId, inputTokens, outputTokens);
    }

    internal static readonly AsyncLocal<AiEgressTicket?> CurrentTicket = new();

    private static string ExtractCandidateText(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            throw new MarkingRejectedException("Gemini response had no candidates.");

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)) continue;
            if (!content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    return text.GetString() ?? throw new MarkingRejectedException("Gemini returned empty text.");
            }
        }

        throw new MarkingRejectedException("Gemini response did not contain structured output text.");
    }

    private static (long Input, long Output, string RequestId) ExtractUsage(string payload, string model)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        if (!root.TryGetProperty("usageMetadata", out var usage)) return (0, 0, model);

        var input = usage.TryGetProperty("promptTokenCount", out var inTok) ? inTok.GetInt64() : 0L;
        var output = usage.TryGetProperty("candidatesTokenCount", out var outTok) ? outTok.GetInt64() : 0L;

        return (input, output, model);
    }
}
