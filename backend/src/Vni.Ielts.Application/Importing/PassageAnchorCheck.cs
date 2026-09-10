using System.Text.Json.Nodes;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Importing;

/// <summary>Whether one question's key answer was found in its own passage.</summary>
public sealed record QuestionAnchor(string QuestionId, int Order, bool Anchored);

/// <summary>
/// A group whose answers stop following the passage's order at
/// <paramref name="Message"/>'s named question.
///
/// <b>Deliberately not a <see cref="PackageFinding"/>.</b> A finding has only
/// a severity string, and a caller that has to read <c>"warning"</c> to know
/// this belongs in <c>draft.Warnings</c> rather than <c>draft.Findings</c> is
/// exactly how the two fell out of sync before: filed as a
/// <c>PackageFinding</c> of severity <c>"warning"</c>, this result never
/// blocked (<see cref="ImportReviewWorkflow.ApproveAsync"/> only checks
/// <c>Severity == "error"</c>) and could never be cleared (there is no
/// resolve path for a finding at all) — a key running backwards mid-group
/// could be approved with nobody having seen it. A distinct type forces the
/// caller to decide, once, which collection this goes in.
/// </summary>
public sealed record AnchorOrderIssue(string Path, string Message);

public sealed record AnchorReport(
    IReadOnlyList<PackageFinding> Findings,
    IReadOnlyList<QuestionAnchor> Anchors,
    IReadOnlyList<AnchorOrderIssue> OrderIssues);

/// <summary>
/// Layer 4 of the cross-check: anchors each answer in the text it must have
/// come from.
///
/// <b>Why this layer is worth more than the three above it.</b> IELTS requires
/// the answer to a completion, short-answer or labelling question to be words
/// taken from the passage, and those types are about half a Reading paper.
/// So the answer must be findable — a string search, no model, no cost — and
/// within a group the answers must appear in the passage in question order.
///
/// A key shifted by one line therefore makes the sequence of found positions
/// run <i>backwards</i> at the shift point, and names the first bad question.
/// That is exactly the failure layers 1 to 3 are blind to: on VOL 9 Test 2,
/// question 13 held FALSE where the answer was TRUE and question 36 held A
/// where the answer was C. Both are legal answers of the right shape.
///
/// <b>What it cannot do.</b> True/False/Not Given, Yes/No/Not Given,
/// multiple-choice and matching answers are not words from the passage, so
/// they are skipped entirely and left to layer 5 and to a human.
/// </summary>
public static class PassageAnchorCheck
{
    public const string NotInPassageCode = "KEY_ANSWER_NOT_IN_PASSAGE";
    public const string OutOfOrderCode = "KEY_ANSWERS_OUT_OF_PASSAGE_ORDER";
    public const string PassageMismatchCode = "PASSAGE_DOES_NOT_MATCH_QUESTIONS";

    /// <summary>The types whose answers IELTS takes from the passage.</summary>
    private static readonly HashSet<string> Anchorable =
        new(StringComparer.OrdinalIgnoreCase) { "completion", "short-answer", "labelling" };

    public static AnchorReport Inspect(string packageJson)
    {
        var package = JsonNode.Parse(packageJson)?.AsObject()
            ?? throw new ArgumentException(
                "The package did not parse as an object.", nameof(packageJson));

        var findings = new List<PackageFinding>();
        var anchors = new List<QuestionAnchor>();
        var orderIssues = new List<AnchorOrderIssue>();

        foreach (var (part, path, source) in Parts(package))
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            var haystack = AnswerMatcher.Normalise(source, AnswerMatchingRules.Default);

            foreach (var group in AnchorableQuestionsByGroup(part))
            {
                var positions = new List<(int Order, string Id, IReadOnlyList<int> At)>();

                foreach (var (question, order, id, answer) in group)
                {
                    var needle = AnswerMatcher.Normalise(answer, AnswerMatchingRules.Default);
                    var at = Occurrences(haystack, needle);
                    positions.Add((order, id, at));
                    anchors.Add(new QuestionAnchor(id, order, at.Count > 0));
                }

                var missing = positions.Where(p => p.At.Count == 0).ToArray();

                /*
                 * Nothing anchored means the passage is wrong, not the key —
                 * a truncated body, the wrong passage, a bad OCR crop. Saying
                 * "12 answers are missing" would send an administrator to fix
                 * twelve answers that are all correct.
                 */
                if (missing.Length == positions.Count && positions.Count > 1)
                {
                    findings.Add(new PackageFinding(
                        "error", PassageMismatchCode, path,
                        $"None of the {positions.Count} anchorable answers in this group appears "
                        + "in the passage. The passage is probably the wrong one or was cut short; "
                        + "re-check the parsed paper before touching the key."));
                    continue;
                }

                foreach (var (order, _, _) in missing)
                {
                    findings.Add(new PackageFinding(
                        "error", NotInPassageCode, path,
                        $"The key answer for question {order} does not appear in the passage. "
                        + "This question type takes its answer from the text, so no learner "
                        + "could produce it."));
                }

                if (missing.Length > 0) continue;

                if (FirstBackwardsAt(positions) is { } culprit)
                {
                    /*
                     * Order has a rare genuine exception; containment and a
                     * whole-group mismatch do not. So this result is not a
                     * PackageFinding at all — it is filed by the caller as an
                     * ImportReviewWarning, which a reviewer can clear with a
                     * recorded reason (P-19), the same shape a missing
                     * transcript already uses.
                     */
                    orderIssues.Add(new AnchorOrderIssue(
                        path,
                        $"Answers in this group stop following the passage's order at question "
                        + $"{culprit}. That is the shape of a key shifted by a line. Rare "
                        + "exceptions exist, so check against the original key before clearing."));
                }
            }
        }

        return new AnchorReport(findings, anchors, orderIssues);
    }

    /// <summary>
    /// The order check, done as a greedy earliest-legal walk rather than on
    /// the first occurrence of each answer.
    ///
    /// A word can occur many times. Taking its first occurrence would report
    /// a perfectly ordered key as backwards whenever an earlier paragraph
    /// happens to repeat a later answer — so common words would become
    /// permanently suspicious and the signal would be worthless. Instead each
    /// answer takes the earliest occurrence after the one before it; only
    /// when no such occurrence exists is the order genuinely broken.
    /// </summary>
    private static int? FirstBackwardsAt(
        IReadOnlyList<(int Order, string Id, IReadOnlyList<int> At)> positions)
    {
        var cursor = -1;
        foreach (var (order, _, at) in positions.OrderBy(p => p.Order))
        {
            var next = at.FirstOrDefault(p => p > cursor, -1);
            if (next < 0) return order;
            cursor = next;
        }

        return null;
    }

    private static IReadOnlyList<int> Occurrences(string haystack, string needle)
    {
        if (needle.Length == 0) return [];

        var found = new List<int>();
        var from = 0;
        while (from <= haystack.Length - needle.Length)
        {
            var at = haystack.IndexOf(needle, from, StringComparison.Ordinal);
            if (at < 0) break;
            found.Add(at);
            from = at + 1;
        }

        return found;
    }

    private static IEnumerable<(JsonObject Part, string Path, string? Source)> Parts(
        JsonObject package)
    {
        foreach (var section in package["sections"]?.AsArray() ?? [])
        {
            var module = section?["module"]?.GetValue<string>() ?? "unknown";
            var partIndex = -1;
            foreach (var part in section?["parts"]?.AsArray() ?? [])
            {
                partIndex++;
                if (part is not JsonObject p) continue;

                // Reading anchors in the passage body; Listening in the
                // transcript, when one was supplied (a missing transcript is
                // a warning under P-19, not a reason to refuse the package).
                var source = p["body"]?.GetValue<string>() ?? p["transcript"]?.GetValue<string>();
                yield return (p, $"/sections/{module}/parts/{partIndex}", source);
            }
        }
    }

    private static IEnumerable<List<(JsonObject Question, int Order, string Id, string Answer)>>
        AnchorableQuestionsByGroup(JsonObject part)
    {
        var groups = new Dictionary<string, List<(JsonObject, int, string, string)>>(
            StringComparer.Ordinal);

        foreach (var node in part["questions"]?.AsArray() ?? [])
        {
            if (node is not JsonObject question) continue;
            if (question["type"]?.GetValue<string>() is not { } type) continue;
            if (!Anchorable.Contains(type)) continue;
            if (question["answerKey"]?["accepted"]?.AsArray() is not { Count: > 0 } accepted) continue;
            if (accepted[0] is not JsonValue first) continue;

            var id = question["id"]?.GetValue<string>() ?? string.Empty;
            var order = question["order"]?.GetValue<int>() ?? 0;

            // A question with no group is its own group of one: containment
            // still applies, order has nothing to compare against.
            var groupId = question["group"]?["id"]?.GetValue<string>() ?? $"__solo-{id}";

            if (!groups.TryGetValue(groupId, out var members))
                groups[groupId] = members = [];

            members.Add((question, order, id, first.GetValue<string>()));
        }

        return groups.Values;
    }
}
