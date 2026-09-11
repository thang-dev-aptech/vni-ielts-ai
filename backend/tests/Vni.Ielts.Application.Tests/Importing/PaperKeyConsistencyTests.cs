using System.Text.Json.Nodes;
using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Application.Tests.Importing;

public sealed class PaperKeyConsistencyTests
{
    [Fact]
    public void A_two_mark_question_answered_with_one_letter_is_refused()
    {
        var package = Package("""
            { "id": "q1", "order": 1, "type": "multiple-select", "marks": 2,
              "answerKey": { "accepted": [ "A" ] } }
            """);

        var findings = PaperKeyConsistency.Inspect(package);

        var finding = Assert.Single(findings);
        Assert.Equal(PaperKeyConsistency.MarkCountCode, finding.Code);
        Assert.Equal("error", finding.Severity);
        Assert.Contains("1", finding.Message);
    }

    [Fact]
    public void A_two_mark_question_answered_with_two_letters_passes()
    {
        var package = Package("""
            { "id": "q1", "order": 1, "type": "multiple-select", "marks": 2,
              "answerKey": { "accepted": [ [ "A", "C" ] ] } }
            """);

        Assert.Empty(PaperKeyConsistency.Inspect(package));
    }

    [Fact]
    public void A_key_answer_over_the_papers_own_word_limit_is_refused()
    {
        var package = Package("""
            { "id": "q1", "order": 1, "type": "completion",
              "constraints": { "maxWords": 2 },
              "answerKey": { "accepted": [ "a very long answer" ] } }
            """);

        var finding = Assert.Single(PaperKeyConsistency.Inspect(package));
        Assert.Equal(PaperKeyConsistency.WordLimitCode, finding.Code);
    }

    /// <summary>
    /// Every alternative is one a learner may type, so every alternative has
    /// to fit. A second alternative over the limit is a trap: it is marked
    /// correct by the key and wrong by the word-limit rule.
    /// </summary>
    [Fact]
    public void Every_accepted_alternative_must_fit_the_word_limit()
    {
        var package = Package("""
            { "id": "q1", "order": 1, "type": "completion",
              "constraints": { "maxWords": 2 },
              "answerKey": { "accepted": [ "slate roof", "a roof made of slate" ] } }
            """);

        Assert.Single(PaperKeyConsistency.Inspect(package));
    }

    [Fact]
    public void A_no_reuse_group_that_reuses_a_label_is_refused()
    {
        var package = Package("""
            { "id": "q1", "order": 1, "type": "matching",
              "group": { "id": "g1", "eachLetterOnce": true },
              "answerKey": { "accepted": [ "B" ] } }
            """, """
            { "id": "q2", "order": 2, "type": "matching",
              "group": { "id": "g1", "eachLetterOnce": true },
              "answerKey": { "accepted": [ "B" ] } }
            """);

        var finding = Assert.Single(PaperKeyConsistency.Inspect(package));
        Assert.Equal(PaperKeyConsistency.LabelReuseCode, finding.Code);
        Assert.Contains("2", finding.Message);
    }

    /// <summary>
    /// Reuse is legal unless the paper says otherwise — "NB You may use any
    /// letter more than once". Refusing it by default would reject correct
    /// papers, which is the expensive kind of false alarm here.
    /// </summary>
    [Fact]
    public void A_group_without_the_no_reuse_flag_may_reuse_a_label()
    {
        var package = Package("""
            { "id": "q1", "order": 1, "type": "matching",
              "group": { "id": "g1" }, "answerKey": { "accepted": [ "B" ] } }
            """, """
            { "id": "q2", "order": 2, "type": "matching",
              "group": { "id": "g1" }, "answerKey": { "accepted": [ "B" ] } }
            """);

        Assert.Empty(PaperKeyConsistency.Inspect(package));
    }

    private static string Package(params string[] questions) =>
        $$"""
          { "formatVersion": "2.0",
            "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
              "kind": "passage", "questions": [ {{string.Join(",", questions)}} ] } ] } ] }
          """;
}
