using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Ai.Writing;

namespace Vni.Ielts.Infrastructure.Assessment;

/// <summary>
/// <see cref="ISectionEvaluator"/> for Writing — calls GPT or Gemini, validates, returns a claim.
/// </summary>
public sealed class WritingSectionEvaluator(
    IOptions<AiOptions> aiOptions,
    IOptions<AssessmentOptions> assessmentOptions,
    WritingEvaluationRouter router,
    ILogger<WritingSectionEvaluator> logger) : ISectionEvaluator
{
    private readonly WritingRubricArtifact _artifact = LoadArtifact(assessmentOptions.Value.WritingMarking);

    public ExamModule Module => ExamModule.Writing;

    public bool IsConfigured => IsConfiguredFor(assessmentOptions.Value, aiOptions.Value);

    public async Task<ClaimedEvaluation> EvaluateAsync(EvaluationRequest request, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Writing evaluator is not configured. Callers must check IsConfigured.");
        }

        var marking = assessmentOptions.Value.WritingMarking;
        var provider = marking.PrimaryProvider
            ?? throw new InvalidOperationException("Assessment:WritingMarking:PrimaryProvider is not set.");

        var ticket = AiEgress.Authorise(
            aiOptions.Value,
            provider,
            AiDataClassification.LearnerPersonal);

        var sanitized = WritingEvaluationPromptBuilder.SanitizeLearnerText(request.LearnerSubmission);
        var wordCount = WritingEvaluationPromptBuilder.CountWords(sanitized);
        var idempotencyKey = ComputeIdempotencyKey(request, _artifact.Version);

        var evalRequest = new WritingEvaluationRequest(
            request.Prompt,
            sanitized,
            wordCount,
            MinWords: null,
            _artifact.Version,
            _artifact.DescriptorSource,
            WritingRubricLoader.FormatDescriptorsForPrompt(_artifact),
            marking.PromptVersion ?? _artifact.PromptVersion,
            idempotencyKey,
            Attempt: 1);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(ClampTimeoutSeconds(marking.TimeoutSeconds)));

        var response = await router.EvaluateAsync(evalRequest, ticket, timeout.Token);

        logger.LogInformation(
            "Writing evaluation completed via {Provider} model {Model}, request {RequestId}, "
            + "rubric {RubricVersion}, prompt {PromptVersion}.",
            response.Provider,
            response.Model,
            response.RequestId,
            _artifact.Version,
            evalRequest.PromptVersion);

        return WritingEvaluationValidator.ToClaimedEvaluation(response.Json);
    }

    /// <summary>
    /// Per-call provider budget. Floor 10s so a typo cannot race the round-trip;
    /// ceiling 300s so a hung provider cannot hold a worker lease forever.
    /// Values are configuration, not production SLOs. → nfr.md FS9.3
    /// </summary>
    internal static int ClampTimeoutSeconds(int configured) => Math.Clamp(configured, 10, 300);

    internal static bool IsConfiguredFor(AssessmentOptions assessment, AiOptions ai)
    {
        var marking = assessment.WritingMarking;

        if (!marking.Enabled) return false;
        if (string.IsNullOrWhiteSpace(marking.PrimaryProvider)) return false;
        if (string.IsNullOrWhiteSpace(assessment.Writing.Version)) return false;
        if (string.IsNullOrWhiteSpace(assessment.Writing.DescriptorSource)) return false;

        var provider = ResolveProvider(ai, marking.PrimaryProvider);
        if (provider is null || !provider.IsConfigured || string.IsNullOrWhiteSpace(provider.Model))
            return false;

        try
        {
            AiEgress.Authorise(
                marking.PrimaryProvider,
                provider,
                ai.AllowCrossBorderTransfer,
                AiDataClassification.LearnerPersonal);
        }
        catch (AiEgressRefusedException)
        {
            return false;
        }

        return true;
    }

    private static AiProviderOptions? ResolveProvider(AiOptions ai, string section) =>
        section switch
        {
            "OpenAi" => ai.OpenAi,
            "Gemini" => ai.Gemini,
            _ => null,
        };

    private static WritingRubricArtifact LoadArtifact(WritingMarkingOptions marking) =>
        WritingRubricLoader.Load(marking.RubricArtifactPath, marking.RubricContentHash);

    private static string ComputeIdempotencyKey(EvaluationRequest request, string rubricVersion)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            $"{rubricVersion}:{request.Prompt}:{request.LearnerSubmission}");

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
