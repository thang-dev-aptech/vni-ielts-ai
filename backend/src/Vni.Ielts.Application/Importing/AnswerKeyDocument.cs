using System.Text.RegularExpressions;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// One line of an answer key: which question numbers it answers, and the raw
/// text beside them.
/// </summary>
/// <param name="First">The first question this entry answers.</param>
/// <param name="Last">
/// The last. Equal to <paramref name="First"/> for an ordinary entry; greater
/// for a multi-mark item, where one line answers several numbers
/// (<c>"24-26. A, B, D"</c>).
/// </param>
/// <param name="Raw">
/// The text exactly as printed, un-interpreted.
///
/// <b>Deliberately not parsed here.</b> Whether <c>"D wood"</c> means the
/// letter D or the word "D wood" depends on what kind of question 27 is, and
/// this type does not know. Keeping the line raw pushes that decision to
/// <see cref="AnswerKeyInjection"/>, which does.
/// </param>
public sealed record AnswerKeyEntry(int First, int Last, string Raw)
{
    public int Marks => Last - First + 1;
}

/// <summary>
/// Reads a supplier's answer key document into ordered entries.
///
/// ── Why this is code and not a prompt ─────────────────────────────────────
///
/// <b>Measured on 2026-09-02: a model given the paper alone invented all forty
/// answers, five of them wrong. Given the official key as well, it still got
/// two wrong — Q13 and Q36 of VOL 9 Test 2.</b> Both were alignment failures,
/// not reading failures: the right answer attached to the wrong number. The key
/// for that paper is a bare ordered list with no numbers printed, so aligning
/// it means counting 42 lines onto 40 questions across three passage
/// boundaries, and one line covering a range ("24-26. A, B, D") shifts
/// everything after it.
///
/// Counting is the one thing a program does better than a language model, and
/// an answer key is the artefact where being right 95% of the time is worthless:
/// a wrong key marks every learner who ever sits the paper, silently. So the
/// model transcribes the paper and this reads the key, and the two never mix.
/// → CLAUDE.md rule 9
///
/// ── The two formats, both real ────────────────────────────────────────────
///
/// VOL 9 ships both, and which one a file uses is not predictable from its
/// name — <c>KEY TEST 1-R.docx</c> is numbered and <c>KET TEST 2-R.docx</c> is
/// not, including the typo:
///
/// <list type="number">
/// <item><b>Numbered.</b> <c>"Câu số 12:"</c> on its own line, the answer on
/// the next. Unambiguous; alignment cannot drift.</item>
/// <item><b>Bare list.</b> Answers in question order with no numbers at all,
/// under a block of section headings. Alignment is positional, which is exactly
/// where the model failed — so every entry it produces is checked against the
/// paper's own question types before it becomes a mark.</item>
/// </list>
/// </summary>
public static class AnswerKeyDocument
{
    /// <summary>
    /// Lines that are structure rather than answers. Headings arrive in a block
    /// at the top of a bare-list key rather than beside the answers they label,
    /// so they cannot be used to segment — only to skip.
    /// </summary>
    private static readonly Regex Noise = new(
        @"^(?:VOL\s*\d+\s*)?(?:TEST\s*\d+\s*)?(?:KEY|ĐÁP\s*ÁN|EXPLANATION|EXPLAINATION|TRANSCRIPT)\b"
        + @"|^(?:PASSAGE|SECTION|PART|READING|LISTENING)\s*\d*\s*$"
        + @"|^\.{2,}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Numbered = new(
        @"^C[âa]u\s*s[ốo]\s*(\d{1,2})\s*(?:[-–]\s*(\d{1,2}))?\s*[:.]?\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A bare-list line that names its own range: <c>24-26. A, B, D</c>.</summary>
    private static readonly Regex InlineRange = new(
        @"^(\d{1,2})\s*[-–&]+\s*(\d{1,2})\s*[.:)]?\s*(.*)$",
        RegexOptions.Compiled);

    /// <summary>
    /// A bare-list line that names its own single number.
    /// VOL 9 writes <c>27. D wood</c>; Cambridge writes <c>27  litter</c>
    /// (number, spaces, answer — no punctuation). Both are accepted.
    /// </summary>
    private static readonly Regex InlineSingle = new(
        @"^(\d{1,2})\s*[.:)]\s*(.+)$|^(\d{1,2})\s{2,}(.+)$",
        RegexOptions.Compiled);

    public static IReadOnlyList<AnswerKeyEntry> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !Noise.IsMatch(l))
            .ToList();

        return lines.Any(l => Numbered.IsMatch(l))
            ? ParseNumbered(lines)
            : ParseOrdered(lines);
    }

    /// <summary>
    /// <c>Câu số N:</c> then the answer, which may be on the same line or the
    /// next. Both shapes appear in VOL 9.
    /// </summary>
    private static List<AnswerKeyEntry> ParseNumbered(List<string> lines)
    {
        var entries = new List<AnswerKeyEntry>();

        for (var i = 0; i < lines.Count; i++)
        {
            var match = Numbered.Match(lines[i]);
            if (!match.Success) continue;

            var first = int.Parse(match.Groups[1].Value);
            var last = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : first;

            var value = match.Groups[3].Value.Trim();

            // The answer is on the following line whenever the header line ends
            // at the colon, which is how every VOL 9 file writes it.
            if (value.Length == 0 && i + 1 < lines.Count && !Numbered.IsMatch(lines[i + 1]))
                value = lines[++i].Trim();

            if (value.Length > 0) entries.Add(new AnswerKeyEntry(first, last, value));
        }

        return entries;
    }

    /// <summary>
    /// Answers in question order, numbered only where a line covers a range.
    ///
    /// <b>The counter is advanced by the entry's own width, not by one.</b> That
    /// single rule is what the model got wrong: after <c>"24-26. A, B, D"</c>
    /// the next line answers question 27, and treating it as 25 shifts every
    /// remaining answer by two.
    /// </summary>
    private static List<AnswerKeyEntry> ParseOrdered(List<string> lines)
    {
        var entries = new List<AnswerKeyEntry>();
        var next = 1;

        foreach (var line in lines)
        {
            /*
             * A paper has forty numbers. In a bare list the forty-first line
             * is not an answer — it is whatever the document prints after
             * the key, and VOL 9's Listening keys print the whole transcript
             * there ("(28:03 - 29:19) And at the same time…"), which became
             * "question 183" on 2026-09-04. Positional numbering has to stop
             * where the paper does.
             */
            if (next > 40) break;

            if (InlineRange.Match(line) is { Success: true } range
                && int.Parse(range.Groups[2].Value) > int.Parse(range.Groups[1].Value))
            {
                var first = int.Parse(range.Groups[1].Value);
                var last = int.Parse(range.Groups[2].Value);
                var value = range.Groups[3].Value.Trim();

                if (value.Length == 0) continue;

                entries.Add(new AnswerKeyEntry(first, last, value));
                next = last + 1;
                continue;
            }

            if (InlineSingle.Match(line) is { Success: true } single)
            {
                var number = int.Parse(single.Groups[1].Success
                    ? single.Groups[1].Value
                    : single.Groups[3].Value);
                var value = (single.Groups[2].Success
                    ? single.Groups[2].Value
                    : single.Groups[4].Value).Trim();
                entries.Add(new AnswerKeyEntry(number, number, value));
                next = number + 1;
                continue;
            }

            entries.Add(new AnswerKeyEntry(next, next, line));
            next++;
        }

        return entries;
    }
}
