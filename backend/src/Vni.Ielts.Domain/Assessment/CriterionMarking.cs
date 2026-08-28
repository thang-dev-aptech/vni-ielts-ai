using System.Text;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Assessment;

/// <summary>
/// One criterion's mark, with the basis for it.
///
/// <b><see cref="Evidence"/> is required, and that is the whole point.</b> A
/// band with a paragraph of prose attached is an opinion; a band with a
/// quotation from the learner's own writing attached is something the learner
/// can look up and argue with. The product owner's instruction on 2026-08-21
/// was that marking must have a stated basis rather than be asserted — this
/// field is where that instruction becomes a type rather than an intention.
/// </summary>
public sealed record CriterionAssessment
{
    private CriterionAssessment(
        string criterion, BandScore band, string feedback, IReadOnlyList<string> evidence)
    {
        Criterion = criterion;
        Band = band;
        Feedback = feedback;
        Evidence = evidence;
    }

    public string Criterion { get; }
    public BandScore Band { get; }
    public string Feedback { get; }

    /// <summary>Verbatim spans from the learner's own submission.</summary>
    public IReadOnlyList<string> Evidence { get; }

    public static CriterionAssessment Create(
        string criterion, BandScore band, string feedback, IReadOnlyList<string> evidence)
    {
        if (string.IsNullOrWhiteSpace(criterion))
            throw new ArgumentException("A mark must name its criterion.", nameof(criterion));

        if (string.IsNullOrWhiteSpace(feedback))
            throw new ArgumentException(
                "A band with no feedback tells the learner nothing they can act on.",
                nameof(feedback));

        if (evidence.Count == 0)
            throw new ArgumentException(
                "A criterion band must cite at least one span of the learner's own work. "
                + "A band asserted without a basis is not a mark, it is an opinion.",
                nameof(evidence));

        return new CriterionAssessment(criterion, band, feedback, [.. evidence]);
    }
}

/// <summary>
/// What went wrong with a mark that was otherwise well-formed.
///
/// <b>A flag is not a rejection.</b> These describe evaluations that parsed,
/// validated against the schema, and produced usable bands — but that carry
/// something an operator should look at. Screen 5.1 of the CMS filters on
/// them. An evaluation that cannot produce a band at all does not reach this
/// type; it throws.
/// </summary>
public enum MarkingFlag
{
    /// <summary>
    /// The section band the model reported differs from the one recomputed
    /// from its own criterion bands. The recomputed value is the one used.
    /// </summary>
    ArithmeticMismatch,

    /// <summary>
    /// A cited span does not occur in the learner's submission. Either the
    /// model paraphrased where it was told to quote, or it invented the
    /// quotation — and a fabricated basis is worse than no basis, because it
    /// reads as verifiable.
    /// </summary>
    EvidenceNotGrounded,
}

/// <summary>
/// A validated set of criterion marks for one section.
///
/// <b>The band on this object is computed here, never taken from the model.</b>
/// → `docs/ai/output-contracts.md` check 5.
/// </summary>
/// <param name="TaskNumber">
/// Which Writing task this marks, or null for a module marked as one unit.
///
/// <b>Writing is two markings, not one, and that is IELTS's shape rather than
/// a modelling preference.</b> Task 1 and Task 2 are each assessed against the
/// full set of four criteria and each earns its own band; the Writing band is
/// those two combined on a ratio that is <i>not published</i> and that this
/// codebase refuses to guess (`H-8b`, and <c>ScoringProfile</c> refuses in the
/// same way). Marking a Writing section as a single unit would have quietly
/// answered that question by averaging — the one outcome `G-11` forbids.
///
/// Speaking is genuinely one marking: four criteria, one band for the whole
/// test, not one per part. So it carries null here, and the null is a fact
/// about Speaking rather than a field nobody filled in.
/// </param>
public sealed record SectionMarking(
    ExamModule Module,
    string RubricVersion,
    IReadOnlyList<CriterionAssessment> Criteria,
    BandScore Band,
    BandScore? ReportedBand,
    IReadOnlyList<MarkingFlag> Flags,
    IReadOnlyList<string> UngroundedEvidence,
    int? TaskNumber = null)
{
    /// <summary>
    /// Whether anything here needs a human to look at it.
    ///
    /// Used by the CMS list screen. It is deliberately not called
    /// <c>IsValid</c>: every <see cref="SectionMarking"/> that exists is
    /// valid, in the sense that its bands are reportable and its arithmetic
    /// has been redone. Flagged means "usable, and worth a look".
    /// </summary>
    public bool IsFlagged => Flags.Count > 0;
}

/// <summary>
/// Turns a model's claimed marks into a <see cref="SectionMarking"/>, or
/// refuses.
///
/// <b>This type never sees a provider SDK.</b> It takes primitives — the
/// criterion keys, the claimed decimals, the cited strings, the learner's
/// text — so the rules stay testable with no network, no key, and no vendor
/// package anywhere near the Domain project. → ADR-0005, and the architecture
/// test that enforces it.
///
/// Three refusals and two flags, in that order of severity:
///
/// <list type="number">
/// <item><b>Refuse</b> a criterion set that is not the rubric's. A missing
/// criterion means a quarter of the mark is absent; an extra one means the
/// model answered a different question than the one asked.</item>
/// <item><b>Refuse</b> a band off the half-step scale. <see cref="BandScore"/>
/// throws rather than clamping — check 4.</item>
/// <item><b>Refuse</b> a criterion with no cited evidence.</item>
/// <item><b>Flag</b> a reported section band that disagrees with the
/// recomputation, and use the recomputation.</item>
/// <item><b>Flag</b> a citation that does not occur in the learner's
/// submission.</item>
/// </list>
/// </summary>
public static class CriterionMarking
{
    /// <param name="taskNumber">
    /// The Writing task being marked, or null for a module marked as one unit.
    /// See the note on <see cref="SectionMarking.TaskNumber"/> — Writing is two
    /// markings because IELTS assesses each task against all four criteria, and
    /// combining them needs a ratio nobody has published.
    /// </param>
    public static SectionMarking Mark(
        Rubric rubric,
        IReadOnlyList<ClaimedCriterion> claimed,
        decimal? reportedSectionBand,
        string learnerSubmission,
        int? taskNumber = null)
    {
        RequireExactCriterionSet(rubric, claimed);

        var byKey = claimed.ToDictionary(c => c.Criterion, StringComparer.Ordinal);
        var normalisedSubmission = Normalise(learnerSubmission);

        var assessments = new List<CriterionAssessment>(rubric.Criteria.Count);
        var ungrounded = new List<string>();

        // Rubric order, not response order — the reporting order is part of
        // the rubric, and a model is free to answer in any order it likes.
        foreach (var key in rubric.Criteria)
        {
            var c = byKey[key];

            assessments.Add(CriterionAssessment.Create(
                key, BandScore.Create(c.Band), c.Feedback, c.Evidence));

            ungrounded.AddRange(
                c.Evidence.Where(quote => !IsGroundedIn(normalisedSubmission, quote)));
        }

        var recomputed = Aggregate([.. assessments.Select(a => a.Band)]);

        var flags = new List<MarkingFlag>();

        BandScore? reported = null;
        if (reportedSectionBand is { } value)
        {
            // Validated, not clamped, even though it is about to be discarded.
            // A model reporting 47 here is a fault worth surfacing rather than
            // a field worth ignoring.
            reported = BandScore.Create(value);
            if (reported != recomputed) flags.Add(MarkingFlag.ArithmeticMismatch);
        }

        if (ungrounded.Count > 0) flags.Add(MarkingFlag.EvidenceNotGrounded);

        return new SectionMarking(
            rubric.Module, rubric.Version, assessments, recomputed, reported, flags, ungrounded,
            taskNumber);
    }

    /// <summary>
    /// The criterion bands combined into a section band.
    ///
    /// <b>`[ASSUMPTION]`</b> — the criteria are equally weighted, averaged, and
    /// rounded on the same asymmetric half-band rule as the overall score.
    /// `docs/domain/band-scoring.md` records this: IELTS publishes the
    /// overall-band rule in full but does not publish criterion aggregation in
    /// the same detail, so this mirrors the published rule rather than
    /// inventing a different one.
    ///
    /// Note what is <i>not</i> here: the Writing Task 1 / Task 2 weighting.
    /// That one is a real unknown rather than a mirrored rule, so it lives on
    /// <see cref="ScoringProfile"/> and refuses to default. Combining two task
    /// bands is a caller's job, and the caller has to have the weights.
    /// </summary>
    public static BandScore Aggregate(IReadOnlyCollection<BandScore> criterionBands) =>
        BandScore.Overall(criterionBands);

    private static void RequireExactCriterionSet(
        Rubric rubric, IReadOnlyList<ClaimedCriterion> claimed)
    {
        var expected = new HashSet<string>(rubric.Criteria, StringComparer.Ordinal);
        var actual = new HashSet<string>(claimed.Select(c => c.Criterion), StringComparer.Ordinal);

        if (actual.SetEquals(expected) && actual.Count == claimed.Count) return;

        var missing = expected.Except(actual).Order(StringComparer.Ordinal).ToArray();
        var unexpected = actual.Except(expected).Order(StringComparer.Ordinal).ToArray();
        var duplicated = claimed.Count != actual.Count;

        var detail = new StringBuilder(
            $"The response does not match rubric '{rubric.Version}'.");

        if (missing.Length > 0) detail.Append($" Missing: {string.Join(", ", missing)}.");
        if (unexpected.Length > 0) detail.Append($" Unexpected: {string.Join(", ", unexpected)}.");
        if (duplicated) detail.Append(" A criterion was marked more than once.");

        throw new MarkingRejectedException(detail.ToString());
    }

    /// <summary>
    /// Whether a cited span occurs in the submission.
    ///
    /// <b>Normalised, but only for the differences a quotation cannot control.</b>
    /// Whitespace runs collapse, because a model re-wrapping a line is not a
    /// fabrication; typographic quotes and dashes fold to their ASCII forms,
    /// because a model normalising `’` to `'` is not one either. Case folds
    /// too — the same words at the start of a sentence are the same words.
    ///
    /// Nothing else. Stemming, or matching on word overlap, would let a
    /// paraphrase pass as a quotation, and the entire value of this check is
    /// that it distinguishes those two things.
    /// </summary>
    private static bool IsGroundedIn(string normalisedSubmission, string quote)
    {
        var needle = Normalise(quote);

        return needle.Length > 0
            && normalisedSubmission.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = true;

        foreach (var raw in text)
        {
            var ch = raw switch
            {
                '‘' or '’' or 'ʼ' => '\'',
                '“' or '”' => '"',
                '–' or '—' or '−' => '-',
                ' ' => ' ',
                _ => raw,
            };

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            sb.Append(ch);
            lastWasSpace = false;
        }

        return sb.ToString().TrimEnd();
    }
}

/// <summary>One criterion as the model reported it, before any validation.</summary>
public sealed record ClaimedCriterion(
    string Criterion, decimal Band, string Feedback, IReadOnlyList<string> Evidence);

/// <summary>
/// The response was well-formed enough to parse but cannot become a mark.
///
/// Distinct from an exception thrown by <see cref="BandScore"/>: this one is
/// about the shape of the answer, not the value of a band. Both end the same
/// way — no band is produced — which is the behaviour requirement `A-8` asks
/// for.
/// </summary>
public sealed class MarkingRejectedException(string message) : Exception(message);
