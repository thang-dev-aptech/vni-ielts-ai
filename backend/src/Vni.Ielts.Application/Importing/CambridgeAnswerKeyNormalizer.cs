using System.Text;
using System.Text.RegularExpressions;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// Turns a Cambridge IELTS Listening/Reading answer-key page (as
/// <c>pdftotext -layout</c> or OCR emits it) into a numbered list that
/// <see cref="AnswerKeyDocument"/> already understands.
///
/// ── Why a separate step ───────────────────────────────────────────────────
///
/// Cambridge prints two columns on one page — Part 1 beside Part 3, Passage 1
/// beside Passage 2 — so a line like <c>"1 litter … 21 A"</c> is two answers,
/// not one. The VOL 9 reader has never seen that shape. Normalising here keeps
/// <see cref="AnswerKeyDocument"/> focused on the two VOL 9 formats it already
/// measures against, and keeps the Cambridge two-column / <c>IN EITHER ORDER</c>
/// quirks in one place that can be tested against a real page extract.
/// </summary>
public static class CambridgeAnswerKeyNormalizer
{
    // "3. NOT GIVEN" / "11. photographer" / "3.E": OCR reads the bold number
    // with a trailing dot on some scans (Cam 16, Cam 18), sometimes with no
    // space after it. The dot is not part of the number; with the dot present
    // the space is optional, without it the space is what separates "1 95"
    // from the answer "195".
    private static readonly Regex NumberedAnswer = new(
        @"^(?<n>\d{1,2})(?:[.:)°]\s*|\s+)(?<a>.+)$",
        RegexOptions.Compiled);

    // Trailing junk after ORDER (" :", " .", " me" — OCR reading the column
    // rule or the page edge) is not part of the heading; Cam 18 T1 lost
    // Q27–30 to a strict `$`, Cam 18 T3 lost Q23–24 to a letters-only guard.
    // The N&M prefix is specific enough that up to six trailing characters
    // of anything can be ignored.
    private static readonly Regex EitherOrder = new(
        @"^(?<a>\d{1,2})\s*(?:&|8|–|-)\s*(?<b>\d{1,2})\s+IN\s+EITHER\s*ORDER\b.{0,6}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Punctuation or symbols OCR put in front of a question number.</summary>
    private static readonly Regex LeadingNoise = new(
        @"^[^\p{L}\p{N}]+(?=\d{1,2}(?:[.:)]?\s|\s*(?:&|–|-)\s*\d))",
        RegexOptions.Compiled);

    private static readonly Regex NoiseLine = new(
        @"^(?:Listening and Reading answer keys|Answer key with extra explanations|"
        + @"in Resource Bank|If you score|you are (?:unlikely|likely)|you may get|"
        + @"acceptable score|examination conditions|recommend that you|"
        + @"LIS\s*TE\s*N\s*ING|RE\s*A\s*D\s*ING|TEST\s+\d+|Reading Passage\s*\d*,?|"
        + @"Part\s+\d+.*|Questions?\s+\d.*|\d{1,3})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns a text block of <c>"N. answer"</c> / <c>"N-M. A, B"</c> lines,
    /// ready for <see cref="AnswerKeyDocument.Parse"/>.
    /// </summary>
    public static string Normalise(string layoutText)
    {
        ArgumentNullException.ThrowIfNull(layoutText);

        // Two columns are interleaved line-by-line. Collect each column as its
        // own stream so "15&16 IN EITHER ORDER" is followed by its letters, not
        // by Part 4's "35 stone" sitting on the same printed line.
        var leftCells = new List<string>();
        var rightCells = new List<string>();
        foreach (var line in layoutText.Split('\n'))
        {
            var (left, right) = SplitColumns(line);
            // OCR glues a stray glyph in front of the number (". 4 pesticides",
            // "« 27 B" — Cam 18 T1), and a line that does not start with its
            // number is a line the numbered parser cannot see, which shifts
            // nothing but silently loses that answer.
            left = LeadingNoise.Replace(left, "");
            right = LeadingNoise.Replace(right, "");
            if (left.Length > 0) leftCells.Add(left);
            if (right.Length > 0) rightCells.Add(right);
        }

        var entries = new SortedDictionary<int, AnswerKeyEntry>();
        ParseColumn(leftCells, entries);
        ParseColumn(rightCells, entries);

        var sb = new StringBuilder();
        foreach (var entry in entries.Values.OrderBy(e => e.First))
        {
            if (entry.Last > entry.First)
                sb.Append(entry.First).Append('-').Append(entry.Last).Append(". ").AppendLine(entry.Raw);
            else
                sb.Append(entry.First).Append(". ").AppendLine(entry.Raw);
        }

        return sb.ToString().TrimEnd();
    }

    private static void ParseColumn(IEnumerable<string> cells, SortedDictionary<int, AnswerKeyEntry> entries)
    {
        PendingEither? pending = null;

        foreach (var cell in cells)
        {
            if (NoiseLine.IsMatch(cell)) continue;

            if (EitherOrder.Match(cell) is { Success: true } either)
            {
                var a = int.Parse(either.Groups["a"].Value);
                var b = int.Parse(either.Groups["b"].Value);
                // "22823 IN EITHER ORDER" is "22&23" with the ampersand read as
                // an 8 (Cam 19 T1). The pair is always consecutive, which is
                // what lets the 8 be accepted as a separator and nothing else.
                if (b == a + 1)
                {
                    FlushPending(pending, entries);
                    pending = new PendingEither(a, b, []);
                    continue;
                }
            }

            // A letter under IN EITHER ORDER, as OCR renders it: "B", "Cc",
            // "8B", "E ." — one option letter with noise around it.
            if (pending is { } open && LoneLetter(cell) is { } letter)
            {
                open.Letters.Add(letter);
                if (open.Letters.Count >= open.Last - open.First + 1)
                {
                    FlushPending(open, entries);
                    pending = null;
                }
                continue;
            }

            if (pending is not null)
            {
                FlushPending(pending, entries);
                pending = null;
            }

            if (NumberedAnswer.Match(cell) is { Success: true } numbered)
            {
                var n = int.Parse(numbered.Groups["n"].Value);
                if (n is < 1 or > 40) continue;
                var answer = CleanAnswer(numbered.Groups["a"].Value);
                if (answer.Length == 0) continue;
                // First reading wins. The key prints each number once; a second
                // "20 …" further down is page furniture that happens to start
                // with a number ("20 and 2019." from the explanations page on
                // Cam 16 T4), and it used to overwrite the real "20 F".
                entries.TryAdd(n, new AnswerKeyEntry(n, n, answer));
            }
        }

        FlushPending(pending, entries);
    }

    /// <summary>
    /// Split a layout line into left/right cells at the largest run of spaces
    /// near the middle of the printable width. A one-column line returns the
    /// whole line on the left.
    ///
    /// <b>Leading indentation is not a column gap.</b> Cambridge prints the
    /// letters under <c>IN EITHER ORDER</c> indented —
    /// <c>"     A"</c> — and treating that indent as a mid-page split puts the
    /// letter into the right-hand stream, where Part 4 then swallows it.
    /// </summary>
    public static (string Left, string Right) SplitColumns(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return ("", "");

        var mid = Math.Max(line.Length / 2, 20);
        var bestStart = -1;
        var bestLen = 0;
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] != ' ') { i++; continue; }
            var start = i;
            while (i < line.Length && line[i] == ' ') i++;
            var len = i - start;
            // A gap that starts at column 0 is indentation, not a column break.
            if (start == 0) continue;
            // Both sides must carry content, otherwise this is trailing space.
            if (i >= line.Length) continue;
            // Cambridge leaves a wide gutter between columns (≥8 spaces). The
            // 2–4 spaces between a question number and its answer must not split.
            if (len >= 8 && Math.Abs(start - mid) <= Math.Abs(bestStart - mid) + 5 && len >= bestLen)
            {
                bestStart = start;
                bestLen = len;
            }
        }

        if (bestStart < 0 || bestLen < 8)
            return (line.Trim(), "");

        var left = line[..bestStart].Trim();
        var right = line[(bestStart + bestLen)..].Trim();
        return (left, right);
    }

    private static void FlushPending(PendingEither? pending, SortedDictionary<int, AnswerKeyEntry> entries)
    {
        if (pending is null || pending.Letters.Count == 0) return;
        entries[pending.First] = new AnswerKeyEntry(
            pending.First, pending.Last, string.Join(", ", pending.Letters));
    }

    /// <summary>
    /// The one option letter a short OCR cell stands for, or null when the
    /// cell is not that: more than one distinct letter, or too long.
    /// </summary>
    private static string? LoneLetter(string cell)
    {
        var trimmed = cell.Trim();
        if (trimmed.Length is 0 or > 4) return null;
        // Upper case first: the option letter is printed in capitals, and the
        // lower-case letters OCR adds ("D z", "Cc") are page noise. Only when
        // no capital survives is a lower-case reading ("b") taken as the letter.
        var upper = trimmed.Where(c => c is >= 'A' and <= 'J').Distinct().ToList();
        if (upper.Count == 1) return upper[0].ToString();
        if (upper.Count > 1) return null;
        var lower = trimmed.Where(c => c is >= 'a' and <= 'j').Select(char.ToUpperInvariant).Distinct().ToList();
        return lower.Count == 1 ? lower[0].ToString() : null;
    }

    private static string CleanAnswer(string raw)
    {
        var value = raw.Trim();
        // Drop trailing page furniture that OCR sometimes glues on.
        value = Regex.Replace(value, @"\s+\d{2,3}$", "");
        // A lone option letter followed by one stray digit ("A 3", "F 4" —
        // Cam 18 T1, the digit is column-edge noise) is the letter. A real
        // answer that ends in a digit ("ten / 10 days", "4.95") is not a lone
        // letter and is left alone.
        value = Regex.Replace(value, @"^([A-Za-z])\s+\d$", "$1");
        return value.Trim();
    }

    private sealed class PendingEither(int first, int last, List<string> letters)
    {
        public int First { get; } = first;
        public int Last { get; } = last;
        public List<string> Letters { get; } = letters;
    }
}
