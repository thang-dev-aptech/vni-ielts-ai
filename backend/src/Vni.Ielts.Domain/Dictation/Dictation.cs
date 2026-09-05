namespace Vni.Ielts.Domain.Dictation;

/// <summary>
/// Nghe chép chính tả — `M-22`.
///
/// <b>Not an exam, and modelled apart from one on purpose.</b> There is no
/// timer, no session, no band and no entitlement: a learner plays a sentence
/// as often as they like and types it back. Folding it into
/// <c>ExamSession</c> would drag a server-authoritative deadline and a scoring
/// profile onto something that has neither, and every later change to the exam
/// engine would have to keep working for a feature that is not an exam.
/// </summary>
public sealed record DictationSet(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<DictationSentence> Sentences);

/// <summary>
/// One sentence.
///
/// <b><see cref="Text"/> never leaves the server before a comparison.</b> Same
/// rule as an exam answer key and for the same reason: a client that holds the
/// sentence can render the exercise as already solved, and the learner is the
/// one who loses. Comparison happens on the server, and what comes back is a
/// per-word verdict — never the sentence itself until the learner has tried.
/// → threat `T7`
/// </summary>
public sealed record DictationSentence(int Order, string AudioKey, string Text);

public enum WordVerdict
{
    /// <summary>Typed, and matches.</summary>
    Correct,

    /// <summary>Typed, but not what was said.</summary>
    Wrong,

    /// <summary>In the sentence, absent from what was typed.</summary>
    Missing,

    /// <summary>Typed, but not in the sentence at all.</summary>
    Extra,
}

public sealed record WordResult(WordVerdict Verdict, string? Expected, string? Typed);

public sealed record DictationComparison(
    IReadOnlyList<WordResult> Words, int Correct, int Total)
{
    public bool IsPerfect => Correct == Total && Words.All(w => w.Verdict != WordVerdict.Extra);
}

/// <summary>
/// Compares what was typed against what was said, word by word.
///
/// <b>An alignment, not a position-by-position walk.</b> Comparing index to
/// index means one missing word at the start marks every word after it wrong,
/// which tells a learner nothing except that they failed. The longest common
/// subsequence finds what genuinely lines up, so a single dropped article is
/// reported as a single dropped article.
///
/// <b>Normalisation is for comparison only.</b> Case and surrounding
/// punctuation are ignored — dictation is about hearing words, and marking
/// someone wrong for a missing comma teaches them nothing about listening. The
/// original spelling is what gets shown back, never the normalised form.
/// </summary>
public static class DictationComparer
{
    public static DictationComparison Compare(string expected, string typed)
    {
        var expectedWords = Split(expected);
        var typedWords = Split(typed);

        var lcs = LongestCommonSubsequence(
            [.. expectedWords.Select(Normalise)],
            [.. typedWords.Select(Normalise)]);

        var results = new List<WordResult>();
        int e = 0, t = 0, correct = 0;

        foreach (var (ei, ti) in lcs)
        {
            // Everything before this match on either side did not line up.
            // Pair them off as substitutions while both have spares, so a
            // mis-heard word reads as one wrong word rather than as a deletion
            // plus an insertion.
            while (e < ei && t < ti)
            {
                results.Add(new WordResult(WordVerdict.Wrong, expectedWords[e], typedWords[t]));
                e++; t++;
            }

            while (e < ei) results.Add(new WordResult(WordVerdict.Missing, expectedWords[e++], null));
            while (t < ti) results.Add(new WordResult(WordVerdict.Extra, null, typedWords[t++]));

            results.Add(new WordResult(WordVerdict.Correct, expectedWords[ei], typedWords[ti]));
            correct++;
            e = ei + 1;
            t = ti + 1;
        }

        while (e < expectedWords.Count && t < typedWords.Count)
        {
            results.Add(new WordResult(WordVerdict.Wrong, expectedWords[e], typedWords[t]));
            e++; t++;
        }

        while (e < expectedWords.Count)
            results.Add(new WordResult(WordVerdict.Missing, expectedWords[e++], null));

        while (t < typedWords.Count)
            results.Add(new WordResult(WordVerdict.Extra, null, typedWords[t++]));

        return new DictationComparison(results, correct, expectedWords.Count);
    }

    private static IReadOnlyList<string> Split(string text) =>
        [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>
    /// Lowercased, with surrounding punctuation removed. Inner apostrophes and
    /// hyphens survive — "don't" and "well-known" are single words a learner
    /// either heard or did not.
    /// </summary>
    private static string Normalise(string word) =>
        word.Trim('.', ',', '!', '?', ';', ':', '"', '“', '”', '(', ')')
            .ToLowerInvariant();

    /// <summary>Index pairs that line up, in order.</summary>
    private static List<(int Expected, int Typed)> LongestCommonSubsequence(
        IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var lengths = new int[a.Count + 1, b.Count + 1];

        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                lengths[i, j] = a[i] == b[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

        var pairs = new List<(int, int)>();
        int x = 0, y = 0;

        while (x < a.Count && y < b.Count)
        {
            if (a[x] == b[y]) { pairs.Add((x, y)); x++; y++; }
            else if (lengths[x + 1, y] >= lengths[x, y + 1]) x++;
            else y++;
        }

        return pairs;
    }
}
