using System.Text.Json.Nodes;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// Models often put independent multiple-choice questions under one group
/// because they share a rubric ("Choose the correct letter, A, B, C or D").
/// The package reader then requires those questions to share one option bank,
/// which they do not — each stem has its own A–D. Splitting the group id
/// keeps the rubric and lets the paper validate.
/// </summary>
public static class QuestionGroupRepair
{
    public const string SplitCode = "QUESTION_GROUP_SPLIT";
    public const string IdNormalisedCode = "QUESTION_GROUP_ID_NORMALISED";

    public sealed record Result(string PackageJson, IReadOnlyList<PackageFinding> Findings);

    public static Result SplitDivergentBanks(string packageJson)
    {
        var package = JsonNode.Parse(packageJson)?.AsObject()
            ?? throw new ArgumentException("The package did not parse as an object.", nameof(packageJson));

        var findings = new List<PackageFinding>();
        if (package["sections"] is not JsonArray sections)
            return new Result(packageJson, findings);

        for (var s = 0; s < sections.Count; s++)
        {
            if (sections[s]?["parts"] is not JsonArray parts) continue;

            for (var p = 0; p < parts.Count; p++)
            {
                if (parts[p]?["questions"] is not JsonArray questions) continue;

                NormaliseGroupIds(questions, $"/sections/{s}/parts/{p}", findings);

                var grouped = questions
                    .OfType<JsonObject>()
                    .Where(q => q["group"]?["id"]?.GetValue<string>() is { Length: > 0 })
                    .GroupBy(q => q["group"]!["id"]!.GetValue<string>()!, StringComparer.Ordinal);

                foreach (var group in grouped)
                {
                    var members = group.ToList();
                    if (members.Count < 2) continue;

                    var banks = members.Select(OptionBank).ToList();
                    if (banks.TrueForAll(b => b == banks[0])) continue;

                    foreach (var question in members)
                    {
                        var order = question["order"]?.GetValue<int>() ?? 0;
                        question["group"]!["id"] = $"{group.Key}-{order}";
                    }

                    findings.Add(new PackageFinding(
                        "warning", SplitCode, $"/sections/{s}/parts/{p}",
                        $"Question group '{group.Key}' mixed {members.Count} different option "
                        + "banks; each question now has its own group id so the shared rubric "
                        + "survives and the identical-bank rule does not fire."));
                }
            }
        }

        return new Result(
            package.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            findings);
    }

    /// <summary>
    /// Group ids are identifiers (<c>^[a-z0-9][a-z0-9._:-]{0,127}$</c>), and a
    /// model writes them like labels — <c>"r-y nng-37-40"</c> on Cam 21 T1,
    /// a space in the middle. One such id fails the whole package at schema
    /// time with a cascade of unrelated-looking errors, after the parse was
    /// paid for. The id carries no meaning beyond "these belong together", so
    /// it is safe to fold: every member that had the same label gets the same
    /// folded id, and the group stays a group.
    /// </summary>
    private static void NormaliseGroupIds(
        JsonArray questions, string path, List<PackageFinding> findings)
    {
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var question in questions.OfType<JsonObject>())
        {
            if (question["group"]?["id"]?.GetValue<string>() is not { Length: > 0 } id) continue;

            var folded = FoldId(id);
            if (folded == id) continue;

            question["group"]!["id"] = folded;
            renamed.TryAdd(id, folded);
        }

        foreach (var (from, to) in renamed)
        {
            findings.Add(new PackageFinding(
                "warning", IdNormalisedCode, path,
                $"Question group id '{from}' is not a valid identifier; folded to '{to}'."));
        }
    }

    private static string FoldId(string id)
    {
        var chars = id.ToLowerInvariant()
            .Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or ':' or '-' ? c : '-')
            .ToArray();
        var folded = System.Text.RegularExpressions.Regex.Replace(new string(chars), "-{2,}", "-")
            .Trim('-', '.', '_', ':');

        return folded.Length == 0 ? "group" : folded[..Math.Min(folded.Length, 128)];
    }

    private static string OptionBank(JsonObject question)
    {
        if (question["options"] is not JsonArray options) return string.Empty;

        return string.Join('\u001f', options.Select(o =>
            $"{o?["key"]?.GetValue<string>()}\u001e{o?["text"]?.GetValue<string>()}"));
    }
}
