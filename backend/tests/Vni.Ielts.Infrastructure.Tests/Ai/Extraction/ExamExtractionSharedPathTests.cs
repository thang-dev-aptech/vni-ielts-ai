using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Ai.Extraction;
using Vni.Ielts.Infrastructure.Observability;

namespace Vni.Ielts.Infrastructure.Tests.Ai.Extraction;

/// <summary>
/// The real adapters, the real parser, and a stubbed socket: the whole
/// extraction path except the provider itself.
/// </summary>
public sealed class ExamExtractionSharedPathTests
{
    private const string ApiKey = "test-key-9f3a4c";

    /// <summary>
    /// <b>The Definition of Done for Plan 03, asserted rather than argued.</b>
    /// Two vendors, two wire formats, one candidate — because everything after
    /// the JSON string is extracted is shared. A reviewer must not be able to
    /// tell which provider parsed a package by looking at the proposal.
    /// </summary>
    [Fact]
    public async Task Both_providers_produce_the_same_candidate_through_the_same_parser()
    {
        var gpt = await ParseWith("OpenAi");
        var gemini = await ParseWith("Gemini");

        Assert.Equal(gpt.Id, gemini.Id);
        Assert.Equal(gpt.Classification, gemini.Classification);
        Assert.Equal(gpt.Title, gemini.Title);
        Assert.Equal(gpt.Confidence, gemini.Confidence);
        Assert.Equal(gpt.Status, gemini.Status);

        Assert.Equal(
            Shape(gpt.Modules.Single()),
            Shape(gemini.Modules.Single()));
    }

    /// <summary>
    /// <b>What ends up in a log file, checked against the strings that must
    /// never be in one.</b> A provider log line is written on every call and
    /// read by whoever is on support, so this is the surface where an exam
    /// passage, a printed answer or a key would leak quietly and permanently.
    /// </summary>
    [Theory]
    [InlineData("OpenAi")]
    [InlineData("Gemini")]
    public async Task Nothing_logged_carries_source_text_an_answer_key_or_a_credential(string provider)
    {
        var logs = new CapturingLoggerProvider();

        await ParseWith(provider, logs);

        var written = string.Join('\n', logs.Messages);

        Assert.NotEmpty(logs.Messages);

        // Sent to the provider.
        Assert.DoesNotContain("Synthetic passage one", written, StringComparison.Ordinal);

        // Returned by the provider.
        Assert.DoesNotContain("municipal transport planning", written, StringComparison.Ordinal);

        // The printed answer, which is the one value that makes a leaked log
        // worth reading.
        Assert.DoesNotContain("1998", written, StringComparison.Ordinal);
        Assert.DoesNotContain("An unexpected consequence", written, StringComparison.Ordinal);

        Assert.DoesNotContain(ApiKey, written, StringComparison.Ordinal);
        Assert.DoesNotContain("reading/paper.docx", written, StringComparison.Ordinal);

        // What is left is what an operator can act on.
        Assert.Contains(provider, written, StringComparison.Ordinal);
        Assert.Contains("correlation", written, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A refusal is logged as a code, for the same reason: it is the line most
    /// likely to be pasted into a ticket, and the payload that caused it is
    /// where an injected string would be sitting.
    /// </summary>
    [Theory]
    [InlineData("OpenAi")]
    [InlineData("Gemini")]
    public async Task A_refusal_is_logged_as_a_code_and_not_as_the_answer_that_caused_it(string provider)
    {
        var logs = new CapturingLoggerProvider();
        var hostile = Fixture("dangling-source-reference.json");

        await Assert.ThrowsAsync<ExamExtractionRejectedException>(
            () => ParseWith(provider, logs, hostile));

        var written = string.Join('\n', logs.Messages);

        Assert.Contains(ExamExtractionRejection.DanglingSource, written, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic passage one", written, StringComparison.Ordinal);
        Assert.DoesNotContain("s9", written, StringComparison.Ordinal);
    }

    private static async Task<ParsedExamCandidate> ParseWith(
        string provider, CapturingLoggerProvider? logs = null, string? answer = null)
    {
        var extraction = answer ?? Fixture("reading-classified-valid.json");
        var handler = new StubHandler(Envelope(provider, extraction));
        var factory = new StubHttpClientFactory(handler);

        var ai = new AiOptions();
        var configured = new AiProviderOptions
        {
            ApiKey = ApiKey,
            Model = "model-under-test",
            BaseUrl = "https://api.vietapi.tech/v1",
        };

        if (provider == "OpenAi") ai.OpenAi = configured;
        else ai.Gemini = configured;

        var aiOptions = Options.Create(ai);

        IExamExtractionClient client = provider == "OpenAi"
            ? new OpenAiExamExtractionClient(
                factory, aiOptions, Logger<OpenAiExamExtractionClient>(logs))
            : new GeminiExamExtractionClient(
                factory, aiOptions, Logger<GeminiExamExtractionClient>(logs));

        var parser = new ConfiguredExamExtractionParser(
            [client],
            Options.Create(new ExamParsingOptions
            {
                Enabled = true,
                Provider = provider,
                PromptVersion = ExamExtractionPrompt.Version,
                TimeoutSeconds = 30,
                SourceRights = "Synthetic",
            }),
            new NullRunStore(),
            new FixedClock(),
            new ExamParsingMetrics(),
            Logger<ConfiguredExamExtractionParser>(logs));

        return await parser.ParseAsync([Document()], default);
    }

    private static ILogger<T> Logger<T>(CapturingLoggerProvider? logs) =>
        logs is null ? NullLogger<T>.Instance : new CapturingLogger<T>(logs);

    private static string Shape(ParsedModuleCandidate module) =>
        JsonSerializer.Serialize(module);

    private static ExtractedSourceDocument Document() => new(
        "package-1",
        "reading/paper.docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "sha-1",
        DocumentExtractionOutcome.Extracted,
        [
            new ExtractedTextChunk("c1", 0, "Synthetic passage one.", null, "Part 1"),
            new ExtractedTextChunk("c2", 1, "Synthetic passage two.", 2, "Part 2"),
        ]);

    private static string Fixture(string name) =>
        File.ReadAllText(ExtractionFixtures.Find($"fixtures/ai/exam-extraction/{name}"));

    private static string Envelope(string provider, string extractionJson)
    {
        var text = JsonSerializer.Serialize(extractionJson);

        return provider == "OpenAi"
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
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class StubHttpClientFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class CapturingLoggerProvider
    {
        public List<string> Messages { get; } = [];
    }

    /// <summary>
    /// Records the rendered message and every structured value, because a
    /// value that never reaches the message template still reaches a
    /// structured sink.
    /// </summary>
    private sealed class CapturingLogger<T>(CapturingLoggerProvider sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var rendered = formatter(state, exception);

            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                rendered += " | " + string.Join(
                    ", ", values.Select(value => $"{value.Key}={value.Value}"));
            }

            sink.Messages.Add($"{logLevel}: {rendered} {exception?.Message}");
        }
    }

    private sealed class NullRunStore : IExamExtractionRunStore
    {
        public Task RecordAsync(ExamExtractionRunMetadata run, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 7, 3, 0, 0, TimeSpan.Zero);
    }
}
