using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Assessment;

public sealed class WritingAdmissionTests
{
    private static readonly Rubric Task2 = Rubric.Create(
        "vni-writing-v2", ExamModule.Writing, CriterionKeys.WritingTask2, "vni-authored");

    [Fact]
    public void Empty_submission_is_band_zero_on_every_criterion()
    {
        var result = WritingAdmission.Assess("   ", "Describe the chart.");

        var terminal = Assert.IsType<WritingAdmission.Result.Terminal>(result);
        Assert.Equal(0m, terminal.Band.Value);
        Assert.Equal(WritingAdmission.Reason.Empty, terminal.Reason);

        var marking = WritingAdmission.ToMarking(Task2, 2, terminal);
        Assert.Equal(0m, marking.Band.Value);
        Assert.All(marking.Criteria, c => Assert.Equal(0m, c.Band.Value));
        Assert.Contains("empty-or-not-english", marking.Advisories!);
    }

    [Fact]
    public void Twenty_words_or_fewer_after_discounting_the_prompt_is_band_one()
    {
        var prompt = "Summarise the information by selecting and reporting the main features.";
        var essay = prompt + " The bars go up.";

        var result = WritingAdmission.Assess(essay, prompt);

        var terminal = Assert.IsType<WritingAdmission.Result.Terminal>(result);
        Assert.Equal(1m, terminal.Band.Value);
        Assert.Equal(WritingAdmission.Reason.UnderLength, terminal.Reason);
        Assert.True(terminal.WordCount <= 20);
        Assert.NotEmpty(terminal.CopiedSpans);
    }

    [Fact]
    public void Copied_prompt_text_is_discounted_before_the_word_count()
    {
        var prompt = "Summarise the information by selecting and reporting the main features.";
        var (remaining, copied) = WritingAdmission.DiscountCopiedPrompt(
            prompt + " Extra words here.", prompt);

        Assert.Contains(copied, s => s.Contains("Summarise", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Summarise", remaining, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, WritingAdmission.CountWords(remaining));
    }

    [Fact]
    public void A_vietnamese_script_is_not_english()
    {
        var result = WritingAdmission.Assess(
            "Bài viết này hoàn toàn bằng tiếng Việt và không có câu tiếng Anh nào cả.",
            "Discuss both views.");

        var terminal = Assert.IsType<WritingAdmission.Result.Terminal>(result);
        Assert.Equal(0m, terminal.Band.Value);
        Assert.Equal(WritingAdmission.Reason.NotEnglish, terminal.Reason);
    }

    [Fact]
    public void An_english_essay_past_the_floor_proceeds()
    {
        var essay = string.Join(
            ' ',
            Enumerable.Repeat("The chart shows a steady rise in coffee consumption over the decade.", 5));

        var result = WritingAdmission.Assess(essay, "Describe the chart.");

        var proceed = Assert.IsType<WritingAdmission.Result.Proceed>(result);
        Assert.True(proceed.WordCount > 20);
    }

    [Fact]
    public void Bullet_notes_are_detected_as_not_prose()
    {
        var notes = """
            - first point about the chart
            - second point about the chart
            - third point about the chart
            - fourth point about the chart
            """;

        Assert.True(WritingAdmission.LooksLikeNotes(notes));
        Assert.False(WritingAdmission.LooksLikeNotes(
            "The chart shows a rise. Tea fell. Coffee recovered after 2015."));
    }
}
