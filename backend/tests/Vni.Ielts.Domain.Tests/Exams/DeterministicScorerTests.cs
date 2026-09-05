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
        Assert.Equal(9m, score.Band!.Value.Value);
    }

    [Fact]
    public void An_unanswered_paper_scores_zero_and_that_is_a_real_band()
    {
        var score = DeterministicScorer.Score(Reading(40), Profile(), new Dictionary<string, string?>());

        Assert.Equal(0, score.RawScore);
        Assert.Equal(0m, score.Band!.Value.Value);
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
        Assert.Equal(expected, score.Band!.Value.Value);
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

  /// <summary>
  /// A "Choose TWO letters" question is one object and two marks.
  ///
  /// <b>The band table is equated against the answer sheet, not against
  /// however many objects an author modelled.</b> A real Listening paper has
  /// forty numbered lines; a two-letter question fills two of them. Counting
  /// objects scored a forty-mark section out of thirty-six, which put the
  /// table's own top bands out of reach and read every score below them off
  /// the wrong row — and it looked like nothing, because the arithmetic was
  /// internally consistent.
  ///
  /// The first real package authored against this schema has three such
  /// questions: 36 objects, 40 marks.
  /// </summary>
  [Fact]
  public void A_multi_mark_question_contributes_all_of_its_marks()
  {
    var section = new Section(ExamModule.Reading, 1,
    [
      new SectionPart(1, "passage", "P", "body", null, null, null, null, null, null, null,
      [
        new Question("r-1", 1, QuestionType.MultipleSelect, "Choose TWO letters.",
          [new QuestionOption("A", "A"), new QuestionOption("B", "B")],
          null,
          new AnswerKey([new AcceptedAnswer(null, ["A", "B"], null)], null),
          null,
          Marks: 2),
        new Question("r-2", 2, QuestionType.ShortAnswer, "Q2", [], null,
          new AnswerKey([new AcceptedAnswer("answer2", null, null)], null)),
      ]),
    ]);

    var both = DeterministicScorer.Score(section, Profile(),
      new Dictionary<string, string?> { ["r-1"] = "A,B", ["r-2"] = "answer2" });

    Assert.Equal(3, both.MaxScore);
    Assert.Equal(3, both.RawScore);

    // And nothing when it is wrong: the answer key is a set that must
    // match, so part-marking a multi-mark question would need an
    // answer-key shape that does not exist.
    //
    // This under-scores a real candidate — IELTS awards one mark per
    // correct letter, so two of three right earns 2 and this earns 0. It
    // stands because the alternative is an invented policy in the
    // direction that inflates a band. → `H-12`
    var half = DeterministicScorer.Score(section, Profile(),
      new Dictionary<string, string?> { ["r-1"] = "A", ["r-2"] = "answer2" });

    Assert.Equal(3, half.MaxScore);
    Assert.Equal(1, half.RawScore);
  }

  [Fact]
  public void Slotted_questions_award_partial_credit_per_slot()
  {
    var section = new Section(ExamModule.Reading, 1,
    [
      new SectionPart(1, "part", "P", null, null, null, null, null, 1, null, null,
      [
        new Question(
          "q-1", 1, QuestionType.MultipleSelect, "Choose TWO",
          [], null, null, null, Marks: 2,
          Slots:
          [
            new ResponseSlot("slot-1", 1, new AnswerKey([new AcceptedAnswer("A", null, null)], null)),
            new ResponseSlot("slot-2", 2, new AnswerKey([new AcceptedAnswer("B", null, null)], null)),
          ]),
      ]),
    ]);

    var oneRight = DeterministicScorer.Score(section, Profile(),
      new Dictionary<string, string?> { ["slot-1"] = "A", ["slot-2"] = "C" });

    Assert.Equal(2, oneRight.MaxScore);
    Assert.Equal(1, oneRight.RawScore);
    Assert.False(oneRight.Questions.Single().IsCorrect);
    Assert.Equal(SlotOutcome.Correct, oneRight.Questions.Single().Slots![0].Status);
    Assert.Equal(SlotOutcome.Incorrect, oneRight.Questions.Single().Slots![1].Status);
  }

  [Fact]
  public void A_blank_slot_is_unanswered_not_incorrect()
  {
    var section = SlottedSection("paper");
    var score = DeterministicScorer.Score(section, Profile(),
      new Dictionary<string, string?> { ["slot-1"] = "paper" });

    Assert.Equal(SlotOutcome.Unanswered, score.Questions.Single().Slots![1].Status);
  }

  [Fact]
  public void Accepted_variants_work_per_slot()
  {
    var section = new Section(ExamModule.Reading, 1,
    [
      new SectionPart(1, "passage", "P", null, null, null, null, null, null, null, null,
      [
        new Question("q-1", 1, QuestionType.ShortAnswer, "Q", [], null, null, null, Marks: 1,
          Slots:
          [
            new ResponseSlot("slot-1", 1, new AnswerKey(
              [new AcceptedAnswer("20th July", null, null), new AcceptedAnswer("July 20", null, null)],
              null)),
          ]),
      ]),
    ]);

    var score = DeterministicScorer.Score(section, Profile(),
      new Dictionary<string, string?> { ["slot-1"] = "july 20" });

    Assert.Equal(1, score.RawScore);
    Assert.Equal("20th July", score.Questions.Single().Slots!.Single().CorrectAnswer);
  }

  [Fact]
  public void Word_limit_violation_marks_a_slot_wrong()
  {
    var section = new Section(ExamModule.Reading, 1,
    [
      new SectionPart(1, "passage", "P", null, null, null, null, null, null, null, null,
      [
        new Question("q-1", 1, QuestionType.ShortAnswer, "Q", [], MaxWords: 2, null, null, Marks: 1,
          Slots:
          [
            new ResponseSlot("slot-1", 1, new AnswerKey([new AcceptedAnswer("pulp paper", null, null)], null)),
          ]),
      ]),
    ]);

    var score = DeterministicScorer.Score(section, Profile(),
      new Dictionary<string, string?> { ["slot-1"] = "the pulp paper" });

    Assert.Equal(0, score.RawScore);
    Assert.Equal(SlotOutcome.Incorrect, score.Questions.Single().Slots!.Single().Status);
  }

  [Fact]
  public void Practice_scope_without_band_omits_the_band_entirely()
  {
    var section = Reading(2);
    var answers = new Dictionary<string, string?> { ["r-1"] = "answer1" };
    var context = new DeterministicScoringContext(new HashSet<string>(["r-1"]), IncludeBand: false);

    var score = DeterministicScorer.Score(section, Profile(), answers, context);

    Assert.Equal(1, score.RawScore);
    Assert.Null(score.Band);
  }

  private static Section SlottedSection(string accepted) => new(ExamModule.Reading, 1,
  [
    new SectionPart(1, "passage", "P", null, null, null, null, null, null, null, null,
    [
      new Question("q-1", 1, QuestionType.ShortAnswer, "Q", [], null, null, null, Marks: 2,
        Slots:
        [
          new ResponseSlot("slot-1", 1, new AnswerKey([new AcceptedAnswer(accepted, null, null)], null)),
          new ResponseSlot("slot-2", 2, new AnswerKey([new AcceptedAnswer("other", null, null)], null)),
        ]),
    ]),
  ]);
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
