using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Infrastructure;
using Vni.Ielts.Infrastructure.Content.Import;
using AiOptions = Vni.Ielts.Infrastructure.Ai.AiOptions;
using ExamParserOptions = Vni.Ielts.Infrastructure.Ai.Importing.ExamParserOptions;

namespace Vni.Ielts.Infrastructure.Tests.Content.Import;

/// <summary>
/// Task 4 — the AI parser wired behind <c>Import:Parser</c>, and the gate
/// that is the whole point of the task: no code default, never a fallback
/// that picks a provider because one happens to be available, and a
/// half-filled section refused before it ever reaches an upload.
///
/// <b><c>Import:Parser</c> selects a provider; it does not hold a second copy
/// of its key.</b> The credential lives at <c>Ai:OpenAi:ApiKey</c> — the same
/// section Writing marking, explanations and coaching already read — so every
/// "fully configured" fixture below sets that key, not one under
/// <c>Import:Parser</c> itself. Fix round 1 moved this: an earlier version of
/// this gate carried its own <c>Import:Parser:ApiKey</c>, which meant one
/// credential with two places to rotate it.
///
/// <b>Calls <see cref="DependencyInjection.AddExamSourceParser"/> directly</b>
/// — the narrow slice of <c>AddInfrastructure</c> this task touches — rather
/// than the whole of <c>AddInfrastructure</c>, so this suite needs no Mongo,
/// no object storage and no network to run.
/// </summary>
public sealed class ExamSourceParserWiringTests
{
    [Fact]
    public void With_no_parser_configured_the_unconfigured_parser_stays()
    {
        using var services = Build(config: new Dictionary<string, string?>());

        Assert.IsType<UnconfiguredExamSourceParser>(services.GetRequiredService<IExamSourceParser>());
    }

    [Fact]
    public void A_configured_provider_and_model_with_a_shared_key_wires_the_real_parser()
    {
        using var services = Build(new Dictionary<string, string?>
        {
            ["Import:Parser:Provider"] = "OpenAi",
            ["Import:Parser:Model"] = "gpt-5",
            ["Ai:OpenAi:ApiKey"] = "test-key",
        });

        Assert.IsType<ProviderNeutralExamSourceParser>(services.GetRequiredService<IExamSourceParser>());
    }

    /// <summary>
    /// Half-configured is worse than unconfigured: it looks enabled and fails at
    /// the first upload, after the operator has already put a package in. This
    /// is also the shape a deployment takes if Writing marking's key is rotated
    /// out of <c>Ai:OpenAi:ApiKey</c> without anyone remembering this gate reads
    /// the same setting — exactly the failure a second, duplicated key would
    /// have hidden instead of naming.
    /// </summary>
    [Fact]
    public void A_provider_named_here_without_a_key_at_Ai_OpenAi_is_refused_at_startup_not_at_first_use()
    {
        var config = new Dictionary<string, string?>
        {
            ["Import:Parser:Provider"] = "OpenAi",
            ["Import:Parser:Model"] = "gpt-5",
            // Ai:OpenAi:ApiKey deliberately absent.
        };

        var refusal = Assert.Throws<InvalidOperationException>(() => Validate(Build(config)));

        Assert.Contains("Import:Parser", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Ai:OpenAi:ApiKey", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_Model_is_refused_at_startup_even_with_a_shared_key_present()
    {
        var config = new Dictionary<string, string?>
        {
            ["Import:Parser:Provider"] = "OpenAi",
            ["Ai:OpenAi:ApiKey"] = "test-key",
            // Import:Parser:Model deliberately absent.
        };

        var refusal = Assert.Throws<InvalidOperationException>(() => Validate(Build(config)));

        Assert.Contains("Import:Parser", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Model", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsupported_provider_name_is_refused_at_startup()
    {
        var config = new Dictionary<string, string?>
        {
            ["Import:Parser:Provider"] = "Gemini",
            ["Import:Parser:Model"] = "gemini-3-pro",
        };

        var refusal = Assert.Throws<InvalidOperationException>(() => Validate(Build(config)));

        Assert.Contains("Import:Parser:Provider", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Claude_exclusion_reaches_this_section_too()
    {
        var config = new Dictionary<string, string?>
        {
            ["Import:Parser:Provider"] = "OpenAi",
            ["Import:Parser:Model"] = "claude-4-opus",
        };

        var refusal = Assert.Throws<InvalidOperationException>(() => Validate(Build(config)));

        Assert.Contains("Claude", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_out_of_range_MaxAttempts_is_refused_at_startup()
    {
        var config = new Dictionary<string, string?>
        {
            ["Import:Parser:Provider"] = "OpenAi",
            ["Import:Parser:Model"] = "gpt-5",
            ["Import:Parser:MaxAttempts"] = "9",
        };

        var refusal = Assert.Throws<InvalidOperationException>(() => Validate(Build(config)));

        Assert.Contains("Import:Parser:MaxAttempts", refusal.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Build(Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        DependencyInjection.AddExamSourceParser(services, configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The production gate, exercised directly: <c>ExamParserOptions.Problem()</c>
    /// is exactly what <c>StartupConfiguration.ValidateOrThrow</c> calls for
    /// <c>Import:Parser</c>, so pinning it here pins the same refusal the API
    /// throws at boot — not a re-implementation of it.
    /// </summary>
    private static void Validate(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var options = configuration.GetSection(ExamParserOptions.SectionName)
            .Get<ExamParserOptions>() ?? new ExamParserOptions();
        var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();

        if (options.Problem(ai) is { } problem)
            throw new InvalidOperationException(problem);
    }
}
