using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

/// <summary>
/// The exact strings the learner app sends, marked by the real marker.
///
/// <b>This suite exists because both sides were tested and both sides passed
/// while the product marked correct answers wrong.</b> `QuestionInput.tsx` had
/// tests proving it serialises a multi-select pick as <c>"A|C"</c>.
/// `AnswerMatcher` had tests proving it marks <c>"A,C"</c> correct. Neither
/// test was wrong; nobody owned the sentence in between.
///
/// So every literal below is copied from the client, not invented here. If the
/// client changes how it spells an answer, the change has to land here too —
/// which is the whole point: the format is a contract, and a contract with one
/// signatory is a preference.
///
/// The client's spellings, from `apps/web/src/features/exam/QuestionInput.tsx`:
/// <list type="bullet">
/// <item>multiple-choice, true/false, yes/no — the option key or label verbatim</item>
/// <item>multiple-select — picks sorted and joined with <c>|</c></item>
/// <item>matching and labelling — the chosen option key alone, e.g. <c>iv</c></item>
/// <item>completion, short-answer — what the learner typed</item>
/// </list>
/// </summary>
public sealed class AnswerContractTests
{
    private static readonly AnswerMatchingRules Rules = AnswerMatchingRules.Default;

    private static Question Question(QuestionType type, AnswerKey key) =>
        new("q1", 1, type, "prompt", [], null, key, null);

    private static AnswerKey Set(params string[] all) =>
        new([new AcceptedAnswer(null, all, null)], null);

    private static AnswerKey Single(params string[] accepted) =>
        new([.. accepted.Select(a => new AcceptedAnswer(a, null, null))], null);

    // ── Multiple select ──────────────────────────────────────────────────

    /// <summary>
    /// <b>The bug, stated as a test.</b> A learner ticking the two correct
    /// boxes on a "Choose TWO letters" question scored zero, on every such
    /// question, in every sitting. Listening in Exam 1 has three of them
    /// carrying seven marks between them.
    /// </summary>
    [Theory]
    [InlineData("A|D")]          // exactly what the client sends
    [InlineData("A,D")]          // and the spellings a hand-written call might use
    [InlineData("A, D")]
    [InlineData("A D")]
    [InlineData("D|A")]          // order is not part of the answer
    public void A_multi_select_pick_is_marked_however_the_caller_spelled_the_set(string submitted)
    {
        var question = Question(QuestionType.MultipleSelect, Set("A", "D"));

        Assert.True(AnswerMatcher.IsCorrect(question, submitted, Rules));
    }

    [Theory]
    [InlineData("A|B")]          // right count, wrong member
    [InlineData("A")]            // short — a set that must all be present
    [InlineData("A|D|E")]        // and nothing extra
    public void A_multi_select_set_that_is_not_the_key_is_still_wrong(string submitted)
    {
        var question = Question(QuestionType.MultipleSelect, Set("A", "D"));

        Assert.False(AnswerMatcher.IsCorrect(question, submitted, Rules));
    }

    // ── Matching ─────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The second half of the same bug, and the more expensive half.</b>
    /// "Matching" covers two different questions that happen to share a name:
    /// choosing a heading for a paragraph — where the answer is one key — and
    /// pairing two lists. The marker assumed the second and refused everything
    /// shaped like the first, so all six matching questions in Exam 1's Reading
    /// were marked wrong no matter what the learner chose.
    ///
    /// The fix is not a third question type. It is to stop asking the question
    /// type how an answer is shaped and ask <b>the answer key</b>, which has
    /// carried the shape all along.
    /// </summary>
    [Fact]
    public void Choosing_a_heading_is_marked_against_a_key_that_holds_one_value()
    {
        var question = Question(QuestionType.Matching, Single("iv"));

        Assert.True(AnswerMatcher.IsCorrect(question, "iv", Rules));
        Assert.False(AnswerMatcher.IsCorrect(question, "ix", Rules));
    }

    [Fact]
    public void Pair_matching_still_needs_both_sides_when_the_key_holds_a_pair()
    {
        // A key that names two sides is asking for two sides. Accepting half of
        // it would make the two shapes indistinguishable, which is how they got
        // conflated in the first place.
        var question = Question(
            QuestionType.Matching,
            new AnswerKey([new AcceptedAnswer(null, null, ("Paris", "France"))], null));

        Assert.True(AnswerMatcher.IsCorrect(question, "Paris:France", Rules));
        Assert.True(AnswerMatcher.IsCorrect(question, "Paris=France", Rules));
        Assert.False(AnswerMatcher.IsCorrect(question, "Paris", Rules));
        Assert.False(AnswerMatcher.IsCorrect(question, "Paris:Germany", Rules));
    }

    [Fact]
    public void Labelling_is_marked_the_same_way_because_the_client_renders_it_the_same_way()
    {
        // `QuestionInput` renders `matching` and `labelling` through one branch
        // and sends one option key for both. A marker that treated them
        // differently would be disagreeing with the only thing that produces
        // these answers.
        var question = Question(QuestionType.Labelling, Single("C"));

        Assert.True(AnswerMatcher.IsCorrect(question, "C", Rules));
    }

    // ── The shapes that already worked, pinned so they keep working ──────

    [Theory]
    [InlineData(QuestionType.MultipleChoice, "B", true)]
    [InlineData(QuestionType.MultipleChoice, "C", false)]
    [InlineData(QuestionType.TrueFalseNotGiven, "TRUE", true)]
    [InlineData(QuestionType.TrueFalseNotGiven, "true", true)]     // case folded
    [InlineData(QuestionType.TrueFalseNotGiven, "FALSE", false)]
    public void The_single_value_types_are_unchanged(
        QuestionType type, string submitted, bool expected)
    {
        var key = type == QuestionType.MultipleChoice ? Single("B") : Single("TRUE");

        Assert.Equal(expected, AnswerMatcher.IsCorrect(Question(type, key), submitted, Rules));
    }

    [Fact]
    public void A_typed_answer_is_still_matched_on_its_text()
    {
        var question = Question(QuestionType.ShortAnswer, Single("35,000", "35000"));

        Assert.True(AnswerMatcher.IsCorrect(question, "35000", Rules));
        Assert.True(AnswerMatcher.IsCorrect(question, "35,000", Rules));
    }
}
