using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Domain.Tests.Sessions;

/// <summary>
/// The luyện đề clock, which counts up and can be stopped.
///
/// <b>The whole point is that the learner never says what time it is.</b> They
/// say "pause" and "resume"; the server reads its own clock and does the
/// arithmetic. A stopwatch that accepted a client's elapsed figure would be a
/// stopwatch the client could wind back, and this is the mode where nobody is
/// watching — so it is the mode where that would actually happen. → ADR-0007
/// </summary>
public sealed class StopwatchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    private static SectionAttempt Practice(int? target = null) =>
        SectionAttempt.OpenEnded(ExamModule.Reading, T0, target);

    [Fact]
    public void An_open_ended_attempt_starts_running_and_has_no_deadline()
    {
        var attempt = Practice();

        // Running from the off: a learner who pressed "bắt đầu" is working, and
        // making them press play as well is a stopwatch that lies about the
        // first thing they did.
        Assert.NotNull(attempt.RunningSince);
        Assert.Null(attempt.DeadlineAt);
        Assert.False(attempt.IsPastDeadline(T0.AddYears(1)));
    }

    [Fact]
    public void Elapsed_counts_forward_while_running()
    {
        var attempt = Practice();

        Assert.Equal(0, attempt.ElapsedSeconds(T0));
        Assert.Equal(90, attempt.ElapsedSeconds(T0.AddSeconds(90)));
    }

    [Fact]
    public void A_paused_clock_does_not_move()
    {
        var attempt = Practice();

        attempt.Pause(T0.AddMinutes(10));

        Assert.Equal(600, attempt.ElapsedSeconds(T0.AddMinutes(10)));

        // An hour of staring at a paused screen costs nothing.
        Assert.Equal(600, attempt.ElapsedSeconds(T0.AddHours(1)));
    }

    [Fact]
    public void Resuming_adds_to_what_was_already_counted_rather_than_replacing_it()
    {
        var attempt = Practice();

        attempt.Pause(T0.AddMinutes(10));
        attempt.Resume(T0.AddMinutes(30));

        // Ten minutes worked, twenty paused, five more worked.
        Assert.Equal(900, attempt.ElapsedSeconds(T0.AddMinutes(35)));
    }

    /// <summary>
    /// <b>Pausing twice must not count the same stretch twice.</b>
    ///
    /// Two tabs, or one learner double-tapping, is ordinary. The compare-and-
    /// swap on the sitting separates the two writes, but the entity has to be
    /// safe on its own as well — a guard is a second line, not the only one.
    /// </summary>
    [Fact]
    public void Pausing_an_already_paused_clock_changes_nothing()
    {
        var attempt = Practice();

        attempt.Pause(T0.AddMinutes(10));
        attempt.Pause(T0.AddMinutes(25));

        Assert.Equal(600, attempt.ElapsedSeconds(T0.AddMinutes(25)));
    }

    [Fact]
    public void Resuming_an_already_running_clock_does_not_restart_it()
    {
        var attempt = Practice();

        attempt.Resume(T0.AddMinutes(10));

        // Still counting from T0, not from the redundant resume.
        Assert.Equal(600, attempt.ElapsedSeconds(T0.AddMinutes(10)));
    }

    /// <summary>
    /// A clock that stepped backwards must not subtract from the total.
    ///
    /// Server clocks are adjusted — NTP, a VM migrating, a container starting
    /// with a bad time. None of those should be able to make a learner's
    /// elapsed time go down, and none of them should ever produce a negative
    /// interval to add.
    /// </summary>
    [Fact]
    public void A_clock_that_runs_backwards_never_reduces_the_total()
    {
        var attempt = Practice();

        attempt.Pause(T0.AddMinutes(10));
        attempt.Resume(T0.AddMinutes(10));
        attempt.Pause(T0.AddMinutes(5));

        Assert.Equal(600, attempt.ElapsedSeconds(T0.AddMinutes(10)));
        Assert.Equal(600, attempt.ElapsedSeconds(T0.AddMinutes(5)));
    }

    /// <summary>
    /// Submitting stops the clock, and this is not tidiness.
    ///
    /// A learner who submits and opens the review screen an hour later would
    /// otherwise be told they took an hour longer than they did — on the one
    /// screen whose whole purpose is telling them how long they took.
    /// </summary>
    [Fact]
    public void Submitting_stops_the_clock()
    {
        var attempt = Practice();

        attempt.Submit(T0.AddMinutes(40));

        Assert.Equal(2400, attempt.ElapsedSeconds(T0.AddHours(3)));
        Assert.Null(attempt.RunningSince);
    }

    [Fact]
    public void A_submitted_attempt_does_not_resume()
    {
        var attempt = Practice();

        attempt.Submit(T0.AddMinutes(40));
        attempt.Resume(T0.AddHours(2));

        Assert.Null(attempt.RunningSince);
        Assert.Equal(2400, attempt.ElapsedSeconds(T0.AddHours(3)));
    }

    /// <summary>
    /// The target is stored and read by nothing.
    ///
    /// <b>The moment a rule reads it, it is a deadline the learner set for
    /// themselves</b> — and luyện đề has become the exam, with the candidate
    /// writing the rules. This test exists to be broken by whoever tries.
    /// </summary>
    [Fact]
    public void The_target_time_never_becomes_a_deadline()
    {
        var attempt = Practice(target: 20 * 60);

        Assert.Equal(1200, attempt.TargetSeconds);

        // An hour past a twenty-minute goal, and nothing about the attempt has
        // ended, expired or refused anything.
        Assert.Null(attempt.DeadlineAt);
        Assert.False(attempt.IsPastDeadline(T0.AddHours(1)));
        Assert.Null(attempt.SubmittedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void A_target_that_is_not_a_duration_is_stored_as_no_target(int seconds)
    {
        var attempt = Practice(target: 20 * 60);

        attempt.AimFor(seconds);

        Assert.Null(attempt.TargetSeconds);
    }

    /// <summary>
    /// A deadlined attempt keeps its deadline and still reports elapsed time.
    ///
    /// Thi thử does not pause, but "how long did that take" is a fair question
    /// there too — and answering it from the same accumulator means one clock
    /// in the system rather than two that can disagree.
    /// </summary>
    [Fact]
    public void A_deadlined_attempt_still_counts_up()
    {
        var attempt = SectionAttempt.Open(ExamModule.Listening, T0, TimeSpan.FromMinutes(30));

        Assert.NotNull(attempt.DeadlineAt);
        Assert.Equal(300, attempt.ElapsedSeconds(T0.AddMinutes(5)));
        Assert.False(attempt.IsPastDeadline(T0.AddMinutes(30)));
        Assert.True(attempt.IsPastDeadline(T0.AddMinutes(30).AddSeconds(1)));
    }
}
