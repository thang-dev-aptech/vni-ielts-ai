using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Infrastructure.Ai;

namespace Vni.Ielts.Infrastructure.Explanations;

/// <summary>
/// OpenAI-compatible chat/completions adapter for Reading/Listening explanations.
/// </summary>
public sealed class OpenAiExplanationGenerator(
    IHttpClientFactory httpFactory,
    IOptions<AiOptions> aiOptions,
    ILogger<OpenAiExplanationGenerator> logger) : IReadingListeningExplanationGenerator
{
    public async Task<ExplanationGenerationResult> GenerateAsync(
        ExplanationGenerationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        AiEgressTicket ticket;
        try
        {
            var classification = request.Personalized
                ? AiDataClassification.LearnerPersonal
                : AiDataClassification.Synthetic;

            ticket = AiEgress.Authorise(aiOptions.Value, "OpenAi", classification);
        }
        catch (AiEgressRefusedException e)
        {
            return Failed($"EXPLANATION_EGRESS_{e.Refusal.ToString().ToUpperInvariant()}");
        }

        var body = new JsonObject
        {
            ["model"] = ticket.Model,
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = SystemPrompt(),
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = request.Personalized
                        ? ExplanationPromptSafety.UserPrompt(
                            request.QuestionPrompt,
                            request.ExpectedAnswer,
                            request.LearnerAnswer,
                            request.PassageOrTranscript)
                        : CanonicalPrompt(request),
                }),
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
        };

        var http = httpFactory.CreateClient(nameof(OpenAiExplanationGenerator));
        var baseUrl = string.IsNullOrWhiteSpace(ticket.BaseUrl)
            ? "https://api.openai.com/v1/"
            : ticket.BaseUrl.TrimEnd('/') + "/";

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.RevealApiKey());

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed("EXPLANATION_PROVIDER_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return Failed("EXPLANATION_PROVIDER_FAILED");
        }

        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "OpenAI explanation generation failed with status {Status}. Response length {Length}.",
                (int)response.StatusCode,
                payload.Length);

            return Failed("EXPLANATION_PROVIDER_REJECTED");
        }

        try
        {
            var json = ExtractMessageContent(payload);
            var requestId = ExtractRequestId(payload, ticket.Model);

            return new ExplanationGenerationResult(
                true,
                json,
                new ExplanationProviderMetadata(
                    "openai",
                    ticket.Model,
                    CanonicalExplanationWorkflow.PromptVersion,
                    requestId),
                null);
        }
        catch (JsonException)
        {
            return Failed("EXPLANATION_PROVIDER_MALFORMED");
        }
        catch (MarkingRejectedException)
        {
            return Failed("EXPLANATION_PROVIDER_MALFORMED");
        }
    }

    private static ExplanationGenerationResult Failed(string code) =>
        new(false, null, null, code);

    private static string SystemPrompt() =>
        """
        You explain IELTS Reading and Listening answer-key items.
        Return one JSON object with exactly these fields:
        - correctAnswer: string, exactly the supplied expected answer
        - shortReason: string
        - evidence: non-empty array of quoted evidence from the passage or transcript
        - commonMistake: string or null

        Do not include a band, score, criteria, rawScore, or isCorrect field.
        The answer key is authoritative; never change the expected answer.
        """;

    private static string CanonicalPrompt(ExplanationGenerationRequest request) =>
        $"""
        Question:
        {request.QuestionPrompt}

        Expected answer (from the answer key - do not change it):
        {request.ExpectedAnswer}

        Passage or transcript (authored content):
        {request.PassageOrTranscript ?? string.Empty}

        Explain why the expected answer is correct. Return JSON matching the explanation schema only.
        """;

    private static string ExtractMessageContent(string payload)
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

    private static string ExtractRequestId(string payload, string model)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        return root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? model
            : model;
    }
}
