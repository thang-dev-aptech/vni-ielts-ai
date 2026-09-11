using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Ai.Extraction;

namespace Vni.Ielts.Infrastructure.Tests.Ai.Extraction;

/// <summary>
/// One suite, both adapters.
///
/// <para>
/// <b>Written as a theory over the provider name rather than as two files.</b>
/// The wire envelope differs; nothing else about an extraction call is allowed
/// to. Two suites that happen to agree today drift the first time one provider
/// is touched, and the drift shows up as a package that behaves differently
/// depending on which provider parsed it.
/// </para>
///
/// <para>
/// <b>No live call and no credential.</b> Every response here is a stub, and
/// the key is the literal string <c>test-key</c>. → CLAUDE.md rule 6
/// </para>
/// </summary>
public sealed class ExamExtractionClientContractTests
{
    public static TheoryData<string> BothProviders() => new("OpenAi", "Gemini");

    [Theory]
    [MemberData(nameof(BothProviders))]
    public async Task A_structured_answer_is_returned_with_the_facts_about_the_call(string provider)
    {
        var extraction = Fixture("reading-classified-valid.json");
        var client = Client(provider, Handler(_ => Ok(provider, extraction)));

        var response = await client.ExtractAsync(Request(), default);

        Assert.Equal(provider, response.Provider);
        Assert.Equal(extraction.Trim(), response.Json.Trim());
        Assert.Equal("model-under-test", response.Model);
        Assert.Equal("provider-request-1", response.RequestId);
        Assert.Equal(11, response.InputTokens);
        Assert.Equal(22, response.OutputTokens);

        ExamExtractionValidator.EnsureSchemaValid(response.Json);
    }

    [Theory]
    [MemberData(nameof(BothProviders))]
    public async Task A_markdown_fence_the_prompt_forbade_is_removed_rather_than_failing_the_package(
        string provider)
    {
        var extraction = Fixture("reading-classified-valid.json");
        var client = Client(provider, Handler(_ => Ok(provider, $"```json\n{extraction}\n```")));

        var response = await client.ExtractAsync(Request(), default);

        ExamExtractionValidator.EnsureSchemaValid(response.Json);
    }

    [Theory]
    [MemberData(nameof(BothProviders))]
    public async Task The_source_text_and_the_key_never_appear_in_the_request_body(string provider)
    {
        string? body = null;
        Uri? url = null;

        var client = Client(provider, Handler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            url = request.RequestUri;
            return Ok(provider, Fixture("reading-classified-valid.json"));
        }));

        await client.ExtractAsync(Request(), default);

        Assert.NotNull(body);

        // The passage is what a provider is for; the things around it are not.
        Assert.Contains("Synthetic passage", body!, StringComparison.Ordinal);
        Assert.DoesNotContain("test-key", body!, StringComparison.Ordinal);
        Assert.DoesNotContain("reading/paper.docx", body!, StringComparison.Ordinal);
        Assert.DoesNotContain("author@", body!, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp/", body!, StringComparison.Ordinal);
        Assert.DoesNotContain("package-1", body!, StringComparison.Ordinal);

        // The provider is reached at its configured endpoint, not a default.
        Assert.Equal("api.vietapi.tech", url!.Host);
    }

    [Theory]
    [InlineData("OpenAi", 500)]
    [InlineData("OpenAi", 503)]
    [InlineData("OpenAi", 429)]
    [InlineData("Gemini", 500)]
    [InlineData("Gemini", 503)]
    [InlineData("Gemini", 429)]
    public async Task A_rate_limit_or_a_server_fault_is_transient(string provider, int status)
    {
        var client = Client(provider, Handler(_ => new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent("{\"error\":\"upstream\"}", Encoding.UTF8, "application/json"),
        }));

        await Assert.ThrowsAsync<TransientExamExtractionException>(
            () => client.ExtractAsync(Request(), default));
    }

    /// <summary>
    /// <b>A 400 is not retried.</b> A structured-output endpoint that rejects
    /// the request rejects it identically the second time, and the package is
    /// waiting for a reviewer while the budget is spent finding that out.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothProviders))]
    public async Task A_rejected_request_is_permanent_and_says_nothing_about_the_body(string provider)
    {
        var client = Client(provider, Handler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"unsupported response_format\"}}",
                Encoding.UTF8,
                "application/json"),
        }));

        var rejected = await Assert.ThrowsAsync<ExamExtractionRejectedException>(
            () => client.ExtractAsync(Request(), default));

        Assert.Equal(ExamExtractionRejection.ProviderRejected, rejected.Code);
        Assert.DoesNotContain("response_format", rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A completion cut off at the output ceiling is truncated JSON, which a
    /// schema check would report as a content fault — sending a reviewer to
    /// look for a problem in the paper that is really a problem in the call.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothProviders))]
    public async Task A_truncated_completion_is_transient_rather_than_a_bad_proposal(string provider)
    {
        var client = Client(provider, Handler(_ => Truncated(provider)));

        await Assert.ThrowsAsync<TransientExamExtractionException>(
            () => client.ExtractAsync(Request(), default));
    }

    [Theory]
    [MemberData(nameof(BothProviders))]
    public async Task An_answer_with_no_content_is_refused_with_a_stable_code(string provider)
    {
        var client = Client(provider, Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"id\":\"provider-request-1\",\"modelVersion\":\"model-under-test\"}",
                Encoding.UTF8,
                "application/json"),
        }));

        var rejected = await Assert.ThrowsAsync<ExamExtractionRejectedException>(
            () => client.ExtractAsync(Request(), default));

        Assert.Equal(ExamExtractionRejection.NoContent, rejected.Code);
    }

    /// <summary>
    /// A body that is not JSON at all is a gateway or a captive portal
    /// answering in the model's place — worth trying again, and not a proposal
    /// to show anyone.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothProviders))]
    public async Task A_non_json_envelope_is_transient(string provider)
    {
        var client = Client(provider, Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>upstream error</html>", Encoding.UTF8, "text/html"),
        }));

        await Assert.ThrowsAsync<TransientExamExtractionException>(
            () => client.ExtractAsync(Request(), default));
    }

    [Theory]
    [MemberData(nameof(BothProviders))]
    public async Task A_response_larger_than_the_cap_is_refused_while_it_is_being_read(string provider)
    {
        var client = Client(provider, Handler(_ => Ok(provider, Fixture("oversized-response.json"))));

        var rejected = await Assert.ThrowsAsync<ExamExtractionRejectedException>(
            () => client.ExtractAsync(Request(maxResponseBytes: 4096), default));

        Assert.Equal(ExamExtractionRejection.ResponseTooLarge, rejected.Code);
        Assert.DoesNotContain("4096", rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A stalled provider is the failure this deadline exists for.</b>
    /// <see cref="HttpClient.Timeout"/> guards a request; it does not guard a
    /// socket that accepted the request and went quiet, and a worker with no
    /// deadline runs until somebody notices the queue.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothProviders))]
    public async Task A_provider_that_never_answers_times_out_as_a_transient_failure(string provider)
    {
        var client = Client(provider, Handler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Ok(provider, "{}");
        }));

        await Assert.ThrowsAsync<TransientExamExtractionException>(
            () => client.ExtractAsync(Request(timeoutSeconds: 1), default));
    }

    /// <summary>
    /// <b>A shutdown is not a provider fault.</b> If the caller's own token is
    /// cancelled the cancellation has to surface as one, or a worker draining
    /// its queue on deploy would record a run of transient provider failures
    /// that never happened.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothProviders))]
    public async Task A_cancelled_caller_is_not_reported_as_a_provider_failure(string provider)
    {
        using var caller = new CancellationTokenSource();

        var client = Client(provider, Handler(async (_, ct) =>
        {
            await caller.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Ok(provider, "{}");
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ExtractAsync(Request(timeoutSeconds: 30), caller.Token));
    }

    /// <summary>
    /// The adapter has no way to reach a provider except through the egress
    /// guard, so an install with no key refuses before a socket is opened.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothProviders))]
    public async Task An_unconfigured_provider_is_refused_by_the_egress_guard(string provider)
    {
        var called = false;
        var client = Client(
            provider,
            Handler(_ =>
            {
                called = true;
                return Ok(provider, "{}");
            }),
            ai: new AiOptions());

        var refused = await Assert.ThrowsAsync<AiEgressRefusedException>(
            () => client.ExtractAsync(Request(), default));

        Assert.Equal(AiEgressRefusal.NotConfigured, refused.Refusal);
        Assert.False(called);
    }

    [Theory]
    [MemberData(nameof(BothProviders))]
    public void A_third_party_endpoint_is_reported_as_a_reseller(string provider)
    {
        Assert.True(Client(provider, Handler(_ => Ok(provider, "{}"))).IsReseller);

        var vendor = Client(
            provider,
            Handler(_ => Ok(provider, "{}")),
            ai: Configured(provider, provider == "OpenAi"
                ? "https://api.openai.com/v1"
                : "https://generativelanguage.googleapis.com/v1beta"));

        Assert.False(vendor.IsReseller);
    }

    [Fact]
    public async Task The_gpt_adapter_posts_chat_completions_with_a_bearer_key()
    {
        HttpRequestMessage? captured = null;
        var client = Client("OpenAi", Handler(request =>
        {
            captured = request;
            return Ok("OpenAi", Fixture("reading-classified-valid.json"));
        }));

        await client.ExtractAsync(Request(), default);

        Assert.Equal("https://api.vietapi.tech/v1/chat/completions", captured!.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("test-key", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task The_gemini_adapter_calls_generate_content_and_asks_for_json()
    {
        HttpRequestMessage? captured = null;
        string? body = null;

        var client = Client("Gemini", Handler(request =>
        {
            captured = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok("Gemini", Fixture("reading-classified-valid.json"));
        }));

        await client.ExtractAsync(Request(), default);

        Assert.StartsWith(
            "https://api.vietapi.tech/v1/models/model-under-test:generateContent",
            captured!.RequestUri!.ToString(),
            StringComparison.Ordinal);

        Assert.Contains("\"responseMimeType\":\"application/json\"", body!, StringComparison.Ordinal);

        /*
         * Gemini takes a subset of JSON Schema — no $defs, no $ref — and the
         * extraction contract is built from both. Sending a rewritten copy
         * would create a second contract that drifts from the real one; the
         * server validates the answer either way.
         */
        Assert.DoesNotContain("responseSchema", body!, StringComparison.Ordinal);
    }

    private static IExamExtractionClient Client(
        string provider, StubHandler handler, AiOptions? ai = null)
    {
        var factory = new StubHttpClientFactory(handler);
        var options = Options.Create(ai ?? Configured(provider, "https://api.vietapi.tech/v1"));

        return provider switch
        {
            "OpenAi" => new OpenAiExamExtractionClient(
                factory, options, NullLogger<OpenAiExamExtractionClient>.Instance),
            "Gemini" => new GeminiExamExtractionClient(
                factory, options, NullLogger<GeminiExamExtractionClient>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
    }

    private static AiOptions Configured(string provider, string? baseUrl)
    {
        var options = new AiProviderOptions
        {
            ApiKey = "test-key",
            Model = "model-under-test",
            BaseUrl = baseUrl,
        };

        return provider == "OpenAi"
            ? new AiOptions { OpenAi = options }
            : new AiOptions { Gemini = options };
    }

    private static ExamExtractionRequest Request(
        int maxResponseBytes = 256 * 1024, int timeoutSeconds = 30) => new(
        [
            new ExamExtractionSource("s1", "application/pdf",
                [new ExamExtractionSourceChunk("s1c1", 1, "Synthetic passage one.")]),
        ],
        ExamExtractionPrompt.Version,
        "correlation-1",
        1,
        maxResponseBytes,
        timeoutSeconds);

    private static HttpResponseMessage Ok(string provider, string extractionJson)
    {
        var text = JsonSerializer.Serialize(extractionJson);

        var envelope = provider == "OpenAi"
            ? $$"""
                {
                  "id": "provider-request-1",
                  "model": "model-under-test",
                  "choices": [{ "message": { "role": "assistant", "content": {{text}} } }],
                  "usage": { "prompt_tokens": 11, "completion_tokens": 22 }
                }
                """
            : $$"""
                {
                  "responseId": "provider-request-1",
                  "modelVersion": "model-under-test",
                  "candidates": [{ "content": { "parts": [{ "text": {{text}} }] } }],
                  "usageMetadata": { "promptTokenCount": 11, "candidatesTokenCount": 22 }
                }
                """;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage Truncated(string provider)
    {
        var envelope = provider == "OpenAi"
            ? """
              {
                "id": "provider-request-1",
                "model": "model-under-test",
                "choices": [{ "finish_reason": "length", "message": { "content": "{\"contractVersion\":\"exam" } }]
              }
              """
            : """
              {
                "responseId": "provider-request-1",
                "modelVersion": "model-under-test",
                "candidates": [{ "finishReason": "MAX_TOKENS", "content": { "parts": [{ "text": "{\"contractVersion\":\"exam" }] } }]
              }
              """;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/json"),
        };
    }

    private static string Fixture(string name) =>
        File.ReadAllText(ExtractionFixtures.Find($"fixtures/ai/exam-extraction/{name}"));

    private static StubHandler Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new((request, _) => Task.FromResult(respond(request)));

    private static StubHandler Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(respond);

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private sealed class StubHttpClientFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }
}
