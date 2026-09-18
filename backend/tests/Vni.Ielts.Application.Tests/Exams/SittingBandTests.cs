using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// What a sitting is allowed to call an overall band — the rule itself,
/// without either screen in the way.
///
/// <b>Rewritten on 2026-09-18 when the rule moved.</b> Until `W1` this class
/// held the four-skill gate and took a list of <c>SittingSectionView</c>; that
/// gate was superseded by `Q-01` and the list could not express the fact the
/// new rule turns on — that a skill will *never* be marked. The cases below
/// are the ones from the old file that outlived it (a single skill is not an
/// overall band; a Reading-only zero is not <c>overall = 0.0</c>; order does
/// not matter) plus the ones `Q-01` introduced.
///
/// <see cref="OverallBandTests"/> exercises the same rule through the results
/// projection and <see cref="SittingHistoryBandTests"/> through both screens
/// at once. This one exists because a rule with two callers should still be
/// testable with none.
/// </summary>
public sealed class SittingBandTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Learner = UserId.New();
    private static readonly ExamSessionId Sitting = ExamSessionId.New();

    [Fact]
    public void A_single_skill_sitting_has_no_overall_band()
    {
        // The bug this file was opened for, in 2026-08. Reading 6.5 is a
        // Reading band; calling it an overall band claims four skills were sat.
        var version = FourSkillVersion();

        var (band, modules) = SittingBand.Overall(
            Sitting_(SessionMode.Single, version),
            version,
            Bands((ExamModule.Reading, 6.5m)),
            []);

        Assert.Null(band);
        Assert.Empty(modules);
    }

    [Fact]
    public void A_reading_only_sitting_scoring_zero_reports_nothing_rather_than_zero()
    {
        // Band 0 is a real, reportable band — a learner who answered nothing
        // earns it. That makes it indistinguishable from a bug, which is
        // exactly why it must not appear under the wrong label.
        var version = FourSkillVersion();

        var (band, _) = SittingBand.Overall(
            Sitting_(SessionMode.Single, version),
            version,
            Bands((ExamModule.Reading, 0m)),
            []);

        Assert.Null(band);
    }

    [Fact]
    public void A_skill_that_might_still_be_marked_withholds_the_band()
    {
        // `L3`, unchanged by `Q-01`: a mean that moves when the running job
        // lands is a number the learner was told falsely the first time.
        var version = FourSkillVersion();

        var (band, modules) = SittingBand.Overall(
            Sitting_(SessionMode.Full, version),
            version,
            Bands((ExamModule.Reading, 7m), (ExamModule.Listening, 6.5m)),
            [Status(ExamModule.Writing, "running", null), Blocked(ExamModule.Speaking)]);

        Assert.Null(band);
        Assert.Empty(modules);
    }

    [Fact]
    public void A_skill_that_will_never_be_marked_does_not_withhold_it()
    {
        // The `Q-01` case. Speaking carries a terminal code (`P-02`), so the
        // mock is finished rather than partial and its three bands are a mean.
        var version = FourSkillVersion();

        var (band, modules) = SittingBand.Overall(
            Sitting_(SessionMode.Full, version),
            version,
            Bands((ExamModule.Reading, 7m), (ExamModule.Listening, 6.5m), (ExamModule.Writing, 6.5m)),
            [Blocked(ExamModule.Speaking)]);

        var expected = BandScore.Overall(
            [BandScore.Create(7m), BandScore.Create(6.5m), BandScore.Create(6.5m)]);

        Assert.Equal(expected.Value, band);
        Assert.Equal(["reading", "listening", "writing"], modules);
    }

    [Fact]
    public void Four_marked_skills_produce_the_official_rounded_mean()
    {
        // 6.5 + 6.5 + 5.0 + 7.0 = 25 / 4 = 6.25, which rounds UP to 6.5. The
        // expected value goes through `BandScore` rather than being worked out
        // here: the rounding rule is asymmetric, lives in one place, and a copy
        // of it in a test is the copy that is wrong.
        var version = FourSkillVersion();

        var (band, modules) = SittingBand.Overall(
            Sitting_(SessionMode.Full, version),
            version,
            Bands(
                (ExamModule.Listening, 6.5m), (ExamModule.Reading, 6.5m),
                (ExamModule.Writing, 5m), (ExamModule.Speaking, 7m)),
            []);

        var expected = BandScore.Overall(
        [
            BandScore.Create(6.5m), BandScore.Create(6.5m),
            BandScore.Create(5m), BandScore.Create(7m),
        ]);

        Assert.Equal(expected.Value, band);
        Assert.Equal(6.5m, band);
        Assert.Equal(["reading", "listening", "writing", "speaking"], modules);
    }

    [Fact]
    public void The_order_bands_arrive_in_does_not_change_the_answer()
    {
        var version = FourSkillVersion();
        var sitting = Sitting_(SessionMode.Full, version);

        var forwards = SittingBand.Overall(
            sitting, version,
            Bands(
                (ExamModule.Listening, 7m), (ExamModule.Reading, 7m),
                (ExamModule.Writing, 7m), (ExamModule.Speaking, 6m)),
            []);

        var backwards = SittingBand.Overall(
            sitting, version,
            Bands(
                (ExamModule.Speaking, 6m), (ExamModule.Writing, 7m),
                (ExamModule.Reading, 7m), (ExamModule.Listening, 7m)),
            []);

        Assert.Equal(forwards.Band, backwards.Band);
        Assert.Equal(forwards.Modules, backwards.Modules);
        Assert.Equal(7m, forwards.Band);   // 6.75 rounds up to the whole band
    }

    // ── Fixtures ──────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<ExamModule, BandScore> Bands(
        params (ExamModule Module, decimal Band)[] bands) =>
        bands.ToDictionary(b => b.Module, b => BandScore.Create(b.Band));

    private static MarkingStatusView Status(ExamModule module, string state, string? code) =>
        new(module.ToString().ToLowerInvariant(), state, 0, null, code);

    private static MarkingStatusView Blocked(ExamModule module) =>
        Status(module, "pending", nameof(MarkingAvailability.AwaitingVoiceProvider));

    private static ExamSession Sitting_(SessionMode mode, ExamVersion version) =>
        ExamSession.Rehydrate(
            Sitting, Learner, version.Id, mode, SessionStatus.Submitted, T0, T0.AddHours(3),
            [SectionAttempt.Rehydrate(ExamModule.Reading, T0, null, T0)],
            SessionTiming.Deadline);

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
