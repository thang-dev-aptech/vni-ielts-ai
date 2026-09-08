using Microsoft.Extensions.Configuration;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

public sealed class ConfirmedCandidatePackageBuilderTests
{
    private static readonly ExamPackageReader Reader = ExamPackageReader.FromSchemaFile(
        ContentImportSchemaPath.Resolve(new ConfigurationBuilder().Build()));

    private readonly ConfirmedCandidatePackageBuilder _sut = new();

    [Fact]
    public void Builds_valid_canonical_exam_for_reading_candidate_that_passes_ExamPackageReader()
    {
        var candidate = CreateReadingCandidate();
        var completion = new CandidateCompletionData(
            ExamVariant.Academic,
            new CandidateTimingProfile(new Dictionary<ExamModule, CandidateSectionTiming>
            {
                [ExamModule.Reading] = new(3600)
            }),
            new CandidateScoringProfile(new Dictionary<ExamModule, IReadOnlyList<CandidateBandBoundary>>
            {
                [ExamModule.Reading] = [new(0, 0m), new(1, 4.0m), new(2, 6.5m), new(3, 9.0m)]
            }));

        var definitionId = new ExamDefinitionId("def-1");
        var json = _sut.BuildCanonicalJson(candidate, completion, definitionId, 1);

        var result = Reader.Read(json, definitionId, 1);

        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => $"{f.Path}: {f.Message}")));
        Assert.NotNull(result.Version);
        Assert.Equal("Reading Practice Test 1", result.Version.Title);
        Assert.Equal(ExamVariant.Academic, result.Version.Variant);
        Assert.Equal(ExamVersionStatus.Draft, result.Version.Status);
        Assert.Single(result.Version.Sections);
        Assert.Equal(3, result.Version.Sections[0].Questions.Count());
    }

    [Fact]
    public void Builds_multi_select_question_with_all_accepted_answers()
    {
        var source = new ParsedSourceProvenance("exam.docx", 1, "Reading", null);
        var qMulti = new ParsedQuestionCandidate(
            "q-multi",
            1,
            QuestionType.MultipleSelect,
            "Choose TWO letters.",
            [
                new ParsedQuestionOptionCandidate("A", "Option A"),
                new ParsedQuestionOptionCandidate("B", "Option B"),
                new ParsedQuestionOptionCandidate("C", "Option C"),
            ],
            new ParsedAnswerKeyCandidate(["A", "B"], null),
            source);

        var part = new ParsedPartCandidate("p1", 1, "Passage 1", "Body text", [qMulti], source);
        var module = new ParsedModuleCandidate(ExamModule.Reading, ParsedExamClassification.Reading, 0.9m, [part], source);
        var candidate = ParsedExamCandidate.Create("c1", "pkg-1", "Multi-Select Test", ParsedExamClassification.Reading, 0.9m, [module], [source]);

        var completion = new CandidateCompletionData(
            ExamVariant.Academic,
            new CandidateTimingProfile(new Dictionary<ExamModule, CandidateSectionTiming>
            {
                [ExamModule.Reading] = new(3600)
            }),
            new CandidateScoringProfile(new Dictionary<ExamModule, IReadOnlyList<CandidateBandBoundary>>
            {
                [ExamModule.Reading] = [new(0, 0m), new(1, 5.0m), new(2, 9.0m)]
            }));

        var definitionId = new ExamDefinitionId("def-multi");
        var json = _sut.BuildCanonicalJson(candidate, completion, definitionId, 1);

        var result = Reader.Read(json, definitionId, 1);

        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => $"{f.Path}: {f.Message}")));
        Assert.NotNull(result.Version);
        var question = result.Version.Sections[0].Questions.First();
        Assert.Equal(QuestionType.MultipleSelect, question.Type);
        Assert.Equal(2, question.Marks);
        Assert.NotNull(question.AnswerKey);
        Assert.Equal(["A", "B"], question.AnswerKey.Accepted[0].All);
    }

    [Fact]
    public void Incomplete_table_coverage_is_rejected_by_ExamPackageReader()
    {
        var candidate = CreateReadingCandidate();
        // Missing 0 threshold fails coverage
        var completion = new CandidateCompletionData(
            ExamVariant.Academic,
            new CandidateTimingProfile(new Dictionary<ExamModule, CandidateSectionTiming>
            {
                [ExamModule.Reading] = new(3600)
            }),
            new CandidateScoringProfile(new Dictionary<ExamModule, IReadOnlyList<CandidateBandBoundary>>
            {
                [ExamModule.Reading] = [new(1, 4.0m)]
            }));

        var definitionId = new ExamDefinitionId("def-1");
        var json = _sut.BuildCanonicalJson(candidate, completion, definitionId, 1);

        var result = Reader.Read(json, definitionId, 1);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "SCORING_TABLE_INCOMPLETE");
    }

    [Fact]
    public void Part_details_with_audio_and_image_assets_are_serialized_correctly()
    {
        var source = new ParsedSourceProvenance("exam.pdf", 1, "Listening", null);
        var q = new ParsedQuestionCandidate(
            "q1", 1, QuestionType.Completion, "Complete the notes.", [], new ParsedAnswerKeyCandidate(["London"], null), source);
        var part = new ParsedPartCandidate("p1", 1, "Section 1", null, [q], source);
        var module = new ParsedModuleCandidate(ExamModule.Listening, ParsedExamClassification.Listening, 0.9m, [part], source);
        var candidate = ParsedExamCandidate.Create("c2", "pkg-1", "Listening Test", ParsedExamClassification.Listening, 0.9m, [module], [source]);

        var completion = new CandidateCompletionData(
            ExamVariant.Academic,
            new CandidateTimingProfile(new Dictionary<ExamModule, CandidateSectionTiming>
            {
                [ExamModule.Listening] = new(1800, 600)
            }),
            new CandidateScoringProfile(new Dictionary<ExamModule, IReadOnlyList<CandidateBandBoundary>>
            {
                [ExamModule.Listening] = [new(0, 0m), new(1, 9.0m)]
            }),
            [
                new CandidatePartCompletion(1, AudioAssetRef: "assets/listening/part1.mp3", ImageAssetRef: "assets/images/map.png")
            ]);

        var definitionId = new ExamDefinitionId("def-listening");
        var json = _sut.BuildCanonicalJson(candidate, completion, definitionId, 1);

        var result = Reader.Read(json, definitionId, 1);

        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => $"{f.Path}: {f.Message}")));
        Assert.NotNull(result.Version);
        var sectionPart = result.Version.Sections[0].Parts[0];
        Assert.Equal("assets/listening/part1.mp3", sectionPart.AudioKey);
        Assert.Equal("assets/images/map.png", sectionPart.ImageKey);
    }

    private static ParsedExamCandidate CreateReadingCandidate()
    {
        var source = new ParsedSourceProvenance("exam.docx", 1, "Reading", null);
        var q1 = new ParsedQuestionCandidate(
            "q1", 1, QuestionType.MultipleChoice, "Question 1 prompt",
            [new ParsedQuestionOptionCandidate("A", "First"), new ParsedQuestionOptionCandidate("B", "Second")],
            new ParsedAnswerKeyCandidate(["A"], null), source);
        var q2 = new ParsedQuestionCandidate(
            "q2", 2, QuestionType.MultipleChoice, "Question 2 prompt",
            [new ParsedQuestionOptionCandidate("A", "Third"), new ParsedQuestionOptionCandidate("B", "Fourth")],
            new ParsedAnswerKeyCandidate(["B"], null), source);
        var q3 = new ParsedQuestionCandidate(
            "q3", 3, QuestionType.TrueFalseNotGiven, "Question 3 prompt",
            [],
            new ParsedAnswerKeyCandidate(["TRUE"], null), source);

        var part = new ParsedPartCandidate("p1", 1, "Passage 1 Title", "Passage body text...", [q1, q2, q3], source);
        var module = new ParsedModuleCandidate(ExamModule.Reading, ParsedExamClassification.Reading, 0.95m, [part], source);
        return ParsedExamCandidate.Create("cand-read", "pkg-read", "Reading Practice Test 1", ParsedExamClassification.Reading, 0.95m, [module], [source]);
    }
}
