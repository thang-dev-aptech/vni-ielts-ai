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

    [Fact]
    public async Task Refuses_learner_personal_data_on_reseller_when_synthetic_only_is_false()
    {
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
        Assert.Equal("EXPLANATION_EGRESS_UNCONTRACTEDPROCESSOR", result.RefusalCode);
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
