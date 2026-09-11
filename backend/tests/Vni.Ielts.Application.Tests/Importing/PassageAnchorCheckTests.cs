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

    /// <summary>
    /// <b>Reported via <see cref="AnchorReport.MissingAnswerIssues"/>, not
    /// <see cref="AnchorReport.Findings"/>, since 2026-09-10.</b> It still
    /// blocks approval, but a reviewer may clear it with a written reason:
    /// "appears in the passage" depends on normalisation choices that do not
    /// round-trip, so a genuinely absent answer cannot be told apart from one
    /// the matcher cannot see. See <see cref="AnchorMissingAnswerIssue"/>.
    /// </summary>
    [Fact]
    public void A_completion_answer_absent_from_the_passage_is_reported_as_a_clearable_issue()
    {
        var report = PassageAnchorCheck.Inspect(Package(Passage,
            Completion(1, "copper roof")));

        Assert.Empty(report.Findings);
        var issue = Assert.Single(report.MissingAnswerIssues);
        Assert.Contains("question 1", issue.Message, StringComparison.Ordinal);
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

    /// <summary>
    /// A Listening part carries its answers in the audio. Its <c>body</c> is
    /// the printed rubric or a one-line context blurb, and nothing a learner
    /// hears is in it.
    ///
    /// <b>The false block this pins.</b> Preferring <c>body</c> regardless of
    /// <c>part.kind</c> measured every anchorable Listening group against that
    /// blurb, anchored nothing, and raised
    /// <see cref="PassageAnchorCheck.PassageMismatchCode"/> — severity
    /// "error", no override, permanently unapprovable. Measured on this
    /// repository's own VOL 9 packages: 12 of 24 Listening parts carry such a
    /// body and none carries a transcript, so this fired on nine correct
    /// groups in that corpus alone. A missing transcript is a warning under
    /// `P-19`, never a reason to refuse a package — so the part is skipped and
    /// nothing at all is reported for it.
    /// </summary>
    [Fact]
    public void A_recording_with_no_transcript_is_skipped_rather_than_measured_against_its_blurb()
    {
        var report = PassageAnchorCheck.Inspect(Recording(
            body: "HEALTH ON THE NIGHT SHIFT",
            transcript: null,
            Completion(1, "internal clock"),
            Completion(2, "one hour")));

        Assert.Empty(report.Findings);
        Assert.Empty(report.OrderIssues);
        Assert.Empty(report.Anchors);
    }

    /// <summary>
    /// The other half of the same choice: a recording that <i>did</i> ship a
    /// transcript is still checked, and against the transcript rather than
    /// whatever its body says.
    /// </summary>
    [Fact]
    public void A_recording_with_a_transcript_is_anchored_against_the_transcript()
    {
        var report = PassageAnchorCheck.Inspect(Recording(
            body: "HEALTH ON THE NIGHT SHIFT",
            transcript: "Your internal clock takes about a week to adjust.",
            Completion(1, "internal clock"),
            Completion(2, "a week")));

        Assert.Empty(report.Findings);
        Assert.All(report.Anchors, a => Assert.True(a.Anchored));
    }

    /// <summary>
    /// <b>A key alternative is an answer, not decoration.</b> Real keys in
    /// this repository land as one comma-joined string — <c>"one hour,1
    /// hour"</c>, <c>"5 years,five years"</c>, <c>"color coding, color coding
    /// system"</c> all appear in the VOL 9 packages. Reading only
    /// <c>accepted[0]</c> raised a non-overridable
    /// <see cref="PassageAnchorCheck.NotInPassageCode"/> against a passage
    /// that prints the other form — a correct paper permanently refused.
    /// </summary>
    [Fact]
    public void An_answer_anchored_by_its_second_alternative_raises_nothing()
    {
        const string passage = "The journey from the station takes 1 hour on foot.";

        var report = PassageAnchorCheck.Inspect(Package(passage,
            Completion(1, "one hour,1 hour")));

        Assert.Empty(report.Findings);
        Assert.True(Assert.Single(report.Anchors).Anchored);
    }

    /// <summary>
    /// An alternative listed as its own <c>accepted</c> entry, rather than
    /// comma-joined inside one — the same fix, the other spelling of it.
    /// </summary>
    [Fact]
    public void An_answer_anchored_by_a_later_accepted_entry_raises_nothing()
    {
        const string passage = "The journey from the station takes 1 hour on foot.";

        var report = PassageAnchorCheck.Inspect(Package(passage, """
            { "id": "q1", "order": 1, "type": "completion",
              "group": { "id": "g1" },
              "answerKey": { "accepted": [ "one hour", "1 hour" ] } }
            """));

        Assert.Empty(report.Findings);
        Assert.True(Assert.Single(report.Anchors).Anchored);
    }

    /// <summary>
    /// The fix must not blunt the check it lives in: an answer none of whose
    /// alternatives — whole or comma-split — is in the passage is still
    /// refused.
    /// </summary>
    [Fact]
    public void An_answer_no_alternative_of_which_is_present_is_still_reported()
    {
        var report = PassageAnchorCheck.Inspect(Package(Passage,
            Completion(1, "copper roof,bronze roof")));

        Assert.Empty(report.Findings);
        Assert.Single(report.MissingAnswerIssues);
    }

    /// <summary>
    /// The split between the two codes, pinned rather than assumed. 4a became
    /// clearable because the matcher cannot always see an answer that is
    /// present; 4c did not, because a whole group anchoring nothing against a
    /// passage that <i>is</i> present says the paper was parsed wrong, and
    /// reporting the individual misses would send an administrator to fix
    /// twelve answers that are all correct.
    /// </summary>
    [Fact]
    public void A_whole_group_mismatch_stays_a_blocking_finding_with_no_override()
    {
        var report = PassageAnchorCheck.Inspect(Package("An unrelated passage entirely.",
            Completion(1, "slate roof"),
            Completion(2, "stained glass")));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(PassageAnchorCheck.PassageMismatchCode, finding.Code);
        Assert.Equal("error", finding.Severity);

        // Not also reported as clearable: one group, one verdict, and the
        // clearable one would let it be waved through.
        Assert.Empty(report.MissingAnswerIssues);
    }

    private static string Completion(int order, string answer) =>
        $$"""
          { "id": "q{{order}}", "order": {{order}}, "type": "completion",
            "group": { "id": "g1" },
            "answerKey": { "accepted": [ "{{answer}}" ] } }
          """;

    /// <summary>A Listening part: <c>kind: "recording"</c>, with or without a transcript.</summary>
    private static string Recording(string body, string? transcript, params string[] questions) =>
        $$"""
          { "formatVersion": "2.0",
            "sections": [ { "module": "listening", "order": 1, "parts": [ { "order": 1,
              "kind": "recording", "body": "{{body}}",
              {{(transcript is null ? string.Empty : $"\"transcript\": \"{transcript}\",")}}
              "questions": [ {{string.Join(",", questions)}} ] } ] } ] }
          """;

    private static string Package(string body, params string[] questions) =>
        $$"""
          { "formatVersion": "2.0",
            "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
              "kind": "passage", "body": "{{body}}",
              "questions": [ {{string.Join(",", questions)}} ] } ] } ] }
          """;
}
