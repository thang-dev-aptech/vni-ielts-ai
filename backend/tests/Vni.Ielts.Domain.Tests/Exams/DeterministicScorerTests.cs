using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

public sealed class DeterministicScorerTests
{
    private static Section Reading(int questionCount) => new(ExamModule.Reading, 1,
    [
        new SectionPart(1, "passage", "P", "body", null, null, null, null, null, null, null,
        [
            .. Enumerable.Range(1, questionCount).Select(i => new Question(
                $"r-{i}", i, QuestionType.ShortAnswer, $"Q{i}", [], null,
                new AnswerKey([new AcceptedAnswer($"answer{i}", null, null)], null))),
        ]),
    ]);

    /// <summary>A realistic 40-question table, thinned to the boundaries under test.</summary>
    private static ScoringProfile Profile() => new(
        new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>
        {
            [ExamModule.Reading] =
            [
                new BandBoundary(39, BandScore.Create(9m)),
                new BandBoundary(37, BandScore.Create(8.5m)),
                new BandBoundary(35, BandScore.Create(8m)),
                new BandBoundary(33, BandScore.Create(7.5m)),
                new BandBoundary(30, BandScore.Create(7m)),
                new BandBoundary(27, BandScore.Create(6.5m)),
                new BandBoundary(23, BandScore.Create(6m)),
                new BandBoundary(19, BandScore.Create(5.5m)),
                new BandBoundary(15, BandScore.Create(5m)),
                new BandBoundary(0, BandScore.Create(0m)),
            ],
        },
        AnswerMatchingRules.Default);

    [Fact]
    public void All_correct_produces_the_top_band()
    {
        var section = Reading(40);
        var answers = Enumerable.Range(1, 40).ToDictionary(i => $"r-{i}", i => (string?)$"answer{i}");

        var score = DeterministicScorer.Score(section, Profile(), answers);

        Assert.Equal(40, score.RawScore);
        Assert.Equal(40, score.MaxScore);
        Assert.Equal(9m, score.Band.Value);
    }

    [Fact]
    public void An_unanswered_paper_scores_zero_and_that_is_a_real_band()
    {
        var score = DeterministicScorer.Score(Reading(40), Profile(), new Dictionary<string, string?>());

        Assert.Equal(0, score.RawScore);
        Assert.Equal(0m, score.Band.Value);
        Assert.All(score.Questions, q => Assert.False(q.IsCorrect));
    }

    [Theory]
    [InlineData(30, 7.0)]
    [InlineData(29, 6.5)]   // one below the boundary
    [InlineData(27, 6.5)]
    [InlineData(26, 6.0)]
    public void The_band_comes_from_the_versioned_table_not_from_a_formula(int correct, decimal expected)
    {
        var section = Reading(40);
        var answers = Enumerable.Range(1, 40)
            .ToDictionary(i => $"r-{i}", i => (string?)(i <= correct ? $"answer{i}" : "wrong"));

        var score = DeterministicScorer.Score(section, Profile(), answers);

        Assert.Equal(correct, score.RawScore);
        Assert.Equal(expected, score.Band.Value);
    }

    [Fact]
    public void A_missing_table_refuses_to_invent_a_band()
    {
        // Returning 0.0 here would be a fabricated score presented as real.
        var empty = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DeterministicScorer.Score(Reading(1), empty, new Dictionary<string, string?>()));

        Assert.Contains("Refusing to invent", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Writing_and_speaking_cannot_be_scored_this_way()
    {
        // Their bands come from a validated evaluation. Allowing them here
        // would create a second, unvalidated path to a band.
        var writing = new Section(ExamModule.Writing, 1, []);

        Assert.Throws<ArgumentException>(() =>
            DeterministicScorer.Score(writing, Profile(), new Dictionary<string, string?>()));
    }

    [Fact]
    public void Ai_evaluated_questions_are_excluded_from_the_raw_count()
    {
        // An essay sitting inside a Reading section must not dilute the
        // denominator — 40 questions means 40 auto-scored questions.
        var mixed = new Section(ExamModule.Reading, 1,
        [
            new SectionPart(1, "passage", "P", "b", null, null, null, null, null, null, null,
            [
                new Question("r-1", 1, QuestionType.ShortAnswer, "q", [], null,
                    new AnswerKey([new AcceptedAnswer("paper", null, null)], null)),
                new Question("w-1", 2, QuestionType.EssayTask, "essay", [], null, null),
            ]),
        ]);

        var score = DeterministicScorer.Score(
            mixed, Profile(), new Dictionary<string, string?> { ["r-1"] = "paper" });

        Assert.Equal(1, score.MaxScore);
        Assert.Equal(1, score.RawScore);
    }
}

/// <summary>
/// Regression tests for a bug found by audit, not by the test suite.
///
/// A raw-to-band table whose lowest entry was 15 returned band 0.0 for a
/// learner who answered 14 correctly. Band 0 is a real, reportable band, so
/// the wrong score was indistinguishable from a genuine zero and nobody would
/// have investigated it. The coverage guard that should have caught the table
/// was checking the wrong end.
/// </summary>
public sealed class BandTableCoverageTests
{
    private static ScoringProfile WithTable(params (int MinRaw, decimal Band)[] rows) => new(
        new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>
        {
            [ExamModule.Reading] = [.. rows.Select(r => new BandBoundary(r.MinRaw, BandScore.Create(r.Band)))],
        },
        AnswerMatchingRules.Default);

    [Fact]
    public void A_score_below_the_lowest_threshold_throws_rather_than_returning_zero()
    {
        // The table starts at 15. A learner scoring 14 has not scored zero.
        var profile = WithTable((39, 9m), (30, 7m), (15, 5m));

        var ex = Assert.Throws<InvalidOperationException>(
            () => profile.BandFor(ExamModule.Reading, 14));

        Assert.Contains("Refusing to invent", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Coverage_detects_a_gap_at_the_bottom_of_the_table()
    {
        // This is the check that was broken. It previously looked at the top.
        var profile = WithTable((39, 9m), (30, 7m), (15, 5m));

        Assert.False(profile.CoversRange(ExamModule.Reading, 40, out var firstUncovered));
        Assert.Equal(0, firstUncovered);
    }

    [Fact]
    public void A_table_starting_at_zero_covers_the_whole_range()
    {
        var profile = WithTable((39, 9m), (30, 7m), (15, 5m), (0, 0m));

        Assert.True(profile.CoversRange(ExamModule.Reading, 40, out _));
        Assert.Equal(0m, profile.BandFor(ExamModule.Reading, 0).Value);
        Assert.Equal(0m, profile.BandFor(ExamModule.Reading, 14).Value);
        Assert.Equal(5m, profile.BandFor(ExamModule.Reading, 15).Value);
    }

    [Fact]
    public void A_table_written_for_more_questions_than_the_section_has_is_rejected()
    {
        // A 40-question table applied to a 15-question section means someone
        // reused the wrong profile, and every band across the range is wrong —
        // a learner answering all 15 would need 39 for band 9.
        var profile = WithTable((39, 9m), (0, 0m));

        Assert.False(profile.CoversRange(ExamModule.Reading, 15, out var firstUncovered));
        Assert.Equal(16, firstUncovered);
    }

    [Fact]
    public void An_absent_table_is_reported_as_uncovered_from_zero()
    {
        var empty = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default);

        Assert.False(empty.CoversRange(ExamModule.Reading, 40, out var firstUncovered));
        Assert.Equal(0, firstUncovered);
    }
}
