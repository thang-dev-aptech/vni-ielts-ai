using Vni.Ielts.Domain.Dictation;

namespace Vni.Ielts.Domain.Tests.Dictation;

/// <summary>
/// What the learner is told they got wrong.
///
/// The alignment is the whole value of this class. A position-by-position
/// comparison is trivial to write and useless to read: drop one article at the
/// start and every word after it is reported wrong, which tells someone they
/// failed without telling them anything they could act on.
/// </summary>
public sealed class DictationComparerTests
{
    private const string Sentence = "The library opens at half past eight every weekday morning.";

    [Fact]
    public void An_exact_transcription_is_perfect()
    {
        var result = DictationComparer.Compare(Sentence, Sentence);

        Assert.True(result.IsPerfect);
        Assert.Equal(10, result.Correct);
        Assert.All(result.Words, w => Assert.Equal(WordVerdict.Correct, w.Verdict));
    }

    [Fact]
    public void Punctuation_and_case_are_not_what_dictation_tests()
    {
        // Marking someone wrong for a missing full stop teaches them nothing
        // about listening, which is the only thing this exercise measures.
        var result = DictationComparer.Compare(
            Sentence, "the library opens at half past eight every weekday morning");

        Assert.True(result.IsPerfect);
    }

    [Fact]
    public void A_misheard_word_is_one_wrong_word_not_a_deletion_and_an_insertion()
    {
        var result = DictationComparer.Compare(
            Sentence, "The library opens at half past nine every weekday morning.");

        var wrong = Assert.Single(result.Words, w => w.Verdict == WordVerdict.Wrong);
        Assert.Equal("eight", wrong.Expected);
        Assert.Equal("nine", wrong.Typed);

        Assert.DoesNotContain(result.Words, w => w.Verdict is WordVerdict.Missing or WordVerdict.Extra);
        Assert.Equal(9, result.Correct);
    }

    [Fact]
    public void A_dropped_word_is_reported_as_one_dropped_word()
    {
        var result = DictationComparer.Compare(
            Sentence, "The library opens at half past eight every morning.");

        var missing = Assert.Single(result.Words, w => w.Verdict == WordVerdict.Missing);
        Assert.Equal("weekday", missing.Expected);
        Assert.Equal(9, result.Correct);
    }

    [Fact]
    public void A_word_dropped_at_the_start_does_not_mark_the_rest_wrong()
    {
        // The failure a position-by-position comparison produces, and the
        // reason this class exists.
        var result = DictationComparer.Compare(
            Sentence, "library opens at half past eight every weekday morning.");

        Assert.Equal(9, result.Correct);
        Assert.Single(result.Words, w => w.Verdict == WordVerdict.Missing);
    }

    [Fact]
    public void A_word_that_was_never_said_is_extra_rather_than_wrong()
    {
        var result = DictationComparer.Compare(
            Sentence, "The public library opens at half past eight every weekday morning.");

        var extra = Assert.Single(result.Words, w => w.Verdict == WordVerdict.Extra);
        Assert.Equal("public", extra.Typed);
        Assert.Null(extra.Expected);

        // Every real word still lines up, so the score is not punished twice.
        Assert.Equal(10, result.Correct);
        Assert.False(result.IsPerfect);
    }

    [Fact]
    public void An_empty_attempt_reports_every_word_as_missing()
    {
        // A learner who heard nothing is a legitimate case, not an error.
        var result = DictationComparer.Compare(Sentence, "");

        Assert.Equal(0, result.Correct);
        Assert.Equal(10, result.Total);
        Assert.All(result.Words, w => Assert.Equal(WordVerdict.Missing, w.Verdict));
    }

    [Fact]
    public void Inner_punctuation_survives_normalisation()
    {
        // "don't" and "well-known" are single words a learner either heard or
        // did not; splitting them would score one mishearing as two.
        var result = DictationComparer.Compare(
            "She doesn't like well-known authors.", "She doesn't like well-known authors");

        Assert.True(result.IsPerfect);
        Assert.Equal(5, result.Total);
    }
}
