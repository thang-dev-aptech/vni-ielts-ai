using System.Text.Json.Nodes;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// A Reading section holds passages and a Listening section holds recordings.
/// A model given a paper whose Writing tasks follow the last passage sometimes
/// files those tasks as extra parts of the Reading section — Cam 21 Test 3 came
/// back with parts 4 and 5 of Reading being Writing Task 1 and Task 2, numbered
/// 1 and 2. That is not a paper anyone can sit: the answer key for Reading
/// questions 1 and 2 then lands on two essay prompts and the schema refuses
/// the whole file.
///
/// The parts are dropped rather than moved. This importer keeps one module per
/// run (<c>--keep-module</c>), and a Writing task that arrived inside Reading
/// is a transcription of the wrong page, not a Writing section anyone
/// reviewed.
/// </summary>
public static class SectionPartRepair
{
    public const string DroppedCode = "SECTION_FOREIGN_PART_DROPPED";

    public sealed record Result(string PackageJson, IReadOnlyList<PackageFinding> Findings);

    private static readonly IReadOnlyDictionary<string, string[]> AllowedKinds =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["reading"] = ["passage"],
            ["listening"] = ["recording", "passage"],
        };

    public static Result DropForeignParts(string packageJson)
    {
        var package = JsonNode.Parse(packageJson)?.AsObject()
            ?? throw new ArgumentException("The package did not parse as an object.", nameof(packageJson));

        var findings = new List<PackageFinding>();
        if (package["sections"] is not JsonArray sections)
            return new Result(packageJson, findings);

        for (var s = 0; s < sections.Count; s++)
        {
            var module = sections[s]?["module"]?.GetValue<string>();
            if (module is null || !AllowedKinds.TryGetValue(module, out var allowed)) continue;
            if (sections[s]?["parts"] is not JsonArray parts) continue;

            for (var p = parts.Count - 1; p >= 0; p--)
            {
                var part = parts[p]?.AsObject();
                var kind = part?["kind"]?.GetValue<string>();
                var essay = part?["questions"] is JsonArray qs
                    && qs.Count > 0
                    && qs.All(q => q?["type"]?.GetValue<string>() is "essay-task" or "speaking-response");

                if (kind is not null && allowed.Contains(kind, StringComparer.Ordinal) && !essay) continue;

                parts.RemoveAt(p);
                findings.Add(new PackageFinding(
                    "warning", DroppedCode, $"/sections/{s}/parts/{p}",
                    $"Dropped a '{kind ?? "?"}' part from the {module} section: {module} holds "
                    + $"{string.Join("/", allowed)} parts only. Its questions belong to another module."));
            }
        }

        return new Result(
            package.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            findings);
    }
}
