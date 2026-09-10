using System.Text.Json.Nodes;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// Compares a keyed package against the rules the paper states about its own
/// questions. Every check here is a contradiction between two declarations in
/// the same document, so every finding is an error and none is a judgement
/// call — there is nothing for a reviewer to weigh, and therefore no override.
///
/// <b>Layer 3 of the cross-check.</b> Layer 1 (counting) and layer 2 (shape)
/// already run inside <see cref="AnswerKeyInjection"/>. This layer exists
/// because those two ask whether an answer *could* belong to a question, and
/// three fields on the paper say more than that.
/// </summary>
public static class PaperKeyConsistency
{
    public const string MarkCountCode = "KEY_MARK_COUNT_MISMATCH";
    public const string WordLimitCode = "KEY_EXCEEDS_WORD_LIMIT";
    public const string LabelReuseCode = "KEY_LABEL_REUSED";

    public static IReadOnlyList<PackageFinding> Inspect(string packageJson)
    {
        var package = JsonNode.Parse(packageJson)?.AsObject()
            ?? throw new ArgumentException(
                "The package did not parse as an object.", nameof(packageJson));

        var findings = new List<PackageFinding>();
        var usedLabels = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var (question, path) in Questions(package))
        {
            var accepted = question["answerKey"]?["accepted"]?.AsArray();
            if (accepted is null || accepted.Count == 0) continue;

            var order = question["order"]?.GetValue<int>() ?? 0;

            CheckMarkCount(question, accepted, order, path, findings);
            CheckWordLimit(question, accepted, order, path, findings);
            CheckLabelReuse(question, accepted, order, path, usedLabels, findings);
        }

        return findings;
    }

    /// <summary>
    /// "Choose TWO letters" occupies two numbered lines on the answer sheet
    /// and is worth two marks. An answer with one letter under it is a key
    /// that lost a value, and the scorer would mark every candidate wrong.
    /// </summary>
    private static void CheckMarkCount(
        JsonObject question, JsonArray accepted, int order, string path,
        List<PackageFinding> findings)
    {
        var marks = question["marks"]?.GetValue<int>() ?? 1;
        if (marks <= 1) return;

        foreach (var alternative in accepted)
        {
            var count = alternative is JsonArray array ? array.Count : 1;
            if (count == marks) continue;

            findings.Add(new PackageFinding(
                "error", MarkCountCode, path,
                $"Question {order} is worth {marks} marks, but the key gives {count} "
                + "answer(s) for it. The key document may have folded them onto one line."));
            return;
        }
    }

    /// <summary>
    /// The paper prints "NO MORE THAN TWO WORDS"; an over-length answer is
    /// marked wrong. <see cref="Vni.Ielts.Domain.Exams.AnswerMatcher.ExceedsWordLimit"/>
    /// already applies that rule to a learner. Nothing applied it to the key.
    /// </summary>
    private static void CheckWordLimit(
        JsonObject question, JsonArray accepted, int order, string path,
        List<PackageFinding> findings)
    {
        var limit = question["constraints"]?["maxWords"]?.GetValue<int>();
        if (limit is not { } maxWords) return;

        foreach (var alternative in accepted)
        {
            if (alternative is not JsonValue value) continue;
            var words = WordCount(value.GetValue<string>());
            if (words <= maxWords) continue;

            findings.Add(new PackageFinding(
                "error", WordLimitCode, path,
                $"Question {order} allows at most {maxWords} word(s); the key holds an "
                + $"answer of {words}. A learner typing it would be marked wrong."));
            return;
        }
    }

    /// <summary>
    /// Only when the paper declares <c>eachLetterOnce</c>. A group that
    /// permits reuse ("NB You may use any letter more than once") is normal
    /// IELTS and must not be reported.
    /// </summary>
    private static void CheckLabelReuse(
        JsonObject question, JsonArray accepted, int order, string path,
        Dictionary<string, Dictionary<string, int>> usedLabels,
        List<PackageFinding> findings)
    {
        var group = question["group"];
        if (group?["eachLetterOnce"]?.GetValue<bool>() is not true) return;
        if (group["id"]?.GetValue<string>() is not { Length: > 0 } groupId) return;
        if (accepted[0] is not JsonValue first) return;

        var label = first.GetValue<string>();
        if (!usedLabels.TryGetValue(groupId, out var seen))
            usedLabels[groupId] = seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (seen.TryGetValue(label, out var earlier))
        {
            findings.Add(new PackageFinding(
                "error", LabelReuseCode, path,
                $"Question {order} reuses a label already used by question {earlier} in a "
                + "group the paper marks as use-each-letter-once. One of the two is wrong."));
            return;
        }

        seen[label] = order;
    }

    private static int WordCount(string value) =>
        value.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static IEnumerable<(JsonObject Question, string Path)> Questions(JsonObject package)
    {
        var sectionIndex = -1;
        foreach (var section in package["sections"]?.AsArray() ?? [])
        {
            sectionIndex++;
            var module = section?["module"]?.GetValue<string>() ?? "unknown";
            var partIndex = -1;
            foreach (var part in section?["parts"]?.AsArray() ?? [])
            {
                partIndex++;
                var questionIndex = -1;
                foreach (var question in part?["questions"]?.AsArray() ?? [])
                {
                    questionIndex++;
                    if (question is JsonObject q)
                        yield return (q, $"/sections/{module}/parts/{partIndex}/questions/{questionIndex}");
                }
            }
        }
    }
}
