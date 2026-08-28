using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

/// <summary>
/// How long a learner actually gets.
///
/// <b>The deadline is the exam.</b> ADR-0007 makes the server the only
/// authority on time, which means every question about fairness ends here: a
/// deadline that is short by a minute takes marks from every candidate who sat
/// that paper, silently, and there is nothing in a results screen that would
/// show it.
/// </summary>
public sealed class TimingProfileTests
{
    private static TimingProfile Profile(int? transferSeconds) =>
        new(
            new Dictionary<ExamModule, int>
            {
                [ExamModule.Reading] = 3600,
                [ExamModule.Listening] = 1800,
                [ExamModule.Writing] = 3600,
            },
            transferSeconds,
            []);

    /// <summary>
    /// <b>The bug this exists to prevent, found by audit on 2026-08-27.</b>
    ///
    /// A real Listening paper is thirty minutes of audio <i>plus</i> ten
    /// minutes to copy answers onto the sheet, and those ten minutes are scored
    /// time — they are when candidates fix spelling and fill the blanks they
    /// skipped. The deadline was built from the section duration alone, so on
    /// Exam 1, whose four parts run 24 minutes 33 seconds inside a 30-minute
    /// section, a learner playing straight through had about five and a half
    /// minutes to transfer forty answers while the paper's own transfer note
    /// promised ten.
    ///
    /// Nothing surfaced it. The clock counted down correctly from a number that
    /// was wrong.
    /// </summary>
    [Fact]
    public void Listening_gets_its_transfer_time_because_that_time_is_scored()
    {
        var profile = Profile(transferSeconds: 600);

        Assert.Equal(TimeSpan.FromSeconds(1800 + 600), profile.DurationFor(ExamModule.Listening));
    }

    [Fact]
    public void Only_Listening_gets_it_because_only_Listening_has_a_second_phase()
    {
        // The other three are written straight onto the sheet. There is nothing
        // to transfer, so adding an allowance would be handing out time the
        // real exam does not.
        var profile = Profile(transferSeconds: 600);

        Assert.Equal(TimeSpan.FromSeconds(3600), profile.DurationFor(ExamModule.Reading));
        Assert.Equal(TimeSpan.FromSeconds(3600), profile.DurationFor(ExamModule.Writing));
    }

    [Fact]
    public void A_version_that_declares_no_transfer_window_is_given_none()
    {
        // An author who left the field out has not asked for ten minutes, and
        // the length of a transfer window is a property of the paper. Supplying
        // a default here would be inventing one exam version's rule for every
        // other version. → `G-11`
        Assert.Equal(TimeSpan.FromSeconds(1800), Profile(null).DurationFor(ExamModule.Listening));
    }

    [Fact]
    public void Speaking_falls_back_to_the_sum_of_its_parts()
    {
        // Speaking is timed per part — preparation plus response — rather than
        // as one block, so a version may carry no section duration for it at
        // all. That is a valid paper, not a broken one.
        var profile = new TimingProfile(
            new Dictionary<ExamModule, int>(),
            null,
            [new SpeakingPartTiming(1, 0, 300), new SpeakingPartTiming(2, 60, 120)]);

        Assert.Equal(TimeSpan.FromSeconds(480), profile.DurationFor(ExamModule.Speaking));
    }

    [Fact]
    public void A_module_with_no_timing_at_all_refuses_rather_than_defaulting()
    {
        // A missing duration is incomplete content. Picking one would start an
        // exam whose deadline nobody chose — and the deadline is the exam.
        Assert.Throws<InvalidOperationException>(
            () => Profile(null).DurationFor(ExamModule.Speaking));
    }
}
