using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

public sealed class AnswerMatcherTests
{
    private static readonly AnswerMatchingRules Default = AnswerMatchingRules.Default;

    private static Question Short(string id, int? maxWords, params string[] accepted) => new(
        id, 1, QuestionType.ShortAnswer, "prompt", [], maxWords,
        new AnswerKey([.. accepted.Select(a => new AcceptedAnswer(a, null, null))], null));

    [Theory]
    [InlineData("paper")]
    [InlineData("Paper")]
    [InlineData("PAPER")]
    [InlineData("  paper  ")]
    public void Case_and_surrounding_whitespace_do_not_change_the_mark(string submitted) =>
        Assert.True(AnswerMatcher.IsCorrect(Short("q1", null, "paper"), submitted, Default));

    [Fact]
    public void Any_listed_alternate_is_accepted()
    {
        // "20th July" / "July 20" / "20 July" are the same answer to a marker.
        var q = Short("q1", null, "20th July", "July 20", "20 July");
        foreach (var form in new[] { "20th july", "JULY 20", " 20 July " })
            Assert.True(AnswerMatcher.IsCorrect(q, form, Default));
    }

    [Fact]
    public void Doubled_inner_spaces_are_collapsed()
    {
        // A learner double-tapping the space bar has not given a wrong answer.
        Assert.True(AnswerMatcher.IsCorrect(Short("q1", null, "pulp paper"), "pulp  paper", Default));
    }

    [Theory]
    [InlineData("1,000")]
    [InlineData("1000")]
    [InlineData("1 000")]
    public void Thousands_separators_do_not_change_the_mark(string submitted) =>
        Assert.True(AnswerMatcher.IsCorrect(Short("q1", null, "1000"), submitted, Default));

    [Fact]
    public void An_over_length_answer_is_wrong_even_when_it_contains_the_right_words()
    {
        // "NO MORE THAN TWO WORDS" is a scored condition, not a hint. Being
        // lenient here inflates scores relative to the real exam.
        var q = Short("q1", maxWords: 2, "pulp paper");
        Assert.True(AnswerMatcher.IsCorrect(q, "pulp paper", Default));
        Assert.False(AnswerMatcher.IsCorrect(q, "the pulp paper", Default));
    }

    [Fact]
    public void Misspelling_is_wrong_by_default()
    {
        // IELTS penalises misspelling. Tolerance is a per-exam decision, not a
        // default kindness.
        Assert.False(AnswerMatcher.IsCorrect(Short("q1", null, "parchment"), "parchmant", Default));
    }

    [Fact]
    public void Case_sensitivity_can_be_turned_on_per_exam()
    {
        var strict = Default with { CaseSensitive = true };
        var q = Short("q1", null, "Paris");
        Assert.True(AnswerMatcher.IsCorrect(q, "Paris", strict));
        Assert.False(AnswerMatcher.IsCorrect(q, "paris", strict));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unanswered_question_scores_zero_rather_than_matching_anything(string? submitted) =>
        Assert.False(AnswerMatcher.IsCorrect(Short("q1", null, "paper"), submitted, Default));

    [Fact]
    public void A_question_with_no_answer_key_never_awards_a_mark()
    {
        // A missing key means a broken import, not a free mark for everyone.
        var q = new Question("q1", 1, QuestionType.ShortAnswer, "p", [], null, null);
        Assert.False(AnswerMatcher.IsCorrect(q, "anything", Default));
    }

    [Fact]
    public void Multiple_select_ignores_order_but_requires_the_exact_set()
    {
        var q = new Question("q1", 1, QuestionType.MultipleSelect, "p",
            [new QuestionOption("A", "a"), new QuestionOption("B", "b"), new QuestionOption("C", "c")],
            null, new AnswerKey([new AcceptedAnswer(null, ["A", "C"], null)], null));

        Assert.True(AnswerMatcher.IsCorrect(q, "A,C", Default));
        Assert.True(AnswerMatcher.IsCorrect(q, "C, A", Default));   // order is not meaning
        Assert.False(AnswerMatcher.IsCorrect(q, "A", Default));      // incomplete
        Assert.False(AnswerMatcher.IsCorrect(q, "A,B,C", Default));  // extra selection
    }

    [Fact]
    public void Matching_pairs_compare_both_sides()
    {
        var q = new Question("q1", 1, QuestionType.Matching, "p", [], null,
            new AnswerKey([new AcceptedAnswer(null, null, ("1", "C"))], null));

        Assert.True(AnswerMatcher.IsCorrect(q, "1:C", Default));
        Assert.True(AnswerMatcher.IsCorrect(q, "1 = c", Default));
        Assert.False(AnswerMatcher.IsCorrect(q, "1:D", Default));
        Assert.False(AnswerMatcher.IsCorrect(q, "2:C", Default));
        Assert.False(AnswerMatcher.IsCorrect(q, "C", Default));   // not a pair at all
    }

    [Fact]
    public void Decomposed_and_composed_diacritics_compare_equal()
    {
        // Exam content is English, but a learner typing on a Vietnamese IME is
        // not hypothetical, and the two byte sequences look identical on screen.
        var composed = "cà phê";
        var decomposed = composed.Normalize(System.Text.NormalizationForm.FormD);

        Assert.NotEqual(composed, decomposed);   // genuinely different strings
        Assert.True(AnswerMatcher.IsCorrect(Short("q1", null, composed), decomposed, Default));
    }
}
