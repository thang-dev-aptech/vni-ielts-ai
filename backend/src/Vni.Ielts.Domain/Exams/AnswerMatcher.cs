using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Vni.Ielts.Domain.Exams;

/// <summary>
/// Compares a learner's answer against the key.
///
/// This is less trivial than it looks and is a common source of unfair
/// marking, which is why every rule here is configuration on the
/// <see cref="ScoringProfile"/> rather than a decision baked into code.
///
/// Deliberately strict in two places:
/// <list type="bullet">
/// <item><b>Word limits are enforced.</b> "NO MORE THAN TWO WORDS" is a scored
/// condition — an over-length answer is wrong even if it contains the right
/// words. Being lenient here inflates scores against the real exam.</item>
/// <item><b>Spelling tolerance is off by default</b>, because IELTS penalises
/// misspelling. Turning it on is a per-exam decision, not a kindness.</item>
/// </list>
/// </summary>
public static partial class AnswerMatcher
{
    public static bool IsCorrect(Question question, string? submitted, AnswerMatchingRules rules)
    {
        if (question.AnswerKey is null || question.AnswerKey.Accepted.Count == 0)
            return false;                      // No key: never award a mark.

        if (string.IsNullOrWhiteSpace(submitted))
            return false;

        var effective = question.AnswerKey.Overrides ?? rules;

        // Word limit first. An over-length answer is wrong regardless of what
        // it contains, so checking it after a content match would award marks
        // the real exam would not.
        if (question.MaxWords is { } limit && CountWords(submitted) > limit)
            return false;

        return question.Type switch
        {
            QuestionType.MultipleSelect => MatchesSet(question.AnswerKey, submitted, effective),
            QuestionType.Matching => MatchesPair(question.AnswerKey, submitted, effective),
            _ => MatchesSingle(question.AnswerKey, submitted, effective),
        };
    }

    private static bool MatchesSingle(AnswerKey key, string submitted, AnswerMatchingRules rules)
    {
        var candidate = Normalise(submitted, rules);
        return key.Accepted.Any(a =>
            a.Single is not null && Normalise(a.Single, rules) == candidate);
    }

    /// <summary>
    /// Multiple-select: every required option must be present and nothing
    /// extra. Order does not matter — "A,C" and "C,A" are the same answer, and
    /// treating them differently would mark a correct response wrong.
    /// </summary>
    private static bool MatchesSet(AnswerKey key, string submitted, AnswerMatchingRules rules)
    {
        var given = submitted
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => Normalise(v, rules))
            .ToHashSet(StringComparer.Ordinal);

        return key.Accepted.Any(a =>
            a.All is not null
            && given.SetEquals(a.All.Select(v => Normalise(v, rules))));
    }

    /// <summary>Matching: "left:right" or "left=right".</summary>
    private static bool MatchesPair(AnswerKey key, string submitted, AnswerMatchingRules rules)
    {
        var separator = submitted.IndexOfAny([':', '=']);
        if (separator <= 0) return false;

        var left = Normalise(submitted[..separator], rules);
        var right = Normalise(submitted[(separator + 1)..], rules);

        return key.Accepted.Any(a =>
            a.Pair is { } pair
            && Normalise(pair.Left, rules) == left
            && Normalise(pair.Right, rules) == right);
    }

    /// <summary>
    /// Both sides of a comparison go through this, so the rules apply
    /// symmetrically. Normalising only the learner's side would make the key
    /// itself sensitive to how it was typed.
    /// </summary>
    private static string Normalise(string value, AnswerMatchingRules rules)
    {
        var result = value;

        if (rules.TrimWhitespace)
            result = result.Trim();

        if (rules.CollapseInnerWhitespace)
            result = WhitespaceRun().Replace(result, " ");

        if (!rules.CaseSensitive)
            result = result.ToLowerInvariant();

        if (rules.NumericEquivalence && LooksNumeric(result))
            result = NormaliseNumber(result);

        // Unicode normalisation last. Vietnamese diacritics have more than one
        // valid byte sequence — a composed 'ế' and a decomposed 'e' + combining
        // acute look identical and compare unequal. Exam content is English,
        // but a learner typing on a Vietnamese keyboard is not hypothetical.
        return result.Normalize(NormalizationForm.FormC);
    }

    private static bool LooksNumeric(string value) =>
        value.Length > 0 && value.All(c => char.IsDigit(c) || c is ',' or '.' or ' ');

    /// <summary>Treats "1,000" and "1000" as the same answer.</summary>
    private static string NormaliseNumber(string value)
    {
        var stripped = value.Replace(",", string.Empty).Replace(" ", string.Empty);
        return decimal.TryParse(stripped, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)
            ? n.ToString(CultureInfo.InvariantCulture)
            : value;
    }

    private static int CountWords(string value) =>
        value.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
