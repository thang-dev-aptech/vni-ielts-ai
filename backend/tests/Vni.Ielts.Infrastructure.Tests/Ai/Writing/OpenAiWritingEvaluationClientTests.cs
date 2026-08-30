using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Ai.Writing;

namespace Vni.Ielts.Infrastructure.Tests.Ai.Writing;

public sealed class OpenAiWritingEvaluationClientTests
{
    [Fact]
    public async Task Parses_structured_output_from_responses_api_shape()
    {
        const string evaluationJson =
            """
            {"criteria":{"taskResponse":{"band":6,"feedback":"ok","evidence":["hello"]},"coherenceAndCohesion":{"band":6,"feedback":"ok","evidence":["hello"]},"lexicalResource":{"band":6,"feedback":"ok","evidence":["hello"]},"grammaticalRangeAndAccuracy":{"band":6,"feedback":"ok","evidence":["hello"]}},"sectionBand":6,"summary":"ok"}
            """;

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                  {
                    "id": "resp_test",
                    "output": [
                      {
                        "content": [
                          { "type": "output_text", "text": {{System.Text.Json.JsonSerializer.Serialize(evaluationJson)}} }
                        ]
                      }
                    ],
                    "usage": { "input_tokens": 10, "output_tokens": 20 }
                  }
                  """,
                Encoding.UTF8,
                "application/json"),
        });

        var factory = new StubHttpClientFactory(handler);
        var client = new OpenAiWritingEvaluationClient(factory, NullLogger<OpenAiWritingEvaluationClient>.Instance);

        OpenAiWritingEvaluationClient.CurrentTicket.Value = new AiEgressTicket(
            "OpenAi", "gpt-test", null, "secret", AiDataClassification.Synthetic);

        try
        {
            var response = await client.EvaluateAsync(
                SampleRequest(), default);

            WritingEvaluationValidator.EnsureSchemaValid(response.Json);
            Assert.Equal("OpenAi", response.Provider);
            Assert.Equal(10, response.InputTokens);
        }
        finally
        {
            OpenAiWritingEvaluationClient.CurrentTicket.Value = null;
        }
    }

    [Fact]
    public async Task Uses_chat_completions_when_ticket_has_third_party_base_url()
    {
        const string evaluationJson =
            """
            {"criteria":{"taskResponse":{"band":6,"feedback":"ok","evidence":["hello"]},"coherenceAndCohesion":{"band":6,"feedback":"ok","evidence":["hello"]},"lexicalResource":{"band":6,"feedback":"ok","evidence":["hello"]},"grammaticalRangeAndAccuracy":{"band":6,"feedback":"ok","evidence":["hello"]}},"sectionBand":6,"summary":"ok"}
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
                        "id": "chatcmpl_test",
                        "choices": [
                          {
                            "message": {
                              "role": "assistant",
                              "content": {{System.Text.Json.JsonSerializer.Serialize(evaluationJson)}}
                            }
                          }
                        ],
                        "usage": { "prompt_tokens": 11, "completion_tokens": 22 }
                      }
                      """,
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var factory = new StubHttpClientFactory(handler);
        var client = new OpenAiWritingEvaluationClient(factory, NullLogger<OpenAiWritingEvaluationClient>.Instance);

        OpenAiWritingEvaluationClient.CurrentTicket.Value = new AiEgressTicket(
            "OpenAi", "gpt-test", "https://api.vietapi.tech/v1", "secret", AiDataClassification.Synthetic);

        try
        {
            var response = await client.EvaluateAsync(SampleRequest(), default);

            WritingEvaluationValidator.EnsureSchemaValid(response.Json);
            Assert.Equal("https://api.vietapi.tech/v1/chat/completions", captured?.RequestUri?.ToString());
            Assert.Equal(11, response.InputTokens);
            Assert.Equal(22, response.OutputTokens);
        }
        finally
        {
            OpenAiWritingEvaluationClient.CurrentTicket.Value = null;
        }
    }

    [Fact]
    public async Task Uses_responses_api_when_ticket_has_official_openai_base_url()
    {
        const string evaluationJson =
            """
            {"criteria":{"taskResponse":{"band":6,"feedback":"ok","evidence":["hello"]},"coherenceAndCohesion":{"band":6,"feedback":"ok","evidence":["hello"]},"lexicalResource":{"band":6,"feedback":"ok","evidence":["hello"]},"grammaticalRangeAndAccuracy":{"band":6,"feedback":"ok","evidence":["hello"]}},"sectionBand":6,"summary":"ok"}
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
                        "id": "resp_test",
                        "output": [
                          {
                            "content": [
                              { "type": "output_text", "text": {{System.Text.Json.JsonSerializer.Serialize(evaluationJson)}} }
                            ]
                          }
                        ],
                        "usage": { "input_tokens": 10, "output_tokens": 20 }
                      }
                      """,
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var factory = new StubHttpClientFactory(handler);
        var client = new OpenAiWritingEvaluationClient(factory, NullLogger<OpenAiWritingEvaluationClient>.Instance);

        OpenAiWritingEvaluationClient.CurrentTicket.Value = new AiEgressTicket(
            "OpenAi", "gpt-test", "https://api.openai.com/v1", "secret", AiDataClassification.Synthetic);

        try
        {
            var response = await client.EvaluateAsync(SampleRequest(), default);

            WritingEvaluationValidator.EnsureSchemaValid(response.Json);
            Assert.Equal("https://api.openai.com/v1/responses", captured?.RequestUri?.ToString());
            Assert.Equal(10, response.InputTokens);
            Assert.Equal(20, response.OutputTokens);
        }
        finally
        {
            OpenAiWritingEvaluationClient.CurrentTicket.Value = null;
        }
    }

    private static WritingEvaluationRequest SampleRequest() => new(
        "Task prompt", "hello world essay", 3, 150,
        "ielts-writing-synthetic-v1", "fixture", "descriptors", "writing-eval-prompt-v1",
        "idempotency", 1);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubHttpClientFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }
}
