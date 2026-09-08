using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// S4 / `P-12` — the combined Writing band on the results view.
///
/// <b>Every expected band here is computed through
/// <see cref="BandScore.Weighted"/>, never by hand.</b> The rounding rule is
/// asymmetric and lives in exactly one place; a test that worked out 6.333…
/// on paper and wrote down 6.5 would be a second implementation of it, and
/// the one that is wrong about 6.75 is always the copy.
/// </summary>
public sealed class WritingBandResultsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Owner = UserId.New();
    private static readonly ExamSessionId Sitting = ExamSessionId.New();

    private static readonly IWritingTaskWeighting OneToTwo =
        new WritingTaskWeightPolicy(new WritingTaskWeights(1m, 2m));

    // ── The band, on the configured ratio ─────────────────────────────────

    [Theory]
    // Task 1 = 6.0, Task 2 = 6.5 → weighted mean 6.333…, the asymmetric
    // case the brief asked for. Whatever `Weighted` says is the answer.
    [InlineData(6.0, 6.5)]
    [InlineData(6.0, 7.0)]   // 6.667
    [InlineData(5.0, 7.0)]   // 6.333
    [InlineData(7.0, 6.0)]   // 6.333 the other way round — order of tasks matters
    [InlineData(5.5, 5.5)]   // equal tasks: the ratio cannot move it
    public void Writing_band_is_the_weighted_band_on_the_configured_ratio(double task1, double task2)
    {
        var version = WritingVersion();
        var t1 = BandScore.Create((decimal)task1);
        var t2 = BandScore.Create((decimal)task2);

        var results = SubmittedWriting(version).ToResults(
            version, [], [Task(1, t1), Task(2, t2)], weighting: OneToTwo);

        var expected = BandScore.Weighted([(t1, 1m), (t2, 2m)]);

        Assert.Equal(expected.Value, results.WritingBand);
        Assert.Null(results.WritingBandReason);
    }

    [Fact]
    public void The_exam_versions_own_ratio_wins_over_the_configured_one()
    {
        // 5.0 and 7.0 is chosen because 1:1 and 1:2 disagree on it — a pair
        // where they agree would pass whichever source won.
        var version = WritingVersion(task1Weight: 1m, task2Weight: 1m);
        var t1 = BandScore.Create(5m);
        var t2 = BandScore.Create(7m);

        var results = SubmittedWriting(version).ToResults(
            version, [], [Task(1, t1), Task(2, t2)], weighting: OneToTwo);

        var onVersionRatio = BandScore.Weighted([(t1, 1m), (t2, 1m)]);
        var onConfiguredRatio = BandScore.Weighted([(t1, 1m), (t2, 2m)]);

        Assert.NotEqual(onVersionRatio.Value, onConfiguredRatio.Value);
        Assert.Equal(onVersionRatio.Value, results.WritingBand);
    }

    [Fact]
    public void Handed_no_policy_the_view_still_honours_the_versions_own_ratio()
    {
        // The default argument is the null implementation: version only.
        var version = WritingVersion(task1Weight: 1m, task2Weight: 2m);
        var t1 = BandScore.Create(6m);
        var t2 = BandScore.Create(6.5m);

        var results = SubmittedWriting(version).ToResults(version, [], [Task(1, t1), Task(2, t2)]);

        Assert.Equal(BandScore.Weighted([(t1, 1m), (t2, 2m)]).Value, results.WritingBand);
    }

    // ── The honest null, with its reason ──────────────────────────────────

    [Fact]
    public void Both_tasks_marked_but_no_ratio_anywhere_reports_no_band_and_says_why()
    {
        var version = WritingVersion();

        var results = SubmittedWriting(version).ToResults(
            version, [], [Task(1, BandScore.Create(6m)), Task(2, BandScore.Create(7m))],
            weighting: WritingTaskWeightPolicy.Unconfigured);

        Assert.Null(results.WritingBand);
        Assert.Equal("weighting-not-configured", results.WritingBandReason);
    }

    [Fact]
    public void One_task_marked_reports_no_band_and_awaits_the_other()
    {
        var version = WritingVersion();

        var results = SubmittedWriting(version).ToResults(
            version, [], [Task(1, BandScore.Create(6m))], weighting: OneToTwo);

        Assert.Null(results.WritingBand);
        Assert.Equal("awaiting-tasks", results.WritingBandReason);
    }

    [Fact]
    public void Nothing_marked_yet_still_awaits_when_writing_was_sat()
    {
        var version = WritingVersion();

        var results = SubmittedWriting(version).ToResults(version, [], [], weighting: OneToTwo);

        Assert.Null(results.WritingBand);
        Assert.Equal("awaiting-tasks", results.WritingBandReason);
    }

    [Fact]
    public void A_sitting_that_never_opened_writing_carries_neither_band_nor_reason()
    {
        var version = WritingVersion();
        var readingOnly = ExamSession.Rehydrate(
            Sitting, Owner, version.Id, SessionMode.Single, SessionStatus.Submitted,
            T0, T0.AddHours(1),
            [SectionAttempt.Rehydrate(ExamModule.Reading, T0, null, T0)],
            SessionTiming.OpenEnded);

        var results = readingOnly.ToResults(version, [], [], weighting: OneToTwo);

        Assert.Null(results.WritingBand);
        Assert.Null(results.WritingBandReason);
    }

    [Fact]
    public void A_writing_band_alone_is_not_an_overall_band()
    {
        // Product law L3 survives: one skill with a band is still one skill.
        var version = WritingVersion();

        var results = SubmittedWriting(version).ToResults(
            version, [], [Task(1, BandScore.Create(6m)), Task(2, BandScore.Create(7m))],
            weighting: OneToTwo);

        Assert.NotNull(results.WritingBand);
        Assert.Null(results.OverallBand);
    }

    // ── Fixtures ──────────────────────────────────────────────────────────

    private static SectionMarking Task(int number, BandScore band) =>
        new(ExamModule.Writing, "ielts-writing-synthetic-v1", [], band, null, [], [], number);

    private static ExamSession SubmittedWriting(ExamVersion version) =>
        ExamSession.Rehydrate(
            Sitting, Owner, version.Id, SessionMode.Single, SessionStatus.Submitted,
            T0, T0.AddHours(1),
            [SectionAttempt.Rehydrate(ExamModule.Writing, T0, null, T0)],
            SessionTiming.OpenEnded);

    private static ExamVersion WritingVersion(decimal? task1Weight = null, decimal? task2Weight = null)
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default, task1Weight, task2Weight);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Writing band", ExamVariant.Academic, scoring, timing,
            [
                new Section(ExamModule.Writing, 1,
                [
                    new SectionPart(
                        1, "writing", "Task 1", "Describe the chart.", null, null, null, 1, null, null, 150,
                        [new Question("w-task-1", 1, QuestionType.EssayTask, "Describe the chart.", [], null, null)]),
                    new SectionPart(
                        2, "writing", "Task 2", "Discuss both views.", null, null, null, 2, null, null, 250,
                        [new Question("w-task-2", 2, QuestionType.EssayTask, "Discuss both views.", [], null, null)]),
                ]),
            ]);
        version.Publish(T0.AddDays(-1));
        return version;
    }
}
