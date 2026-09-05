using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// A valid exam owned by this test project, copied beside the assembly.
    ///
    /// <b>Not <c>fixtures/exams</c>, and the move is the point.</b> That
    /// directory is what the development seeder loads, so it was simultaneously
    /// the product's catalogue and this suite's specimen of a well-formed
    /// package. Emptying the catalogue — the owner's direction on 2026-08-27,
    /// that only supplied content ships — would have deleted the schema
    /// contract along with it. Owning the file here keeps the two jobs apart.
    /// </summary>
    private static string ValidExamJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content", "valid-exam.json"));

    private static string ValidV2Json() => """
    {
      "formatVersion": "2.0", "formatProfile": "vni-practice", "scoringProfileRef": "validation-v1",
      "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
      "title": "Validation", "variant": "academic",
      "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
      "scoringProfile": { "rawToBand": { "reading": [
        { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }, { "minRaw": 2, "band": 2 } ] } },
      "sequenceProfile": { "modules": ["reading"] },
      "sections": [{ "module": "reading", "order": 1, "parts": [{ "order": 1, "kind": "passage",
        "body": "Evidence here.", "questions": [{
          "id": "q-1", "order": 1, "type": "multiple-select", "marks": 2,
          "options": [{ "key": "A", "text": "Alpha" }, { "key": "B", "text": "Beta" }],
          "group": { "id": "bank-1", "instruction": "Choose." },
          "slots": [
            { "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } },
            { "id": "slot-2", "number": 2, "answerKey": { "accepted": ["B"] } }
          ],
          "explanation": { "shortReason": "Both are stated.", "evidence": ["Evidence here."] }
        }]
      }]}]
    }
    """;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static ExamPackageResult Read(string json) =>
        Reader.Read(json, ExamDefinitionId.New(), 1);

    [Fact]
    public void Listening_playback_rules_are_versioned_package_data()
    {
        var root = JsonNode.Parse(ValidV2Json())!.AsObject();
        root["policyProfile"] = JsonNode.Parse("""
        {
          "listeningPlayback": {
            "practice": { "playOnce": false, "allowSeek": true },
            "mock": { "playOnce": true, "allowSeek": false }
          }
        }
        """);

        var result = Read(root.ToJsonString());

        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => f.Message)));
        Assert.Equal(new AudioPlaybackRule(false, true), result.Version!.ListeningPlayback.Practice);
        Assert.Equal(new AudioPlaybackRule(true, false), result.Version.ListeningPlayback.Mock);
    }

    [Fact]
    public void An_incomplete_listening_playback_profile_is_rejected_not_guessed()
    {
        var root = JsonNode.Parse(ValidV2Json())!.AsObject();
        root["policyProfile"] = JsonNode.Parse("""
        {
          "listeningPlayback": {
            "practice": { "playOnce": false, "allowSeek": true }
          }
        }
        """);

        var result = Read(root.ToJsonString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, finding => finding.Code == "SCHEMA_INVALID");
    }

    [Fact]
    public void The_committed_specimen_exam_is_valid_against_the_committed_schema()
    {
        // If this ever fails, the specimen and the schema have drifted —
        // which is the exact failure the reader exists to make impossible.
        var result = Read(ValidExamJson());

        Assert.True(result.IsValid,
            "Specimen exam rejected: " + string.Join("; ", result.Findings.Select(f => $"{f.Path} {f.Message}")));
        Assert.NotNull(result.Version);
        Assert.Equal(ExamVariant.Academic, result.Version!.Variant);
        Assert.Single(result.Version.Sections);
        Assert.Equal(40, result.Version.Sections[0].AutoScoredMarks);
        Assert.Equal(3, result.Version.Sections[0].Parts.Count);
    }

    /// <summary>
    /// A perfect candidate, answering the way the learner app actually answers.
    ///
    /// <b>This is the test that was missing on 2026-08-27, and its absence cost
    /// six Reading marks and seven Listening marks on every sitting of Exam
    /// 1.</b> The client and the marker each had passing tests. The client's
    /// proved it spells a multi-select pick <c>"A|D"</c>; the marker's proved it
    /// accepts <c>"A,D"</c>. Both were true. Nobody owned the sentence between
    /// them, so "Choose TWO letters" scored zero and every heading-matching
    /// question was refused outright — the marker demanded <c>left:right</c> for
    /// a question whose answer is one heading.
    ///
    /// So the answers below are built the way `QuestionInput.tsx` builds them,
    /// per shape, rather than by reaching into the key for whatever field
    /// happens to be populated. A round trip that marks its own internals
    /// correct proves the parser works and says nothing about the product.
    /// → <c>AnswerContractTests</c>
    /// </summary>

    /// <summary>
    /// The test-suite catalogue fixture is valid, and it is a whole Full Test.
    ///
    /// <b>Written for schema validation only — never learner-facing.</b>
    /// Owner-supplied content lives in <c>fixtures/exams/exam-1.json</c>.
    /// <c>synthetic-full-1.json</c> is invented material kept under test
    /// fixtures so the exam contract suite can run without demo papers in the
    /// learner catalogue.
    /// </summary>
    [Fact]
    public void The_committed_catalogue_fixture_is_a_valid_four_module_paper()
    {
        var path = Path.Combine(
            RepoRoot,
            "backend",
            "tests",
            "Vni.Ielts.Infrastructure.Tests",
            "Content",
            "Fixtures",
            "synthetic-full-1.json");

        Assert.True(
            File.Exists(path),
            $"{path} is missing. It is the only exam a clean checkout has, and the exam "
            + "contract suite needs a paper with all four modules to sit a Full Test.");

        var result = Read(File.ReadAllText(path));

        Assert.True(
            result.IsValid,
            "Catalogue fixture rejected: "
            + string.Join("; ", result.Findings.Select(f => $"{f.Path} {f.Message}")));

        var version = result.Version!;
        var modules = version.Sections.Select(s => s.Module).ToHashSet();

        Assert.Equal(
            new[]
            {
                ExamModule.Reading, ExamModule.Listening, ExamModule.Writing, ExamModule.Speaking,
            }.ToHashSet(),
            modules);

        // A band table that does not reach the section's mark total produces a
        // wrong band inside the gap, and the reader only rejects that if the
        // totals here are what the fixture actually carries.
        foreach (var section in version.Sections.Where(
            s => s.Module is ExamModule.Reading or ExamModule.Listening))
        {
            Assert.True(
                version.Scoring.CoversRange(section.Module, section.AutoScoredMarks, out var gap),
                $"{section.Module} has no band for a raw score of {gap}.");
        }
    }

    [Fact]
    public void A_perfect_candidate_answering_the_way_the_client_does_scores_full_marks()
    {
        var version = Read(ValidExamJson()).Version!;
        var section = version.Sections[0];

        var answers = section.Questions.ToDictionary(
            q => q.Id,
            q => (string?)AsTheClientWouldSend(q.AnswerKey!.Accepted[0]));

        var score = DeterministicScorer.Score(section, version.Scoring, answers);

        Assert.Equal(40, score.MaxScore);
        Assert.Equal(40, score.RawScore);
        Assert.Equal(9.0m, score.Band!.Value.Value);   // 40 correct on the specimen's table
    }

    /// <summary>
    /// The learner app's spelling of one accepted answer.
    ///
    /// Kept beside the assertion rather than in a helper class, because its
    /// only justification is the file it mirrors:
    /// `apps/web/src/features/exam/QuestionInput.tsx`.
    /// </summary>
    private static string AsTheClientWouldSend(AcceptedAnswer accepted) => accepted switch
    {
        // Checkboxes, sorted, joined with a pipe — a character no answer holds.
        { All: { } all } => string.Join('|', all.Order(StringComparer.Ordinal)),

        // Pair matching is the only shape that carries two sides.
        { Pair: { } pair } => $"{pair.Left}:{pair.Right}",

        // Everything else — a typed answer, a chosen option key, a heading.
        { Single: { } single } => single,

        _ => throw new InvalidOperationException("An accepted answer with no shape."),
    };

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
        var json = ValidExamJson().Replace("\"short-answer\"", "\"drag-and-drop\"");
        var result = Read(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "SCHEMA_INVALID");
    }

    [Fact]
    public void An_auto_scored_question_without_an_answer_key_is_rejected()
    {
        // Otherwise it silently scores zero for every candidate, which looks
        // like a hard question rather than a broken import.
        var doc = JsonDocument.Parse(ValidExamJson());
        var mutated = ValidExamJson().Replace(
            "\"answerKey\": {\n            \"accepted\": [\n              \"answer1\"\n            ]\n          }",
            "\"prompt\": \"no key here\"");

        // Fall back to a direct construction if the string replace missed.
        var json = mutated != ValidExamJson() ? mutated : BuildExamWithoutAnswerKey();
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
        var json = ValidExamJson().Replace("\"band\": 6.5", "\"band\": 6.3");
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
        var json = ValidExamJson().Replace(
            "\"kind\": \"passage\"", "\"kind\": \"passage\", \"audio\": \"../../etc/passwd\"");

        var result = Read(json);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Findings_carry_a_json_pointer_path_so_an_author_can_locate_the_problem()
    {
        var json = ValidExamJson().Replace("\"variant\": \"academic\"", "\"variant\": \"nonsense\"");
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

    /// <summary>
    /// Question groups survive the read.
    ///
    /// <b>They are the half of a question that cannot be inferred.</b> A
    /// matching question's options are the letters; its <i>group</i> is the
    /// instruction telling the candidate that each letter is used once, and a
    /// map labelling's group is the map. Reading a package and dropping the
    /// group produces content that validates, persists, renders — and cannot
    /// be answered. On the first real package authored against this schema, 55
    /// of 76 auto-scored questions carry one, so "dropped silently" would have
    /// meant most of an exam.
    /// </summary>
    [Fact]
    public void A_question_group_survives_the_read()
    {
        var json = """
        {
          "formatVersion": "1.0",
          "title": "Group probe",
          "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [{ "minRaw": 0, "band": 0 }] } },
          "sections": [{
            "module": "reading",
            "order": 1,
            "parts": [{
              "order": 1,
              "kind": "passage",
              "body": "A passage.",
              "questions": [{
                "id": "r-1",
                "order": 1,
                "type": "labelling",
                "prompt": "shower room",
                "options": [{ "key": "A", "text": "A" }, { "key": "B", "text": "B" }],
                "group": {
                  "id": "map-1-6",
                  "title": "Plan of Community Centre",
                  "instruction": "Label the map below.",
                  "image": "assets/map.jpg",
                  "eachLetterOnce": true
                },
                "answerKey": { "accepted": ["A"] }
              }]
            }]
          }]
        }
        """;

        var result = Read(json);
        Assert.True(result.IsValid,
            "Rejected: " + string.Join("; ", result.Findings.Select(f => $"{f.Path} {f.Message}")));

        var question = result.Version!.Sections[0].Parts[0].Questions[0];
        var group = Assert.IsType<QuestionGroup>(question.Group);

        Assert.Equal("map-1-6", group.Id);
        Assert.Equal("Plan of Community Centre", group.Title);
        Assert.Equal("Label the map below.", group.Instruction);
        Assert.Equal("assets/map.jpg", group.Image);
        Assert.True(group.EachLetterOnce);
    }

    /// <summary>
    /// A bank-answered question without its bank is rejected at the gate.
    ///
    /// Without the rule it reads cleanly and renders as a free text box — a
    /// control that accepts anything and scores almost nothing, which looks
    /// like a hard question rather than a broken import.
    /// </summary>
    [Fact]
    public void Matching_without_options_is_rejected()
    {
        var json = """
        {
          "formatVersion": "1.0",
          "title": "No bank",
          "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [{ "minRaw": 0, "band": 0 }] } },
          "sections": [{
            "module": "reading",
            "order": 1,
            "parts": [{
              "order": 1,
              "kind": "passage",
              "body": "A passage.",
              "questions": [{
                "id": "r-1",
                "order": 1,
                "type": "matching",
                "prompt": "Choose a heading for paragraph B.",
                "answerKey": { "accepted": ["i"] }
              }]
            }]
          }]
        }
        """;

        var result = Read(json);
        Assert.False(result.IsValid);

        // Named, because a negative assertion that passes for the wrong reason
        // is worse than none: it stays green when the rule is deleted.
        Assert.Contains(result.Findings, f => f.Path.Contains("questions/0", StringComparison.Ordinal));
    }

    [Fact]
    public void V2_preserves_two_response_slots_and_part_timing()
    {
        var result = Read("""
        {
          "formatVersion": "2.0",
          "formatProfile": "vni-practice",
          "scoringProfileRef": "reading-demo-v2",
          "contentSourceRef": { "sourceId": "synthetic-slots", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          "title": "Slots",
          "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [
            { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }, { "minRaw": 2, "band": 2 } ] } },
          "sequenceProfile": { "modules": ["reading"] },
          "sections": [{ "module": "reading", "order": 1, "parts": [{
            "order": 1, "kind": "passage", "timing": { "durationSeconds": 120 },
            "questions": [{
              "id": "q-1", "order": 1, "type": "multiple-select", "marks": 2,
              "options": [{ "key": "A", "text": "A" }, { "key": "B", "text": "B" }],
              "group": { "id": "inline-gaps", "text": "Complete [1] and [2]." },
              "slots": [
                { "id": "q-1-slot-1", "number": 1, "answerKey": { "accepted": ["A"] } },
                { "id": "q-1-slot-2", "number": 2, "answerKey": { "accepted": ["B"] } }
              ],
              "explanation": { "shortReason": "The text states both.", "evidence": [{ "source": "passage", "quote": "both" }] }
            }]
          }]}]
        }
        """);

        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => f.Message)));
        var question = result.Version!.Sections[0].Questions.Single();
        Assert.Equal(2, question.Slots!.Count);
        Assert.Equal(new[] { 1, 2 }, question.Slots.Select(s => s.Number));
        Assert.Equal("A", question.Slots[0].AnswerKey!.Accepted[0].Single);
        Assert.Equal("Complete [1] and [2].", question.Group!.Text);
        Assert.Equal("both", question.Explanation!.Evidence[0]);
        Assert.Equal(120, result.Version.Sections[0].Parts[0].Timing!.DurationSeconds);
    }

    [Fact]
    public void V1_multiple_mark_question_migrates_to_stable_slots_without_changing_answers()
    {
        var result = Read(ValidExamJson());
        Assert.True(result.IsValid);
        var question = result.Version!.Sections[0].Questions.Single(q => q.Marks == 2);

        Assert.Equal(2, question.Slots!.Count);
        Assert.Equal(new[] { 2, 3 }, question.Slots.Select(s => s.Number));
        Assert.Equal("A", question.Slots[0].AnswerKey!.Accepted[0].Single);
        Assert.Equal("D", question.Slots[1].AnswerKey!.Accepted[0].Single);
    }

    [Fact]
    public void Duplicate_response_slot_number_is_rejected_by_the_reader()
    {
        var json = """
        {
          "formatVersion": "2.0", "formatProfile": "vni-practice", "scoringProfileRef": "duplicate-v1",
          "contentSourceRef": { "sourceId": "synthetic-duplicate", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          "title": "Duplicate", "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [{ "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }] } },
          "sections": [{ "module": "reading", "order": 1, "parts": [{ "order": 1, "kind": "passage",
            "questions": [{ "id": "q-1", "order": 1, "type": "short-answer", "marks": 2,
              "slots": [
                { "id": "s1", "number": 1, "answerKey": { "accepted": ["a"] } },
                { "id": "s2", "number": 1, "answerKey": { "accepted": ["b"] } }
              ]
            }]
          }]}]
        }
        """;

        var result = Read(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "RESPONSE_SLOT_DUPLICATE");
    }

    [Fact]
    public void Unknown_v2_major_is_rejected_instead_of_guessed()
    {
        var json = ValidExamJson().Replace("\"formatVersion\": \"1.0\"", "\"formatVersion\": \"3.0\"");

        var result = Read(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "SCHEMA_INVALID" && f.Path.Contains("formatVersion"));
    }

    [Fact]
    public void Auto_scored_slot_without_a_key_is_rejected()
    {
        var root = JsonNode.Parse(ValidV2Json())!.AsObject();
        root["sections"]![0]!["parts"]![0]!["questions"]![0]!["slots"]![1]!.AsObject().Remove("answerKey");

        var result = Read(root.ToJsonString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "RESPONSE_SLOT_KEY_MISSING");
    }

    [Fact]
    public void Absent_sequence_profile_resolves_canonical_order_for_present_modules()
    {
        var root = JsonNode.Parse(ValidV2Json())!.AsObject();
        root.Remove("sequenceProfile");

        var result = Read(root.ToJsonString());

        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => f.Message)));
        Assert.Equal([ExamModule.Reading], result.Version!.ModuleSequence);
    }

    [Fact]
    public void Sequence_naming_an_absent_module_is_rejected()
    {
        var root = JsonNode.Parse(ValidV2Json())!.AsObject();
        root["sequenceProfile"]!["modules"] = new JsonArray("reading", "speaking");

        var result = Read(root.ToJsonString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "SEQUENCE_MODULE_MISMATCH");
    }

    [Fact]
    public void V2_referenced_asset_without_checksum_manifest_is_rejected()
    {
        var root = JsonNode.Parse(ValidV2Json())!.AsObject();
        root["sections"]![0]!["parts"]![0]!["audio"] = "assets/part-1.mp3";

        var result = Read(root.ToJsonString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "ASSET_CHECKSUM_MISSING");
    }

    [Fact]
    public void Duplicate_option_key_is_rejected()
    {
        var root = JsonNode.Parse(ValidV2Json())!.AsObject();
        root["sections"]![0]!["parts"]![0]!["questions"]![0]!["options"]![1]!["key"] = "A";

        var result = Read(root.ToJsonString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "QUESTION_OPTION_DUPLICATE");
    }

    [Fact]
    public void Members_of_one_group_cannot_disagree_on_the_option_bank()
    {
        var root = JsonNode.Parse(ValidV2Json())!.AsObject();
        var questions = root["sections"]![0]!["parts"]![0]!["questions"]!.AsArray();
        var first = questions[0]!.AsObject();
        first["marks"] = 1;
        first["slots"]!.AsArray().RemoveAt(1);

        var second = first.DeepClone().AsObject();
        second["id"] = "q-2";
        second["order"] = 2;
        second["slots"]![0]!["id"] = "slot-2";
        second["slots"]![0]!["number"] = 2;
        second["slots"]![0]!["answerKey"]!["accepted"]![0] = "B";
        second["options"]![1]!["text"] = "Different bank";
        questions.Add(second);

        var result = Read(root.ToJsonString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "QUESTION_GROUP_OPTION_MISMATCH");
    }

    [Fact]
    public void Authored_policy_without_question_evidence_is_rejected()
    {
        var root = JsonNode.Parse(ValidV2Json())!.AsObject();
        root["policyProfile"] = JsonNode.Parse("""{ "explanation": { "mode": "authored" } }""");
        root["sections"]![0]!["parts"]![0]!["questions"]![0]!.AsObject().Remove("explanation");

        var result = Read(root.ToJsonString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "AUTHORED_EXPLANATION_MISSING");
    }

    [Fact]
    public void Full_IELTS_profile_refuses_practice_shape()
    {
        var root = JsonNode.Parse(ValidV2Json())!.AsObject();
        root["formatProfile"] = "ielts-academic-full";

        var result = Read(root.ToJsonString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, f => f.Code == "FORMAT_PROFILE_PART_COUNT");
        Assert.Contains(result.Findings, f => f.Code == "FORMAT_PROFILE_SLOT_COUNT");
    }
}
