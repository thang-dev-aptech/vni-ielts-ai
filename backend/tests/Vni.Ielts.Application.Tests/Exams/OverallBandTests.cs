using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// The overall band of a mock, and what it is allowed to be a mean of.
///
/// <b>Owner decision, 2026-09-18: a three-skill mock shows its overall band
/// normally.</b> That closes the `[BUSINESS DECISION]` the blueprint left
/// open in § 04 — of its three options ("hide it", "label it an estimate",
/// "show it with a footnote") the owner chose the third.
///
/// <b>What did not change is why the old gate existed.</b> Product law `L3`
/// refuses a *partial* mean: averaging two marked skills while a third is
/// still being marked produces a number that moves every time another job
/// lands, and a learner who read the first one was told something false. The
/// rule here still refuses that. What it stops doing is confusing "partial"
/// with "fewer than four": Speaking carries a terminal blocker
/// (`AwaitingVoiceProvider`, `P-02`), so a mock whose other three skills are
/// all marked is *finished*, not partial, and has an overall band.
///
/// Every expected value is computed through <see cref="BandScore"/>. The
/// rounding rule is asymmetric and lives in one place; a test that worked
/// 6.833… out on paper would be a second implementation of it, and the copy
/// is the one that is wrong.
/// </summary>
public sealed class OverallBandTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Owner = UserId.New();
    private static readonly ExamSessionId Sitting = ExamSessionId.New();

    private static readonly IWritingTaskWeighting OneToTwo =
        new WritingTaskWeightPolicy(new WritingTaskWeights(1m, 2m));

    [Fact]
    public void A_mock_whose_three_markable_skills_are_all_marked_has_an_overall_band()
    {
        var version = FourSkillVersion();

        var results = Mock(version).ToResults(
            version,
            [Score(ExamModule.Reading, 7m), Score(ExamModule.Listening, 6.5m)],
            [Task(1, 6m), Task(2, 7m)],
            [SpeakingBlocked()],
            weighting: OneToTwo);

        var writing = BandScore.Weighted([(BandScore.Create(6m), 1m), (BandScore.Create(7m), 2m)]);
        var expected = BandScore.Overall(
            [BandScore.Create(7m), BandScore.Create(6.5m), writing]);

        Assert.Equal(expected.Value, results.OverallBand);
    }

    /// <summary>
    /// <b>The number says what it is a mean of.</b> A client that had to
    /// count `sections` to discover Speaking was absent would be re-deriving
    /// a business fact from a shape — the exact habit slice `S1` spent its
    /// budget removing from this codebase.
    /// </summary>
    [Fact]
    public void The_overall_band_names_the_skills_it_covers()
    {
        var version = FourSkillVersion();

        var results = Mock(version).ToResults(
            version,
            [Score(ExamModule.Reading, 7m), Score(ExamModule.Listening, 6.5m)],
            [Task(1, 6m), Task(2, 7m)],
            [SpeakingBlocked()],
            weighting: OneToTwo);

        Assert.Equal(["reading", "listening", "writing"], results.OverallBandModules);
    }

    /// <summary>
    /// `L3`, intact. Writing is still running, so the mean would move when it
    /// lands — and a learner who read the earlier number was misled by it.
    /// </summary>
    [Fact]
    public void A_skill_still_being_marked_withholds_the_overall_band()
    {
        var version = FourSkillVersion();

        var results = Mock(version).ToResults(
            version,
            [Score(ExamModule.Reading, 7m), Score(ExamModule.Listening, 6.5m)],
            [],
            [SpeakingBlocked(), Running(ExamModule.Writing)],
            weighting: OneToTwo);

        Assert.Null(results.OverallBand);
        Assert.Empty(results.OverallBandModules);
    }

    /// <summary>
    /// A mean of one number is that number wearing a bigger name. Single-skill
    /// practice has a band already and does not need it restated.
    /// </summary>
    [Fact]
    public void One_band_is_never_an_overall_band()
    {
        var version = FourSkillVersion();

        var results = Mock(version).ToResults(
            version,
            [Score(ExamModule.Reading, 7m)],
            [],
            [
                SpeakingBlocked(),
                Blocked(ExamModule.Listening, nameof(MarkingAvailability.NothingSubmitted)),
                Blocked(ExamModule.Writing, nameof(MarkingAvailability.NothingSubmitted)),
            ],
            weighting: OneToTwo);

        Assert.Null(results.OverallBand);
    }

    /// <summary>
    /// <b>Speaking is not special-cased, and that is the point.</b> The rule
    /// is "no skill is still capable of gaining a band", not "ignore
    /// Speaking". When an ASR provider is finally chosen, Speaking stops
    /// carrying a terminal code, starts producing one, and a four-skill mock
    /// averages four skills — with no edit here.
    /// </summary>
    [Fact]
    public void A_fourth_band_joins_the_mean_without_a_code_change()
    {
        var version = FourSkillVersion();

        var results = Mock(version).ToResults(
            version,
            [Score(ExamModule.Reading, 7m), Score(ExamModule.Listening, 6.5m)],
            [Task(1, 6m), Task(2, 7m), Speaking(6.5m)],
            [],
            weighting: OneToTwo);

        var writing = BandScore.Weighted([(BandScore.Create(6m), 1m), (BandScore.Create(7m), 2m)]);
        var expected = BandScore.Overall(
            [BandScore.Create(7m), BandScore.Create(6.5m), writing, BandScore.Create(6.5m)]);

        Assert.Equal(expected.Value, results.OverallBand);
        Assert.Equal(["reading", "listening", "writing", "speaking"], results.OverallBandModules);
    }

    // ── Fixtures ──────────────────────────────────────────────────────────

    private static SectionScore Score(ExamModule module, decimal band) =>
        new(module, 30, 40, BandScore.Create(band), []);

    private static SectionMarking Task(int number, decimal band) =>
        new(ExamModule.Writing, "ielts-writing-synthetic-v1", [], BandScore.Create(band),
            null, [], [], number);

    private static SectionMarking Speaking(decimal band) =>
        new(ExamModule.Speaking, "ielts-speaking-synthetic-v1", [], BandScore.Create(band),
            null, [], [], null);

    /// <summary>A job that will never produce a band in this build — `P-02`.</summary>
    private static MarkingJob SpeakingBlocked() =>
        Blocked(ExamModule.Speaking, nameof(MarkingAvailability.AwaitingVoiceProvider));

    private static MarkingJob Blocked(ExamModule module, string code) =>
        Job(module, MarkingJobState.Pending, code);

    private static MarkingJob Running(ExamModule module) =>
        Job(module, MarkingJobState.Running, null);

    private static MarkingJob Job(ExamModule module, MarkingJobState state, string? code) =>
        new(Guid.NewGuid().ToString("n"), Sitting, module, "v1", state, 0, T0,
            null, null, null, code, null, null);

    private static ExamSession Mock(ExamVersion version) =>
        ExamSession.Rehydrate(
            Sitting, Owner, version.Id, SessionMode.Full, SessionStatus.Submitted,
            T0, T0.AddHours(3),
            [
                SectionAttempt.Rehydrate(ExamModule.Reading, T0, null, T0),
                SectionAttempt.Rehydrate(ExamModule.Listening, T0, null, T0),
                SectionAttempt.Rehydrate(ExamModule.Writing, T0, null, T0),
                SectionAttempt.Rehydrate(ExamModule.Speaking, T0, null, T0),
            ],
            SessionTiming.OpenEnded);

    private static ExamVersion FourSkillVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default, 1m, 2m);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);

        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Four skills", ExamVariant.Academic, scoring, timing,
            [
                new Section(ExamModule.Reading, 1,
                [
                    new SectionPart(
                        1, "passage", "Passage 1", "Body.", null, null, null, null, null, null, null,
                        [new Question("r-1", 1, QuestionType.Completion, "Q", [], null, null)]),
                ]),
                new Section(ExamModule.Listening, 1,
                [
                    new SectionPart(
                        1, "listening", "Part 1", null, "audio/l1.mp3", null, null, null, 1, null, null,
                        [new Question("l-1", 1, QuestionType.Completion, "Q", [], null, null)]),
                ]),
                new Section(ExamModule.Writing, 1,
                [
                    new SectionPart(
                        1, "writing", "Task 1", "Describe.", null, null, null, 1, null, null, 150,
                        [new Question("w-1", 1, QuestionType.EssayTask, "Describe.", [], null, null)]),
                    new SectionPart(
                        2, "writing", "Task 2", "Discuss.", null, null, null, 2, null, null, 250,
                        [new Question("w-2", 2, QuestionType.EssayTask, "Discuss.", [], null, null)]),
                ]),
                new Section(ExamModule.Speaking, 1,
                [
                    new SectionPart(
                        1, "speaking-part", "Part 1", null, null, null, null, null, 1, null, null,
                        [new Question("sp-1", 1, QuestionType.SpeakingResponse, "Talk.", [], null, null)]),
                ]),
            ]);

        version.Publish(T0.AddDays(-1));
        return version;
    }
}
