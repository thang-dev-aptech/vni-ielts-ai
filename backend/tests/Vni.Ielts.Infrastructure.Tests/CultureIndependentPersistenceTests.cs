using System.Globalization;
using System.Runtime.ExceptionServices;
using Vni.Ielts.Infrastructure.Assessment;
using Vni.Ielts.Infrastructure.Persistence.Learning;

namespace Vni.Ielts.Infrastructure.Tests;

/// <summary>
/// The reading half of the same defect — and the more dangerous half.
///
/// <b>Formatting a number in the wrong culture makes a string somebody can see
/// is odd. Parsing one in the wrong culture makes a different number, silently.</b>
/// Both directions are here because a value written on one host and read on
/// another is a round trip through two ambient cultures, and pinning only the
/// writer leaves the reader free to disagree with it.
///
/// Every expectation below was measured on .NET 10 before it was written; none
/// of these cultures is hypothetical.
///
/// → <c>CultureIndependentResponseTests</c> for the writing half,
///   <c>scripts/check-culture.mjs</c> for the gate.
/// </summary>
public sealed class CultureIndependentPersistenceTests
{
    /// <summary>
    /// <b>The one that changes a number rather than a separator.</b>
    ///
    /// Rubric descriptors are keyed by band in the artifact JSON — <c>"9"</c>,
    /// <c>"8.5"</c>, <c>"6.5"</c> — and sorted by parsing that key. In
    /// <c>vi-VN</c>, <c>.</c> is the <i>group</i> separator, so
    /// <c>decimal.Parse("6.5")</c> is <b>65</b>: band 6.5 sorts above band 9 and
    /// the descriptor block handed to the model comes out claiming an order it
    /// does not have. Nothing throws, nothing logs.
    /// </summary>
    [Theory]
    [InlineData("vi-VN")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Rubric_descriptors_are_ordered_by_band_in_every_culture(string culture)
    {
        var artifact = new WritingRubricArtifact(
            "test-v1", "VNI", new DateOnly(2026, 9, 18), "hash", "p1",
            ["taskAchievement"],
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["taskAchievement"] = new Dictionary<string, string>
                {
                    ["9"] = "nine",
                    ["8.5"] = "eight and a half",
                    ["6.5"] = "six and a half",
                    ["5"] = "five",
                },
            });

        var text = InCulture(culture, () => WritingRubricLoader.FormatDescriptorsForPrompt(artifact));

        Assert.Equal(
            ["- Band 9: nine", "- Band 8.5: eight and a half", "- Band 6.5: six and a half", "- Band 5: five"],
            text.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("- Band", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A stored day is written and read as the same day, whatever locale either
    /// side of the round trip runs under. <c>th-TH</c> is the interesting one:
    /// its calendar, not its separators, is what breaks <c>"yyyy-MM-dd"</c>.
    /// </summary>
    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    public void A_stored_day_survives_the_round_trip_in_every_culture(string culture)
    {
        var day = new DateOnly(2026, 9, 18);

        var text = InCulture(culture, () => LearningDays.ToText(day));
        Assert.Equal("2026-09-18", text);

        // Read back on a *different* host from the one that wrote it — the
        // case a single-culture test cannot produce.
        Assert.Equal(day, InCulture(culture, () => LearningDays.Parse("2026-09-18")));
    }

    /// <summary>
    /// Text that is not in the stored shape fails loudly instead of becoming a
    /// plausible wrong date. <c>ParseExact</c> rather than <c>Parse</c> buys
    /// this: the stored shape is known exactly, so anything else is a bug
    /// upstream and should say so here.
    ///
    /// <b>What it does not buy, and cannot.</b> A row already written as
    /// <c>2569-09-18</c> by a <c>th-TH</c> host before this fix parses cleanly —
    /// 2569 is a perfectly good Gregorian year. The guard is about shape, not
    /// era, and no parser can tell a Buddhist 2569 from a Gregorian one once
    /// the era is gone. Data written before this commit on such a host is
    /// wrong and stays wrong; the fix is that no more of it is produced.
    /// </summary>
    [Fact]
    public void A_day_that_is_not_in_the_stored_shape_is_refused()
    {
        Assert.Throws<FormatException>(() => LearningDays.Parse("18/09/2026"));
        Assert.Throws<FormatException>(() => LearningDays.Parse("2026-9-18"));
        Assert.Throws<FormatException>(() => LearningDays.Parse(""));
    }

    private static T InCulture<T>(string culture, Func<T> body)
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
                result = body();
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
