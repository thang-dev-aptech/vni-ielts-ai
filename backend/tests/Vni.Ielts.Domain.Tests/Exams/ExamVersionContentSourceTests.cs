using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

public sealed class ExamVersionContentSourceTests
{
    [Fact]
    public void CreateDraft_keeps_an_explicit_content_source_id()
    {
        var source = new ContentSourceId("source-a");
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Paper", ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
                AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int> { [ExamModule.Reading] = 60 }, null, []),
            [new Section(ExamModule.Reading, 1, [])],
            contentSourceId: source);

        Assert.Equal("source-a", version.ContentSourceId?.Value);
    }

    [Fact]
    public void A_blank_cms_draft_has_no_content_source()
    {
        var version = ExamVersion.CreateBlankDraft(
            ExamDefinitionId.New(), 1, "Blank", ExamVariant.Academic, UserId.New());

        Assert.Null(version.ContentSourceId);
    }
}
