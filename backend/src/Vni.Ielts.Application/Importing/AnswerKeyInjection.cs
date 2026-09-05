using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// Writes a supplier's answer key onto a parsed package, and refuses when the
/// two do not line up.
///
/// ── The check that earns this class its place ─────────────────────────────
///
/// <b>A positional key and a positional paper can disagree silently.</b> If the
/// key list is short by one — a heading the reader mistook for an answer, a
/// range item read as a single — then every answer after that point attaches to
/// the wrong question, and the result is a complete, well-formed, entirely
/// wrong package. That is the failure mode measured on 2026-09-02, and no
/// amount of schema validation sees it: a wrong answer is the same shape as a
/// right one.
///
/// What does see it is the paper itself. A True/False/Not Given question can
/// only be answered TRUE, FALSE or NOT GIVEN; a multiple-choice question can
/// only be answered with one of its own printed option letters. So a shift of
/// one or two positions almost always lands a letter where a sentence belongs
/// or a word where a letter does — and this refuses, naming the question.
///
/// <b>It is not a proof of correctness.</b> A shift that happens to land
/// letters on letters still passes, which is why Q13 and Q36 of Test 2 —
/// FALSE for TRUE, A for C — would not have been caught by a type check alone.
/// They are caught because the key is now read positionally by a counter that
/// advances by an entry's own width, and this check is the second line of
/// defence rather than the first.
/// </summary>
public static class AnswerKeyInjection
{
    public const string CoverageCode = "ANSWER_KEY_COVERAGE";
    public const string TypeMismatchCode = "ANSWER_KEY_TYPE_MISMATCH";
    public const string FoldedChoiceCode = "ANSWER_KEY_FOLDED_CHOICE";
    public const string TypeRetypedCode = "ANSWER_KEY_TYPE_RETYPED";
    public const string OptionAddedCode = "ANSWER_KEY_OPTION_ADDED";
    public const string BankLabelAlternativesCode = "ANSWER_KEY_BANK_LABEL_ALTERNATIVES";

    private static readonly string[] TrueFalseNotGiven = ["TRUE", "FALSE", "NOT GIVEN"];
    private static readonly string[] YesNoNotGiven = ["YES", "NO", "NOT GIVEN"];

    /// <summary>
    /// <c>"D wood"</c> — a single capital letter, then its word. An IELTS
    /// completion answer is at most three words from the passage and never
    /// begins with a standalone capital letter, so this shape means a bank
    /// label every time.
    /// </summary>
    private static readonly Regex BankLabel = new(@"^[A-Z]\s+\S", RegexOptions.Compiled);

    public sealed record Result(
        string PackageJson, IReadOnlyList<PackageFinding> Findings)
    {
        public bool IsSuccess => Findings.All(f => f.Severity != "error");
    }

    public static Result Apply(string packageJson, IReadOnlyList<AnswerKeyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var package = JsonNode.Parse(packageJson)?.AsObject()
            ?? throw new ArgumentException("The package did not parse as an object.", nameof(packageJson));

        var findings = new List<PackageFinding>();
        FoldMultiMarkChoices(package, entries, findings);
        var questions = Questions(package).OrderBy(q => Order(q.Node)).ToList();
        var byFirst = entries.ToDictionary(e => e.First);
        var used = new HashSet<int>();
        var retypedGroups = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (node, path) in questions)
        {
            var order = Order(node);

            if (!byFirst.TryGetValue(order, out var entry))
            {
                /*
                 * A question whose number is inside a multi-mark entry is
                 * answered by that entry and correctly has no key of its own —
                 * "24-26. A, B, D" is one answer to question 24, and 25 and 26
                 * exist only as marks. The paper agrees: it prints one question
                 * with `coversQuestions`, not three.
                 */
                if (entries.Any(e => order > e.First && order <= e.Last)) continue;

                findings.Add(new PackageFinding(
                    "error", CoverageCode, path,
                    $"Question {order} has no answer in the key. The key document may be short, "
                    + "or an entry before this one may have been read as covering fewer marks "
                    + "than it does — which shifts every answer after it."));

                continue;
            }

            used.Add(order);

            var type = node["type"]?.GetValue<string>() ?? string.Empty;
            var accepted = Accept(entry, type, node, path, findings, retypedGroups);

            if (accepted is null) continue;

            node["answerKey"] = new JsonObject { ["accepted"] = accepted };
        }

        /*
         * A rubric belongs to the group, so a retype does too. NOT GIVEN sits
         * in both vocabularies and says nothing about which was printed, so a
         * question answered NOT GIVEN keeps whatever type the model gave it —
         * and Cam 17 T3 Q34 sat as true-false between three yes-no siblings.
         * Follow the siblings the key did settle.
         */
        foreach (var (node, path) in questions)
        {
            var groupId = node["group"]?["id"]?.GetValue<string>();
            var type = node["type"]?.GetValue<string>();

            if (groupId is null || type is not ("true-false-notgiven" or "yes-no-notgiven")) continue;

            // A retype this run, or — when the key is re-applied to a draft
            // whose siblings were already retyped on an earlier pass — the
            // family the siblings' own YES/NO or TRUE/FALSE answers prove.
            if (!retypedGroups.TryGetValue(groupId, out var target))
                target = FamilyProvedByGroup(questions, groupId);

            if (target is null || type == target) continue;

            node["type"] = target;
            findings.Add(new PackageFinding(
                "warning", TypeRetypedCode, path,
                $"Question {Order(node)} was {type}; retyped to {target} to match its group {groupId}."));
        }

        foreach (var entry in entries.Where(e => !used.Contains(e.First)))
        {
            findings.Add(new PackageFinding(
                "error", CoverageCode, "/sections",
                $"The key answers question {entry.First}, which the paper does not contain. "
                + $"Key text: \"{Truncate(entry.Raw)}\"."));
        }

        return new Result(package.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        }), findings);
    }

    /// <summary>
    /// Cambridge (and VOL 9) print "Choose TWO letters" as one numbered pair
    /// on the answer sheet. Models routinely emit that as two
    /// <c>multiple-choice</c> questions sharing a prompt. Left alone, the key
    /// line <c>"C, D"</c> cannot land on a single-letter control, and the extra
    /// question inflates the raw total past 40.
    ///
    /// When a multi-mark key entry lines up with that many sibling choice
    /// questions, they are one question. Fold them before the key is written.
    /// </summary>
    private static void FoldMultiMarkChoices(
        JsonObject package, IReadOnlyList<AnswerKeyEntry> entries, List<PackageFinding> findings)
    {
        if (package["sections"] is not JsonArray sections) return;

        foreach (var entry in entries.Where(e => e.Marks > 1))
        {
            for (var s = 0; s < sections.Count; s++)
            {
                if (sections[s]?["parts"] is not JsonArray parts) continue;

                for (var p = 0; p < parts.Count; p++)
                {
                    if (parts[p]?["questions"] is not JsonArray questions) continue;

                    var members = questions
                        .OfType<JsonObject>()
                        .Where(q =>
                        {
                            var order = Order(q);
                            return order >= entry.First && order <= entry.Last;
                        })
                        .OrderBy(Order)
                        .ToList();

                    if (members.Count == 0) continue;

                    if (members.Count == 1)
                    {
                        PromoteMultiSelect(members[0], entry.Marks);
                        continue;
                    }

                    if (members.Count != entry.Marks || !SameChoiceFamily(members))
                        continue;

                    var keep = members[0];
                    PromoteMultiSelect(keep, entry.Marks);

                    foreach (var extra in members.Skip(1))
                        questions.Remove(extra);

                    findings.Add(new PackageFinding(
                        "warning", FoldedChoiceCode,
                        $"/sections/{s}/parts/{p}/questions",
                        $"Questions {entry.First}–{entry.Last} were {entry.Marks} separate choice "
                        + "items sharing a prompt; folded into one multiple-select so the key "
                        + $"\"{Truncate(entry.Raw)}\" can land on a {entry.Marks}-mark question."));
                }
            }
        }
    }

    private static void PromoteMultiSelect(JsonObject question, int marks)
    {
        question["type"] = "multiple-select";
        question["marks"] = marks;
    }

    private static bool SameChoiceFamily(IReadOnlyList<JsonObject> members)
    {
        var groupIds = members
            .Select(q => q["group"]?["id"]?.GetValue<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (groupIds.Count == 1) return true;

        var banks = members.Select(OptionKeys).ToList();
        return banks.TrueForAll(b => b.Count > 0 && b.SetEquals(banks[0]));
    }

    private static HashSet<string> OptionKeys(JsonObject question)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (question["options"] is not JsonArray options) return keys;

        foreach (var option in options)
        {
            if (option?["key"]?.GetValue<string>() is { Length: > 0 } key)
                keys.Add(key);
        }

        return keys;
    }

    /// <summary>
    /// Turns one key line into the `accepted` array the schema wants, using the
    /// question's own type to decide how to read it — and refusing when the two
    /// cannot be reconciled.
    /// </summary>
    /// <summary>
    /// The verdict family a group's accepted answers prove: YES/NO proves
    /// yes-no, TRUE/FALSE proves true-false, NOT GIVEN proves nothing, and a
    /// group carrying both is left alone for a person.
    /// </summary>
    private static string? FamilyProvedByGroup(
        IReadOnlyList<(JsonObject Node, string Path)> questions, string groupId)
    {
        var yesNo = false;
        var trueFalse = false;

        foreach (var (node, _) in questions)
        {
            if (node["group"]?["id"]?.GetValue<string>() != groupId) continue;
            if (node["answerKey"]?["accepted"] is not JsonArray accepted) continue;

            foreach (var value in accepted.Select(a => a?.GetValue<string>()))
            {
                if (value is "YES" or "NO") yesNo = true;
                if (value is "TRUE" or "FALSE") trueFalse = true;
            }
        }

        return (yesNo, trueFalse) switch
        {
            (true, false) => "yes-no-notgiven",
            (false, true) => "true-false-notgiven",
            _ => null,
        };
    }

    private static JsonArray? Accept(
        AnswerKeyEntry entry, string type, JsonObject question, string path,
        List<PackageFinding> findings, Dictionary<string, string> retypedGroups)
    {
        var raw = entry.Raw.Trim();

        switch (type)
        {
            case "true-false-notgiven":
            case "yes-no-notgiven":
            {
                var vocabulary = type == "true-false-notgiven" ? TrueFalseNotGiven : YesNoNotGiven;
                var value = Normalise(raw);

                /*
                 * <b>YES on a TRUE/FALSE question is the paper's type being
                 * wrong, not the key's.</b> The two families share a shape and
                 * differ only in the printed rubric ("does the passage say" vs
                 * "does the writer think"), which a model reads past — Cam 17
                 * Test 3 Q32–35 came back `true-false-notgiven` under a
                 * YES/NO/NOT GIVEN rubric. The key is the more reliable witness
                 * to the rubric, and NOT GIVEN sits in both vocabularies, so a
                 * YES or NO is unambiguous. Retype the question and say so; a
                 * word from a completion answer still refuses below, because
                 * that is the slipped-list case this check exists for.
                 */
                var sibling = vocabulary == TrueFalseNotGiven ? YesNoNotGiven : TrueFalseNotGiven;
                if (!vocabulary.Contains(value, StringComparer.Ordinal)
                    && sibling.Contains(value, StringComparer.Ordinal))
                {
                    var retyped = vocabulary == TrueFalseNotGiven ? "yes-no-notgiven" : "true-false-notgiven";
                    question["type"] = retyped;
                    if (question["group"]?["id"]?.GetValue<string>() is { } groupId)
                        retypedGroups[groupId] = retyped;

                    findings.Add(new PackageFinding(
                        "warning", TypeRetypedCode, path,
                        $"Question {entry.First} was {type} and the key says \"{value}\"; retyped to "
                        + $"{retyped}. Check the printed rubric for this group."));

                    return [value];
                }

                if (!vocabulary.Contains(value, StringComparer.Ordinal))
                {
                    findings.Add(new PackageFinding(
                        "error", TypeMismatchCode, path,
                        $"Question {entry.First} is {type} and the key says \"{Truncate(raw)}\". "
                        + $"Expected one of {string.Join(", ", vocabulary)}. A key line that does "
                        + "not fit its question is usually a sign the list has slipped out of step "
                        + "with the paper."));

                    return null;
                }

                return [value];
            }

            case "matching":
            case "multiple-choice":
            {
                var letter = LeadingKey(raw, question);

                /*
                 * <b>A paragraph letter the model did not list.</b> "Which
                 * paragraph contains…" is answered from paragraphs A–H, and the
                 * rubric says so ("Choose the correct letter, A-H"); the model
                 * transcribed A–F as options and the key says H (VOL 9 T2 Q15).
                 * The rubric's range is printed content, the option list is the
                 * model's, so the range wins: the letter is added as an option
                 * whose text is itself — which is exactly what a paragraph
                 * label is — and the addition is reported.
                 */
                if (letter is null && RubricRangeAdmits(question, raw) is { } admitted)
                {
                    AddOption(question, admitted);
                    findings.Add(new PackageFinding(
                        "warning", OptionAddedCode, path,
                        $"Question {entry.First}: the key says \"{admitted}\", which the rubric's letter "
                        + "range allows but the transcribed options did not list; added as an option."));

                    return [admitted];
                }

                if (letter is null)
                {
                    findings.Add(new PackageFinding(
                        "error", TypeMismatchCode, path,
                        $"Question {entry.First} is {type} and the key says \"{Truncate(raw)}\", "
                        + "which is not one of the options printed on it."));

                    return null;
                }

                return [letter];
            }

            case "multiple-select":
            {
                var parts = raw
                    .Split([',', '/', ';', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(p => LeadingKey(p, question))
                    .ToList();

                if (parts.Count != entry.Marks || parts.Any(p => p is null))
                {
                    findings.Add(new PackageFinding(
                        "error", TypeMismatchCode, path,
                        $"Question {entry.First} carries {entry.Marks} mark(s) and the key says "
                        + $"\"{Truncate(raw)}\", which does not resolve to that many of its own "
                        + "options."));

                    return null;
                }

                return [new JsonArray([.. parts.Select(p => JsonValue.Create(p!))])];
            }

            default:
                /*
                 * ── The paper outranks the label ──────────────────────────
                 *
                 * <b>A question that prints options can only be answered by one
                 * of them, whatever `type` says.</b> On VOL 9 Test 2 the model
                 * typed questions 27–30 as `completion` while transcribing four
                 * options onto each — and the key writes them as "D wood", a
                 * letter with a gloss. Trusting the label produced the answer
                 * "D WOOD" four times: well-formed, schema-valid, and wrong for
                 * every learner who picks D.
                 *
                 * The options are the paper's own content and the type is the
                 * model's opinion of it, so the options win. The mismatch is
                 * still reported, because a mistyped question renders as the
                 * wrong control even once its answer is right.
                 */
                if (Offers(question) && LeadingKey(raw, question) is { } key)
                {
                    findings.Add(new PackageFinding(
                        "warning", TypeMismatchCode, path,
                        $"Question {entry.First} is typed \"{type}\" but prints options, and the "
                        + $"key says \"{Truncate(raw)}\". The answer was read as the option key "
                        + $"\"{key}\". The type is probably wrong and the question will render as "
                        + "a text box rather than a choice."));

                    return [key];
                }

                /*
                 * ── A bank label with no bank ─────────────────────────────
                 *
                 * <b>"D wood" on a question that offers no options is not an
                 * answer this can resolve, and guessing either way is wrong.</b>
                 * Taking the whole line keys the paper to the literal string
                 * "D wood"; taking the letter keys it to an option that does not
                 * exist on the question. Both are well-formed and both mark
                 * every learner incorrectly.
                 *
                 * Measured on VOL 9 Test 2, questions 27–30: a summary with a
                 * word bank "A poverty  B agriculture  C boats  D wood". The
                 * bank belongs on each question as `options` with the type
                 * `matching`; the transcription put it inside the group's
                 * `text` as prose, where nothing can match against it. That is a
                 * defect in the paper, not in the key, so this refuses and says
                 * which — a reviewer who is told "the key is wrong" goes and
                 * looks at the wrong file.
                 */
                if (BankLabel.IsMatch(raw))
                {
                    /*
                     * Revised 2026-09-04, after the refusal above held VOL 9 T2
                     * back through three prompt versions: the key is not
                     * ambiguous here, it is doubly explicit — "D wood" names
                     * both the label and the word it stands for. A learner who
                     * types either into the text box the question renders as
                     * has answered it. Both are accepted; the question is still
                     * reported, because it should have carried the bank as
                     * options and a reviewer may want to make it so.
                     */
                    var labelled = Regex.Match(raw.Trim(), @"^(?<label>[A-Z])\s+(?<word>\S.*)$");
                    var word = labelled.Groups["word"].Value.Trim();
                    var label = labelled.Groups["label"].Value;

                    findings.Add(new PackageFinding(
                        "warning", BankLabelAlternativesCode, path,
                        $"Question {entry.First} is typed \"{type}\" and offers no options, but the key "
                        + $"says \"{Truncate(raw)}\" — a bank label and its word. Both \"{label}\" and "
                        + $"\"{word}\" are accepted. The bank was transcribed into the group's text "
                        + "instead of onto the question as `options`; a reviewer may want to fix that."));

                    return [label, .. SplitAlternatives(word).Select(v => (JsonNode)JsonValue.Create(v))];
                }

                /*
                 * Completion and short answer. The line is the answer, verbatim
                 * — including a printed alternative like "flavour / flavor",
                 * which is two accepted spellings rather than two answers.
                 */
                return [.. SplitAlternatives(raw).Select(v => (JsonNode)JsonValue.Create(v))];
        }
    }

    /// <summary>
    /// The option key a line begins with, when the question actually offers it.
    ///
    /// VOL 9 prints a gloss beside the letter — <c>"D wood"</c>, <c>"C boats"</c>
    /// — so the letter is taken from the front, but only after checking it is
    /// one this question offers. Without that check a completion answer
    /// beginning with a capital letter would silently become an option key.
    /// </summary>
    /// <summary>Whether the question prints an option bank of its own.</summary>
    private static bool Offers(JsonObject question) =>
        question["options"] is JsonArray { Count: > 0 };

    private static readonly Regex RubricRange = new(@"\b([A-Z])\s*[-–]\s*([A-Z])\b", RegexOptions.Compiled);

    /// <summary>
    /// The single capital letter the key gives, when the question's own rubric
    /// ("Choose the correct letter, A-H") prints a range that contains it and
    /// every option the question does list is itself a single letter.
    /// </summary>
    private static string? RubricRangeAdmits(JsonObject question, string raw)
    {
        var token = raw.Trim();
        if (token.Length != 1 || token[0] is < 'A' or > 'Z') return null;

        var instruction = question["group"]?["instruction"]?.GetValue<string>() ?? string.Empty;
        var range = RubricRange.Match(instruction);
        if (!range.Success) return null;

        var from = range.Groups[1].Value[0];
        var to = range.Groups[2].Value[0];
        if (token[0] < from || token[0] > to) return null;

        var offered = question["options"] as JsonArray;
        if (offered is null || offered.Any(o => (o?["key"]?.GetValue<string>() ?? "").Length != 1)) return null;

        return token;
    }

    private static void AddOption(JsonObject question, string key)
    {
        if (question["options"] is not JsonArray options) question["options"] = options = [];
        options.Add(new JsonObject { ["key"] = key, ["text"] = key });
    }

    private static string? LeadingKey(string raw, JsonObject question)
    {
        var offered = question["options"] is JsonArray options
            ? options.Select(o => o?["key"]?.GetValue<string>()).Where(k => k is not null).ToList()
            : [];

        var token = raw.Split([' ', '\t', '.', ')', ':'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();

        if (token is null) return null;

        // Roman numerals for heading matching arrive lower-cased in the fixtures.
        foreach (var key in offered)
        {
            if (string.Equals(key, token, StringComparison.OrdinalIgnoreCase)) return key;
        }

        // A question with no printed options (a matching item whose bank lives
        // on its group) still has a letter answer, and there is nothing to
        // check it against.
        if (offered.Count == 0) return token;

        /*
         * <b>OCR reads a lone capital letter badly, and always in the same
         * ways.</b> Cam 18 T1 as tesseract returned it: "Cc" for C, "8B" for
         * B, "OD" for D, "=F" for F — a stray glyph glued to the letter, or the
         * letter doubled in lower case. The token is short, and exactly one
         * of its letters is a key the question actually prints; that letter is
         * the answer. Two different printed keys in one token ("AB" on a bank
         * that has both) is left refused — that is a real ambiguity, not noise.
         */
        // A roman numeral with a glyph glued on ("Ovi" for vi, Cam 16 T4 Q28):
        // keep only i/v/x and try again against the printed heading numerals.
        var roman = new string(token.ToLowerInvariant().Where(c => c is 'i' or 'v' or 'x').ToArray());
        if (roman.Length > 0 && roman.Length < token.Length)
        {
            foreach (var key in offered)
            {
                if (string.Equals(key, roman, StringComparison.OrdinalIgnoreCase)) return key;
            }
        }

        if (token.Length <= 5)
        {
            var candidates = token
                .Where(char.IsLetter)
                .Select(c => char.ToUpperInvariant(c).ToString())
                .Where(l => offered.Contains(l, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (candidates.Count == 1) return candidates[0];
        }

        return null;
    }

    /// <summary>
    /// <c>"flavour / flavor"</c> is one answer with two accepted spellings.
    /// A comma, by contrast, is left alone: <c>"large office, good pay"</c> in a
    /// completion answer is the answer.
    /// </summary>
    private static IEnumerable<string> SplitAlternatives(string raw) =>
        raw.Contains('/')
            ? raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [raw];

    private static string Normalise(string raw)
    {
        var collapsed = string.Join(' ', raw.ToUpperInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // pdftotext -layout on Cambridge's two-column key page runs the two
        // words together ("NOTGIVEN", Cam 21 T1 Q9/Q38) and OCR sometimes
        // hyphenates them. Both are the one verdict that has a space in it.
        return collapsed is "NOTGIVEN" or "NOT-GIVEN" ? "NOT GIVEN" : collapsed;
    }

    private static IEnumerable<(JsonObject Node, string Path)> Questions(JsonObject package)
    {
        if (package["sections"] is not JsonArray sections) yield break;

        for (var s = 0; s < sections.Count; s++)
        {
            if (sections[s]?["parts"] is not JsonArray parts) continue;

            for (var p = 0; p < parts.Count; p++)
            {
                if (parts[p]?["questions"] is not JsonArray questions) continue;

                for (var q = 0; q < questions.Count; q++)
                {
                    if (questions[q] is JsonObject node)
                        yield return (node, $"/sections/{s}/parts/{p}/questions/{q}");
                }
            }
        }
    }

    private static int Order(JsonObject question) =>
        question["order"]?.GetValue<int>() ?? int.MaxValue;

    private static string Truncate(string value) =>
        value.Length <= 60 ? value : value[..60] + "…";
}
