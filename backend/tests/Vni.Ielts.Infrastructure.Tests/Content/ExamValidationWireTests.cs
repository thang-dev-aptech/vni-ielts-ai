using System.Text.Json;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

/// <summary>
/// Phase 3 Plan 02 — the authoring checklist and the ZIP importer must name
/// the same finding the same way, or an author who already learned the import
/// codes sees a second vocabulary in the builder.
/// </summary>
public sealed class ExamValidationWireTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly ExamPackageReader Reader =
        ExamPackageReader.FromSchemaFile(Path.Combine(RepoRoot, "contracts/schemas/exam.schema.json"));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private const string InvalidJson = """
    {
      "formatVersion": "1.0", "title": "T", "variant": "academic",
      "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
      "scoringProfile": { "rawToBand": { "reading": [ { "minRaw": 0, "band": 0 } ] } },
      "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1, "kind": "passage",
        "questions": [ { "id": "r-1", "order": 1, "type": "multiple-choice" } ] } ] } ]
    }
    """;

    [Fact]
    public void Reader_findings_round_trip_to_the_same_import_and_checklist_codes()
    {
        var result = Reader.Read(InvalidJson, Domain.Exams.ExamDefinitionId.New(), 1);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Findings);

        foreach (var finding in result.Findings)
        {
            var imported = ExamValidationWire.ToPackageFinding(finding);
            var checklist = JsonSerializer.SerializeToElement(ExamValidationWire.ToChecklist(finding));

            Assert.Equal(ExamValidationWire.SchemaStage, imported.Stage);
            Assert.Equal(finding.Code, imported.Code);
            Assert.Equal(finding.Path, imported.Pointer);
            Assert.Equal(finding.Message, imported.Message);

            Assert.Equal(imported.Stage, checklist.GetProperty("stage").GetString());
            Assert.Equal(imported.Code, checklist.GetProperty("code").GetString());
            Assert.Equal(imported.Pointer, checklist.GetProperty("pointer").GetString());
            Assert.Equal(imported.Message, checklist.GetProperty("message").GetString());
        }
    }

    [Fact]
    public void Schema_failures_keep_the_import_code_not_a_builder_specific_alias()
    {
        var result = Reader.Read(InvalidJson, Domain.Exams.ExamDefinitionId.New(), 1);
        Assert.Contains(result.Findings, f => f.Code == "SCHEMA_INVALID");
        Assert.DoesNotContain(result.Findings, f => f.Code == "STRUCTURAL");
    }
}
