using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Infrastructure;
using AiOptions = Vni.Ielts.Infrastructure.Ai.AiOptions;
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
/// (<c>P-02</c>); that it stays wired regardless of this section is pinned by
/// <see cref="The_speaking_marking_seam_is_a_different_port_and_is_untouched"/>.
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
