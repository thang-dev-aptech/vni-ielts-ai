using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Infrastructure;
using AiOptions = Vni.Ielts.Infrastructure.Ai.AiOptions;
using AiProviderOptions = Vni.Ielts.Infrastructure.Ai.AiProviderOptions;
using AudioTranscriptionOptions = Vni.Ielts.Infrastructure.Ai.Importing.AudioTranscriptionOptions;
using OpenAiAudioTranscriber = Vni.Ielts.Infrastructure.Ai.Importing.OpenAiAudioTranscriber;
using UnconfiguredAudioTranscriber = Vni.Ielts.Infrastructure.Ai.Importing.UnconfiguredAudioTranscriber;

namespace Vni.Ielts.Infrastructure.Tests.Content.Import;

/// <summary>
/// Task 5 — the exam-audio transcriber wired behind
/// <c>Import:Transcription</c>, and the gate that makes a half-filled section
/// a boot refusal rather than a surprise at the first import.
///
/// <b><c>Import:Transcription</c> selects a provider; it does not hold a
/// second copy of its key.</b> The credential lives at <c>Ai:OpenAi:ApiKey</c>
/// — the same section Writing marking, explanations, coaching and
/// <c>Import:Parser</c> already read — so every "fully configured" fixture
/// below sets that key and not one under <c>Import:Transcription</c>.
///
/// <b>Nothing here touches Speaking.</b> <c>ITranscriptSource</c> /
/// <c>NoTranscriptSource</c> is a different port under a different deferral
/// (<c>P-02</c>), registered elsewhere in <c>DependencyInjection</c>. What
/// <see cref="The_speaking_marking_seam_is_a_different_port_and_is_untouched"/>
/// pins is that <see cref="DependencyInjection.AddAudioTranscriber"/> does not
/// register it: in a container where only the audio wiring ran, the Speaking
/// port resolves to nothing. That goes red the moment this wiring reaches for
/// the Speaking seam — which is the mistake worth catching, since the two
/// ports are one careless read apart.
/// </summary>
public sealed class AudioTranscriberWiringTests
{
    [Fact]
    public void With_no_transcription_configured_the_unconfigured_transcriber_stays()
    {
        using var services = Build(config: []);

        Assert.IsType<UnconfiguredAudioTranscriber>(services.GetRequiredService<IAudioTranscriber>());
    }

    /// <summary>
    /// The null implementation's own contract, as opposed to the stage's
    /// behaviour when handed one: it reports itself unconfigured and refuses
    /// rather than returning empty text. Absent and empty are different
    /// states. → <c>IP-09</c>
    /// </summary>
    [Fact]
    public async Task The_unconfigured_transcriber_refuses_rather_than_returning_empty_text()
    {
        var transcriber = new UnconfiguredAudioTranscriber();

        var result = await transcriber.TranscribeAsync(
            new MemoryStream([1, 2, 3]), "part1.mp3", CancellationToken.None);

        Assert.False(transcriber.IsConfigured);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Text);
        Assert.Equal(TranscriptionRefusalCodes.NotConfigured, result.RefusalCode);
    }

    [Fact]
    public void A_configured_provider_and_model_with_a_shared_key_wires_the_real_transcriber()
    {
        using var services = Build(new Dictionary<string, string?>
        {
            ["Import:Transcription:Provider"] = "OpenAi",
            ["Import:Transcription:Model"] = "whisper-1",
            ["Ai:OpenAi:ApiKey"] = "test-key",
        });

        Assert.IsType<OpenAiAudioTranscriber>(services.GetRequiredService<IAudioTranscriber>());
    }

    /// <summary>
    /// Half-configured is worse than unconfigured: it looks enabled and does
    /// nothing at the first import, after an operator has already uploaded a
    /// package and waited. This is also the shape a deployment takes when
    /// <c>Ai:OpenAi:ApiKey</c> is rotated out from under a feature that reads
    /// it — which is exactly the failure a second, duplicated key would have
    /// hidden instead of named.
    /// </summary>
    [Fact]
    public void A_provider_named_here_without_a_key_at_Ai_OpenAi_is_refused_at_startup()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => Validate(new Dictionary<string, string?>
        {
            ["Import:Transcription:Provider"] = "OpenAi",
            ["Import:Transcription:Model"] = "whisper-1",
            // Ai:OpenAi:ApiKey deliberately absent.
        }));

        Assert.Contains("Import:Transcription", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Ai:OpenAi:ApiKey", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_Model_is_refused_at_startup_even_with_a_shared_key_present()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => Validate(new Dictionary<string, string?>
        {
            ["Import:Transcription:Provider"] = "OpenAi",
            ["Ai:OpenAi:ApiKey"] = "test-key",
            // Import:Transcription:Model deliberately absent.
        }));

        Assert.Contains("Import:Transcription", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Model", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsupported_provider_name_is_refused_at_startup()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => Validate(new Dictionary<string, string?>
        {
            ["Import:Transcription:Provider"] = "Gemini",
            ["Import:Transcription:Model"] = "gemini-3-pro",
        }));

        Assert.Contains("Import:Transcription:Provider", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Claude_exclusion_reaches_this_section_too()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => Validate(new Dictionary<string, string?>
        {
            ["Import:Transcription:Provider"] = "OpenAi",
            ["Import:Transcription:Model"] = "claude-4-opus",
        }));

        Assert.Contains("Claude", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_out_of_range_timeout_is_refused_at_startup()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => Validate(new Dictionary<string, string?>
        {
            ["Import:Transcription:Provider"] = "OpenAi",
            ["Import:Transcription:Model"] = "whisper-1",
            ["Import:Transcription:TimeoutSeconds"] = "1",
        }));

        Assert.Contains("Import:Transcription:TimeoutSeconds", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>IP-07</c>, pinned rather than asserted in prose. Turning exam-audio
    /// transcription on must not turn Speaking marking on: that is a different
    /// port, carrying a learner's personal data under Vietnam's PDPL and
    /// needing word-level timings, and it is deferred by <c>P-02</c>. The two
    /// are easy to merge on a quick read, which is exactly why this is a test.
    /// </summary>
    [Fact]
    public void The_speaking_marking_seam_is_a_different_port_and_is_untouched()
    {
        using var services = Build(new Dictionary<string, string?>
        {
            ["Import:Transcription:Provider"] = "OpenAi",
            ["Import:Transcription:Model"] = "whisper-1",
            ["Ai:OpenAi:ApiKey"] = "test-key",
        });

        Assert.IsType<OpenAiAudioTranscriber>(services.GetRequiredService<IAudioTranscriber>());

        // Not registered by AddAudioTranscriber at all — the whole point.
        Assert.Null(services.GetService<Vni.Ielts.Application.Assessment.ITranscriptSource>());
    }

    // ── A 200 that is not a transcript must not become one ────────────────

    /// <summary>
    /// <b>CLAUDE.md rule 2, at the one place it was breachable.</b> The
    /// plain-text fallback exists for resellers that ignore
    /// <c>response_format</c>, and it used to return <i>any</i> non-JSON 200
    /// body verbatim. A proxy answering 200 with an HTML error page therefore
    /// had that page written onto the package as <c>part.transcript</c>.
    ///
    /// <b>And nothing would have warned.</b> The transcript would look
    /// present, so <see cref="PassageAnchorCheck"/> would search a gateway
    /// error page for the paper's answers and report every one of them absent
    /// — a screen of "this answer is not in the recording" against a correct
    /// paper, which is precisely the false-warning wave that teaches reviewers
    /// to stop reading warnings.
    ///
    /// <b>Both halves are asserted.</b> No transcript on the part <i>and</i> a
    /// refusal warning naming it: a version that refused and still wrote
    /// something would pass on the warning alone, and a version that wrote
    /// nothing silently would pass on the transcript alone. The failure being
    /// pinned needs both to be true at once.
    /// </summary>
    [Theory]
    [InlineData("text/html", "<!doctype html><html><body><h1>502 Bad Gateway</h1></body></html>")]
    [InlineData("text/plain", "<html><head><title>Rate limited</title></head></html>")]
    public async Task A_200_carrying_a_page_instead_of_speech_is_refused_and_writes_no_transcript(
        string mediaType, string body)
    {
        var package = ListeningPackageWithOneRecording();

        var result = await AudioTranscriptionStage.RunAsync(
            package,
            [new ImportAudioFile(
                "listening/audio/part1.mp3",
                _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])))],
            TranscriberAnswering(HttpStatusCode.OK, body, mediaType),
            CancellationToken.None);

        Assert.Null(TranscriptAt(result.PackageJson));

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(TranscriptionWarningCodes.Refused, warning.Code);
        Assert.Contains("part 1", warning.Message, StringComparison.OrdinalIgnoreCase);

        // The page's own words never reach a message an administrator reads.
        Assert.DoesNotContain("Bad Gateway", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Rate limited", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The fallback still works, which is why it is gated rather than removed:
    /// a reseller that ignores <c>response_format</c> and answers in plain
    /// text is a real case and its transcript is real speech.
    /// </summary>
    [Fact]
    public async Task A_200_carrying_plain_speech_is_still_accepted()
    {
        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackageWithOneRecording(),
            [new ImportAudioFile(
                "listening/audio/part1.mp3",
                _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])))],
            TranscriberAnswering(
                HttpStatusCode.OK, "The train leaves at half past six.", "text/plain"),
            CancellationToken.None);

        Assert.Equal("The train leaves at half past six.", TranscriptAt(result.PackageJson));
        Assert.Empty(result.Warnings);
    }

    /// <summary>The declared shape, and the one a cooperating host sends.</summary>
    [Fact]
    public async Task A_200_carrying_the_declared_json_shape_is_accepted()
    {
        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackageWithOneRecording(),
            [new ImportAudioFile(
                "listening/audio/part1.mp3",
                _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])))],
            TranscriberAnswering(
                HttpStatusCode.OK,
                """{"text":"The train leaves at half past six."}""",
                "application/json"),
            CancellationToken.None);

        Assert.Equal("The train leaves at half past six.", TranscriptAt(result.PackageJson));
    }

    /// <summary>
    /// A body long enough to be a dump rather than eight minutes of speech.
    /// The backstop behind the two real discriminators.
    /// </summary>
    [Fact]
    public async Task A_200_carrying_a_bulk_dump_is_refused()
    {
        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackageWithOneRecording(),
            [new ImportAudioFile(
                "listening/audio/part1.mp3",
                _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])))],
            TranscriberAnswering(HttpStatusCode.OK, new string('x', 200_001), "text/plain"),
            CancellationToken.None);

        Assert.Null(TranscriptAt(result.PackageJson));
        Assert.Equal(TranscriptionWarningCodes.Refused, Assert.Single(result.Warnings).Code);
    }

    /// <summary>
    /// The real adapter, pointed at a handler that answers whatever the test
    /// says. Everything but the socket is production code: the egress ticket,
    /// the multipart build, the response reading and the gate.
    /// </summary>
    private static IAudioTranscriber TranscriberAnswering(
        HttpStatusCode status, string body, string mediaType)
    {
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);

        return new OpenAiAudioTranscriber(
            new StubHttpClientFactory(new StubHandler(status, content)),
            Options.Create(new AiOptions
            {
                OpenAi = new AiProviderOptions { ApiKey = "test-key", Model = "whisper-1" },
            }),
            Options.Create(new AudioTranscriptionOptions
            {
                Provider = "OpenAi",
                Model = "whisper-1",
                BaseUrl = "https://transcription.invalid/v1",
            }),
            NullLogger<OpenAiAudioTranscriber>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(HttpStatusCode status, HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = content });
    }

    private static string ListeningPackageWithOneRecording() =>
        new JsonObject
        {
            ["sections"] = new JsonArray(
                new JsonObject
                {
                    ["module"] = "listening",
                    ["parts"] = new JsonArray(
                        new JsonObject { ["order"] = 1, ["kind"] = "recording" }),
                }),
        }.ToJsonString();

    private static string? TranscriptAt(string packageJson) =>
        JsonNode.Parse(packageJson)?["sections"]?[0]?["parts"]?[0]?["transcript"]
            ?.GetValue<string>();

    private static ServiceProvider Build(Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        DependencyInjection.AddAudioTranscriber(services, configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The production gate, exercised directly:
    /// <see cref="AudioTranscriptionOptions.Problem"/> is exactly what
    /// <c>StartupConfiguration.ValidateOrThrow</c> calls for
    /// <c>Import:Transcription</c>, so pinning it here pins the same refusal
    /// the API throws at boot rather than a re-implementation of it.
    /// </summary>
    private static void Validate(Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();

        var options = configuration.GetSection(AudioTranscriptionOptions.SectionName)
            .Get<AudioTranscriptionOptions>() ?? new AudioTranscriptionOptions();
        var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();

        if (options.Problem(ai) is { } problem)
            throw new InvalidOperationException(problem);
    }
}
