using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Assessment;

/// <summary>
/// Why a Writing or Speaking section has no band yet.
///
/// <b>A dash needs a reason, and the reason is worth storing.</b> Product law
/// L3 says an unmarked skill shows `—` rather than a zero or an average. That
/// is right, and it is also where the trail usually goes cold: a learner, or a
/// person answering a support ticket, is looking at a dash with no way to tell
/// whether the essay never arrived, whether nothing has been wired up yet, or
/// whether a model answered and its answer was refused. Those are four
/// different situations with four different fixes.
/// </summary>
public enum MarkingAvailability
{
    /// <summary>Marked. A band exists and was recomputed from the criterion bands.</summary>
    Marked,

    /// <summary>The learner submitted nothing for this task.</summary>
    NothingSubmitted,

    /// <summary>
    /// No rubric is configured for this module, so there is no criterion set to
    /// mark against and no descriptor provenance to record. → `H-8a`
    /// </summary>
    AwaitingRubric,

    /// <summary>
    /// No evaluator is wired. Expected today: `B-2` (the PDPL cross-border
    /// position) is unresolved, so no real learner work may cross a border.
    /// </summary>
    AwaitingEvaluator,

    /// <summary>
    /// Speaking only. The learner's words exist as audio and nothing has
    /// turned them into text — speech-to-text is still unselected, and the
    /// requirement for word-level timings narrows the field. Marking
    /// pronunciation from no transcript would be inventing the whole judgement.
    /// </summary>
    AwaitingTranscript,

    /// <summary>
    /// A model answered and the answer was refused — wrong criterion set, a
    /// band off the half-step grid, or a criterion with no cited evidence.
    ///
    /// <b>Distinct from every other value here, and the distinction matters.</b>
    /// The others mean nothing was attempted. This one means something was
    /// attempted and rejected, which is a signal about the prompt or the
    /// provider rather than about the learner. → `A-8`
    /// </summary>
    Rejected,
}

/// <summary>One task's outcome: a marking, or the reason there is none.</summary>
public sealed record MarkingOutcome(
    ExamModule Module,
    int? TaskNumber,
    MarkingAvailability Availability,
    SectionMarking? Marking,
    string? Detail);

/// <summary>
/// Marks the sections whose band comes from a judgement rather than a key.
///
/// <b>The counterpart to <see cref="ScoreIfDeterministic"/>, and deliberately
/// the same shape.</b> Both run at submission, both take the exam version and
/// the session, and neither knows what the other did. That symmetry is what
/// makes "four skills are marked" a true statement about one pipeline rather
/// than two subsystems that happen to both write results.
///
/// <b>It never writes a placeholder.</b> A section it cannot mark produces a
/// reason, not a band — no zero, no average of the criteria it does have, no
/// "pending" row that later reads as a real mark. → `G-11`, product law L3
/// </summary>
/// <b>No logger, and that is the project boundary rather than an oversight.</b>
/// Application takes no dependency on the logging abstractions — an
/// architecture test enforces it — so every outcome, including every refusal
/// and its reason, is <i>returned</i>. The caller decides what to record. That
/// is the better shape anyway: a reason that is only ever logged is a reason
/// the learner's results screen cannot show.
public sealed class SectionMarkingRunner(
    IRubricSource rubrics,
    IEnumerable<ISectionEvaluator> evaluators,
    ISectionMarkingStore store,
    ITranscriptSource transcripts)
{
    public async Task<IReadOnlyList<MarkingOutcome>> RunAsync(
        ExamVersion version, ExamModule module, ExamSessionId sessionId,
        IAnswerSheetStore answers, CancellationToken ct)
    {
        if (module is not (ExamModule.Writing or ExamModule.Speaking)) return [];
        if (version.Section(module) is not { } section) return [];

        var rubric = rubrics.For(module);
        var evaluator = evaluators.FirstOrDefault(e => e.Module == module);

        var sheet = await answers.LoadAsync(sessionId, module, ct);

        // <b>What is already marked is not marked again, and the check is a
        // read rather than a hope.</b> The store refuses a duplicate insert, so
        // a second run could never corrupt a band — but it refuses it
        // <i>after</i> the evaluation, which is the expensive half. A mobile
        // client retrying a submit, or a learner refreshing the results screen
        // of a sitting the server has just expired, would each buy a second
        // opinion on the same essay and throw it away. Asking first is one
        // query per run against a bill and a band that can move.
        var already = await store.ListAsync(sessionId, ct);
        var outcomes = new List<MarkingOutcome>();

        foreach (var unit in Units(section, module))
        {
            if (already.FirstOrDefault(
                    m => m.Module == unit.Module && m.TaskNumber == unit.TaskNumber) is { } done)
            {
                outcomes.Add(new MarkingOutcome(
                    unit.Module, unit.TaskNumber, MarkingAvailability.Marked, done, null));
                continue;
            }

            var outcome = await MarkOneAsync(
                unit, rubric, evaluator, sheet, sessionId, ct);

            outcomes.Add(outcome);

            if (outcome.Marking is { } marking)
                await store.SaveAsync(sessionId, marking, ct);
        }

        return outcomes;
    }

    private async Task<MarkingOutcome> MarkOneAsync(
        MarkableUnit unit, Rubric? rubric, ISectionEvaluator? evaluator,
        IReadOnlyDictionary<string, string?> sheet, ExamSessionId sessionId, CancellationToken ct)
    {
        MarkingOutcome Pending(MarkingAvailability why, string? detail = null) =>
            new(unit.Module, unit.TaskNumber, why, null, detail);

        if (rubric is null)
            return Pending(
                MarkingAvailability.AwaitingRubric,
                $"No rubric is configured for {unit.Module}. A rubric records which criteria were "
                + "used and where their descriptors came from; neither can be assumed. → H-8a");

        string? submission;

        if (unit.Module == ExamModule.Speaking)
        {
            // <b>Did the learner answer at all?</b> This question used to be
            // unaskable for Speaking, and the consequence was a lie: the
            // transcript was fetched first, came back null, and every Speaking
            // section in the product reported "awaiting transcript" — the
            // platform blaming its own missing ASR for a learner who had said
            // nothing. The two are not the same outcome and only one of them
            // is anybody's fault.
            //
            // The sheet is the index of what was recorded, written by the
            // server on upload, so an empty one means silence.
            var recordings = unit.QuestionIds
                .Select(id => (Id: id, Recording: sheet.GetValueOrDefault(id)))
                .Where(r => !string.IsNullOrWhiteSpace(r.Recording))
                .Select(r => new SpeakingRecording(r.Id, r.Recording!))
                .ToList();

            if (recordings.Count == 0) return Pending(MarkingAvailability.NothingSubmitted);

            // <b>The transcript is asked for before the evaluator is checked.</b>
            // Speaking has two independent blockers — no ASR provider and no
            // evaluator — and reporting whichever was checked first would make
            // the more tractable one invisible. Ordering by which is further
            // from being solved puts the real answer in front of whoever reads it.
            submission = await transcripts.ForAsync(sessionId, recordings, ct);

            if (string.IsNullOrWhiteSpace(submission))
                return Pending(
                    MarkingAvailability.AwaitingTranscript,
                    $"{recordings.Count} recording(s) exist for this section and no transcript "
                    + "does: no speech-to-text provider has been selected, and pronunciation "
                    + "additionally needs word-level timings.");
        }
        else
        {
            submission = sheet.GetValueOrDefault(unit.QuestionIds[0]);

            if (string.IsNullOrWhiteSpace(submission))
                return Pending(MarkingAvailability.NothingSubmitted);
        }

        if (evaluator is null || !evaluator.IsConfigured)
            return Pending(
                MarkingAvailability.AwaitingEvaluator,
                $"No evaluator is wired for {unit.Module}. Production AI is gated on B-2, the "
                + "Vietnam PDPL cross-border position.");

        try
        {
            var claim = await evaluator.EvaluateAsync(
                new EvaluationRequest(rubric, submission, unit.Prompt), ct);

            var marking = CriterionMarking.Mark(
                rubric, claim.Criteria, claim.ReportedBand, submission, unit.TaskNumber);

            return new MarkingOutcome(
                unit.Module, unit.TaskNumber, MarkingAvailability.Marked, marking, null);
        }
        catch (Exception e) when (e is MarkingRejectedException or ArgumentException)
        {
            // <b>Refused, not repaired, and not retried here.</b> A response
            // that failed validation is evidence about the prompt or the
            // provider. Silently asking again would hide the signal and bill
            // for it twice.
            return Pending(MarkingAvailability.Rejected, e.Message);
        }
    }

    /// <summary>
    /// What gets marked, one unit at a time.
    ///
    /// <b>Writing yields two units and Speaking yields one.</b> IELTS assesses
    /// each Writing task against the full set of four criteria and gives each
    /// its own band; Speaking gives one band for the whole test rather than one
    /// per part. So the split is the exam's, not a convenience.
    /// </summary>
    private static IEnumerable<MarkableUnit> Units(Section section, ExamModule module)
    {
        if (module == ExamModule.Speaking)
        {
            // One unit covering the whole section. Its "prompt" is every part's
            // body, because Part 1, the long turn and the discussion are one
            // performance being judged, not three.
            var prompt = string.Join(
                "\n\n", section.Parts.OrderBy(p => p.Order).Select(p => p.Body).Where(b => !string.IsNullOrWhiteSpace(b)));

            // <b>The section's real question ids, in the order they were
            // asked.</b> This used to be a made-up `speaking-{order}` — a key
            // that appears in no exam package, on no answer sheet and in no
            // recording's metadata, so the lookup it fed could only ever miss.
            // The ids have to be the ones the upload was filed under, because
            // that is the only thing that connects a stored recording to the
            // performance being marked.
            var questionIds = section.Parts
                .OrderBy(p => p.Order)
                .SelectMany(p => p.Questions.OrderBy(q => q.Order))
                .Select(q => q.Id)
                .ToList();

            if (questionIds.Count == 0) yield break;

            yield return new MarkableUnit(module, null, questionIds, prompt);
            yield break;
        }

        foreach (var part in section.Parts.OrderBy(p => p.Order))
        {
            var question = part.Questions.FirstOrDefault();
            if (question is null) continue;

            yield return new MarkableUnit(
                module, part.TaskNumber, [question.Id], part.Body ?? question.Prompt ?? string.Empty);
        }
    }

    /// <summary>
    /// <paramref name="QuestionIds"/> is a list because Speaking is one mark
    /// over several answers while Writing is one mark over exactly one. The
    /// non-Speaking path reads element zero and there is always one.
    /// </summary>
    private sealed record MarkableUnit(
        ExamModule Module, int? TaskNumber, IReadOnlyList<string> QuestionIds, string Prompt);
}

/// <summary>
/// The learner's spoken words as text.
///
/// <b>A port with no implementation, and that is the current state of the
/// product rather than an omission.</b> Speech-to-text has not been selected,
/// and requiring word-level timings — which Pronunciation needs — narrows the
/// field enough that the choice is not a formality. Until one is chosen this
/// returns null and Speaking reports
/// <see cref="MarkingAvailability.AwaitingTranscript"/>. → `G-11`
/// </summary>
public interface ITranscriptSource
{
    /// <summary>
    /// <b>Given the recordings, not a question id.</b> An implementation has
    /// to fetch audio from <see cref="Exams.IRecordingStore"/> and send it
    /// somewhere, and it can only do that if it is told which bytes. The
    /// earlier signature passed a question id and left the adapter to guess
    /// the rest, which is how the chain stayed broken without looking broken.
    ///
    /// The list is in the order the parts were asked, because a Speaking band
    /// is one judgement over the whole performance and the discussion does not
    /// read the same before the long turn.
    /// </summary>
    Task<string?> ForAsync(
        ExamSessionId sessionId, IReadOnlyList<SpeakingRecording> recordings, CancellationToken ct);
}

/// <summary>
/// One stored answer: which question it answered, and the server-generated key
/// it was filed under in <see cref="Exams.IRecordingStore"/>.
/// </summary>
public sealed record SpeakingRecording(string QuestionId, string RecordingId);
