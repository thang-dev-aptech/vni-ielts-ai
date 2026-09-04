using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Application.Tests.Importing;

/// <summary>
/// Cambridge answer-key pages are two-column. The sample below is the
/// Listening half of Cam 17 Test 1 as <c>pdftotext -layout</c> emits it
/// (page 119), cut down to the lines that matter.
/// </summary>
public sealed class CambridgeAnswerKeyNormalizerTests
{
    private const string Cam17Test1Listening = """
        Listening and Reading answer keys
                                                  TEST 1


        LIS TE N ING

                  Answer key with extra explanations
                  in Resource Bank


        Part 1, Questions 1–10                          Part 3, Questions 21–30
         1   litter                                     21    A
         2   dogs                                       22    B
         3   insects                                    23    B
         4   butterflies                                24    A
         5   wall                                       25    C
         6   island                                     26    C
         7   boots                                      27    A
         8   beginners                                  28    E
         9   spoons                                     29    F
        10   35 / thirty five                           30    C


        Part 2, Questions 11–20                         Part 4, Questions 31–40
        11 A                                            31    puzzle
        12 C                                            32    logic
        13 B                                            33    confusion
        14 B                                            34    meditation
        15&16 IN EITHER ORDER                           35    stone
             A                                          36    coins
             D                                          37    tree
        17&18 IN EITHER ORDER                           38    breathing
             B                                          39    paper
             C                                          40    anxiety
        19&20 IN EITHER ORDER
             D
             E
        """;

    [Fact]
    public void Two_column_layout_yields_forty_numbered_answers()
    {
        var normalised = CambridgeAnswerKeyNormalizer.Normalise(Cam17Test1Listening);
        var entries = AnswerKeyDocument.Parse(normalised);

        Assert.Equal(40, entries.Sum(e => e.Marks));
        Assert.Equal("litter", entries.Single(e => e.First == 1).Raw);
        Assert.Equal("A", entries.Single(e => e.First == 21).Raw);
        Assert.Equal("anxiety", entries.Single(e => e.First == 40).Raw);
    }

    /// <summary>
    /// tesseract on Cam 18 T1's key page: ". 4 pesticides" and "« 27 B". A
    /// number the parser cannot see is an answer silently lost.
    /// </summary>
    [Fact]
    public void A_glyph_ocr_put_in_front_of_the_number_is_stripped()
    {
        var normalised = CambridgeAnswerKeyNormalizer.Normalise(
            "READING\n3. (food) consumption\n. 4 pesticides\n5 journeys                       « 27 B\n31° G\n33.E\n. . 35&36 IN EITHER ORDER\nA\nB\n22823 IN EITHER ORDER\nC\nE\n");

        Assert.Contains("4. pesticides", normalised, StringComparison.Ordinal);
        Assert.Contains("27. B", normalised, StringComparison.Ordinal);
        Assert.Contains("3. (food) consumption", normalised, StringComparison.Ordinal);
        Assert.Contains("31. G", normalised, StringComparison.Ordinal);
        Assert.Contains("33. E", normalised, StringComparison.Ordinal);
        Assert.Contains("35-36. A, B", normalised, StringComparison.Ordinal);
        Assert.Contains("22-23. C, E", normalised, StringComparison.Ordinal);
    }

    /// <summary>Cam 16 T4: "20 and 2019." from the explanations page must not replace "20 F".</summary>
    [Fact]
    public void A_later_line_that_starts_with_an_already_seen_number_does_not_overwrite_it()
    {
        var normalised = CambridgeAnswerKeyNormalizer.Normalise("READING\n20 F\n21 B\nIf you score\n20 and 2019.\n");

        Assert.Contains("20. F", normalised, StringComparison.Ordinal);
        Assert.DoesNotContain("2019", normalised, StringComparison.Ordinal);
    }

    [Fact]
    public void In_either_order_becomes_a_multi_mark_entry()
    {
        var normalised = CambridgeAnswerKeyNormalizer.Normalise(Cam17Test1Listening);
        var entries = AnswerKeyDocument.Parse(normalised);

        var fifteen = Assert.Single(entries, e => e.First == 15);
        Assert.Equal(16, fifteen.Last);
        Assert.Equal("A, D", fifteen.Raw);

        var nineteen = Assert.Single(entries, e => e.First == 19);
        Assert.Equal(20, nineteen.Last);
        Assert.Equal("D, E", nineteen.Raw);
    }

    [Fact]
    public void SplitColumns_prefers_the_wide_mid_gap()
    {
        var (left, right) = CambridgeAnswerKeyNormalizer.SplitColumns(
            " 1   litter                                     21    A");

        Assert.Equal("1   litter", left);
        Assert.Equal("21    A", right);
    }

    [Fact]
    public void SplitColumns_does_not_split_number_from_its_answer()
    {
        // Right-column-only line: wide leading indent, then "29    F".
        // The four spaces between 29 and F must stay together.
        var (left, right) = CambridgeAnswerKeyNormalizer.SplitColumns(
            "                                                29    F");

        Assert.Equal("29    F", left);
        Assert.Equal("", right);
    }
}
