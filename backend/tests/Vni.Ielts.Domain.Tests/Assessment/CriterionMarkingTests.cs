using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Assessment;

/// <summary>
/// The rules that make an AI band defensible rather than asserted.
///
/// Every test here describes a way a model has actually been observed to
/// answer badly — a paraphrase where a quotation was asked for, arithmetic
/// that does not follow from its own criterion bands, a criterion quietly
/// dropped. None of them is caught by JSON Schema validation, which is why
/// they are checked in code.
/// </summary>
public sealed class CriterionMarkingTests
{
    private static readonly Rubric Writing = Rubric.Create(
        "writing-v1", ExamModule.Writing, CriterionKeys.Writing, "test fixture");

    private const string Submission =
        "Some people believe that the government should fund public transport. "
        + "In my opinion, this policy brings clear benefits to the city, although "
        + "it is not without cost. Firstly, the government\u2019s spending on roads "
        + "would fall, and fewer cars means cleaner air.";

    private static ClaimedCriterion Claim(
        string criterion, decimal band, string? evidence = null) =>
        new(criterion, band, "Nhận xét mẫu.", [evidence ?? "fewer cars means cleaner air"]);

    private static List<ClaimedCriterion> FourClaims(
        decimal tr = 6, decimal cc = 6, decimal lr = 6, decimal gra = 6) =>
    [
        Claim(CriterionKeys.TaskResponse, tr),
        Claim(CriterionKeys.CoherenceAndCohesion, cc),
        Claim(CriterionKeys.LexicalResource, lr),
        Claim(CriterionKeys.GrammaticalRangeAndAccuracy, gra),
    ];

    [Fact]
    public void A_well_formed_response_produces_a_band_with_no_flags()
    {
        var marking = CriterionMarking.Mark(Writing, FourClaims(), 6m, Submission);

        Assert.Equal("6.0", marking.Band.ToString());
        Assert.False(marking.IsFlagged);
        Assert.Equal(4, marking.Criteria.Count);
    }

    [Fact]
    public void Criteria_are_reported_in_rubric_order_whatever_order_the_model_answered_in()
    {
        var shuffled = FourClaims();
        shuffled.Reverse();

        var marking = CriterionMarking.Mark(Writing, shuffled, null, Submission);

        Assert.Equal(CriterionKeys.Writing, [.. marking.Criteria.Select(c => c.Criterion)]);
    }

    // ── Refusals ────────────────────────────────────────────────────────────

    [Fact]
    public void A_missing_criterion_is_refused_rather_than_marked_on_three()
    {
        var three = FourClaims();
        three.RemoveAt(0);

        var ex = Assert.Throws<MarkingRejectedException>(
            () => CriterionMarking.Mark(Writing, three, null, Submission));

        Assert.Contains("Missing: taskResponse", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_criterion_the_rubric_does_not_have_is_refused()
    {
        var claims = FourClaims();
        claims.Add(Claim("pronunciation", 7));

        var ex = Assert.Throws<MarkingRejectedException>(
            () => CriterionMarking.Mark(Writing, claims, null, Submission));

        Assert.Contains("Unexpected: pronunciation", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_criterion_marked_twice_is_refused()
    {
        var claims = FourClaims();
        claims.Add(Claim(CriterionKeys.LexicalResource, 8));

        var ex = Assert.Throws<MarkingRejectedException>(
            () => CriterionMarking.Mark(Writing, claims, null, Submission));

        Assert.Contains("more than once", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(6.3)]  // a numeric range was ignored
    [InlineData(47)]   // something went badly wrong upstream
    public void A_band_off_the_scale_throws_and_is_never_clamped(decimal band)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => CriterionMarking.Mark(Writing, FourClaims(tr: band), null, Submission));

        Assert.Contains("never clamp", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_criterion_citing_nothing_is_refused()
    {
        List<ClaimedCriterion> claims =
        [
            new(CriterionKeys.TaskResponse, 6m, "Nhận xét.", []),
            Claim(CriterionKeys.CoherenceAndCohesion, 6),
            Claim(CriterionKeys.LexicalResource, 6),
            Claim(CriterionKeys.GrammaticalRangeAndAccuracy, 6),
        ];

        var ex = Assert.Throws<ArgumentException>(
            () => CriterionMarking.Mark(Writing, claims, null, Submission));

        Assert.Contains("at least one span", ex.Message, StringComparison.Ordinal);
    }

    // ── Flags ───────────────────────────────────────────────────────────────

    [Fact]
    public void The_recomputed_band_wins_when_the_model_reports_a_different_one()
    {
        // Criterion bands average to 6.0; the model claims 7.5.
        var marking = CriterionMarking.Mark(Writing, FourClaims(), 7.5m, Submission);

        Assert.Equal("6.0", marking.Band.ToString());
        Assert.Equal("7.5", marking.ReportedBand?.ToString());
        Assert.Contains(MarkingFlag.ArithmeticMismatch, marking.Flags);
    }

    [Fact]
    public void A_quotation_that_is_not_in_the_submission_is_flagged()
    {
        var claims = FourClaims();
        claims[0] = Claim(
            CriterionKeys.TaskResponse, 6, "the candidate discusses economic growth at length");

        var marking = CriterionMarking.Mark(Writing, claims, null, Submission);

        Assert.Contains(MarkingFlag.EvidenceNotGrounded, marking.Flags);
        Assert.Single(marking.UngroundedEvidence);
    }

    [Theory]
    // Re-wrapped by the model — not a fabrication.
    [InlineData("fewer   cars\n  means cleaner air")]
    // Sentence-initial capital restored — same words.
    [InlineData("Fewer cars means cleaner air")]
    // The submission has a typographic apostrophe; the model quoted it with an
    // ASCII one. That is a normalisation, not an invention.
    [InlineData("the government's spending on roads")]
    public void Differences_a_quotation_cannot_control_do_not_count_as_fabrication(string quote)
    {
        var claims = FourClaims();
        claims[0] = Claim(CriterionKeys.TaskResponse, 6, quote);

        var marking = CriterionMarking.Mark(Writing, claims, null, Submission);

        Assert.Empty(marking.UngroundedEvidence);
    }

    [Fact]
    public void A_paraphrase_does_not_pass_as_a_quotation()
    {
        // Every content word appears in the submission; the span does not.
        // Word-overlap matching would accept this, which is precisely why the
        // check is a substring match.
        var claims = FourClaims();
        claims[0] = Claim(CriterionKeys.TaskResponse, 6, "cleaner air means fewer cars");

        var marking = CriterionMarking.Mark(Writing, claims, null, Submission);

        Assert.Contains(MarkingFlag.EvidenceNotGrounded, marking.Flags);
    }

    [Fact]
    public void An_empty_quotation_is_not_grounded_in_everything()
    {
        // A naive Contains("") returns true for any text, which would let a
        // model satisfy the evidence requirement with a blank string.
        var claims = FourClaims();
        claims[0] = Claim(CriterionKeys.TaskResponse, 6, "   ");

        var marking = CriterionMarking.Mark(Writing, claims, null, Submission);

        Assert.Contains(MarkingFlag.EvidenceNotGrounded, marking.Flags);
    }

    // ── Aggregation ─────────────────────────────────────────────────────────

    [Theory]
    // TR   CC   LR   GRA    mean    expected
    [InlineData(6, 6, 6, 6, "6.0")]
    [InlineData(6, 6, 6, 7, "6.5")]   // 6.25 -> up to the half band
    [InlineData(7, 7, 7, 6, "7.0")]   // 6.75 -> up to the whole band
    [InlineData(5, 6, 6, 6, "6.0")]   // 5.75 -> up to the whole band
    public void Criterion_bands_aggregate_on_the_official_asymmetric_rule(
        decimal tr, decimal cc, decimal lr, decimal gra, string expected)
    {
        var marking = CriterionMarking.Mark(Writing, FourClaims(tr, cc, lr, gra), null, Submission);

        Assert.Equal(expected, marking.Band.ToString());
    }

    // ── The weighting that must not default ─────────────────────────────────

    [Fact]
    public void An_exam_version_with_no_writing_task_weighting_refuses_to_supply_one()
    {
        var profile = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default);

        var ex = Assert.Throws<InvalidOperationException>(() => profile.RequireWritingTaskWeights());

        Assert.Contains("Refusing to invent a weighting", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configured_weighting_is_returned_as_configured()
    {
        var profile = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default, 1m, 2m);

        Assert.Equal((1m, 2m), profile.RequireWritingTaskWeights());
    }
}
