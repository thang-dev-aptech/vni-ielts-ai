using System.Text.Json;
using System.Text.Json.Nodes;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Explanations;

/// <summary>
/// Generates canonical explanations at import/publish time when the package
/// declares <c>policyProfile.explanation.mode = ai-generated</c>.
/// CMS review happens before publication via ImportReviewWorkflow.
/// </summary>
public sealed class CanonicalExplanationWorkflow(
    IReadingListeningExplanationGenerator generator,
    ICanonicalExplanationCache cache)
{
    public const string PromptVersion = "rl-explanation-v1";

    public async Task<CanonicalEnrichmentResult> EnrichDraftAsync(
        ExamImportDraft draft, CancellationToken ct)
    {
        if (!RequiresGeneration(draft.Version))
            return CanonicalEnrichmentResult.Unchanged(draft);

        /*
         * The guarantee, not the optimisation. `ExamPackageImportPipeline`
         * checks this too, before it ever calls in here — but that check only
         * saves the cost of a call this method would refuse anyway. This is
         * the check that holds even when a future caller forgets: content
         * nobody has cleared for AI processing must never reach a provider,
         * and a rights control that lives only at one call site is one the
         * second caller — a CMS "regenerate explanations" button, say —
         * forgets to repeat.
         */
        if (!AllowsAiGeneration(draft.PackageJson))
            return CanonicalEnrichmentResult.Unchanged(draft);

        var package = JsonNode.Parse(draft.PackageJson)?.AsObject()
            ?? throw new InvalidOperationException("Draft package is not valid JSON.");

        var warnings = new List<ImportReviewWarning>();
        var generated = 0;
        var cached = 0;
        var refused = 0;

        foreach (var (section, part, question) in EnumerateAutoScored(draft.Version))
        {
            if (question.Explanation is not null) continue;

            var expected = FormatExpectedAnswer(question);
            var source = BuildSource(section, part);

            var cachedEntry = await cache.FindAsync(draft.Version.Id, question.Id, ct);
            if (cachedEntry is not null)
            {
                ApplyExplanation(package, section.Module, question.Id, cachedEntry.Explanation);
                cached++;
                continue;
            }

            var result = await generator.GenerateAsync(
                new ExplanationGenerationRequest(
                    section.Module,
                    question.Id,
                    ExplanationQuestionText.Compose(question),
                    expected,
                    LearnerAnswer: null,
                    PassageOrTranscript: source.PassageBody ?? source.Transcript,
                    Personalized: false,
                    QuestionOptions: ExplanationPromptSafety.FormatOptions(question.Options)),
                ct);

            if (!result.IsSuccess || result.RawJson is null)
            {
                refused++;
                warnings.Add(new ImportReviewWarning(
                    $"exp-{question.Id}",
                    ImportReviewCategory.TranscriptAndEvidence,
                    $"/sections/{section.Module}/questions/{question.Id}",
                    $"Explanation generation refused: {result.RefusalCode ?? "unknown"}",
                    Resolved: false));
                continue;
            }

            var validation = ExplanationOutputValidator.Validate(
                result.RawJson, expected, section.Module, source);

            if (!validation.IsValid || validation.Explanation is null)
            {
                refused++;
                warnings.Add(new ImportReviewWarning(
                    $"exp-{question.Id}",
                    ImportReviewCategory.TranscriptAndEvidence,
                    $"/sections/{section.Module}/questions/{question.Id}",
                    $"Explanation validation refused: {validation.RefusalCode}",
                    Resolved: false));
                continue;
            }

            await cache.SaveAsync(
                new StoredCanonicalExplanation(
                    draft.Version.Id,
                    question.Id,
                    validation.Explanation,
                    result.Metadata ?? new ExplanationProviderMetadata("recorded", "fixture", PromptVersion, question.Id)),
                ct);

            ApplyExplanation(package, section.Module, question.Id, validation.Explanation);
            generated++;
        }

        if (generated == 0 && cached == 0 && refused == 0)
            return CanonicalEnrichmentResult.Unchanged(draft);

        var json = package.ToJsonString();
        return new CanonicalEnrichmentResult(
            draft with
            {
                PackageJson = json,
                PackageHash = ExamImportWorkflow.Hash(json),
                ApprovalState = ImportApprovalState.ReviewRequired,
                Checklist = ImportReviewChecklist.Empty,
                Warnings = [.. draft.Warnings, .. warnings],
                Revision = draft.Revision + 1,
            },
            generated,
            cached,
            refused);
    }

    public static bool RequiresGeneration(ExamVersion version) =>
        version.Sections.Any(s =>
            s.Module is ExamModule.Reading or ExamModule.Listening
            && s.Questions.Any(q => q.Type.IsAutoScored() && q.Explanation is null));

    /// <summary>
    /// Reads the raw package rather than <see cref="ExamVersion"/> —
    /// <c>policyProfile.explanation.mode</c> is a rights decision the schema
    /// carries and the domain model does not. Omitting the object, or any
    /// value other than <c>ai-generated</c>, refuses: the default is silence,
    /// never AI processing. Malformed JSON also refuses rather than throws —
    /// this method is a gate, not a validator, and a draft whose package
    /// cannot be read is exactly the case that must not reach a provider.
    /// </summary>
    internal static bool AllowsAiGeneration(string packageJson)
    {
        try
        {
            return JsonNode.Parse(packageJson)?["policyProfile"]?["explanation"]?["mode"]
                ?.GetValue<string>() == "ai-generated";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IEnumerable<(Section Section, SectionPart Part, Question Question)> EnumerateAutoScored(
        ExamVersion version)
    {
        foreach (var section in version.Sections.Where(s =>
                     s.Module is ExamModule.Reading or ExamModule.Listening))
        {
            foreach (var part in section.Parts)
            {
                foreach (var question in part.Questions.Where(q => q.Type.IsAutoScored()))
                    yield return (section, part, question);
            }
        }
    }

    private static EvidenceSourceContext BuildSource(Section section, SectionPart part) =>
        section.Module == ExamModule.Reading
            ? new EvidenceSourceContext(part.Body, null)
            : new EvidenceSourceContext(null, part.Transcript);

    private static string FormatExpectedAnswer(Question question)
    {
        if (question.Slots is { Count: > 0 } slots)
        {
            return string.Join(
                " / ",
                slots.OrderBy(s => s.Number)
                    .Select(s => AnswerMatcher.FormatAcceptedAnswer(s.AnswerKey, AnswerMatchingRules.Default)
                             ?? string.Empty));
        }

        return AnswerMatcher.FormatAcceptedAnswer(question.AnswerKey, AnswerMatchingRules.Default)
               ?? string.Empty;
    }

    private static void ApplyExplanation(
        JsonObject package, ExamModule module, string questionId, ValidatedExplanation explanation)
    {
        var sections = package["sections"]?.AsArray();
        if (sections is null) return;

        foreach (var sectionNode in sections)
        {
            if (!string.Equals(
                    sectionNode?["module"]?.GetValue<string>(),
                    module.ToString(),
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = sectionNode?["parts"]?.AsArray();
            if (parts is null) continue;

            foreach (var part in parts)
            {
                var questions = part?["questions"]?.AsArray();
                if (questions is null) continue;

                foreach (var q in questions)
                {
                    if (!string.Equals(q?["id"]?.GetValue<string>(), questionId, StringComparison.Ordinal))
                        continue;

                    var node = new JsonObject
                    {
                        ["correctAnswer"] = explanation.CorrectAnswer,
                        ["shortReason"] = explanation.ShortReason,
                        ["evidence"] = new JsonArray(
                            explanation.Evidence.Select(e => JsonValue.Create(e)).ToArray()),
                    };

                    // Only written when present: the package schema declares both
                    // keys as optional strings, and a JSON null fails `type:
                    // string` — caught by the revalidation
                    // ImportReviewWorkflow.EnrichCanonicalExplanationsAsync runs
                    // right after this, once anything actually called it.
                    if (explanation.CommonMistake is not null)
                        node["commonMistake"] = explanation.CommonMistake;
                    if (explanation.Translation is not null)
                        node["translation"] = explanation.Translation;

                    q!["explanation"] = node;
                    return;
                }
            }
        }
    }
}

public sealed record CanonicalEnrichmentResult(
    ExamImportDraft Draft,
    int Generated,
    int Cached,
    int Refused)
{
    public static CanonicalEnrichmentResult Unchanged(ExamImportDraft draft) =>
        new(draft, 0, 0, 0);
}
