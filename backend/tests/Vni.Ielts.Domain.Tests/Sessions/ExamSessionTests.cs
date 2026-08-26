using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Domain.Tests.Sessions;

/// <summary>
/// Timer behaviour is tested adversarially, not functionally.
///
/// "The timer counts down" is not a test. "A client that forges a deadline,
/// replays an old start time, or submits one second late is rejected" is —
/// because an exploitable timer invalidates every score the platform produces
/// (threat T6), and a passing happy-path test says nothing about that.
/// </summary>
public sealed class ExamSessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Learner = UserId.New();
    private static readonly UserId Author = UserId.New();

    private static ExamVersion Version(params ExamModule[] modules)
    {
        var sections = modules
            .Select((m, i) => new Section(m, i + 1,
            [
                new SectionPart(1, "passage", "P", "body", null, null, null, null, null, null, null,
                [
                    new Question($"{m}-1".ToLowerInvariant(), 1, QuestionType.ShortAnswer, "q", [], null,
                        new AnswerKey([new AcceptedAnswer("paper", null, null)], null)),
                ]),
            ]))
            .ToList();

        var timing = new TimingProfile(
            modules.ToDictionary(m => m, m => m switch
            {
                ExamModule.Reading => 3600,
                ExamModule.Listening => 1800,
                ExamModule.Writing => 3600,
                _ => 900,
            }),
            null, []);

        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>
            {
                [ExamModule.Reading] = [new BandBoundary(0, BandScore.Create(0m))],
            },
            AnswerMatchingRules.Default);

        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Test", ExamVariant.Academic, Author, scoring, timing, sections);
        version.Publish(T0.AddDays(-1));
        return version;
    }

    [Fact]
    public void A_draft_version_cannot_be_sat()
    {
        // Publishing is the only human review point in the pipeline. Sitting a
        // draft would route unreviewed content straight to a real candidate.
        var draft = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Draft", ExamVariant.Academic, Author,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
                AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int> { [ExamModule.Reading] = 60 }, null, []),
            [new Section(ExamModule.Reading, 1, [])]);

        Assert.Throws<InvalidOperationException>(() =>
            ExamSession.Start(Learner, draft, SessionMode.Single, ExamModule.Reading, T0));
    }

    [Fact]
    public void The_deadline_is_derived_from_the_server_clock_and_the_timing_profile()
    {
        var session = ExamSession.Start(Learner, Version(ExamModule.Reading), SessionMode.Single,
            ExamModule.Reading, T0);

        // Nothing in the call supplied a deadline. It cannot be influenced.
        Assert.Equal(T0, session.Current!.StartedAt);
        Assert.Equal(T0.AddHours(1), session.Current.DeadlineAt);
    }

    [Fact]
    public void A_submission_one_second_late_is_outside_the_deadline()
    {
        var session = ExamSession.Start(Learner, Version(ExamModule.Reading), SessionMode.Single,
            ExamModule.Reading, T0);

        Assert.True(session.IsWithinDeadline(T0.AddHours(1)));                    // exactly on time
        Assert.False(session.IsWithinDeadline(T0.AddHours(1).AddSeconds(1)));     // one second late
    }

    [Fact]
    public void Full_test_advances_reading_listening_writing_speaking_in_that_order()
    {
        // E-12. A VNI product decision, not the official IELTS order.
        var version = Version(ExamModule.Reading, ExamModule.Listening, ExamModule.Writing, ExamModule.Speaking);
        var session = ExamSession.Start(Learner, version, SessionMode.Full, version.FirstModule(), T0);

        Assert.Equal(ExamModule.Reading, session.Current!.Module);

        Assert.Equal(AdvanceOutcome.Advanced, session.AdvanceToNextSection(version, T0.AddMinutes(60)));
        Assert.Equal(ExamModule.Listening, session.Current!.Module);

        Assert.Equal(AdvanceOutcome.Advanced, session.AdvanceToNextSection(version, T0.AddMinutes(90)));
        Assert.Equal(ExamModule.Writing, session.Current!.Module);

        Assert.Equal(AdvanceOutcome.Advanced, session.AdvanceToNextSection(version, T0.AddMinutes(150)));
        Assert.Equal(ExamModule.Speaking, session.Current!.Module);

        Assert.Equal(AdvanceOutcome.SessionComplete, session.AdvanceToNextSection(version, T0.AddMinutes(165)));
        Assert.Equal(SessionStatus.Submitted, session.Status);
    }

    [Fact]
    public void Each_section_gets_a_fresh_deadline_rather_than_inheriting_the_previous_one()
    {
        // Carrying a deadline forward would silently shorten every later
        // section — the learner loses time they were entitled to, and nothing
        // in the UI would show why.
        var version = Version(ExamModule.Reading, ExamModule.Listening);
        var session = ExamSession.Start(Learner, version, SessionMode.Full, version.FirstModule(), T0);

        var advancedAt = T0.AddMinutes(45);       // finished Reading early
        session.AdvanceToNextSection(version, advancedAt);

        var listening = session.Current!;
        Assert.Equal(advancedAt, listening.StartedAt);
        Assert.Equal(advancedAt.AddMinutes(30), listening.DeadlineAt);   // full 30 minutes

        // Not shortened by the time Reading consumed.
        Assert.NotEqual(T0.AddMinutes(30), listening.DeadlineAt);
    }

    [Fact]
    public void A_single_skill_session_never_auto_advances()
    {
        // CLAUDE.md rule 10. Its call to action is "làm đề mới", which is a
        // different operation with a different entitlement effect.
        var version = Version(ExamModule.Reading, ExamModule.Listening);
        var session = ExamSession.Start(Learner, version, SessionMode.Single, ExamModule.Reading, T0);

        Assert.Equal(AdvanceOutcome.NotAFullTest, session.AdvanceToNextSection(version, T0.AddMinutes(30)));
        Assert.Equal(ExamModule.Reading, session.Current!.Module);
        Assert.Single(session.Attempts);
    }

    [Fact]
    public void The_next_module_comes_from_the_version_not_from_a_caller()
    {
        // There is no parameter through which a caller could request Speaking
        // and skip Writing. This test documents the absence.
        var version = Version(ExamModule.Reading, ExamModule.Writing, ExamModule.Speaking);
        var session = ExamSession.Start(Learner, version, SessionMode.Full, version.FirstModule(), T0);

        session.AdvanceToNextSection(version, T0.AddMinutes(60));

        // Listening is absent from this version, so Writing follows Reading.
        Assert.Equal(ExamModule.Writing, session.Current!.Module);
    }

    [Fact]
    public void Submitting_closes_every_open_section()
    {
        var version = Version(ExamModule.Reading, ExamModule.Listening);
        var session = ExamSession.Start(Learner, version, SessionMode.Full, version.FirstModule(), T0);

        session.Submit(T0.AddMinutes(20));

        Assert.Equal(SessionStatus.Submitted, session.Status);
        Assert.All(session.Attempts, a => Assert.NotNull(a.SubmittedAt));
    }

    [Fact]
    public void A_submitted_session_cannot_be_submitted_again()
    {
        // Answers are immutable after submission — otherwise a direct API call
        // could alter them post hoc. Threat T7.
        var session = ExamSession.Start(Learner, Version(ExamModule.Reading), SessionMode.Single,
            ExamModule.Reading, T0);
        session.Submit(T0.AddMinutes(10));

        Assert.Throws<InvalidOperationException>(() => session.Submit(T0.AddMinutes(11)));
    }

    [Fact]
    public void Expiring_marks_the_session_expired_rather_than_submitted()
    {
        // The two are different outcomes and a learner should be able to tell
        // them apart in their history.
        var session = ExamSession.Start(Learner, Version(ExamModule.Reading), SessionMode.Single,
            ExamModule.Reading, T0);

        session.Expire(T0.AddHours(2));

        Assert.Equal(SessionStatus.Expired, session.Status);
        Assert.All(session.Attempts, a => Assert.NotNull(a.SubmittedAt));
    }

    [Fact]
    public void Advancing_a_finished_session_does_nothing()
    {
        var version = Version(ExamModule.Reading, ExamModule.Listening);
        var session = ExamSession.Start(Learner, version, SessionMode.Full, version.FirstModule(), T0);
        session.Submit(T0.AddMinutes(10));

        Assert.Equal(AdvanceOutcome.SessionNotInProgress,
            session.AdvanceToNextSection(version, T0.AddMinutes(11)));
    }
}
