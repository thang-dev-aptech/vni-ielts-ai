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
    public static bool IsCorrectSlot(
        ResponseSlot slot, Question question, string? submitted, AnswerMatchingRules rules)
    {
        if (slot.AnswerKey is null || slot.AnswerKey.Accepted.Count == 0)
            return false;

        if (string.IsNullOrWhiteSpace(submitted))
            return false;

        if (ExceedsWordLimit(question, submitted))
            return false;

        var effective = slot.AnswerKey.Overrides ?? rules;
        return slot.AnswerKey.Accepted.Any(accepted => Matches(accepted, submitted, effective));
    }

    public static bool IsCorrect(Question question, string? submitted, AnswerMatchingRules rules)
    {
        if (question.AnswerKey is null || question.AnswerKey.Accepted.Count == 0)
            return false;                      // No key: never award a mark.

        if (string.IsNullOrWhiteSpace(submitted))
            return false;

        var effective = question.AnswerKey.Overrides ?? rules;

        if (ExceedsWordLimit(question, submitted))
            return false;

        // <b>The shape of the answer comes from the key, not from the question
        // type, and that distinction is what this class got wrong.</b>
        //
        // `AcceptedAnswer` already carries exactly one of three shapes — a
        // value, a set, a pair — because the package schema models it that way.
        // Routing on `question.Type` instead meant guessing at that, and the
        // guess was wrong for the commonest Reading question in the catalogue:
        // "Matching" covers both *choose a heading for this paragraph*, whose
        // answer is one key, and *pair these two lists*. The marker assumed the
        // second, so all six matching questions in Exam 1's Reading were marked
        // wrong whatever the learner picked.
        //
        // Asking the key removes the guess. A key holding a pair wants a pair;
        // a key holding one value wants one value; and the question type is
        // left to do what it is actually for, which is deciding how the
        // question is drawn.
        return question.AnswerKey.Accepted.Any(
            accepted => Matches(accepted, submitted, effective));
    }

    private static bool Matches(AcceptedAnswer accepted, string submitted, AnswerMatchingRules rules) =>
        accepted switch
        {
            { All: { } all } => MatchesSet(all, submitted, rules),
            { Pair: { } pair } => MatchesPair(pair, submitted, rules),
            { Single: { } single } => Normalise(single, rules) == Normalise(submitted, rules),

            // A key entry with nothing in it cannot be satisfied. Awarding the
            // mark would be crediting an answer nobody wrote down.
            _ => false,
        };

    /// <summary>
    /// The characters a caller may use between the members of a set.
    ///
    /// <b>The pipe is here because the learner app uses it, and it uses it for
    /// a reason:</b> a comma and a space both occur inside real answers, so a
    /// separator that is also a legal character is a separator that will one
    /// day split an answer in half. The others stay accepted because a set is
    /// unordered punctuation either way, and refusing a hand-written
    /// <c>"A, D"</c> would be strictness that protects nothing.
    ///
    /// This list is the written half of a contract. → <c>AnswerContractTests</c>
    /// </summary>
    private static readonly char[] SetSeparators = ['|', ',', ';', ' '];

    /// <summary>
    /// Multiple-select: every required option present, and nothing extra.
    ///
    /// Order does not matter — "A|C" and "C|A" are the same answer, and
    /// treating them differently would mark a correct response wrong.
    /// </summary>
    private static bool MatchesSet(
        IReadOnlyList<string> all, string submitted, AnswerMatchingRules rules)
    {
        var given = submitted
            .Split(SetSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => Normalise(v, rules))
            .ToHashSet(StringComparer.Ordinal);

        return given.SetEquals(all.Select(v => Normalise(v, rules)));
    }

    /// <summary>Pair matching: "left:right" or "left=right".</summary>
    private static bool MatchesPair(
        (string Left, string Right) pair, string submitted, AnswerMatchingRules rules)
    {
        var separator = submitted.IndexOfAny([':', '=']);
        if (separator <= 0) return false;

        return Normalise(pair.Left, rules) == Normalise(submitted[..separator], rules)
            && Normalise(pair.Right, rules) == Normalise(submitted[(separator + 1)..], rules);
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

    public static bool ExceedsWordLimit(Question question, string? submitted) =>
        question.MaxWords is { } limit
        && !string.IsNullOrWhiteSpace(submitted)
        && CountWords(submitted) > limit;

    public static SlotOutcome Outcome(string? submitted, bool isCorrect)
    {
        if (string.IsNullOrWhiteSpace(submitted)) return SlotOutcome.Unanswered;
        return isCorrect ? SlotOutcome.Correct : SlotOutcome.Incorrect;
    }

    /// <summary>
    /// The canonical correct answer for post-submit review. Uses the first
    /// accepted variant only — alternates are equivalent for marking.
    /// </summary>
    public static string? FormatAcceptedAnswer(AnswerKey? key, AnswerMatchingRules rules)
    {
        if (key is null || key.Accepted.Count == 0) return null;

        var accepted = key.Accepted[0];
        return accepted switch
        {
            { Single: { } single } => single,
            { All: { } all } => string.Join(", ", all),
            { Pair: { } pair } => $"{pair.Left}:{pair.Right}",
            _ => null,
        };
    }

    private static int CountWords(string value) =>
        value.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
