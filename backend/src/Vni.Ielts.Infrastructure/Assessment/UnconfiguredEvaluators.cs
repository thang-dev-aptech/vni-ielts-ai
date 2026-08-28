using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Infrastructure.Assessment;

/// <summary>
/// The evaluator that is not there.
///
/// <b>A registered absence, rather than an unregistered port.</b> The
/// difference is what a caller sees: an empty DI registration is a
/// <c>null</c> that every call site has to remember to check, and the one that
/// forgets throws a <c>NullReferenceException</c> somewhere unhelpful. This
/// answers honestly — <see cref="IsConfigured"/> is false — and refuses loudly
/// if called anyway.
///
/// <b>It exists because the absence is expected and long-lived.</b> `B-2`, the
/// Vietnam PDPL cross-border position, is unresolved, so no real learner essay
/// may be sent to a foreign endpoint. The pipeline around this port is
/// finished; what is missing is a legal answer, and the product runs without
/// one by reporting `AwaitingEvaluator` on two of four skills. → `G-11`
/// </summary>
public sealed class UnconfiguredEvaluator(ExamModule module) : ISectionEvaluator
{
    public ExamModule Module { get; } = module;

    public bool IsConfigured => false;

    public Task<ClaimedEvaluation> EvaluateAsync(EvaluationRequest request, CancellationToken ct) =>
        throw new InvalidOperationException(
            $"No evaluator is configured for {Module}. Callers must check IsConfigured — a "
            + "fabricated mark is worse than an absent one, so this refuses rather than "
            + "returning something shaped like a result.");
}

/// <summary>
/// Speaking transcripts, of which there are none.
///
/// <b>Not a stub waiting to be filled in — the honest current answer.</b>
/// Speech-to-text has not been selected for this platform, and Pronunciation
/// needs word-level timings, which narrows the field enough that the choice is
/// a real decision rather than a formality. Every recording the product holds
/// is audio and nothing has read it.
///
/// Returning null here is what makes Speaking report
/// <c>AwaitingTranscript</c> instead of `AwaitingEvaluator` — two different
/// blockers, and naming the further one first is the difference between a
/// useful status and a shrug.
/// </summary>
public sealed class NoTranscriptSource : ITranscriptSource
{
    public Task<string?> ForAsync(
        ExamSessionId sessionId, IReadOnlyList<SpeakingRecording> recordings,
        CancellationToken ct) =>
        Task.FromResult<string?>(null);
}
