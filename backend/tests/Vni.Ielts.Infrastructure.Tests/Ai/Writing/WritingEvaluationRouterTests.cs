using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Ai.Writing;
using Vni.Ielts.Infrastructure.Assessment;

namespace Vni.Ielts.Infrastructure.Tests.Ai.Writing;

/// <summary>
/// FS9.3 — provider retry budget and primary→fallback selection.
///
/// <b>Not a circuit breaker.</b> The product bounds spend with a configured
/// attempt ceiling, a per-call timeout, and outbox backoff — not a half-open
/// breaker that invents a production open threshold. These cases lock the
/// budget seams so a silent change cannot turn a provider blip into an
/// unbounded bill.
/// </summary>
public sealed class WritingEvaluationRouterTests
{
    [Fact]
    public async Task Transient_failures_stop_at_MaxAttempts_without_calling_further()
    {
        var openAi = new CountingClient("OpenAi", failTimes: 10);
        var router = Router(openAi, maxAttempts: 3, primaryName: "OpenAi");

        await Assert.ThrowsAsync<TransientWritingEvaluationException>(() =>
            router.EvaluateAsync(SampleRequest(), SyntheticTicket("OpenAi"), default));

        Assert.Equal(3, openAi.Calls);
    }

    [Fact]
    public async Task Fallback_is_tried_only_after_primary_exhausts_its_share_of_attempts()
    {
        var openAi = new CountingClient("OpenAi", failTimes: 10);
        var gemini = new CountingClient("Gemini", failTimes: 0);
        var router = Router(
            openAi,
            maxAttempts: 2,
            primaryName: "OpenAi",
            fallbackName: "Gemini",
            fallbackClient: gemini);

        var response = await router.EvaluateAsync(SampleRequest(), SyntheticTicket("OpenAi"), default);

        Assert.Equal("Gemini", response.Provider);
        Assert.Equal(2, openAi.Calls);
        Assert.Equal(1, gemini.Calls);
    }

    [Fact]
    public async Task Fallback_provider_uses_its_own_egress_ticket()
    {
        const string evaluationJson =
            """
            {"criteria":{"taskResponse":{"band":6,"feedback":"ok","evidence":["hello"]},"coherenceAndCohesion":{"band":6,"feedback":"ok","evidence":["hello"]},"lexicalResource":{"band":6,"feedback":"ok","evidence":["hello"]},"grammaticalRangeAndAccuracy":{"band":6,"feedback":"ok","evidence":["hello"]}},"sectionBand":6,"summary":"ok"}
            """;

        HttpRequestMessage? geminiRequest = null;
        var factory = new StubHttpClientFactory(request =>
        {
            if (request.RequestUri?.Host == "api.openai.com")
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            geminiRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                      {
                        "candidates": [
                          {
                            "content": {
                              "parts": [
                                { "text": {{System.Text.Json.JsonSerializer.Serialize(evaluationJson)}} }
                              ]
                            }
                          }
                        ],
                        "usageMetadata": { "promptTokenCount": 11, "candidatesTokenCount": 22 }
                      }
                      """,
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var router = Router(
            new OpenAiWritingEvaluationClient(factory, NullLogger<OpenAiWritingEvaluationClient>.Instance),
            maxAttempts: 1,
            primaryName: "OpenAi",
            fallbackName: "Gemini",
            fallbackClient: new GeminiWritingEvaluationClient(
                factory,
                NullLogger<GeminiWritingEvaluationClient>.Instance),
            ai: new AiOptions
            {
                OpenAi = new AiProviderOptions
                {
                    ApiKey = "openai-secret",
                    Model = "openai-test",
                },
                Gemini = new AiProviderOptions
                {
                    ApiKey = "gemini-secret",
                    Model = "gemini-test",
                },
            });

        var response = await router.EvaluateAsync(SampleRequest(), SyntheticTicket("OpenAi"), default);

        Assert.Equal("Gemini", response.Provider);
        Assert.Contains("/models/gemini-test:generateContent", geminiRequest?.RequestUri?.ToString());
    }


    [Fact]
    public void Writing_marking_timeout_and_attempt_defaults_are_configured_seams()
    {
        // Documented in nfr.md FS9.3. Changing either without updating the
        // ops runbook is a silent commitment change (G-11).
        var options = new WritingMarkingOptions();

        Assert.Equal(120, options.TimeoutSeconds);
        Assert.Equal(3, options.MaxAttempts);
        Assert.False(options.Enabled);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(10, 10)]
    [InlineData(120, 120)]
    [InlineData(300, 300)]
    [InlineData(999, 300)]
    public void Per_call_timeout_is_clamped_to_the_documented_budget(int configured, int expected) =>
        Assert.Equal(expected, WritingSectionEvaluator.ClampTimeoutSeconds(configured));

    private static WritingEvaluationRouter Router(
        IWritingEvaluationClient primaryClient,
        int maxAttempts,
        string primaryName,
        string? fallbackName = null,
        IWritingEvaluationClient? fallbackClient = null,
        AiOptions? ai = null)
    {
        var clients = fallbackClient is null
            ? new IWritingEvaluationClient[] { primaryClient }
            : new IWritingEvaluationClient[] { primaryClient, fallbackClient };

        var assessment = Options.Create(new AssessmentOptions
        {
            WritingMarking = new WritingMarkingOptions
            {
                Enabled = true,
                PrimaryProvider = primaryName,
                FallbackProvider = fallbackName,
                MaxAttempts = maxAttempts,
                TimeoutSeconds = 120,
            },
        });

        return new WritingEvaluationRouter(
            clients,
            assessment,
            Options.Create(ai ?? ConfiguredAi()),
            new NullWritingEvaluationCostMetric(),
            NullLogger<WritingEvaluationRouter>.Instance);
    }

    private static WritingEvaluationRequest SampleRequest() => new(
        "Task prompt", "hello world essay", 3, 150,
        "ielts-writing-synthetic-v1", "fixture", "descriptors", "writing-eval-prompt-v1",
        "idempotency", 1);

    private static AiEgressTicket SyntheticTicket(string provider) =>
        new(provider, "model-test", null, "secret", AiDataClassification.Synthetic);

    private static AiOptions ConfiguredAi() => new()
    {
        OpenAi = new AiProviderOptions
        {
            ApiKey = "openai-secret",
            Model = "openai-test",
        },
        Gemini = new AiProviderOptions
        {
            ApiKey = "gemini-secret",
            Model = "gemini-test",
        },
    };

    private sealed class CountingClient(string provider, int failTimes) : IWritingEvaluationClient
    {
        private int _remainingFailures = failTimes;

        public string Provider { get; } = provider;
        public int Calls { get; private set; }

        public Task<WritingEvaluationResponse> EvaluateAsync(
            WritingEvaluationRequest request, CancellationToken ct)
        {
            Calls++;
            if (_remainingFailures-- > 0)
                throw new TransientWritingEvaluationException($"{Provider} transient");

            return Task.FromResult(new WritingEvaluationResponse(
                """{"ok":true}""", Provider, "model-test", "req", 1, 1));
        }
    }

    private sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(respond));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
