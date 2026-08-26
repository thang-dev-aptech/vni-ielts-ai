using System.Text.Json;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

/// <summary>
/// The gate that ADR-0012 rests on.
///
/// Deferring the CMS removed the ZIP importer as the thing that exercises the
/// package format early. These tests are the replacement: if content can reach
/// the domain without passing the schema, the drift the ordering was meant to
/// prevent comes straight back.
/// </summary>
public sealed class ExamPackageReaderTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly ExamPackageReader Reader =
        ExamPackageReader.FromSchemaFile(Path.Combine(RepoRoot, "contracts/schemas/exam.schema.json"));

    private static string DemoJson() =>
        File.ReadAllText(Path.Combine(RepoRoot, "fixtures/exams/reading-demo.json"));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static ExamPackageResult Read(string json) =>
        Reader.Read(json, ExamDefinitionId.New(), 1, UserId.New());

    [Fact]
    public void The_committed_demo_exam_is_valid_against_the_committed_schema()
    {
        // If this ever fails, the fixture and the schema have drifted — which
        // is the exact failure the seeder exists to make impossible.
        var result = Read(DemoJson());

        Assert.True(result.IsValid,
            "Demo exam rejected: " + string.Join("; ", result.Findings.Select(f => $"{f.Path} {f.Message}")));
        Assert.NotNull(result.Version);
        Assert.Equal(ExamVariant.Academic, result.Version!.Variant);
        Assert.Single(result.Version.Sections);
        Assert.Equal(40, result.Version.Sections[0].AutoScoredCount);
        Assert.Equal(3, result.Version.Sections[0].Parts.Count);
    }

    [Fact]
    public void A_valid_exam_round_trips_into_something_actually_scoreable()
    {
        // Parsing is not the goal — producing a version the scorer can use is.
        var version = Read(DemoJson()).Version!;
        var section = version.Sections[0];

        var answers = section.Questions.ToDictionary(
            q => q.Id,
            q => (string?)(q.AnswerKey!.Accepted[0].Single ?? "B"));

        var score = DeterministicScorer.Score(section, version.Scoring, answers);

        Assert.Equal(40, score.RawScore);
        Assert.Equal(9.0m, score.Band.Value);   // 40 correct on the demo table
    }

    [Fact]
    public void Malformed_json_is_rejected_with_a_code_not_an_exception()
    {
        var result = Read("{ this is not json");

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "MALFORMED_JSON");
    }

    [Fact]
    public void An_unknown_question_type_is_rejected()
    {
        // The taxonomy is frozen. A type nobody has built a renderer, a
        // validator or a scoring rule for must not reach the domain.
        var json = DemoJson().Replace("\"short-answer\"", "\"drag-and-drop\"");
        var result = Read(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "SCHEMA_INVALID");
    }

    [Fact]
    public void An_auto_scored_question_without_an_answer_key_is_rejected()
    {
        // Otherwise it silently scores zero for every candidate, which looks
        // like a hard question rather than a broken import.
        var doc = JsonDocument.Parse(DemoJson());
        var mutated = DemoJson().Replace(
            "\"answerKey\": {\n            \"accepted\": [\n              \"answer1\"\n            ]\n          }",
            "\"prompt\": \"no key here\"");

        // Fall back to a direct construction if the string replace missed.
        var json = mutated != DemoJson() ? mutated : BuildExamWithoutAnswerKey();
        _ = doc;

        var result = Read(json);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void An_essay_question_carrying_an_answer_key_is_rejected()
    {
        // An answer key on an AI-evaluated question implies a deterministic
        // path that does not exist, and would invite one being built.
        var json = """
        {
          "formatVersion": "1.0", "title": "T", "variant": "academic",
          "timingProfile": { "sections": { "writing": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [ { "minRaw": 0, "band": 0 } ] } },
          "sections": [ { "module": "writing", "order": 1, "parts": [ { "order": 1, "kind": "task",
            "questions": [ { "id": "w-1", "order": 1, "type": "essay-task",
              "answerKey": { "accepted": ["anything"] } } ] } ] } ]
        }
        """;

        var result = Read(json);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_band_off_the_half_step_grid_is_rejected_by_the_schema()
    {
        var json = DemoJson().Replace("\"band\": 6.5", "\"band\": 6.3");
        var result = Read(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "SCHEMA_INVALID");
    }

    [Fact]
    public void A_table_that_does_not_reach_down_to_zero_is_rejected()
    {
        // The realistic mistake: a table pasted from a source that only listed
        // bands 5 and above. The schema cannot catch it — shape is fine — so
        // the coverage check has to, or a learner scoring 1 out of 2 gets no
        // band at all.
        var json = """
        {
          "formatVersion": "1.0", "title": "T", "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [ { "minRaw": 2, "band": 5 } ] } },
          "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1, "kind": "passage",
            "questions": [
              { "id": "r-1", "order": 1, "type": "short-answer", "answerKey": { "accepted": ["a"] } },
              { "id": "r-2", "order": 2, "type": "short-answer", "answerKey": { "accepted": ["b"] } }
            ] } ] } ]
        }
        """;

        var result = Read(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "SCORING_TABLE_INCOMPLETE");
    }

    [Fact]
    public void A_table_sized_for_a_different_question_count_is_rejected()
    {
        // A 40-question table on a 2-question section means someone reused the
        // wrong scoring profile, and every band across the range is wrong.
        // This check caught exactly that mistake in this repository's own demo
        // fixture on the day it was written.
        var json = """
        {
          "formatVersion": "1.0", "title": "T", "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [
            { "minRaw": 39, "band": 9 }, { "minRaw": 0, "band": 0 } ] } },
          "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1, "kind": "passage",
            "questions": [
              { "id": "r-1", "order": 1, "type": "short-answer", "answerKey": { "accepted": ["a"] } },
              { "id": "r-2", "order": 2, "type": "short-answer", "answerKey": { "accepted": ["b"] } }
            ] } ] } ]
        }
        """;

        var result = Read(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "SCORING_TABLE_INCOMPLETE");
    }

    [Fact]
    public void An_asset_path_escaping_the_package_is_rejected()
    {
        // Zip Slip's cousin. Even before there is a ZIP, an asset reference
        // must stay inside the package namespace and must never be usable as a
        // storage key.
        var json = DemoJson().Replace(
            "\"kind\": \"passage\"", "\"kind\": \"passage\", \"audio\": \"../../etc/passwd\"");

        var result = Read(json);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Findings_carry_a_json_pointer_path_so_an_author_can_locate_the_problem()
    {
        var json = DemoJson().Replace("\"variant\": \"academic\"", "\"variant\": \"nonsense\"");
        var result = Read(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Path.Contains("variant", StringComparison.Ordinal));
    }

    private static string BuildExamWithoutAnswerKey() => """
    {
      "formatVersion": "1.0", "title": "T", "variant": "academic",
      "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
      "scoringProfile": { "rawToBand": { "reading": [ { "minRaw": 0, "band": 0 } ] } },
      "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1, "kind": "passage",
        "questions": [ { "id": "r-1", "order": 1, "type": "short-answer" } ] } ] } ]
    }
    """;
}
