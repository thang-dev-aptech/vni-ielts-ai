using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Assessment;

/// <summary>
/// Layer 1 of Writing marking: deterministic admission before any AI call.
///
/// Empty, ≤20 remaining words after discounting copied prompt, or not English
/// are terminal — a band is produced in code, and nothing is sent to a model.
/// That is both a cost saving and the injection defence: an essay whose only
/// purpose is to instruct the model never reaches it. → writing-marking-rubric-v2
/// </summary>
public static class WritingAdmission
{
    public const int UnderLengthWordCeiling = 20;

    public abstract record Result
    {
        public sealed record Proceed(
            int WordCount,
            IReadOnlyList<string> CopiedSpans) : Result;

        public sealed record Terminal(
            BandScore Band,
            Reason Reason,
            int WordCount,
            IReadOnlyList<string> CopiedSpans) : Result;
    }

    public enum Reason
    {
        Empty,
        UnderLength,
        NotEnglish,
    }

    public static Result Assess(string submission, string prompt)
    {
        var sanitized = submission ?? string.Empty;
        var (remaining, copied) = DiscountCopiedPrompt(sanitized, prompt ?? string.Empty);
        var wordCount = CountWords(remaining);

        if (wordCount == 0)
        {
            return new Result.Terminal(
                BandScore.Create(0m), Reason.Empty, 0, copied);
        }

        if (!LooksLikeEnglish(remaining))
        {
            return new Result.Terminal(
                BandScore.Create(0m), Reason.NotEnglish, wordCount, copied);
        }

        if (wordCount <= UnderLengthWordCeiling)
        {
            return new Result.Terminal(
                BandScore.Create(1m), Reason.UnderLength, wordCount, copied);
        }

        return new Result.Proceed(wordCount, copied);
    }

    /// <summary>
    /// A terminal admission becomes a <see cref="SectionMarking"/> with no
    /// model claim behind it. Feedback language follows the approved default
    /// (Vietnamese) unless the caller asks for English.
    /// </summary>
    public static SectionMarking ToMarking(
        Rubric rubric,
        int? taskNumber,
        Result.Terminal terminal,
        string language = "vi")
    {
        var feedback = FeedbackFor(terminal.Reason, language);
        var assessments = rubric.Criteria
            .Select(key => CriterionAssessment.ForAdmission(key, terminal.Band, feedback))
            .ToList();

        return new SectionMarking(
            rubric.Module,
            rubric.Version,
            assessments,
            terminal.Band,
            ReportedBand: null,
            Flags: [],
            UngroundedEvidence: [],
            taskNumber,
            Advisories: [AdvisoryId(terminal.Reason)]);
    }

    public static string AdvisoryId(Reason reason) => reason switch
    {
        Reason.Empty => "empty-or-not-english",
        Reason.NotEnglish => "empty-or-not-english",
        Reason.UnderLength => "under-20-words",
        _ => reason.ToString(),
    };

    internal static (string Remaining, IReadOnlyList<string> Copied) DiscountCopiedPrompt(
        string submission, string prompt)
    {
        if (string.IsNullOrWhiteSpace(submission)) return (string.Empty, []);

        var remaining = submission;
        var copied = new List<string>();

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            var promptNorm = CollapseWhitespace(prompt);
            if (promptNorm.Length >= 12
                && remaining.Contains(promptNorm, StringComparison.OrdinalIgnoreCase))
            {
                copied.Add(promptNorm);
                remaining = ReplaceInsensitive(remaining, promptNorm, " ");
            }

            foreach (var sentence in SplitSentences(prompt))
            {
                if (CountWords(sentence) < 8) continue;
                if (!remaining.Contains(sentence, StringComparison.OrdinalIgnoreCase)) continue;

                copied.Add(sentence);
                remaining = ReplaceInsensitive(remaining, sentence, " ");
            }
        }

        return (CollapseWhitespace(remaining), copied);
    }

    internal static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        return text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// Script + Vietnamese-diacritic heuristic. Latin English is the pass;
    /// CJK/Cyrillic/Arabic fail; a high density of Vietnamese diacritics is
    /// treated as not English. This is a gate, not a language identifier.
    /// </summary>
    internal static bool LooksLikeEnglish(string text)
    {
        var letters = text.Where(char.IsLetter).ToArray();
        if (letters.Length == 0) return false;

        var latin = letters.Count(IsLatinLetter);
        if (latin * 2 < letters.Length) return false;

        var vietnamese = letters.Count(IsVietnameseDiacriticLetter);
        return vietnamese * 8 <= letters.Length;
    }

    public static bool LooksLikeNotes(string text)
    {
        var lines = text
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count < 3) return false;

        var bullets = lines.Count(l =>
            l.StartsWith('-') || l.StartsWith('*') || l.StartsWith('•')
            || Regex.IsMatch(l, @"^\d+[.)]\s"));

        return bullets * 2 >= lines.Count;
    }

    private static string FeedbackFor(Reason reason, string language)
    {
        var vi = !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);

        return reason switch
        {
            Reason.Empty => vi
                ? "Không có bài viết để chấm. Band 0 trên mọi tiêu chí."
                : "No assessable writing was submitted. Band 0 on every criterion.",
            Reason.UnderLength => vi
                ? "Bài viết còn lại không quá 20 từ sau khi trừ phần chép đề. Band 1 trên mọi tiêu chí."
                : "After discounting copied prompt text, 20 words or fewer remain. Band 1 on every criterion.",
            Reason.NotEnglish => vi
                ? "Bài viết không phải tiếng Anh. Band 0 trên mọi tiêu chí."
                : "The response is not written in English. Band 0 on every criterion.",
            _ => vi ? "Không chấm được bài này." : "The response cannot be assessed.",
        };
    }

    private static bool IsLatinLetter(char ch)
    {
        var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
        if (cat is not (UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter))
        {
            return false;
        }

        return ch <= 0x024F || (ch >= 0x1E00 && ch <= 0x1EFF);
    }

    private static bool IsVietnameseDiacriticLetter(char ch) =>
        "ăâêôơưđĂÂÊÔƠƯĐáàảãạấầẩẫậắằẳẵặéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵÁÀẢÃẠẤẦẨẪẬẮẰẲẴẶÉÈẺẼẸẾỀỂỄỆÍÌỈĨỊÓÒỎÕỌỐỒỔỖỘỚỜỞỠỢÚÙỦŨỤỨỪỬỮỰÝỲỶỸỴ"
            .Contains(ch);

    private static string CollapseWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = true;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            sb.Append(ch);
            lastWasSpace = false;
        }

        return sb.ToString().Trim();
    }

    private static IEnumerable<string> SplitSentences(string prompt) =>
        prompt
            .Split(['.', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(CollapseWhitespace)
            .Where(s => s.Length > 0);

    private static string ReplaceInsensitive(string haystack, string needle, string replacement)
    {
        var index = haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return haystack;

        return string.Concat(
            haystack.AsSpan(0, index),
            replacement,
            haystack.AsSpan(index + needle.Length));
    }
}
