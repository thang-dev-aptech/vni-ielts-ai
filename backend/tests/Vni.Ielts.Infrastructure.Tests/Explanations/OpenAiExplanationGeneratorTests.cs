using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Explanations;

namespace Vni.Ielts.Infrastructure.Tests.Explanations;

public sealed class OpenAiExplanationGeneratorTests
{
    [Fact]
    public async Task Generates_explanation_with_synthetic_egress_for_reseller_test_profile()
    {
        const string explanationJson =
            """
            {"correctAnswer":"B","shortReason":"The passage states this directly.","evidence":["sample passage evidence"],"commonMistake":"Choosing the nearby distractor."}
            """;

        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                      {
                        "id": "chatcmpl_explanation",
                        "choices": [
                          {
                            "message": {
                              "role": "assistant",
                              "content": {{System.Text.Json.JsonSerializer.Serialize(explanationJson)}}
                            }
                          }
                        ]
                      }
                      """,
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var options = Options.Create(new AiOptions
        {
            AllowCrossBorderTransfer = false,
            OpenAi = new AiProviderOptions
            {
                ApiKey = "secret",
                BaseUrl = "https://api.vietapi.tech/v1",
                Model = "gpt-test",
                SyntheticDataOnly = true,
            },
        });

        var generator = new OpenAiExplanationGenerator(
            new StubHttpClientFactory(handler),
            options,
            NullLogger<OpenAiExplanationGenerator>.Instance);

        var result = await generator.GenerateAsync(
            new ExplanationGenerationRequest(
                ExamModule.Reading,
                "q1",
                "Question prompt",
                "B",
                LearnerAnswer: null,
                PassageOrTranscript: "The passage includes sample passage evidence for the answer.",
                Personalized: false),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(explanationJson, result.RawJson);
        Assert.Equal("openai", result.Metadata?.Provider);
        Assert.Equal(CanonicalExplanationWorkflow.PromptVersion, result.Metadata?.PromptVersion);
        Assert.Equal("https://api.vietapi.tech/v1/chat/completions", captured?.RequestUri?.ToString());
        Assert.Equal("Bearer", captured?.Headers.Authorization?.Scheme);
    }

    /// <summary>
    /// <b>An <i>uncontracted</i> reseller, which is what this test has always
    /// been about.</b> It used to name <c>api.vietapi.tech</c>, because on the
    /// day it was written that host was uncontracted and so the two facts were
    /// indistinguishable. The 2026-09-02 decision put that host on
    /// <see cref="AiProviderPolicy.ContractedProcessorHosts"/> and separated
    /// them, so the test now names a host that is genuinely on no list —
    /// otherwise it would have quietly become a test of the opposite property.
    /// </summary>
    [Fact]
    public async Task Refuses_learner_personal_data_on_an_uncontracted_reseller()
    {
        var options = Options.Create(new AiOptions
        {
            AllowCrossBorderTransfer = true,
            OpenAi = new AiProviderOptions
            {
                ApiKey = "secret",
                BaseUrl = "https://reseller.example/v1",
                Model = "gpt-test",
                SyntheticDataOnly = false,
            },
        });

        var generator = new OpenAiExplanationGenerator(
            new StubHttpClientFactory(new StubHandler(_ => throw new InvalidOperationException("should not call"))),
            options,
            NullLogger<OpenAiExplanationGenerator>.Instance);

        var result = await generator.GenerateAsync(
            new ExplanationGenerationRequest(
                ExamModule.Reading,
                "q1",
                "Question prompt",
                "B",
                LearnerAnswer: "A",
                PassageOrTranscript: "sample passage evidence",
                Personalized: true),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("EXPLANATION_EGRESS_UNCONTRACTEDPROCESSOR", result.RefusalCode);
    }

    /// <summary>
    /// <b>The contracted host still refuses while it is marked synthetic-only.</b>
    /// Contracting answers the processor question and nothing else; the
    /// operator's own switch is a separate gate, and this is the test that says
    /// so for the explanation path. The expected code changed from
    /// <c>UNCONTRACTEDPROCESSOR</c> to <c>SYNTHETICDATAONLY</c> on 2026-09-02
    /// for exactly that reason — a different gate now catches it first.
    /// </summary>
    [Fact]
    public async Task Refuses_personalized_learner_answer_on_synthetic_only_reseller()
    {
        var options = Options.Create(new AiOptions
        {
            AllowCrossBorderTransfer = false,
            OpenAi = new AiProviderOptions
            {
                ApiKey = "secret",
                BaseUrl = "https://api.vietapi.tech/v1",
                Model = "gpt-test",
                SyntheticDataOnly = true,
            },
        });

        var generator = new OpenAiExplanationGenerator(
            new StubHttpClientFactory(new StubHandler(_ => throw new InvalidOperationException("should not call"))),
            options,
            NullLogger<OpenAiExplanationGenerator>.Instance);

        var result = await generator.GenerateAsync(
            new ExplanationGenerationRequest(
                ExamModule.Reading,
                "q1",
                "Question prompt",
                "B",
                LearnerAnswer: "A",
                PassageOrTranscript: "sample passage evidence",
                Personalized: true),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("EXPLANATION_EGRESS_SYNTHETICDATAONLY", result.RefusalCode);
    }

    /// <summary>
    /// The model can only translate options it was shown, and only answers in
    /// Vietnamese when told to. Both instructions must survive into the wire
    /// body, for the canonical and the personalized prompt alike.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_body_carries_the_options_block_and_the_vietnamese_instruction(bool personalized)
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"x","choices":[{"message":{"role":"assistant","content":"{}"}}]}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var options = Options.Create(new AiOptions
        {
            AllowCrossBorderTransfer = true,
            OpenAi = new AiProviderOptions
            {
                ApiKey = "secret",
                BaseUrl = "https://api.vietapi.tech/v1",
                Model = "gpt-test",
                SyntheticDataOnly = false,
            },
        });

        var generator = new OpenAiExplanationGenerator(
            new StubHttpClientFactory(handler),
            options,
            NullLogger<OpenAiExplanationGenerator>.Instance);

        await generator.GenerateAsync(
            new ExplanationGenerationRequest(
                ExamModule.Reading,
                "q1",
                "Which map was drawn first?",
                "B",
                LearnerAnswer: personalized ? "A" : null,
                PassageOrTranscript: "The passage includes sample passage evidence for the answer.",
                Personalized: personalized,
                QuestionOptions: "A. The coastal chart\nB. The river survey"),
            default);

        Assert.NotNull(body);
        using var doc = System.Text.Json.JsonDocument.Parse(body!);
        var messages = doc.RootElement.GetProperty("messages");
        var system = messages[0].GetProperty("content").GetString()!;
        var user = messages[1].GetProperty("content").GetString()!;

        Assert.Contains("A. The coastal chart", user, StringComparison.Ordinal);
        Assert.Contains("B. The river survey", user, StringComparison.Ordinal);
        Assert.Contains("in Vietnamese", user, StringComparison.Ordinal);
        Assert.Contains("translation", user, StringComparison.Ordinal);
        Assert.Contains("verbatim, contiguous substring", user, StringComparison.Ordinal);
        Assert.Contains("Vietnamese", system, StringComparison.Ordinal);
        Assert.Contains("character for character", system, StringComparison.Ordinal);
        Assert.Contains("\"source\": \"prompt\"", system, StringComparison.Ordinal);
        Assert.Contains("band", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_body_switches_to_prompt_sourced_evidence_when_no_source_is_supplied()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"x","choices":[{"message":{"role":"assistant","content":"{}"}}]}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var options = Options.Create(new AiOptions
        {
            OpenAi = new AiProviderOptions
            {
                ApiKey = "secret",
                BaseUrl = "https://api.vietapi.tech/v1",
                Model = "gpt-test",
                SyntheticDataOnly = true,
            },
        });

        var generator = new OpenAiExplanationGenerator(
            new StubHttpClientFactory(handler),
            options,
            NullLogger<OpenAiExplanationGenerator>.Instance);

        await generator.GenerateAsync(
            new ExplanationGenerationRequest(
                ExamModule.Reading, "q1", "Which map was drawn first?", "B",
                LearnerAnswer: null, PassageOrTranscript: null, Personalized: false),
            default);

        using var doc = System.Text.Json.JsonDocument.Parse(body!);
        var user = doc.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;

        Assert.Contains(ExplanationPromptSafety.NoSourceNotice, user, StringComparison.Ordinal);
        Assert.Contains("\"source\": \"prompt\"", user, StringComparison.Ordinal);
        Assert.Contains("Options: none", user, StringComparison.Ordinal);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }
}
