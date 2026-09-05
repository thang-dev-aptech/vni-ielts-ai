using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Assessment;

/// <summary>
/// Marks one Writing or Speaking section against a rubric.
///
/// <b>A port, so no vendor SDK reaches Domain or Application.</b> Whatever
/// produces the marks — a model behind an HTTP call today, something else
/// later — lives in an Infrastructure adapter. → ADR-0005, and the
/// architecture test that fails if a provider type crosses the line.
///
/// <b>The absence of an implementation is a supported state, not a fault.</b>
/// No AI provider is wired: `B-2` (the PDPL cross-border position) is
/// unresolved, so nothing may carry a real learner's essay to a foreign
/// endpoint yet, and speech-to-text has not been selected at all. Reading and
/// Listening are marked from the answer key and never touch a model (`A-11`),
/// so an install with no evaluator is a working install with two of four bands.
/// That is why <see cref="IsConfigured"/> exists and why callers ask before
/// they call. → `G-11`
/// </summary>
public interface ISectionEvaluator
{
    /// <summary>Which module this evaluator marks.</summary>
    ExamModule Module { get; }

    /// <summary>
    /// False when nothing is wired behind this port. A caller that ignores it
    /// and calls anyway gets an exception, never a fabricated mark.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// The model's claim — not a mark.
    ///
    /// What comes back is unvalidated: a criterion set that might not match the
    /// rubric, bands that might be off the half-step grid, evidence that might
    /// not appear in the learner's own words. Turning it into a
    /// <see cref="SectionMarking"/> is <see cref="CriterionMarking.Mark"/>'s
    /// job, and it refuses rather than repairs. → `docs/ai/output-contracts.md`
    /// </summary>
    Task<ClaimedEvaluation> EvaluateAsync(EvaluationRequest request, CancellationToken ct);
}

/// <summary>What an evaluator is given. Primitives and a rubric — no session, no learner.</summary>
/// <param name="Rubric">Carries the criterion set, its version, and where its descriptors came from.</param>
/// <param name="LearnerSubmission">
/// The learner's own words. For Writing that is the essay text; for Speaking it
/// is a transcript, which is why Speaking cannot be evaluated before a
/// speech-to-text provider exists.
/// </param>
/// <param name="Prompt">The task the learner was answering, so the model can judge relevance.</param>
public sealed record EvaluationRequest(
    Rubric Rubric,
    string LearnerSubmission,
    string Prompt);

/// <summary>
/// A model's unvalidated claim about one section.
///
/// <b><see cref="ReportedBand"/> is kept precisely because it is not trusted.</b>
/// The section band is recomputed from the criterion bands; carrying what the
/// model said lets the recomputation disagree out loud rather than silently.
/// → `docs/ai/output-contracts.md` check 5
/// </summary>
public sealed record ClaimedEvaluation(
    IReadOnlyList<ClaimedCriterion> Criteria,
    decimal? ReportedBand);

/// <summary>
/// The rubric in force for a module.
///
/// <b>Configuration, not a constant.</b> A rubric is the thing an evaluation is
/// reproducible against, and `Rubric.DescriptorSource` has to record where the
/// band descriptors came from — an open legal question (`H-8a`: the official
/// descriptors carry a joint copyright with no stated third-party reuse terms).
/// A rubric compiled into a class could not record an answer that has not been
/// given yet.
/// </summary>
public interface IRubricSource
{
    /// <summary>Null when no rubric is configured for the module. Never a fabricated one.</summary>
    Rubric? For(ExamModule module);
}

/// <summary>
/// Marked Writing and Speaking sections.
///
/// Separate from <see cref="Exams.ISectionResultStore"/> because the two carry
/// different facts and conflating them loses one of them. A
/// <see cref="SectionScore"/> is arithmetic over an answer key: a raw score, a
/// max, and which questions were right. A <see cref="SectionMarking"/> is a
/// judgement: four criterion bands, the evidence quoted for each, the rubric
/// version it was produced under, and any flag raised while validating it.
/// One table with both shapes would leave half its columns null on every row.
/// </summary>
public interface ISectionMarkingStore
{
    Task SaveAsync(ExamSessionId sessionId, SectionMarking marking, CancellationToken ct);

    Task<IReadOnlyList<SectionMarking>> ListAsync(ExamSessionId sessionId, CancellationToken ct);
}
