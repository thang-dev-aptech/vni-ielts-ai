using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Application.Tests.Importing;

public sealed class PassageAnchorCheckTests
{
    private const string Passage =
        "The hall has a slate roof. Beneath it the timber frame was rebuilt in 1974 "
        + "after a fire, and the west window was replaced with stained glass.";

    [Fact]
    public void A_completion_answer_present_in_the_passage_is_anchored()
    {
        var report = PassageAnchorCheck.Inspect(Package(Passage,
            Completion(1, "slate roof")));

        Assert.Empty(report.Findings);
        Assert.True(Assert.Single(report.Anchors).Anchored);
    }

    [Fact]
    public void A_completion_answer_absent_from_the_passage_is_refused()
    {
        var report = PassageAnchorCheck.Inspect(Package(Passage,
            Completion(1, "copper roof")));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(PassageAnchorCheck.NotInPassageCode, finding.Code);
        Assert.Equal("error", finding.Severity);
        Assert.False(Assert.Single(report.Anchors).Anchored);
    }

    /// <summary>
    /// Case and spacing differ constantly between a key document and a
    /// passage. Reporting those as missing answers would bury the real ones.
    /// </summary>
    [Fact]
    public void Case_and_spacing_differences_do_not_count_as_absent()
    {
        var report = PassageAnchorCheck.Inspect(Package(Passage,
            Completion(1, "  SLATE   ROOF ")));

        Assert.Empty(report.Findings);
    }

    /// <summary>
    /// The shift this whole layer exists for. Answers 1 and 2 are each in the
    /// passage, and each is legal for its question type, so layers 1 to 3 see
    /// nothing. Only their order gives it away.
    ///
    /// <b>Reported via <see cref="AnchorReport.OrderIssues"/>, not
    /// <see cref="AnchorReport.Findings"/>.</b> Unlike containment and a
    /// whole-group mismatch, the order convention has rare genuine
    /// exceptions, so its result is a distinct type the caller files as a
    /// clearable <c>ImportReviewWarning</c> rather than a blocking
    /// <c>PackageFinding</c> — see <see cref="AnchorOrderIssue"/>'s remarks.
    /// </summary>
    [Fact]
    public void Answers_running_backwards_within_a_group_are_reported_at_the_shift()
    {
        var report = PassageAnchorCheck.Inspect(Package(Passage,
            Completion(1, "stained glass"),
            Completion(2, "slate roof")));

        Assert.Empty(report.Findings);
        var issue = Assert.Single(report.OrderIssues);
        Assert.Contains("2", issue.Message);
    }

    /// <summary>
    /// A word occurring several times must not fire the order check: some
    /// choice of occurrences may well be in order, and reporting on the first
    /// one found would make common words permanently suspicious.
    /// </summary>
    [Fact]
    public void A_repeated_word_does_not_produce_an_order_warning()
    {
        const string repeated = "The roof was replaced. Later the roof leaked again.";

        var report = PassageAnchorCheck.Inspect(Package(repeated,
            Completion(1, "roof"),
            Completion(2, "roof")));

        Assert.Empty(report.Findings);
        Assert.Empty(report.OrderIssues);
    }

    /// <summary>
    /// Nothing in the group anchored means the passage is wrong, not the key.
    /// The administrator's next action is completely different — re-parse the
    /// paper, do not touch the key — so the finding has to say which.
    /// </summary>
    [Fact]
    public void A_group_with_nothing_anchored_blames_the_paper_not_the_key()
    {
        var report = PassageAnchorCheck.Inspect(Package("An unrelated passage entirely.",
            Completion(1, "slate roof"),
            Completion(2, "stained glass")));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(PassageAnchorCheck.PassageMismatchCode, finding.Code);
        Assert.Contains("passage", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True/False/Not Given answers are not words from the passage. Searching
    /// for "TRUE" in a passage would report almost every one as absent.
    /// </summary>
    [Fact]
    public void Question_types_whose_answers_are_not_passage_words_are_skipped()
    {
        var report = PassageAnchorCheck.Inspect(Package(Passage, """
            { "id": "q1", "order": 1, "type": "true-false-notgiven",
              "group": { "id": "g1" }, "answerKey": { "accepted": [ "TRUE" ] } }
            """));

        Assert.Empty(report.Findings);
        Assert.Empty(report.Anchors);
    }

    private static string Completion(int order, string answer) =>
        $$"""
          { "id": "q{{order}}", "order": {{order}}, "type": "completion",
            "group": { "id": "g1" },
            "answerKey": { "accepted": [ "{{answer}}" ] } }
          """;

    private static string Package(string body, params string[] questions) =>
        $$"""
          { "formatVersion": "2.0",
            "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
              "kind": "passage", "body": "{{body}}",
              "questions": [ {{string.Join(",", questions)}} ] } ] } ] }
          """;
}
