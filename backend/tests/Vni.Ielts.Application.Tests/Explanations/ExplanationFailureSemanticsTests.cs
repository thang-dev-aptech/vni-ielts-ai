using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Tests.Explanations;

public sealed class ExplanationFailureSemanticsTests
{
  [Fact]
  public void Deterministic_score_returned_while_explanation_job_failed()
  {
    var version = ReadingVersion();
    var session = SubmittedSession(version);
    var score = DeterministicScorer.Score(
        version.Section(ExamModule.Reading)!,
        version.Scoring,
        new Dictionary<string, string?> { ["q1"] = "A" },
        new DeterministicScoringContext(IncludeBand: false));

    var failedJob = new PersonalizedExplanationJob(
        "op-1",
        session.Id,
        version.Id,
        "q1",
        ExplanationAnswerHash.Compute("A"),
        ExplanationJobState.Failed,
        null,
        null,
        3,
        "EXPLANATION_PROVIDER_TIMEOUT",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    var view = session.ToResults(version, [score], [], [], [failedJob]);

    Assert.Single(view.Sections);
    Assert.Equal(0, view.Sections[0].RawScore);
    Assert.Contains(view.ExplanationStatuses, s => s.QuestionId == "q1" && s.State == "failed");
  }

  [Fact]
  public void Canonical_explanation_ready_does_not_change_score()
  {
    var version = ReadingVersion(withExplanation: true);
    var session = SubmittedSession(version);
    var score = DeterministicScorer.Score(
        version.Section(ExamModule.Reading)!,
        version.Scoring,
        new Dictionary<string, string?> { ["q1"] = "A" },
        new DeterministicScoringContext(IncludeBand: false));

    var view = session.ToResults(version, [score], [], [], []);

    Assert.False(view.Sections[0].Questions[0].IsCorrect);
    Assert.NotNull(view.Sections[0].Questions[0].CanonicalExplanation);
    Assert.Contains(view.ExplanationStatuses, s => s.State == "ready");
  }

  private static ExamVersion ReadingVersion(bool withExplanation = false)
  {
    var explanation = withExplanation
        ? new QuestionExplanation("B", "Text says B.", ["sample passage evidence"])
        : null;

    var question = new Question(
        "q1", 1, QuestionType.MultipleChoice, "Pick one", [], null,
        new AnswerKey([new AcceptedAnswer("B", null, null)], null),
        Explanation: explanation);

    var part = new SectionPart(
        1, "passage", null, "Passage with sample passage evidence inside.", null, null, null,
        null, null, null, null, [question]);

    return ExamVersion.CreateDraft(
        ExamDefinitionId.New(), 1, "Sample", ExamVariant.Academic,
        new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
        new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
        [new Section(ExamModule.Reading, 1, [part])]);
  }

  private static ExamSession SubmittedSession(ExamVersion version)
  {
    version.Publish(DateTimeOffset.UtcNow);
    var session = ExamSession.Start(
        UserId.New(),
        version,
        SessionMode.Single,
        SessionTiming.OpenEnded,
        version.FirstModule(),
        DateTimeOffset.UtcNow);

    session.Submit(DateTimeOffset.UtcNow);
    return session;
  }
}
