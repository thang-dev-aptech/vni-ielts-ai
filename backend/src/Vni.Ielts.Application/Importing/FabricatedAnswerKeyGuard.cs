using System.Text.Json;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// Refuses a parsed package that carries answer keys the source could not have
/// contained.
///
/// ── The failure this exists for, observed on 2026-09-02 ───────────────────
///
/// The first real run of AI-assisted import was given <c>TEST 2-R.docx</c>, an
/// IELTS Reading paper that ends at question 40's options and contains **no
/// answer key**. The prompt told the model, in its own numbered rule, that
/// answer keys come only from a supplied key text and that it must emit none if
/// there is none.
///
/// It emitted all forty. Checked against the paper's real key, five questions
/// were wrong — seven marks in forty, which moves a band.
///
/// <b>Every one of those forty answers passed <c>ExamPackageValidator</c>,</b>
/// because a fabricated answer key is perfectly well-formed. That is the whole
/// point: schema validity is a statement about shape and says nothing about
/// truth, and the failure it cannot see is the expensive one — a paper that
/// looks finished and marks every learner who sits it against invented answers.
///
/// ── Why this is a gate and not a better prompt ────────────────────────────
///
/// The instruction was already explicit and was ignored. A second, firmer
/// instruction is the same control with more words. What can actually be relied
/// on is a check that does not ask the model anything: <b>if no answer key was
/// put in, no answer key may come out.</b> The caller knows whether it supplied
/// one; that fact is not the model's to report.
///
/// → CLAUDE.md rule 2, <c>docs/ai/output-contracts.md</c>
/// </summary>
public static class FabricatedAnswerKeyGuard
{
    public const string FindingCode = "ANSWER_KEY_NOT_IN_SOURCE";

    /// <summary>
    /// A finding per question that claims an answer key, or empty when the
    /// source did supply one.
    /// </summary>
    /// <param name="packageJson">The parser's output, before validation.</param>
    /// <param name="sourceIncludesAnswerKey">
    /// Whether the caller put a key document into the source text. <b>Supplied
    /// by the caller and never inferred from the package</b> — inferring it
    /// from the presence of keys would make the check vacuous.
    /// </param>
    /// <summary>
    /// Removes every <c>answerKey</c> the model wrote, and says how many.
    ///
    /// <b>For the case where an official key document is in hand.</b> A model
    /// that solved the paper anyway (VOL 9 T3, 40 answers, prompt v7) has
    /// produced guesses; refusing the package protects nobody once the real key
    /// is about to be written over every question, and it throws away a paid
    /// transcription. Without a key document the guard still refuses.
    /// </summary>
    public static (string PackageJson, int Removed) Strip(string packageJson)
    {
        var package = System.Text.Json.Nodes.JsonNode.Parse(packageJson)?.AsObject()
            ?? throw new ArgumentException("The package did not parse as an object.", nameof(packageJson));

        var removed = 0;
        foreach (var section in package["sections"]?.AsArray() ?? [])
        foreach (var part in section?["parts"]?.AsArray() ?? [])
        foreach (var question in part?["questions"]?.AsArray() ?? [])
        {
            if (question is System.Text.Json.Nodes.JsonObject q && q.ContainsKey("answerKey"))
            {
                q.Remove("answerKey");
                removed++;
            }
        }

        return (package.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), removed);
    }

    public static IReadOnlyList<PackageFinding> Inspect(
        string packageJson, bool sourceIncludesAnswerKey)
    {
        if (sourceIncludesAnswerKey) return [];

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(packageJson);
        }
        catch (JsonException)
        {
            // Not this guard's problem; the validator reports malformed JSON.
            return [];
        }

        using (document)
        {
            var findings = new List<PackageFinding>();
            Walk(document.RootElement, "", findings);

            if (findings.Count > 0)
            {
                findings.Insert(0, new PackageFinding(
                    "error",
                    FindingCode,
                    "/sections",
                    $"The package carries {findings.Count} answer key(s) and no answer key document "
                    + "was supplied with the source. They were produced by the model rather than "
                    + "read from the paper, so they are guesses — and a wrong key marks every "
                    + "learner who sits this paper. Supply the key document and parse again."));
            }

            return findings;
        }
    }

    private static void Walk(JsonElement element, string path, List<PackageFinding> findings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("answerKey"))
                    {
                        findings.Add(new PackageFinding(
                            "error", FindingCode, $"{path}/answerKey",
                            "This answer key was not in the source document."));

                        continue;
                    }

                    Walk(property.Value, $"{path}/{property.Name}", findings);
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Walk(item, $"{path}/{index++}", findings);

                break;
        }
    }
}
