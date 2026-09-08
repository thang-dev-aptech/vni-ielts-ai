using System.Text.Json.Nodes;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

/// <summary>
/// `S1a` / `P-11` — <c>scoringProfile.bandTableProvenance</c> reaches the
/// domain through <see cref="ExamPackageReader"/> exactly as declared, with
/// no status substituted when the package says nothing.
/// </summary>
public sealed class BandTableProvenanceReaderTests
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

    /// <summary>
    /// The same minimal valid v2 package shape <c>ExamPackageReaderTests</c>
    /// uses, duplicated rather than shared — that file's copy is private to
    /// its own class.
    /// </summary>
    private static JsonObject ValidV2() => (JsonObject)JsonNode.Parse("""
    {
      "formatVersion": "2.0", "formatProfile": "vni-practice", "scoringProfileRef": "validation-v1",
      "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
      "title": "Validation", "variant": "academic",
      "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
      "scoringProfile": { "rawToBand": { "reading": [
        { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 } ] } },
      "sequenceProfile": { "modules": ["reading"] },
      "sections": [{ "module": "reading", "order": 1, "parts": [{ "order": 1, "kind": "passage",
        "body": "Evidence here.", "questions": [{
          "id": "q-1", "order": 1, "type": "short-answer", "marks": 1,
          "answerKey": { "accepted": ["paper"] }
        }]
      }]}]
    }
    """)!;

    private static ExamPackageResult Read(JsonObject root) =>
        Reader.Read(root.ToJsonString(), ExamDefinitionId.New(), 1);

    [Fact]
    public void Absent_provenance_reads_as_null_not_as_a_default_status()
    {
        var result = Read(ValidV2());

        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => f.Message)));
        Assert.Null(result.Version!.Scoring.Provenance);
    }

    [Theory]
    [InlineData("synthetic", BandTableProvenanceStatus.Synthetic)]
    [InlineData("provisional", BandTableProvenanceStatus.Provisional)]
    public void A_non_equated_status_round_trips_without_a_source(
        string wireStatus, BandTableProvenanceStatus expected)
    {
        var root = ValidV2();
        root["scoringProfile"]!["bandTableProvenance"] = JsonNode.Parse($$"""
        { "status": "{{wireStatus}}" }
        """);

        var result = Read(root);

        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => f.Message)));
        Assert.Equal(expected, result.Version!.Scoring.Provenance!.Status);
        Assert.Null(result.Version.Scoring.Provenance.Source);
    }

    [Fact]
    public void An_equated_status_carries_its_source_and_note_through()
    {
        var root = ValidV2();
        root["scoringProfile"]!["bandTableProvenance"] = JsonNode.Parse("""
        {
          "status": "equated",
          "source": "VNI academic team, equated against Cambridge 19 Test 1, 2026-09",
          "note": "Internal pilot, n=40."
        }
        """);

        var result = Read(root);

        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => f.Message)));
        var provenance = result.Version!.Scoring.Provenance!;
        Assert.Equal(BandTableProvenanceStatus.Equated, provenance.Status);
        Assert.Equal(
            "VNI academic team, equated against Cambridge 19 Test 1, 2026-09", provenance.Source);
        Assert.Equal("Internal pilot, n=40.", provenance.Note);
    }

    [Fact]
    public void An_equated_status_with_no_source_is_rejected_by_the_schema_not_guessed()
    {
        // The schema's own allOf requires `source` whenever `status` is
        // `equated` — this proves the reader does not quietly accept what
        // the schema refuses, the same guarantee every other schema rule in
        // this reader already has.
        var root = ValidV2();
        root["scoringProfile"]!["bandTableProvenance"] = JsonNode.Parse("""
        { "status": "equated" }
        """);

        var result = Read(root);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "SCHEMA_INVALID");
    }
}
