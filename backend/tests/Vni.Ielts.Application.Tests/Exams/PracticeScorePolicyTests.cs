using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Practice;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Tests.Exams;

public sealed class PracticeScorePolicyTests
{
    [Fact]
    public void Part_scope_scores_only_selected_questions_and_omits_band()
    {
        var version = Version();
        var unit = PracticeUnitProjection.From(version)
            .First(u => u.Scope == PracticeScope.Part && u.Module == ExamModule.Reading);
        var session = StartSession(unit);

        var context = PracticeScorePolicy.ScoringContext(session, version);

        Assert.False(context.IncludeBand);
        Assert.NotNull(context.QuestionIds);
        Assert.Single(context.QuestionIds!);
        Assert.Equal("reading-1", context.QuestionIds!.Single());
    }

    [Fact]
    public void Legacy_exam_session_scores_full_section_with_band()
    {
        var version = Version();
        var session = ExamSession.Start(
            UserId.New(), version, SessionMode.Single, SessionTiming.Deadline,
            ExamModule.Reading, DateTimeOffset.UtcNow);

        var context = PracticeScorePolicy.ScoringContext(session, version);

        Assert.True(context.IncludeBand);
        Assert.Null(context.QuestionIds);
    }

    private static ExamSession StartSession(PracticeUnit unit)
    {
        var version = Version();
        return ExamSession.Start(
            UserId.New(), version, SessionMode.Single, SessionTiming.OpenEnded,
            unit.Module ?? ExamModule.Reading, DateTimeOffset.UtcNow,
            targetSeconds: 900, unit.Id, unit.PartIds);
    }

    private static ExamVersion Version()
    {
        var sections = new[]
        {
            new Section(ExamModule.Reading, 1,
            [
                new SectionPart(1, "part", "P1", null, null, null, null, null, 1, null, null,
                [Question("reading-1")]),
                new SectionPart(2, "part", "P2", null, null, null, null, null, 2, null, null,
                [Question("reading-2")]),
            ]),
        };
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>
            {
                [ExamModule.Reading] = [new BandBoundary(0, BandScore.Create(5m))],
            },
            AnswerMatchingRules.Default);
        var timing = new TimingProfile(
            new Dictionary<ExamModule, int> { [ExamModule.Reading] = 3600 },
            null,
            []);
        var paper = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Paper", ExamVariant.Academic, scoring, timing, sections);
        paper.Publish(DateTimeOffset.UtcNow);
        return paper;
    }

    private static Question Question(string id) => new(
        id, 1, QuestionType.ShortAnswer, "prompt", [], null,
        new AnswerKey([new AcceptedAnswer("correct", null, null)], null));
}
