using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

public sealed class ParsedExamCandidateTests
{
    private static readonly UserId Reviewer = UserId.New();
    private static readonly ParsedSourceProvenance Source = new("exam.pdf", 1, "Reading", null);

    [Fact]
    public void Unclassified_candidate_cannot_be_confirmed()
    {
        var candidate = CreateCandidate(ParsedExamClassification.Unclassified);

        var action = () => candidate.Confirm(Reviewer, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(action);
        Assert.Equal(ParsedCandidateStatus.PendingReview, candidate.Status);
    }

    [Fact]
    public void Reviewer_can_correct_classification_and_confirmation_is_idempotent()
    {
        var candidate = CreateCandidate(ParsedExamClassification.NeedsReview);
        candidate.CorrectClassification(Reviewer, ParsedExamClassification.Reading, DateTimeOffset.UtcNow);

        candidate.Confirm(Reviewer, DateTimeOffset.UtcNow);
        candidate.Confirm(Reviewer, DateTimeOffset.UtcNow);

        Assert.Equal(ParsedCandidateStatus.Confirmed, candidate.Status);
        Assert.Equal(ParsedExamClassification.Reading, candidate.Classification);
        Assert.Single(candidate.Corrections);
    }

    [Fact]
    public void Confirmed_candidate_cannot_be_corrected_or_rejected()
    {
        var candidate = CreateCandidate(ParsedExamClassification.Listening);
        candidate.Confirm(Reviewer, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => candidate.CorrectTitle(Reviewer, "Changed", DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => candidate.Reject(Reviewer, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Candidate_with_empty_part_cannot_be_confirmed()
    {
        var candidate = CreateCandidate(ParsedExamClassification.Writing, includePart: false);

        Assert.Throws<InvalidOperationException>(() => candidate.Confirm(Reviewer, DateTimeOffset.UtcNow));
    }

    private static ParsedExamCandidate CreateCandidate(
        ParsedExamClassification classification, bool includePart = true)
    {
        var question = new ParsedQuestionCandidate(
            "q1", 1, QuestionType.MultipleChoice, "Choose",
            [new ParsedQuestionOptionCandidate("A", "Answer")],
            new ParsedAnswerKeyCandidate(["A"], null), Source);
        var part = new ParsedPartCandidate("p1", 1, "Part 1", null,
            [question], Source);
        var module = new ParsedModuleCandidate(
            classification switch
            {
                ParsedExamClassification.Unclassified => null,
                ParsedExamClassification.Listening => ExamModule.Listening,
                ParsedExamClassification.Writing => ExamModule.Writing,
                ParsedExamClassification.Speaking => ExamModule.Speaking,
                _ => ExamModule.Reading,
            },
            classification, 0.8m, includePart ? [part] : [], Source);

        return ParsedExamCandidate.Create("candidate-1", "package-1", "Mock exam",
            classification, 0.8m, [module], [Source]);
    }
}
