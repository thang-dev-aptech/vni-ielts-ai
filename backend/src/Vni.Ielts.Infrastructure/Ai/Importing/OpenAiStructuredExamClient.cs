using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Infrastructure.Ai.Importing;

/// <summary>
/// The GPT side of AI-assisted exam import — the implementation
/// <see cref="IStructuredExamAiClient"/> has been waiting for since the port was
/// written.
///
/// ── What this is allowed to do, and what it is not ────────────────────────
///
/// <b>It produces a candidate package and nothing else.</b> It cannot persist,
/// cannot publish, and its output is not an exam until
/// <c>ExamPackageValidator</c> has accepted it and a person has reviewed it.
/// That is the same rule the Writing evaluator lives under: a model's answer is
/// a claim. → CLAUDE.md rule 2
///
/// <b>It goes through <see cref="AiEgress"/> like every other adapter</b>, and
/// classifies its payload <see cref="AiDataClassification.Synthetic"/>. That is
/// not a loophole: the classification asks whether the payload is a real
/// person's data, and an exam paper has no data subject in it. The rights
/// question — whose copyright is being sent to a third party — is a different
/// question with a different gate, and
/// <c>ProviderNeutralExamSourceParser</c> asks it before this class is reached.
/// </summary>
public sealed class OpenAiStructuredExamClient(
    IHttpClientFactory httpFactory,
    IOptions<AiOptions> aiOptions,
    ExamParsePromptSources prompts,
    ILogger<OpenAiStructuredExamClient> logger) : IStructuredExamAiClient
{
    public string Provider => "OpenAi";

    public bool IsReseller =>
        !AiProviderPolicy.IsVendorEndpoint("OpenAi", aiOptions.Value.OpenAi.BaseUrl);

    public async Task<StructuredParseResponse> ParseAsync(
        StructuredParseRequest request, CancellationToken ct)
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
                    ["content"] = ExamSourceParsePrompt.System(
                        prompts.SectionsSchema, prompts.ShapeExample),
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = ExamSourceParsePrompt.User(
                        request.SourceMediaType, request.SourceText),
                }),

            /*
             * <b>`json_object`, not `json_schema`, and the reason is the schema
             * rather than the endpoint.</b> OpenAI's strict structured output
             * requires every object to set `additionalProperties: false` and to
             * list every property as required; `exam.schema.json` is 31KB of
             * conditional `allOf` and optional fields and satisfies neither.
             * Sending it as a strict schema is a 400, and sending it non-strict
             * buys nothing the system prompt does not already do.
             *
             * <b>`StrictJsonSchema` on the request is therefore honoured by the
             * validator rather than by the wire.</b> That is the weaker place
             * to enforce shape and it is also the only place that was ever
             * going to decide: a provider that validated its own output would
             * still be a provider grading its own homework.
             */
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["temperature"] = 0,

            /*
             * <b>Named, because the default is too small for a paper.</b> A
             * Reading section with three passages and forty questions is tens
             * of thousands of output tokens. Several OpenAI-compatible hosts
             * default `max_tokens` to 4k, which truncates mid-object and
             * surfaces as a schema failure — the failure this client already
             * maps to "split the source", which is the wrong diagnosis when
             * the model was still going. 32k is the ceiling we ask for; a
             * host that cannot honour it will still stop at its own limit and
             * the `finish_reason = length` path below still fires.
             */
            ["max_tokens"] = 32768,
            ["stream"] = true,
        };

        var http = httpFactory.CreateClient(nameof(OpenAiStructuredExamClient));
        var baseUrl = string.IsNullOrWhiteSpace(ticket.BaseUrl)
            ? "https://api.openai.com/v1/"
            : ticket.BaseUrl.TrimEnd('/') + "/";

        using var message = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(baseUrl), "chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.RevealApiKey());

        /*
         * <b>Own deadline, not only HttpClient.Timeout.</b> With
         * ResponseHeadersRead the client timeout stops guarding the body on
         * several .NET hosts, and a silent SSE stall then runs for hours.
         * Eighteen minutes is longer than every successful Cam 17 parse so far
         * and short enough that a hung socket fails the attempt and retries.
         */
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(18));
        var parseCt = deadline.Token;

        HttpResponseMessage response;
        try
        {
            /*
             * <b>Headers, not the body.</b> A whole paper's JSON takes minutes
             * to stream. `SendAsync`'s default waits for every byte, and the
             * reseller in front of this model killed that wait at 304s with a
             * 504 — twice, on 2026-09-03, before a single token was logged.
             * Streaming keeps the socket talking so the gateway does not
             * decide the origin is dead.
             */
            response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, parseCt);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TransientAiParserException("The exam parse request timed out.");
        }
        catch (HttpRequestException e)
        {
            throw new TransientAiParserException(e.Message);
        }

        using (response)
        {
        if ((int)response.StatusCode is >= 500 or 429)
        {
            var errorBody = await response.Content.ReadAsStringAsync(parseCt);
            // The body names the fault (a model that is down, a request the
            // gateway would not forward); the byte count does not. Capped, as
            // for the 4xx path below. Cam 19 T4 returned 500 four times on
            // 2026-09-04 while its neighbours parsed, and "175 byte body" was
            // all there was to go on.
            var excerpt = errorBody.Length <= 300 ? errorBody : errorBody[..300] + "…";
            throw new TransientAiParserException(
                $"The provider returned {(int)response.StatusCode}: {excerpt}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(parseCt);
            // The body is the provider's own error text — a parameter name, a
            // context limit, a model id — and it is the only thing that tells
            // an operator which of those it was. The length alone did not:
            // gpt-5.4 rejected every Reading paper with "status 400, 191 bytes"
            // on 2026-09-04 and the number said nothing. Capped, because a
            // proxy that echoes the request back would put the paper in a log.
            logger.LogWarning(
                "Exam parse request rejected with status {Status}. Response length {Length}. Body: {Body}",
                (int)response.StatusCode,
                errorBody.Length,
                errorBody.Length <= 600 ? errorBody : errorBody[..600] + "…");

            throw new InvalidOperationException(
                $"The exam parse request was rejected with status {(int)response.StatusCode}.");
        }

        string json;
        string model;
        string requestId;
        long inputTokens;
        long outputTokens;
        string? finishReason;
        try
        {
            (json, model, requestId, inputTokens, outputTokens, finishReason) =
                await ReadSseCompletion(response, ticket.Model, logger, parseCt);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TransientAiParserException(
                "The exam parse stream stalled before the response finished.");
        }

        if (finishReason is "length")
        {
            throw new TransientAiParserException(
                "The parse response was cut off by the model's output limit. Split the source "
                + "into one module per request.");
        }

        /*
         * Some hosts end the SSE stream without finish_reason=length when they
         * hit their own ceiling. The content then stops mid-string and only
         * surfaces later as a JsonReaderException in the importer. Treat that
         * as the same transient length failure so MaxAttempts can retry.
         */
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new TransientAiParserException(
                "The parse response was not complete JSON"
                + (finishReason is null ? "" : $" (finish_reason={finishReason})")
                + $": {e.Message}");
        }

        logger.LogInformation(
            "Exam source parsed by {Model}, request {RequestId}, prompt {PromptVersion}, "
            + "attempt {Attempt}, {InputTokens} in / {OutputTokens} out.",
            model,
            requestId,
            request.PromptVersion,
            request.Attempt,
            inputTokens,
            outputTokens);

        return new StructuredParseResponse(
            json, model, requestId, inputTokens, outputTokens, EstimatedCostUsd: 0m);
        }
    }

    /// <summary>
    /// Reads a streamed <c>chat/completions</c> body, or a non-stream JSON
    /// body if the host ignored <c>stream: true</c>.
    /// </summary>
    private static async Task<(string Json, string Model, string RequestId, long Input, long Output, string? Finish)>
        ReadSseCompletion(
            HttpResponseMessage response,
            string requestedModel,
            ILogger logger,
            CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? first = null;
        while (await reader.ReadLineAsync(ct) is { } probe)
        {
            if (string.IsNullOrWhiteSpace(probe)) continue;
            first = probe;
            break;
        }

        if (first is null)
            throw new InvalidOperationException("The parse response was empty.");

        if (first.StartsWith('{'))
        {
            var payload = first + "\n" + await reader.ReadToEndAsync(ct);
            var (json, model, requestId, input, output) = ReadCompletion(payload, requestedModel);
            return (json, model, requestId, input, output, Finish: null);
        }

        var text = new StringBuilder();
        var modelName = requestedModel;
        var requestIdValue = requestedModel;
        long inputTokens = 0, outputTokens = 0;
        string? finish = null;
        var sawData = false;
        var firstTokenLogged = false;

        for (var line = first; line is not null; line = await reader.ReadLineAsync(ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line["data:".Length..].Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") break;

            sawData = true;
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            if (root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
                modelName = m.GetString() ?? modelName;

            if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                requestIdValue = id.GetString() ?? requestIdValue;

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var i) && i.ValueKind == JsonValueKind.Number)
                    inputTokens = i.GetInt64();
                if (usage.TryGetProperty("completion_tokens", out var o) && o.ValueKind == JsonValueKind.Number)
                    outputTokens = o.GetInt64();
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("finish_reason", out var reason)
                    && reason.ValueKind == JsonValueKind.String)
                {
                    finish = reason.GetString();
                }

                if (choice.TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("content", out var deltaContent)
                    && deltaContent.ValueKind == JsonValueKind.String)
                {
                    text.Append(deltaContent.GetString());
                    if (!firstTokenLogged && text.Length > 0)
                    {
                        firstTokenLogged = true;
                        logger.LogInformation("Exam parse stream opened after headers.");
                    }
                }
                else if (choice.TryGetProperty("message", out var msg)
                    && msg.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String)
                {
                    text.Append(content.GetString());
                }
            }
        }

        if (!sawData)
            throw new InvalidOperationException("The parse response contained no SSE data.");

        if (text.Length == 0)
            throw new InvalidOperationException("The parse response contained no assistant content.");

        return (Unfence(text.ToString()), modelName, requestIdValue, inputTokens, outputTokens, finish);
    }

    /// <summary>
    /// <b>Cost is reported as zero, deliberately, rather than estimated.</b>
    /// This route's per-token price is not published and its <c>usage</c> block
    /// has been observed carrying fields from a different vendor entirely, so
    /// any number here would be a guess wearing a decimal point — and a guessed
    /// cost is worse than no cost, because it gets summed and put in a report.
    /// The token counts beside it are real and are what a bill can be
    /// reconciled against. → <c>docs/development/ai-provider-setup.md</c>
    /// </summary>
    private static (string Json, string Model, string RequestId, long Input, long Output)
        ReadCompletion(string payload, string requestedModel)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() ?? requestedModel
            : requestedModel;

        var requestId = root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? model
            : model;

        long input = 0, output = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            input = usage.TryGetProperty("prompt_tokens", out var i) ? i.GetInt64() : 0L;
            output = usage.TryGetProperty("completion_tokens", out var o) ? o.GetInt64() : 0L;
        }

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The parse response contained no choices.");

        foreach (var choice in choices.EnumerateArray())
        {
            /*
             * <b>A truncated completion is a transient failure, not a bad
             * package.</b> A whole IELTS paper is a large output and hitting the
             * ceiling produces JSON that ends mid-object — which the validator
             * would report as a hundred schema errors, sending a reviewer to
             * look for a content problem that is really a length problem.
             */
            if (choice.TryGetProperty("finish_reason", out var reason)
                && reason.ValueKind == JsonValueKind.String
                && reason.GetString() is "length")
            {
                throw new TransientAiParserException(
                    "The parse response was cut off by the model's output limit. Split the source "
                    + "into one module per request.");
            }

            if (choice.TryGetProperty("message", out var msg)
                && msg.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String
                && content.GetString() is { Length: > 0 } text)
            {
                return (Unfence(text), model, requestId, input, output);
            }
        }

        throw new InvalidOperationException("The parse response contained no assistant content.");
    }

    /// <summary>
    /// Strips a ```json fence when one arrives anyway. The prompt forbids it;
    /// models produce it regardless, and failing the whole import over three
    /// backticks would be a bad trade.
    /// </summary>
    private static string Unfence(string text)
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

/// <summary>
/// The two fixed halves of the parse prompt — the schema fragment and the
/// worked example — computed once.
///
/// <b>Separate from the client because they are inputs to it, not properties of
/// it.</b> Both are derived from files in the repository, both cost real work
/// to build (a transitive walk over <c>$defs</c>; a deep copy with every long
/// string truncated), and both are identical for every request. Building them
/// per call would repeat that on every paper in a batch of sixteen.
/// </summary>
public sealed class ExamParsePromptSources
{
    private readonly Lazy<string> _sectionsSchema;
    private readonly Lazy<string> _shapeExample;

    /// <param name="repositoryRoot">
    /// The directory holding <c>contracts/</c>. Passed in rather than
    /// discovered, so a caller that already knows it does not search twice and
    /// a test can point at a fixture tree.
    /// </param>
    /// <param name="templatePackagePath">
    /// An accepted package whose <c>sections</c> demonstrate the shape.
    /// Defaults to VOL 9 Test 1, the paper the rest of this series matches.
    /// </param>
    public ExamParsePromptSources(string repositoryRoot, string? templatePackagePath = null)
    {
        var schemaPath = Path.Combine(repositoryRoot, "contracts", "schemas", "exam.schema.json");
        var templatePath = templatePackagePath
            ?? Path.Combine(repositoryRoot, "fixtures", "exams", "vol9-test-1.json");

        _sectionsSchema = new Lazy<string>(
            () => ExamSourceParsePrompt.SectionsSchema(File.ReadAllText(schemaPath)));

        _shapeExample = new Lazy<string>(
            () => ExamSourceParsePrompt.ShapeExample(File.ReadAllText(templatePath)));
    }

    public string SectionsSchema => _sectionsSchema.Value;

    public string ShapeExample => _shapeExample.Value;
}

/// <summary>
/// Records nothing. The counterpart to
/// <c>NullWritingEvaluationCostMetric</c>, and present for the same reason: the
/// port exists so that cost is observable when somebody wires a meter to it,
/// and inventing a price in the meantime would put a fabricated number into
/// whatever reads it. → <c>G-11</c>
/// </summary>
public sealed class NullAiImportCostMetric : IAiImportCostMetric
{
    public void Record(
        string provider, string model, long inputTokens, long outputTokens, decimal estimatedCostUsd)
    {
    }
}
