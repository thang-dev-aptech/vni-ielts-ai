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
        var generalTraining = request.Variant == ExamVariant.General;
        var descriptors = WritingRubricLoader.FormatDescriptorsForPrompt(
            _artifact, request.TaskNumber, generalTraining);
        var language = string.IsNullOrWhiteSpace(assessmentOptions.Value.Writing.FeedbackLanguage)
            ? "vi"
            : assessmentOptions.Value.Writing.FeedbackLanguage;
        var wholeBands = _artifact.IsV2
            && !string.Equals(
                assessmentOptions.Value.Writing.CriterionGranularity, "half-step",
                StringComparison.OrdinalIgnoreCase);
        var requestedModel = ResolveProvider(aiOptions.Value, provider)?.Model;

        var evalRequest = new WritingEvaluationRequest(
            request.Prompt,
            sanitized,
            wordCount,
            MinWords: null,
            _artifact.Version,
            _artifact.DescriptorSource,
            descriptors,
            marking.PromptVersion ?? _artifact.PromptVersion,
            idempotencyKey,
            Attempt: 1,
            request.TaskNumber,
            generalTraining ? "generalTraining" : "academic",
            language,
            wholeBands,
            requestedModel);

        /*
         * Per-call bound is the named HttpClient's Timeout, not a CTS around
         * the whole router. Wrapping `EvaluateAsync` in `TimeoutSeconds` (120)
         * while HttpClient defaulted to 100 s meant the first attempt ate
         * the budget and the router's retry was cancelled mid-flight — which
         * the worker then recorded as a failed job, not a transient.
         */
        var response = await router.EvaluateAsync(evalRequest, ticket, ct);

        logger.LogInformation(
            "Writing evaluation completed via {Provider} model {Model}, request {RequestId}, "
            + "rubric {RubricVersion}, prompt {PromptVersion}.",
            response.Provider,
            response.Model,
            response.RequestId,
            _artifact.Version,
            evalRequest.PromptVersion);

        var claim = WritingEvaluationValidator.ToClaimedEvaluation(response.Json, wholeBands);
        var limiterInput = WritingEvaluationValidator.LimitersFrom(
            response.Json,
            request.TaskNumber,
            generalTraining,
            WritingAdmission.LooksLikeNotes(sanitized),
            insufficientSentenceControl: wordCount < 50);
        var limited = WritingLimiters.Apply(claim.Criteria, limiterInput);

        var requested = response.RequestedModel ?? requestedModel ?? response.Model;
        var mismatch = !string.Equals(requested, response.Model, StringComparison.OrdinalIgnoreCase);

        return new ClaimedEvaluation(
            limited.Criteria,
            claim.ReportedBand,
            limited.Advisories,
            new WritingMarkingProvenance(
                evalRequest.PromptVersion,
                response.Provider,
                requested,
                response.Model,
                mismatch,
                response.RequestId));
    }

    /// <summary>
    /// Per-call provider budget, applied to the named HttpClient Timeout.
    /// Floor 10s so a typo cannot race the round-trip; ceiling 300s so a hung
    /// provider cannot hold a worker lease forever. Values are configuration,
    /// not production SLOs. → nfr.md FS9.3
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
