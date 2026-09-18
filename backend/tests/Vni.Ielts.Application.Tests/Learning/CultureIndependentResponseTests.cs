using System.Globalization;
using System.Runtime.ExceptionServices;
using Vni.Ielts.Application.Learning;
using Vni.Ielts.Application.Tests.Exams;

namespace Vni.Ielts.Application.Tests.Learning;

/// <summary>
/// <b>What the API sends must not depend on the operating system of the host
/// that sends it.</b>
///
/// A number or a date formatted without an explicit <see cref="IFormatProvider"/>
/// is formatted in the ambient culture of the *process*. That culture comes from
/// the machine, so the same build answers one thing on a developer's laptop and
/// another thing in a container whose locale nobody chose deliberately. The
/// client has no way to tell which it received: <c>"6,0"</c> and <c>"6.0"</c>
/// are both plausible strings for the same band.
///
/// <b>Why it stayed invisible here.</b> The defect was in the string
/// <c>CoachingSkillView.Detail</c> carries — <c>"Task 1 6,0 · Task 2 7,0"</c> on
/// a Vietnamese host. Every developer on this project runs a Vietnamese or a
/// US machine; on the first the comma looks correct, on the second the defect
/// does not exist. Nothing in the test suite pinned it, because the assertion
/// that would have caught it was written using the same ambient culture as the
/// code under test, and two wrongs agree.
///
/// So these tests do not run in the ambient culture. They pick a hostile one on
/// purpose and demand the invariant answer:
///
/// <list type="bullet">
/// <item><c>vi-VN</c> — the deployment locale. Decimal separator <c>,</c>,
/// group separator <c>.</c>. This is the one that reaches learners.</item>
/// <item><c>th-TH</c> — Buddhist calendar. <c>2026-09-18</c> formats as
/// <c>2569-09-18</c> and reads back as <c>1483-09-18</c>. Chosen because a date
/// is not safe merely for having no separator in its format string: the
/// *calendar* is cultural too, and only a pinned provider fixes it.</item>
/// </list>
///
/// <b>The culture is set on a thread of this test's own</b> rather than on the
/// ambient one. <see cref="CultureInfo.CurrentCulture"/> flows across
/// <c>await</c> with the execution context and is restored when the
/// continuation unwinds, so a dedicated thread contains the switch even while
/// xUnit runs other collections in parallel beside it.
///
/// → the gate that keeps new ones out: <c>scripts/check-culture.mjs</c>
/// </summary>
public sealed class CultureIndependentResponseTests
{
    /// <summary>
    /// The Writing per-task detail on <c>GET /api/v1/me/coaching</c>. This is
    /// the string the defect was found in.
    /// </summary>
    [Fact]
    public void Coaching_writing_detail_reads_the_same_on_a_Vietnamese_host()
    {
        var view = InCulture("vi-VN", async () =>
        {
            var h = new SittingHistoryHarness();
            await h.ThreeSkillMockAsync();
            return await h.CoachingAsync();
        });

        var writing = Assert.Single(view.Skills, s => s.Module == "writing");

        // A literal, deliberately. Reproducing the handler's own formatting in
        // the expectation is what hid this for as long as it was hidden.
        Assert.Equal("Task 1 6.0 · Task 2 7.0", writing.Detail);
    }

    /// <summary>
    /// The day keys on <c>GET /api/v1/me/activity</c>. The client parses these
    /// as ISO dates to lay out the heatmap; a Buddhist year is not a late
    /// heatmap, it is an empty one.
    /// </summary>
    [Fact]
    public void Activity_day_keys_are_ISO_dates_on_a_Buddhist_calendar_host()
    {
        var view = InCulture("th-TH", async () =>
        {
            var h = new SittingHistoryHarness();
            await h.ThreeSkillMockAsync();
            return await h.ActivityAsync();
        });

        // SittingHistoryHarness.T0 is 2026-09-18, and its clock does not move.
        Assert.Equal("2026-09-18", view.Today);
        Assert.Equal(["2026-09-18"], view.Days.Select(d => d.Date));
    }

    /// <summary>
    /// The coaching cache key is derived from the learner's bands, and two
    /// learners with the same standing are meant to share one row. Formatted in
    /// the ambient culture, the key is a function of the host as well as of the
    /// standing: the same learner moving between a US node and a Vietnamese
    /// node misses a cache that holds their answer, and pays for a second AI
    /// call to produce it. → <c>P-14</c>
    /// </summary>
    [Fact]
    public void The_coaching_cache_key_is_a_function_of_the_standing_and_not_of_the_host()
    {
        var vietnamese = InCulture("vi-VN", KeyAsync);
        var german = InCulture("de-DE", KeyAsync);
        var invariant = InCulture("en-US", KeyAsync);

        Assert.Equal(invariant, vietnamese);
        Assert.Equal(invariant, german);
        return;

        static async Task<string> KeyAsync()
        {
            var h = new SittingHistoryHarness();
            await h.ThreeSkillMockAsync();
            var view = await h.CoachingAsync();

            return GetCoaching.CacheKey(
                new CoachingFacts(
                    7.5m,
                    [.. view.Skills.Select(s =>
                        new CoachingSkillFact(s.Module, s.CurrentBand, s.Gap, s.Detail))]));
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> to completion with
    /// <paramref name="culture"/> installed, on a thread this test owns.
    /// </summary>
    private static T InCulture<T>(string culture, Func<Task<T>> body)
    {
        var result = default(T)!;
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            var chosen = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = chosen;
            CultureInfo.CurrentUICulture = chosen;

            try
            {
                result = body().GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });

        thread.Start();
        thread.Join();
        failure?.Throw();

        return result;
    }
}
