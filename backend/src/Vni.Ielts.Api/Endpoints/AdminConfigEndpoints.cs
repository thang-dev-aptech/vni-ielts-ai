using System.Security.Claims;
using Microsoft.Extensions.Options;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Ai.Importing;
using Vni.Ielts.Infrastructure.Assessment;
using Vni.Ielts.Infrastructure.Content.Import;

namespace Vni.Ielts.Api.Endpoints;

/// <summary>
/// The operator's deliberately small view of the effective runtime settings.
///
/// This is a projection, never an options dump: provider credentials, endpoint
/// URLs and every other raw provider property stay inside infrastructure.
/// </summary>
public static class AdminConfigEndpoints
{
    public static void MapAdminConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/config")
            .WithTags("Admin")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.InSessionRead);

        group.MapGet("", GetConfigEndpoint)
            .WithName("AdminGetRuntimeConfig")
            .WithSummary("Safe, read-only view of effective runtime configuration")
            .Produces<AdminConfigResponse>();
    }

    private static IResult GetConfigEndpoint(
        ClaimsPrincipal principal,
        IOptions<AiOptions> aiOptions,
        IOptions<AssessmentOptions> assessmentOptions,
        IOptions<ExamParserOptions> parserOptions,
        IOptions<AudioTranscriptionOptions> transcriptionOptions,
        IOptions<ImportArchiveOptions> archiveOptions)
    {
        if (Denied(principal, PermissionKeys.ConfigRead) is { } denial) return denial;

        var ai = aiOptions.Value;
        var assessment = assessmentOptions.Value;
        var parser = parserOptions.Value;
        var transcription = transcriptionOptions.Value;

        return Results.Ok(new AdminConfigResponse(
            new AiConfigurationResponse(
                [
                    WritingSkill(assessment, ai),
                    ImportSkill("import-parser", parser.IsConfigured(ai), parser.Provider, parser.Model,
                        parser.PromptVersion),
                    ImportSkill("import-transcription", transcription.IsConfigured(ai),
                        transcription.Provider, transcription.Model, null),
                ]),
            WritingConfigurationResponse.From(assessment),
            ImportArchiveConfigurationResponse.From(archiveOptions.Value),
            TokenPricingConfigurationResponse.Pending));
    }

    private static AiSkillConfigurationResponse WritingSkill(AssessmentOptions assessment, AiOptions ai)
    {
        var marking = assessment.WritingMarking;
        var provider = Provider(ai, marking.PrimaryProvider);

        return new AiSkillConfigurationResponse(
            "writing-marking",
            IsWritingAvailable(assessment, ai) ? "available" : "unavailable",
            marking.PrimaryProvider,
            provider?.Model,
            marking.PromptVersion,
            Fallback(marking.FallbackProvider, ai));
    }

    private static AiSkillConfigurationResponse ImportSkill(
        string skill, bool isAvailable, string? providerName, string? model, string? version) =>
        new(skill, isAvailable ? "available" : "unavailable", providerName, model, version, null);

    private static AiProviderFallbackResponse? Fallback(string? providerName, AiOptions ai)
    {
        if (string.IsNullOrWhiteSpace(providerName)) return null;

        var provider = Provider(ai, providerName);
        return new AiProviderFallbackResponse(
            providerName,
            IsProviderAvailable(ai, providerName, provider) ? "available" : "unavailable",
            provider?.Model);
    }

    private static bool IsWritingAvailable(AssessmentOptions assessment, AiOptions ai)
    {
        var marking = assessment.WritingMarking;
        if (!marking.Enabled
            || string.IsNullOrWhiteSpace(marking.PrimaryProvider)
            || string.IsNullOrWhiteSpace(assessment.Writing.Version)
            || string.IsNullOrWhiteSpace(assessment.Writing.DescriptorSource))
        {
            return false;
        }

        return IsProviderAvailable(ai, marking.PrimaryProvider, Provider(ai, marking.PrimaryProvider));
    }

    private static bool IsProviderAvailable(AiOptions ai, string providerName, AiProviderOptions? provider)
    {
        if (provider is null || !provider.IsConfigured || string.IsNullOrWhiteSpace(provider.Model)) return false;

        try
        {
            AiEgress.Authorise(ai, providerName, AiDataClassification.LearnerPersonal);
            return true;
        }
        catch (AiEgressRefusedException)
        {
            return false;
        }
    }

    private static AiProviderOptions? Provider(AiOptions ai, string? providerName) => providerName switch
    {
        _ when string.Equals(providerName, "OpenAi", StringComparison.OrdinalIgnoreCase) => ai.OpenAi,
        _ when string.Equals(providerName, "Gemini", StringComparison.OrdinalIgnoreCase) => ai.Gemini,
        _ => null,
    };

    private static IResult? Denied(ClaimsPrincipal principal, string permission)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        return principal.Permissions().Contains(permission) ? null : Results.Forbid();
    }
}

/// <summary>
/// Explicit response contract. None of these types has an options object,
/// credential, URL, connection string or HTTP header property to serialize.
/// </summary>
public sealed record AdminConfigResponse(
    AiConfigurationResponse Ai,
    WritingConfigurationResponse Writing,
    ImportArchiveConfigurationResponse ImportArchive,
    TokenPricingConfigurationResponse TokenPricing);

public sealed record AiConfigurationResponse(IReadOnlyList<AiSkillConfigurationResponse> Skills);

public sealed record AiSkillConfigurationResponse(
    string Skill,
    string Status,
    string? Provider,
    string? Model,
    string? Version,
    AiProviderFallbackResponse? Fallback);

public sealed record AiProviderFallbackResponse(string Provider, string Status, string? Model);

public sealed record WritingConfigurationResponse(
    string? RubricVersion,
    decimal? Task1Weight,
    decimal? Task2Weight,
    string FeedbackLanguage,
    string CriterionGranularity)
{
    public static WritingConfigurationResponse From(AssessmentOptions assessment) => new(
        assessment.Writing.Version,
        assessment.Writing.TaskWeights?.Task1,
        assessment.Writing.TaskWeights?.Task2,
        assessment.Writing.FeedbackLanguage,
        assessment.Writing.CriterionGranularity);
}

public sealed record ImportArchiveConfigurationResponse(
    int MaxEntries,
    long MaxTotalUncompressedBytes,
    long MaxEntryUncompressedBytes,
    int MaxCompressionRatio,
    long MaxArchiveBytes,
    int ExtractionTimeoutSeconds)
{
    public static ImportArchiveConfigurationResponse From(ImportArchiveOptions options) => new(
        options.MaxEntries,
        options.MaxTotalUncompressedBytes,
        options.MaxEntryUncompressedBytes,
        options.MaxCompressionRatio,
        options.MaxArchiveBytes,
        options.ExtractionTimeoutSeconds);
}

public sealed record TokenPricingConfigurationResponse(string Status, IReadOnlyList<string> Blockers)
{
    public static readonly TokenPricingConfigurationResponse Pending = new("pending", ["B-5a", "B-5b"]);
}
