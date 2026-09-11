using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Ai.Importing;
using Vni.Ielts.Infrastructure.Content;
using Vni.Ielts.Infrastructure.Content.Import;
using static Vni.Ielts.Infrastructure.Tests.Content.Import.HostileArchives;

namespace Vni.Ielts.Infrastructure.Tests.Content.Import;

/// <summary>
/// The boundary this suite exists for is one call: what
/// <see cref="IExamSourceParser.ParseAsync"/> is handed.
///
/// <b>Everything else in the pipeline is real.</b> The archive inspector, the
/// source extractor, the import workflow and the package validator are the
/// production types, because a faked inspector would decide the very layout the
/// security property is derived from, and a faked validator would let a package
/// through that the real one refuses. Only the parser is a double — it is an AI
/// call, and what it is shown is the whole point.
/// </summary>
public sealed class ExamPackageImportPipelineTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>
    /// A parser that remembers its input, so a test can assert what the model
    /// would have seen. The real parser is an AI call; what it is handed is the
    /// whole security property of this task.
    /// </summary>
    private sealed class RecordingParser(string? packageJson = null) : IExamSourceParser
    {
        public string PromptVersion => "test-parse-prompt";

        public string? LastSourceText { get; private set; }

        public Task<ParsedExamPackage> ParseAsync(ExtractedImportSource source, CancellationToken ct)
        {
            LastSourceText = source.Text;
            // Metadata is non-nullable on ParsedExamPackage; a fake still has to
            // name a provider, because the draft records what produced it.
            return Task.FromResult(new ParsedExamPackage(
                packageJson ?? OneReadingQuestion(),
                // The same value this parser reports on the port. A real one
                // reads both off `ExamParserOptions.PromptVersion`, and the
                // resume lookup matches a draft on what produced it — a fake
                /// that disagreed with itself would never resume.
                new ParserRunMetadata("fake", "recording-parser", PromptVersion, "req-1")));
        }

        /// <summary>
        /// A schema-valid one-question Reading package carrying an answer the
        /// model invented.
        ///
        /// <b>The invented answer is deliberate and the schema forces it.</b>
        /// An auto-scored question must carry an <c>answerKey</c> or
        /// <c>slots</c>, so a real parser cannot return a keyless package and
        /// have it validate — which is exactly why the fabrication measured on
        /// 2026-09-02 was invisible. Here the model guesses FALSE; the supplied
        /// key says TRUE, and the pipeline must end up with TRUE.
        /// </summary>
        private static string OneReadingQuestion() =>
            """
            {
              "formatVersion": "2.0", "formatProfile": "vni-practice",
              "scoringProfileRef": "validation-v1",
              "contentSourceRef": { "sourceId": "recording-parser",
                "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              "title": "T",
              "variant": "academic",
              "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
              "scoringProfile": { "rawToBand": { "reading": [
                { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 } ] } },
              "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
                "kind": "passage", "body": "The roof is made of slate.",
                "questions": [ { "id": "r1", "order": 1, "type": "true-false-notgiven",
                  "answerKey": { "accepted": ["FALSE"] } } ] } ] } ]
            }
            """;

        /// <summary>
        /// The same paper with a second question the supplied key says nothing
        /// about — the shape a model that solved the paper anyway produces.
        /// Both answers are the model's own guesses.
        /// </summary>
        public static string TwoReadingQuestions() =>
            """
            {
              "formatVersion": "2.0", "formatProfile": "vni-practice",
              "scoringProfileRef": "validation-v1",
              "contentSourceRef": { "sourceId": "recording-parser",
                "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              "title": "T",
              "variant": "academic",
              "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
              "scoringProfile": { "rawToBand": { "reading": [
                { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 },
                { "minRaw": 2, "band": 2 } ] } },
              "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
                "kind": "passage", "body": "The roof is made of slate.",
                "questions": [
                  { "id": "r1", "order": 1, "type": "true-false-notgiven",
                    "answerKey": { "accepted": ["FALSE"] } },
                  { "id": "r2", "order": 2, "type": "true-false-notgiven",
                    "answerKey": { "accepted": ["FALSE"] } } ] } ] } ]
            }
            """;

        /// <summary>
        /// A Reading question and a Listening question, both answered by the
        /// model, both numbered 1. The pair is what proves a strip aimed at one
        /// skill does not reach into the other.
        /// </summary>
        public static string ReadingAndListening() =>
            """
            {
              "formatVersion": "2.0", "formatProfile": "vni-practice",
              "scoringProfileRef": "validation-v1",
              "contentSourceRef": { "sourceId": "recording-parser",
                "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              "title": "T",
              "variant": "academic",
              "timingProfile": { "sections": {
                "reading": { "durationSeconds": 3600 },
                "listening": { "durationSeconds": 1800 } } },
              "scoringProfile": { "rawToBand": {
                "reading": [ { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 } ],
                "listening": [ { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 } ] } },
              "sections": [
                { "module": "reading", "order": 1, "parts": [ { "order": 1,
                  "kind": "passage", "body": "The roof is made of slate.",
                  "questions": [ { "id": "r1", "order": 1, "type": "true-false-notgiven",
                    "answerKey": { "accepted": ["FALSE"] } } ] } ] },
                { "module": "listening", "order": 2, "parts": [ { "order": 1,
                  "kind": "recording", "transcript": "The bell rings at noon.",
                  "questions": [ { "id": "l1", "order": 1, "type": "true-false-notgiven",
                    "answerKey": { "accepted": ["FALSE"] } } ] } ] } ]
            }
            """;

        /// <summary>
        /// A Listening recording with <b>no transcript</b> and a completion
        /// question — the shape every one of the 24 Listening parts in this
        /// repository's Cambridge packages has, and the shape
        /// <see cref="PassageAnchorCheck"/> skips entirely because a
        /// <c>recording</c> part anchors in its <c>transcript</c> and there is
        /// none.
        /// </summary>
        public static string ListeningRecordingWithNoTranscript() =>
            """
            {
              "formatVersion": "2.0", "formatProfile": "vni-practice",
              "scoringProfileRef": "validation-v1",
              "contentSourceRef": { "sourceId": "recording-parser",
                "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              "title": "T",
              "variant": "academic",
              "timingProfile": { "sections": { "listening": { "durationSeconds": 1800 } } },
              "scoringProfile": { "rawToBand": { "listening": [
                { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 } ] } },
              "sections": [ { "module": "listening", "order": 1, "parts": [ { "order": 1,
                "kind": "recording",
                "questions": [ { "id": "l1", "order": 1, "type": "completion",
                  "answerKey": { "accepted": ["slate"] } } ] } ] } ]
            }
            """;

        /// <summary>
        /// A completion question the model answered with five words against its
        /// own two-word limit — no key folder involved, so the invented answer is
        /// exactly what reaches <see cref="PaperKeyConsistency"/>.
        /// </summary>
        public static string OneCompletionQuestionOverItsOwnWordLimit() =>
            """
            {
              "formatVersion": "2.0", "formatProfile": "vni-practice",
              "scoringProfileRef": "validation-v1",
              "contentSourceRef": { "sourceId": "recording-parser",
                "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              "title": "T",
              "variant": "academic",
              "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
              "scoringProfile": { "rawToBand": { "reading": [
                { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 } ] } },
              "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
                "kind": "passage", "body": "The roof is made of slate.",
                "questions": [ { "id": "r1", "order": 1, "type": "completion",
                  "constraints": { "maxWords": 2 },
                  "answerKey": { "accepted": ["a very long answer"] } } ] } ] } ]
            }
            """;

        /// <summary>
        /// A completion question whose invented answer is nowhere in its own
        /// passage — no key folder involved, so the model's guess is exactly
        /// what reaches <see cref="PassageAnchorCheck"/>.
        /// </summary>
        public static string OneCompletionQuestionWithAnAnswerNotInThePassage() =>
            """
            {
              "formatVersion": "2.0", "formatProfile": "vni-practice",
              "scoringProfileRef": "validation-v1",
              "contentSourceRef": { "sourceId": "recording-parser",
                "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              "title": "T",
              "variant": "academic",
              "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
              "scoringProfile": { "rawToBand": { "reading": [
                { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 } ] } },
              "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
                "kind": "passage", "body": "The hall has a slate roof.",
                "questions": [ { "id": "r1", "order": 1, "type": "completion",
                  "group": { "id": "g1" },
                  "answerKey": { "accepted": ["copper roof"] } } ] } ] } ]
            }
            """;

        /// <summary>
        /// Two completion questions in one group, neither answer anywhere in a
        /// passage that is nonetheless present and intact — the shape of a
        /// paper parsed wrong, which stays a blocking error with no override.
        /// </summary>
        public static string TwoCompletionQuestionsNotInTheirPassage() =>
            """
            {
              "formatVersion": "2.0", "formatProfile": "vni-practice",
              "scoringProfileRef": "validation-v1",
              "contentSourceRef": { "sourceId": "recording-parser",
                "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              "title": "T",
              "variant": "academic",
              "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
              "scoringProfile": { "rawToBand": { "reading": [
                { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 },
                { "minRaw": 2, "band": 2 } ] } },
              "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
                "kind": "passage", "body": "An unrelated passage entirely.",
                "questions": [
                  { "id": "r1", "order": 1, "type": "completion",
                    "group": { "id": "g1" },
                    "answerKey": { "accepted": ["slate roof"] } },
                  { "id": "r2", "order": 2, "type": "completion",
                    "group": { "id": "g1" },
                    "answerKey": { "accepted": ["stained glass"] } } ] } ] } ]
            }
            """;

        /// <summary>
        /// Two completion questions in the same group, both answers present in
        /// the passage, but numbered the wrong way round against where they sit
        /// in the text — the shape of a key shifted by a line. No key folder
        /// involved, so these are the model's own answers, and this is what
        /// reaches <see cref="PassageAnchorCheck"/> as an out-of-order result.
        /// </summary>
        public static string TwoCompletionQuestionsOutOfPassageOrder() =>
            """
            {
              "formatVersion": "2.0", "formatProfile": "vni-practice",
              "scoringProfileRef": "validation-v1",
              "contentSourceRef": { "sourceId": "recording-parser",
                "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              "title": "T",
              "variant": "academic",
              "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
              "scoringProfile": { "rawToBand": { "reading": [
                { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 },
                { "minRaw": 2, "band": 2 } ] } },
              "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
                "kind": "passage",
                "body": "The hall has a slate roof. The west window was replaced with stained glass.",
                "questions": [
                  { "id": "r1", "order": 1, "type": "completion",
                    "group": { "id": "g1" },
                    "answerKey": { "accepted": ["stained glass"] } },
                  { "id": "r2", "order": 2, "type": "completion",
                    "group": { "id": "g1" },
                    "answerKey": { "accepted": ["slate roof"] } } ] } ] } ]
            }
            """;
    }

    /// <summary>
    /// A key file whose text layer carries only its own headings — the shape a
    /// scanned or image-only key arrives in, where every printed answer is a
    /// picture and nothing survives extraction.
    /// </summary>
    private const string UnreadableKey = """
        ĐÁP ÁN
        READING 1
        ........
        """;

    [Fact]
    public async Task The_answer_key_never_reaches_the_parser()
    {
        var parser = new RecordingParser();
        var pipeline = PipelineWith(parser);
        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap-an/key.txt", "Câu số 1: TRUE"));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        Assert.NotNull(parser.LastSourceText);
        Assert.DoesNotContain("TRUE", parser.LastSourceText);
        Assert.DoesNotContain("Câu số", parser.LastSourceText);
        Assert.Contains("slate", parser.LastSourceText);
    }

    [Fact]
    public async Task The_supplied_key_is_written_onto_the_parsed_package()
    {
        var pipeline = PipelineWith(new RecordingParser());
        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap-an/key.txt", "Câu số 1: TRUE"));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        var doc = JsonNode.Parse(attempt.Draft!.PackageJson)!.AsObject();
        var question = doc["sections"]![0]!["parts"]![0]!["questions"]![0]!;
        Assert.Equal("TRUE", question["answerKey"]!["accepted"]!.AsArray()[0]!.GetValue<string>());
    }

    /// <summary>
    /// The strip, proven where it actually bites: a question the key does not
    /// cover. <see cref="AnswerKeyInjection"/> overwrites the questions it
    /// keys, so a guess on those is replaced either way — the guess that
    /// survives without the strip is the one on the question nobody keyed, and
    /// it would mark every learner who ever sits the paper.
    /// </summary>
    [Fact]
    public async Task A_guess_on_a_question_the_key_does_not_cover_does_not_survive()
    {
        var pipeline = PipelineWith(new RecordingParser(RecordingParser.TwoReadingQuestions()));
        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap-an/key.txt", "Câu số 1: TRUE"));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted);
        var questions = JsonNode.Parse(attempt.Draft!.PackageJson)!
            .AsObject()["sections"]![0]!["parts"]![0]!["questions"]!.AsArray();

        Assert.Equal("TRUE", questions[0]!["answerKey"]!["accepted"]!.AsArray()[0]!.GetValue<string>());
        Assert.Null(questions[1]!["answerKey"]);
    }

    /// <summary>
    /// A key folder that exists but yields nothing readable.
    ///
    /// <b>The model's answers go with it.</b> The caller supplied a key, so the
    /// guesses were never meant to stand, and nothing arrived to replace them.
    /// Persisting them is the 2026-09-02 failure written to disk — well-formed,
    /// unverifiable answers on a draft that looks finished. The finding blocks
    /// approval; the missing keys are the safer half of that trade.
    /// </summary>
    [Fact]
    public async Task An_unreadable_key_folder_takes_the_model_written_answers_with_it()
    {
        var pipeline = PipelineWith(new RecordingParser());
        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap-an/key.txt", UnreadableKey));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        Assert.Contains(
            attempt.Draft!.Findings,
            f => f.Code == "ANSWER_KEY_UNREADABLE" && f.Severity == "error");

        var question = JsonNode.Parse(attempt.Draft.PackageJson)!
            .AsObject()["sections"]![0]!["parts"]![0]!["questions"]![0]!;

        Assert.Null(question["answerKey"]);
    }

    /// <summary>
    /// Reading's key is readable, Listening's is not, and the loop visits
    /// Reading first. An unscoped strip on the Listening pass would delete the
    /// answer Reading's own key had just written — a silent loss of exactly the
    /// verified data this whole task exists to protect.
    /// </summary>
    [Fact]
    public async Task An_unreadable_key_for_one_skill_does_not_disturb_another_skills_answers()
    {
        var pipeline = PipelineWith(new RecordingParser(RecordingParser.ReadingAndListening()));
        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap-an/key.txt", "Câu số 1: TRUE"),
            File("listening/de/section-1.txt", "The bell rings at noon."),
            File("listening/dap-an/key.txt", UnreadableKey));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        var sections = JsonNode.Parse(attempt.Draft!.PackageJson)!.AsObject()["sections"]!.AsArray();

        var reading = sections[0]!["parts"]![0]!["questions"]![0]!;
        var listening = sections[1]!["parts"]![0]!["questions"]![0]!;

        Assert.NotNull(reading["answerKey"]);
        Assert.Equal("TRUE", reading["answerKey"]!["accepted"]!.AsArray()[0]!.GetValue<string>());
        Assert.Null(listening["answerKey"]);
    }

    /// <summary>
    /// The other half of the layout-derived choice: no key folder, so the
    /// fabrication guard runs and the model's invented answer is reported for
    /// review rather than silently kept.
    /// </summary>
    [Fact]
    public async Task Without_a_key_folder_the_fabrication_guard_still_runs()
    {
        var pipeline = PipelineWith(new RecordingParser());
        var archive = Build(File("reading/de/passage.txt", "The roof is made of slate."));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        Assert.Contains(
            attempt.Draft!.Warnings,
            w => w.Id.StartsWith("FABRICATED_ANSWER_KEY", StringComparison.Ordinal));
    }

    /// <summary>
    /// <see cref="PaperKeyConsistency"/> runs from inside the pipeline, not
    /// only against a hand-built JSON string in isolation. No key folder is
    /// supplied, so the model's own invented answer — five words against a
    /// two-word limit the paper itself states — is what the checker sees, and
    /// the pipeline must persist the finding on the draft it saves.
    /// </summary>
    [Fact]
    public async Task The_papers_own_word_limit_is_checked_against_the_key_the_pipeline_saves()
    {
        var pipeline = PipelineWith(
            new RecordingParser(RecordingParser.OneCompletionQuestionOverItsOwnWordLimit()));
        var archive = Build(File("reading/de/passage.txt", "The roof is made of slate."));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        Assert.Contains(
            attempt.Draft!.Findings,
            f => f.Code == PaperKeyConsistency.WordLimitCode && f.Severity == "error");
    }

    /// <summary>
    /// A key folder for Reading and none for Listening.
    ///
    /// <b>The decision has to be per skill.</b> A package-wide "a key was
    /// supplied" flag sends the whole import down the keyed route, so
    /// Listening gets neither a real key nor the fabrication guard and the
    /// model's invented Listening answers are persisted with nothing said about
    /// them — worse than a package with no key folder at all, which always
    /// flagged them.
    /// </summary>
    [Fact]
    public async Task A_skill_with_no_key_folder_still_meets_the_fabrication_guard()
    {
        var pipeline = PipelineWith(new RecordingParser(RecordingParser.ReadingAndListening()));
        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap-an/key.txt", "Câu số 1: TRUE"),
            File("listening/de/section-1.txt", "The bell rings at noon."));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        var sections = JsonNode.Parse(attempt.Draft!.PackageJson)!.AsObject()["sections"]!.AsArray();

        Assert.Equal(
            "TRUE",
            sections[0]!["parts"]![0]!["questions"]![0]!["answerKey"]!["accepted"]!
                .AsArray()[0]!.GetValue<string>());

        var fabricated = attempt.Draft.Warnings
            .Where(w => w.Id.StartsWith("FABRICATED_ANSWER_KEY", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(fabricated);
        Assert.All(fabricated, w => Assert.StartsWith("/sections/1/", w.Path, StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>Version</c> is materialised from the parser's output, so a draft that
    /// is not re-validated after the key is written carries the model's guess in
    /// one field and the supplier's key in another — one record, two answers to
    /// "what is correct for question 1".
    /// </summary>
    [Fact]
    public async Task The_materialised_version_agrees_with_the_keyed_package()
    {
        var pipeline = PipelineWith(new RecordingParser());
        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap-an/key.txt", "Câu số 1: TRUE"));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        Assert.DoesNotContain(
            attempt.Draft!.Findings,
            f => f.Code == ExamPackageImportPipeline.RevalidationFailedCode);

        var question = attempt.Draft.Version.Sections
            .Single(sec => sec.Module == ExamModule.Reading)
            .Parts.Single()
            .Questions.Single();

        Assert.Equal("TRUE", Assert.Single(question.AnswerKey!.Accepted).Single);
    }

    /// <summary>
    /// The unreadable-key path leaves the package deliberately schema-invalid:
    /// its questions carry no answer at all. The version is then left as it was
    /// and the state is named, rather than a version being invented for a
    /// package that does not validate.
    /// </summary>
    [Fact]
    public async Task A_package_that_stops_validating_says_so_instead_of_inventing_a_version()
    {
        var pipeline = PipelineWith(new RecordingParser());
        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap-an/key.txt", UnreadableKey));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        Assert.Contains(
            attempt.Draft!.Findings,
            f => f.Code == ExamPackageImportPipeline.RevalidationFailedCode && f.Severity == "error");
    }

    /// <summary>
    /// No key folder means the answers came from the model. Those are exactly
    /// the answers most worth checking.
    ///
    /// <b>Filed as a blocking, clearable warning since 2026-09-10</b>, not as
    /// an error with no override — see <see cref="AnchorMissingAnswerIssue"/>.
    /// It must still reach a gate: this asserts the warning is unresolved on
    /// the draft and absent from <c>draft.Findings</c>.
    /// </summary>
    [Fact]
    public async Task A_package_with_no_key_folder_is_still_cross_checked_for_passage_anchors()
    {
        var pipeline = PipelineWith(
            new RecordingParser(RecordingParser.OneCompletionQuestionWithAnAnswerNotInThePassage()));
        var archive = Build(File("reading/de/passage.txt", "The hall has a slate roof."));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        var missing = Assert.Single(
            attempt.Draft!.Warnings,
            w => w.Id.StartsWith(PassageAnchorCheck.NotInPassageCode, StringComparison.Ordinal));
        Assert.False(missing.Resolved);
        Assert.DoesNotContain(
            attempt.Draft.Findings, f => f.Code == PassageAnchorCheck.NotInPassageCode);
    }

    /// <summary>
    /// The ruling of 2026-09-10, end to end: a key answer the matcher cannot
    /// find still stops the draft being approved, and a reviewer with a
    /// written reason can clear it. The premise that made it an
    /// un-overridable error — "the answer is not in the passage" being a clean
    /// fact — is false: normalisation choices do not round-trip, so a correct
    /// answer the matcher cannot see looks exactly like an absent one.
    /// </summary>
    [Fact]
    public async Task An_answer_not_found_in_the_passage_blocks_approval_until_a_reviewer_clears_it()
    {
        var (pipeline, drafts, validator) = PipelineWithStore(
            new RecordingParser(RecordingParser.OneCompletionQuestionWithAnAnswerNotInThePassage()));
        var archive = Build(File("reading/de/passage.txt", "The hall has a slate roof."));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);
        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        var missing = Assert.Single(
            attempt.Draft!.Warnings,
            w => w.Id.StartsWith(PassageAnchorCheck.NotInPassageCode, StringComparison.Ordinal));

        var review = new ImportReviewWorkflow(drafts, validator);
        var checklisted = await review.SetChecklistAsync(
            attempt.Draft.Id, attempt.Draft.Revision,
            Enum.GetValues<ImportReviewCategory>().ToHashSet(), ReviewerActor, default);
        var revision = checklisted.Draft!.Revision;

        foreach (var other in checklisted.Draft.Warnings.Where(w => !w.Resolved && w.Id != missing.Id))
        {
            var step = await review.ResolveWarningAsync(
                attempt.Draft.Id, revision, other.Id, "checked", ReviewerActor, default);
            Assert.True(step.IsSuccess, step.ErrorCode);
            revision = step.Draft!.Revision;
        }

        var blocked = await review.ApproveAsync(attempt.Draft.Id, revision, ReviewerActor, default);
        Assert.False(blocked.IsSuccess);
        Assert.Equal("IMPORT_WARNINGS_UNRESOLVED", blocked.ErrorCode);

        var resolved = await review.ResolveWarningAsync(
            attempt.Draft.Id, revision, missing.Id,
            "the paper prints the answer with different spacing", ReviewerActor, default);
        Assert.True(resolved.IsSuccess, resolved.ErrorCode);

        var approved = await review.ApproveAsync(
            attempt.Draft.Id, resolved.Draft!.Revision, ReviewerActor, default);

        Assert.True(approved.IsSuccess, approved.ErrorCode);
        Assert.Equal(ImportApprovalState.Approved, approved.Draft!.ApprovalState);
    }

    /// <summary>
    /// The other half of the same split, at the pipeline boundary: a whole
    /// group anchoring nothing is still an error on <c>draft.Findings</c>, and
    /// `ApproveAsync` refuses it with no override at all. Resolving every
    /// warning does not help.
    /// </summary>
    [Fact]
    public async Task A_whole_group_passage_mismatch_still_refuses_with_no_override()
    {
        var (pipeline, drafts, validator) = PipelineWithStore(
            new RecordingParser(RecordingParser.TwoCompletionQuestionsNotInTheirPassage()));
        var archive = Build(File("reading/de/passage.txt", "An unrelated passage entirely."));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);
        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        Assert.Contains(
            attempt.Draft!.Findings,
            f => f.Code == PassageAnchorCheck.PassageMismatchCode && f.Severity == "error");

        var review = new ImportReviewWorkflow(drafts, validator);
        var checklisted = await review.SetChecklistAsync(
            attempt.Draft.Id, attempt.Draft.Revision,
            Enum.GetValues<ImportReviewCategory>().ToHashSet(), ReviewerActor, default);
        var revision = checklisted.Draft!.Revision;

        foreach (var warning in checklisted.Draft.Warnings.Where(w => !w.Resolved))
        {
            var step = await review.ResolveWarningAsync(
                attempt.Draft.Id, revision, warning.Id, "checked", ReviewerActor, default);
            Assert.True(step.IsSuccess, step.ErrorCode);
            revision = step.Draft!.Revision;
        }

        var result = await review.ApproveAsync(attempt.Draft.Id, revision, ReviewerActor, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_FINDINGS_BLOCKING", result.ErrorCode);
    }

    /// <summary>
    /// The ruling of 2026-09-10 on the layout: <c>reading/dap_an/</c> is not
    /// the same event as <c>__MACOSX/</c>. The unknown role folder means the
    /// file <b>was used</b>, as paper — so an answer key spelled with an
    /// underscore went to the model — and the administrator who typed it has
    /// to be told. It reaches the draft as a blocking, clearable warning under
    /// its own code; <c>LAYOUT_UNKNOWN_ENTRY</c> is not routed and stays
    /// exactly as it was.
    /// </summary>
    [Fact]
    public async Task A_misspelled_key_folder_reaches_the_draft_as_a_blocking_clearable_warning()
    {
        var (pipeline, drafts, validator) = PipelineWithStore(new RecordingParser());
        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap_an/key.txt", "Câu số 1: TRUE"),
            File("notes.txt", "a stray root file, ignored"));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);
        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));

        var layout = Assert.Single(
            attempt.Draft!.Warnings,
            w => w.Id.StartsWith(ArchiveFindingCodes.LayoutUnknownRoleFolder, StringComparison.Ordinal));
        Assert.False(layout.Resolved);
        Assert.Equal("reading/dap_an/", layout.Path);

        // The ignored root file is noise and must not have been routed.
        Assert.DoesNotContain(
            attempt.Draft.Warnings,
            w => w.Id.StartsWith(ArchiveFindingCodes.LayoutUnknownEntry, StringComparison.Ordinal));

        var review = new ImportReviewWorkflow(drafts, validator);
        var checklisted = await review.SetChecklistAsync(
            attempt.Draft.Id, attempt.Draft.Revision,
            Enum.GetValues<ImportReviewCategory>().ToHashSet(), ReviewerActor, default);
        var revision = checklisted.Draft!.Revision;

        foreach (var other in checklisted.Draft.Warnings.Where(w => !w.Resolved && w.Id != layout.Id))
        {
            var step = await review.ResolveWarningAsync(
                attempt.Draft.Id, revision, other.Id, "checked", ReviewerActor, default);
            Assert.True(step.IsSuccess, step.ErrorCode);
            revision = step.Draft!.Revision;
        }

        var blocked = await review.ApproveAsync(attempt.Draft.Id, revision, ReviewerActor, default);
        Assert.False(blocked.IsSuccess);
        Assert.Equal("IMPORT_WARNINGS_UNRESOLVED", blocked.ErrorCode);

        var resolved = await review.ResolveWarningAsync(
            attempt.Draft.Id, revision, layout.Id,
            "the folder holds figures, not a key", ReviewerActor, default);
        Assert.True(resolved.IsSuccess, resolved.ErrorCode);

        var approved = await review.ApproveAsync(
            attempt.Draft.Id, resolved.Draft!.Revision, ReviewerActor, default);
        Assert.True(approved.IsSuccess, approved.ErrorCode);
    }

    /// <summary>
    /// The defect this pins: before the fix, Layer 4b's out-of-order result
    /// was filed as a "warning"-severity <see cref="PackageFinding"/>.
    /// <see cref="ImportReviewWorkflow.ApproveAsync"/> only blocks on
    /// <c>Severity == "error"</c> findings, and there is no resolve path for
    /// a finding at all — so the result fell between both gates and a key
    /// running backwards mid-group could be approved with nobody having seen
    /// it. It now has to reach <c>draft.Warnings</c> as an unresolved
    /// <see cref="ImportReviewWarning"/>, which does block.
    ///
    /// The AI-parsed route always carries a second, unrelated warning
    /// (<c>AI_PARSE_REVIEW</c> — compare source against parsed output) — it is
    /// resolved here so the refusal below is provably about the order result,
    /// not incidental noise from the route.
    /// </summary>
    [Fact]
    public async Task A_draft_carrying_an_out_of_order_result_cannot_be_approved()
    {
        var (pipeline, drafts, validator) = PipelineWithStore(
            new RecordingParser(RecordingParser.TwoCompletionQuestionsOutOfPassageOrder()));
        var archive = Build(
            File(
                "reading/de/passage.txt",
                "The hall has a slate roof. The west window was replaced with stained glass."),
            File("reading/dap-an/key.txt", "Câu số 1: stained glass\nCâu số 2: slate roof"));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);
        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));

        var order = Assert.Single(
            attempt.Draft!.Warnings,
            w => w.Id.StartsWith(PassageAnchorCheck.OutOfOrderCode, StringComparison.Ordinal));
        Assert.False(order.Resolved);
        Assert.DoesNotContain(attempt.Draft.Findings, f => f.Code == PassageAnchorCheck.OutOfOrderCode);

        var review = new ImportReviewWorkflow(drafts, validator);
        var checklisted = await review.SetChecklistAsync(
            attempt.Draft.Id, attempt.Draft.Revision,
            Enum.GetValues<ImportReviewCategory>().ToHashSet(), ReviewerActor, default);
        var resolvedParseReview = await review.ResolveWarningAsync(
            attempt.Draft.Id, checklisted.Draft!.Revision, "AI_PARSE_REVIEW",
            "source and parsed package compared", ReviewerActor, default);
        Assert.True(resolvedParseReview.IsSuccess, resolvedParseReview.ErrorCode);

        var result = await review.ApproveAsync(
            attempt.Draft.Id, resolvedParseReview.Draft!.Revision, ReviewerActor, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_WARNINGS_UNRESOLVED", result.ErrorCode);
    }

    /// <summary>
    /// The other half of the fix: once the reviewer's gate can see the
    /// warning at all, `P-19`'s override path — resolve with a recorded
    /// reason — has to actually unblock it.
    /// </summary>
    [Fact]
    public async Task Resolving_the_out_of_order_warning_then_permits_approval()
    {
        var (pipeline, drafts, validator) = PipelineWithStore(
            new RecordingParser(RecordingParser.TwoCompletionQuestionsOutOfPassageOrder()));
        var archive = Build(
            File(
                "reading/de/passage.txt",
                "The hall has a slate roof. The west window was replaced with stained glass."),
            File("reading/dap-an/key.txt", "Câu số 1: stained glass\nCâu số 2: slate roof"));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);
        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        var order = Assert.Single(
            attempt.Draft!.Warnings,
            w => w.Id.StartsWith(PassageAnchorCheck.OutOfOrderCode, StringComparison.Ordinal));

        var review = new ImportReviewWorkflow(drafts, validator);
        var checklisted = await review.SetChecklistAsync(
            attempt.Draft.Id, attempt.Draft.Revision,
            Enum.GetValues<ImportReviewCategory>().ToHashSet(), ReviewerActor, default);
        var resolvedParseReview = await review.ResolveWarningAsync(
            attempt.Draft.Id, checklisted.Draft!.Revision, "AI_PARSE_REVIEW",
            "source and parsed package compared", ReviewerActor, default);
        Assert.True(resolvedParseReview.IsSuccess, resolvedParseReview.ErrorCode);
        var resolved = await review.ResolveWarningAsync(
            attempt.Draft.Id, resolvedParseReview.Draft!.Revision, order.Id,
            "checked against the original key", ReviewerActor, default);
        Assert.True(resolved.IsSuccess, resolved.ErrorCode);

        var approved = await review.ApproveAsync(attempt.Draft.Id, resolved.Draft!.Revision, ReviewerActor, default);

        Assert.True(approved.IsSuccess, approved.ErrorCode);
        Assert.Equal(ImportApprovalState.Approved, approved.Draft!.ApprovalState);
    }

    /// <summary>
    /// <b>An injection warning has to reach a gate a person can see.</b>
    /// <see cref="AnswerKeyInjection"/> returns six things at severity
    /// <c>"warning"</c> — here <c>ANSWER_KEY_TYPE_RETYPED</c>: the model typed
    /// question 1 as True/False/Not Given and the supplier's key answers YES,
    /// so the question's own type is rewritten to make the key fit.
    ///
    /// Added to <c>draft.Findings</c>, as this branch first did, that fell
    /// between both gates: <see cref="ImportReviewWorkflow.ApproveAsync"/>
    /// blocks only on <c>Severity == "error"</c>, and
    /// <see cref="ImportReviewWorkflow.ResolveWarningAsync"/> operates only on
    /// <c>draft.Warnings</c> — so it never blocked, could never be cleared and
    /// was never audited. Before this branch the HTTP pipeline never called
    /// <see cref="AnswerKeyInjection"/> at all, so this branch is what makes
    /// it reachable.
    ///
    /// The unrelated <c>AI_PARSE_REVIEW</c> warning the AI-parsed route always
    /// carries is resolved first, so the refusal below is provably about the
    /// injection warning rather than incidental noise from the route.
    /// </summary>
    [Fact]
    public async Task A_draft_carrying_an_answer_key_injection_warning_cannot_be_approved()
    {
        var (pipeline, drafts, validator) = PipelineWithStore(new RecordingParser());
        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap-an/key.txt", "Câu số 1: YES"));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);
        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));

        var retyped = Assert.Single(
            attempt.Draft!.Warnings,
            w => w.Id.StartsWith(AnswerKeyInjection.TypeRetypedCode, StringComparison.Ordinal));
        Assert.False(retyped.Resolved);
        Assert.DoesNotContain(
            attempt.Draft.Findings, f => f.Code == AnswerKeyInjection.TypeRetypedCode);

        var review = new ImportReviewWorkflow(drafts, validator);
        var checklisted = await review.SetChecklistAsync(
            attempt.Draft.Id, attempt.Draft.Revision,
            Enum.GetValues<ImportReviewCategory>().ToHashSet(), ReviewerActor, default);
        var resolvedParseReview = await review.ResolveWarningAsync(
            attempt.Draft.Id, checklisted.Draft!.Revision, "AI_PARSE_REVIEW",
            "source and parsed package compared", ReviewerActor, default);
        Assert.True(resolvedParseReview.IsSuccess, resolvedParseReview.ErrorCode);

        var result = await review.ApproveAsync(
            attempt.Draft.Id, resolvedParseReview.Draft!.Revision, ReviewerActor, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_WARNINGS_UNRESOLVED", result.ErrorCode);
    }

    /// <summary>
    /// The other half of the same fix: `P-19`'s override path — resolve with a
    /// recorded reason — has to actually unblock it once the gate can see it.
    /// A warning that blocks and can never be cleared is the other way to make
    /// a check worthless.
    /// </summary>
    [Fact]
    public async Task Resolving_the_injection_warning_then_permits_approval()
    {
        var (pipeline, drafts, validator) = PipelineWithStore(new RecordingParser());
        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap-an/key.txt", "Câu số 1: YES"));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);
        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        var retyped = Assert.Single(
            attempt.Draft!.Warnings,
            w => w.Id.StartsWith(AnswerKeyInjection.TypeRetypedCode, StringComparison.Ordinal));

        var review = new ImportReviewWorkflow(drafts, validator);
        var checklisted = await review.SetChecklistAsync(
            attempt.Draft.Id, attempt.Draft.Revision,
            Enum.GetValues<ImportReviewCategory>().ToHashSet(), ReviewerActor, default);
        var resolvedParseReview = await review.ResolveWarningAsync(
            attempt.Draft.Id, checklisted.Draft!.Revision, "AI_PARSE_REVIEW",
            "source and parsed package compared", ReviewerActor, default);
        Assert.True(resolvedParseReview.IsSuccess, resolvedParseReview.ErrorCode);
        var resolved = await review.ResolveWarningAsync(
            attempt.Draft.Id, resolvedParseReview.Draft!.Revision, retyped.Id,
            "printed rubric checked: the group is YES/NO/NOT GIVEN", ReviewerActor, default);
        Assert.True(resolved.IsSuccess, resolved.ErrorCode);

        var approved = await review.ApproveAsync(
            attempt.Draft.Id, resolved.Draft!.Revision, ReviewerActor, default);

        Assert.True(approved.IsSuccess, approved.ErrorCode);
        Assert.Equal(ImportApprovalState.Approved, approved.Draft!.ApprovalState);
    }

    private static readonly ImportReviewActor ReviewerActor = new("reviewer", false, true, false);

    // ── Wiring ───────────────────────────────────────────────────────────

    // ── Task 5: Listening audio becomes text, before the anchor check runs ──

    /// <summary>
    /// The measured state this task exists to end, pinned so it cannot be
    /// mistaken for working. A Listening recording with no transcript is
    /// <b>skipped entirely</b> by <see cref="PassageAnchorCheck"/> — an answer
    /// nobody could have produced raises nothing at all, because there is no
    /// text to search. 0 of 24 Listening parts in this repository's Cambridge
    /// packages carry a transcript, so this is every one of them.
    /// </summary>
    [Fact]
    public async Task Without_a_transcriber_a_listening_recording_is_not_anchor_checked_at_all()
    {
        var pipeline = PipelineWith(
            new RecordingParser(RecordingParser.ListeningRecordingWithNoTranscript()),
            new UnconfiguredAudioTranscriber());

        var archive = Build(
            File("listening/de/section-1.txt", "The station is old."),
            Bytes("listening/audio/part1.mp3", [0x49, 0x44, 0x33, 0x04]));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));

        var part = JsonNode.Parse(attempt.Draft!.PackageJson)!["sections"]![0]!["parts"]![0]!;
        Assert.Null(part["transcript"]);

        Assert.DoesNotContain(
            attempt.Draft.Warnings,
            w => w.Id.StartsWith(PassageAnchorCheck.NotInPassageCode, StringComparison.Ordinal));
    }

    /// <summary>
    /// The same package, the same answer, one difference: a transcriber is
    /// configured. The transcript is written onto the <b>persisted</b> package
    /// and <see cref="PassageAnchorCheck"/> now has something to search — so
    /// the answer that is nowhere in the recording is finally reported.
    ///
    /// <b>This is the whole point of running at
    /// <see cref="ImportJobStage.Transcribing"/>, between the parse and the
    /// keying.</b> A transcript produced after the anchor check would protect
    /// nothing.
    /// </summary>
    [Fact]
    public async Task A_transcribed_recording_is_persisted_and_the_anchor_check_then_reads_it()
    {
        var pipeline = PipelineWith(
            new RecordingParser(RecordingParser.ListeningRecordingWithNoTranscript()),
            new FixedTranscriber("The station roof is made of copper throughout."));

        var archive = Build(
            File("listening/de/section-1.txt", "The station is old."),
            Bytes("listening/audio/part1.mp3", [0x49, 0x44, 0x33, 0x04]));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));

        var part = JsonNode.Parse(attempt.Draft!.PackageJson)!["sections"]![0]!["parts"]![0]!;
        Assert.Equal(
            "The station roof is made of copper throughout.", part["transcript"]!.GetValue<string>());

        Assert.Contains(
            attempt.Draft.Warnings,
            w => w.Id.StartsWith(PassageAnchorCheck.NotInPassageCode, StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>IP-08</c> reaching the pipeline, not only the stage: a package whose
    /// parse already carried a transcript is left alone and no model is called.
    /// </summary>
    [Fact]
    public async Task A_package_that_already_carries_a_transcript_calls_no_transcriber()
    {
        var transcriber = new FixedTranscriber("should never be used");
        var pipeline = PipelineWith(
            new RecordingParser(RecordingParser.ReadingAndListening()), transcriber);

        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("listening/de/section-1.txt", "The bell rings at noon."),
            Bytes("listening/audio/part1.mp3", [0x49, 0x44, 0x33, 0x04]));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        Assert.Equal(0, transcriber.Calls);

        var listening = JsonNode.Parse(attempt.Draft!.PackageJson)!["sections"]![1]!["parts"]![0]!;
        Assert.Equal("The bell rings at noon.", listening["transcript"]!.GetValue<string>());
    }

    /// <summary>
    /// <c>IP-09</c> reaching the pipeline. One recording needing text, two
    /// audio files: nothing is transcribed, and the package carries a warning
    /// a reviewer has to clear rather than a transcript that might belong to
    /// the other recording.
    /// </summary>
    [Fact]
    public async Task A_mismatched_audio_count_transcribes_nothing_and_raises_a_clearable_warning()
    {
        var transcriber = new FixedTranscriber("whatever this recording said");
        var pipeline = PipelineWith(
            new RecordingParser(RecordingParser.ListeningRecordingWithNoTranscript()), transcriber);

        var archive = Build(
            File("listening/de/section-1.txt", "The station is old."),
            Bytes("listening/audio/part1.mp3", [0x49, 0x44, 0x33, 0x04]),
            Bytes("listening/audio/part2.mp3", [0x49, 0x44, 0x33, 0x04]));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        Assert.Equal(0, transcriber.Calls);

        var part = JsonNode.Parse(attempt.Draft!.PackageJson)!["sections"]![0]!["parts"]![0]!;
        Assert.Null(part["transcript"]);

        var warning = Assert.Single(
            attempt.Draft.Warnings,
            w => w.Id.StartsWith(
                TranscriptionWarningCodes.AudioCountMismatch, StringComparison.Ordinal));

        Assert.False(warning.Resolved);
        Assert.Equal(ImportReviewCategory.AssetMapping, warning.Category);
    }

    /// <summary>
    /// A transcriber that refuses once and then answers — the shape a real one
    /// has, since transcription is neither free nor deterministic.
    /// </summary>
    private sealed class RefusesOnceTranscriber(string text) : IAudioTranscriber
    {
        private bool _refused;

        public bool IsConfigured => true;

        public Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string fileName, CancellationToken ct)
        {
            if (_refused) return Task.FromResult(TranscriptionResult.Transcribed(text));

            _refused = true;
            return Task.FromResult(TranscriptionResult.Refused("PROVIDER_UNAVAILABLE"));
        }
    }

    /// <summary>
    /// <b>F-13: a warning on the draft describes the content on the draft.</b>
    ///
    /// A resumed import skips only the parse, so the transcription runs again —
    /// and it is not deterministic. Run 1 is refused and leaves a
    /// <c>TRANSCRIPT_REFUSED</c> warning on the draft with no transcript; run 2
    /// succeeds and writes one. Merging by id and letting the stored copy win —
    /// which is what the first fix wave did, to avoid reopening a warning a
    /// reviewer had cleared — leaves run 2's transcript sitting beside run 1's
    /// warning saying there is none. A reviewer is then asked to judge a
    /// sentence about content that is not there.
    ///
    /// <b>The reviewer's decision is still not undone</b>; that half is pinned
    /// by <see cref="A_resumed_run_keeps_a_reviewers_resolution_of_a_warning_that_recurs"/>.
    /// The content belongs to this run, the decision belongs to the reviewer.
    /// </summary>
    [Fact]
    public async Task A_resumed_run_drops_a_warning_whose_condition_no_longer_holds()
    {
        var (pipeline, drafts, _) = PipelineWithStore(
            new RecordingParser(RecordingParser.ListeningRecordingWithNoTranscript()),
            new RefusesOnceTranscriber("The station roof is made of copper throughout."));

        var definitionId = ExamDefinitionId.New();

        var first = await pipeline.ImportAsync(
            Build(
                File("listening/de/section-1.txt", "The station is old."),
                Bytes("listening/audio/part1.mp3", [0x49, 0x44, 0x33, 0x04])),
            definitionId, 1, default);

        Assert.True(first.IsAccepted, Describe(first.Findings));
        Assert.Null(JsonNode.Parse(first.Draft!.PackageJson)!["sections"]![0]!["parts"]![0]!["transcript"]);
        Assert.Contains(
            first.Draft.Warnings,
            w => w.Id.StartsWith(TranscriptionWarningCodes.Refused, StringComparison.Ordinal));

        // The retry, resuming past a parse it already paid for.
        var second = await pipeline.ImportAsync(
            Build(
                File("listening/de/section-1.txt", "The station is old."),
                Bytes("listening/audio/part1.mp3", [0x49, 0x44, 0x33, 0x04])),
            definitionId, 1, default, null, ImportJobStage.Keying);

        Assert.True(second.IsAccepted, Describe(second.Findings));

        // The draft really did change under the warning — otherwise there is
        // nothing for a stale warning to be stale about.
        var stored = await drafts.FindAsync(second.Draft!.Id, default);
        Assert.Equal(
            "The station roof is made of copper throughout.",
            JsonNode.Parse(stored!.PackageJson)!["sections"]![0]!["parts"]![0]!["transcript"]!
                .GetValue<string>());

        // The assertion this test exists for.
        Assert.DoesNotContain(
            stored.Warnings,
            w => w.Id.StartsWith(TranscriptionWarningCodes.Refused, StringComparison.Ordinal));

        // And the warnings nobody here recomputes are left exactly where they
        // were: dropping "not in my new set" wholesale would delete a
        // reviewer's outstanding work on the first retry.
        Assert.Contains(
            stored.Warnings,
            w => w.Id.StartsWith(ExamImportWorkflow.AiParseReviewWarningId, StringComparison.Ordinal));
    }

    /// <summary>
    /// The other half of <see cref="Reconcile"/>'s rule, and the reason the
    /// first fix wave chose first-wins in the first place: a warning a reviewer
    /// has cleared with a recorded, audited reason (<c>P-19</c>) must not come
    /// back unresolved because a worker retried. The content is this run's; the
    /// decision is the reviewer's.
    /// </summary>
    [Fact]
    public async Task A_resumed_run_keeps_a_reviewers_resolution_of_a_warning_that_recurs()
    {
        var (pipeline, drafts, _) = PipelineWithStore(
            new RecordingParser(RecordingParser.ListeningRecordingWithNoTranscript()),
            new FixedTranscriber("Nothing in here matches the key."));

        var definitionId = ExamDefinitionId.New();

        var first = await pipeline.ImportAsync(
            Build(
                File("listening/de/section-1.txt", "The station is old."),
                Bytes("listening/audio/part1.mp3", [0x49, 0x44, 0x33, 0x04])),
            definitionId, 1, default);

        Assert.True(first.IsAccepted, Describe(first.Findings));

        var recurring = Assert.Single(
            first.Draft!.Warnings,
            w => w.Id.StartsWith(PassageAnchorCheck.NotInPassageCode, StringComparison.Ordinal));

        // A reviewer clears it with a reason, exactly as ImportReviewWorkflow
        // records one.
        var reviewed = first.Draft with
        {
            Warnings =
            [
                .. first.Draft.Warnings.Select(w => w.Id == recurring.Id
                    ? w with { Resolved = true, OverrideReason = "Đã đối chiếu với bản ghi." }
                    : w),
            ],
            Revision = first.Draft.Revision + 1,
        };

        Assert.True(await drafts.ReplaceAsync(reviewed, first.Draft.Revision, default));

        var second = await pipeline.ImportAsync(
            Build(
                File("listening/de/section-1.txt", "The station is old."),
                Bytes("listening/audio/part1.mp3", [0x49, 0x44, 0x33, 0x04])),
            definitionId, 1, default, null, ImportJobStage.Keying);

        Assert.True(second.IsAccepted, Describe(second.Findings));

        var stored = await drafts.FindAsync(second.Draft!.Id, default);
        var after = Assert.Single(
            stored!.Warnings,
            w => w.Id.StartsWith(PassageAnchorCheck.NotInPassageCode, StringComparison.Ordinal));

        Assert.True(after.Resolved);
        Assert.Equal("Đã đối chiếu với bản ghi.", after.OverrideReason);
    }

    /// <summary>A transcriber that answers the same text for every recording.</summary>
    private sealed class FixedTranscriber(string text) : IAudioTranscriber
    {
        public int Calls { get; private set; }

        public bool IsConfigured => true;

        public Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string fileName, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(TranscriptionResult.Transcribed(text));
        }
    }

    /// <summary>
    /// The complaint this plan exists for: a learner must not wait for an AI call
    /// at results time.
    /// </summary>
    [Fact]
    public async Task An_imported_package_carries_its_explanations_before_any_learner_sees_it()
    {
        var attempt = await ImportWithExplanations(AiGeneratedPolicyPackage());

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        var question = FirstAutoScoredQuestion(attempt.Draft!.PackageJson);
        Assert.NotNull(question["explanation"]);
        Assert.False(string.IsNullOrWhiteSpace(
            question["explanation"]!["shortReason"]!.GetValue<string>()));
    }

    /// <summary>
    /// Rights, not capability. A package nobody cleared for AI processing must not
    /// be sent to one.
    /// </summary>
    [Fact]
    public async Task A_package_whose_policy_forbids_explanations_gets_none_and_costs_nothing()
    {
        var generator = new CountingExplanationGenerator();

        var attempt = await ImportWithExplanations(NoExplanationPolicyPackage(), generator);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        Assert.Equal(0, generator.Calls);
        Assert.Null(FirstAutoScoredQuestion(attempt.Draft!.PackageJson)["explanation"]);
    }

    /// <summary>
    /// A refused explanation is a review warning, not a lost import. Forty
    /// questions and one refusal must not discard a paid parse.
    /// </summary>
    [Fact]
    public async Task A_refused_explanation_becomes_a_warning_and_the_draft_survives()
    {
        var attempt = await ImportWithExplanations(
            AiGeneratedPolicyPackage(), new RefusingExplanationGenerator());

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        Assert.Contains(attempt.Draft!.Warnings, w => w.Id.StartsWith("exp-", StringComparison.Ordinal));
    }

    /// <summary>
    /// Wires a full pipeline — the same production stack <see cref="PipelineWithStore"/>
    /// builds — plus an <see cref="ImportReviewWorkflow"/> carrying a real
    /// <see cref="CanonicalExplanationWorkflow"/>, so <c>ImportJobStage.Explaining</c> has
    /// something to call. The paper carries no key folder answer for the parser to guess
    /// right — the archive's own <c>reading/dap-an</c> key overrides it — so an explanation
    /// generated on anything but the applied key would be caught by
    /// <c>ExplanationOutputValidator</c>.
    /// </summary>
    private static async Task<ExamImportAttempt> ImportWithExplanations(
        string packageJson, IReadingListeningExplanationGenerator? generator = null)
    {
        var drafts = new InMemoryDraftStore();
        var validator = new ExamPackageValidator(
            ExamPackageReader.FromSchemaFile(
                Path.Combine(RepoRoot, "contracts", "schemas", "exam.schema.json")));
        var canonical = new CanonicalExplanationWorkflow(
            generator ?? new SucceedingExplanationGenerator(), new InMemoryExplanationCache());
        var reviewWorkflow = new ImportReviewWorkflow(drafts, validator, canonical);

        var pipeline = new ExamPackageImportPipeline(
            new ExamPackageArchiveInspector(),
            new SafeSourceDocumentExtractor(new NoAssets()),
            new ExamImportWorkflow(validator, drafts, new RecordingParser(packageJson)),
            validator,
            drafts,
            Options.Create(new ImportArchiveOptions()),
            new UnconfiguredAudioTranscriber(),
            reviewWorkflow);

        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap-an/key.txt", "Câu số 1: TRUE"));

        return await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);
    }

    /// <summary>
    /// <see cref="RecordingParser.OneReadingQuestion"/>, with a
    /// <c>policyProfile.explanation.mode</c> of <paramref name="mode"/> —
    /// the only thing these two fixtures differ on.
    /// </summary>
    private static string ReadingPackageWithPolicy(string mode) =>
        $$"""
        {
          "formatVersion": "2.0", "formatProfile": "vni-practice",
          "scoringProfileRef": "validation-v1",
          "contentSourceRef": { "sourceId": "recording-parser",
            "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          "title": "T",
          "variant": "academic",
          "policyProfile": { "explanation": { "mode": "{{mode}}" } },
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [
            { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 } ] } },
          "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
            "kind": "passage", "body": "The roof is made of slate.",
            "questions": [ { "id": "r1", "order": 1, "type": "true-false-notgiven",
              "answerKey": { "accepted": ["FALSE"] } } ] } ] } ]
        }
        """;

    private static string AiGeneratedPolicyPackage() => ReadingPackageWithPolicy("ai-generated");

    private static string NoExplanationPolicyPackage() => ReadingPackageWithPolicy("none");

    private static JsonNode FirstAutoScoredQuestion(string packageJson) =>
        JsonNode.Parse(packageJson)!["sections"]![0]!["parts"]![0]!["questions"]![0]!;

    /// <summary>
    /// Echoes the answer the caller was handed — which, once the archive's key
    /// has been applied, is the key's answer and never the model's original guess
    /// — and quotes the passage it was given so
    /// <see cref="ExplanationOutputValidator"/> has real evidence to find.
    /// </summary>
    private sealed class SucceedingExplanationGenerator : IReadingListeningExplanationGenerator
    {
        public Task<ExplanationGenerationResult> GenerateAsync(
            ExplanationGenerationRequest request, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(new
            {
                correctAnswer = request.ExpectedAnswer,
                shortReason = "Because the passage says so.",
                evidence = new[] { "The roof is made of slate." },
            });

            return Task.FromResult(new ExplanationGenerationResult(
                true, json, new ExplanationProviderMetadata("test", "fixture", "v1", "req-1"), null));
        }
    }

    /// <summary>Never expected to be called — see the policy-gate test.</summary>
    private sealed class CountingExplanationGenerator : IReadingListeningExplanationGenerator
    {
        public int Calls { get; private set; }

        public Task<ExplanationGenerationResult> GenerateAsync(
            ExplanationGenerationRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new ExplanationGenerationResult(
                true,
                """{ "correctAnswer": "TRUE", "shortReason": "unused", "evidence": [] }""",
                new ExplanationProviderMetadata("test", "fixture", "v1", "req-1"),
                null));
        }
    }

    /// <summary>A provider call that always refuses, the way a filtered or empty model reply does.</summary>
    private sealed class RefusingExplanationGenerator : IReadingListeningExplanationGenerator
    {
        public Task<ExplanationGenerationResult> GenerateAsync(
            ExplanationGenerationRequest request, CancellationToken ct) =>
            Task.FromResult(new ExplanationGenerationResult(false, null, null, "test-refusal"));
    }

    private sealed class InMemoryExplanationCache : ICanonicalExplanationCache
    {
        private readonly Dictionary<string, StoredCanonicalExplanation> entries = new();

        public Task<StoredCanonicalExplanation?> FindAsync(
            ExamVersionId versionId, string questionId, CancellationToken ct) =>
            Task.FromResult(entries.TryGetValue($"{versionId.Value}:{questionId}", out var e) ? e : null);

        public Task SaveAsync(StoredCanonicalExplanation entry, CancellationToken ct)
        {
            entries[$"{entry.VersionId.Value}:{entry.QuestionId}"] = entry;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// The production pipeline with exactly one seam replaced.
    ///
    /// Real: <see cref="ExamPackageArchiveInspector"/> (it decides the layout,
    /// which is where <c>Paper</c> and <c>Key</c> come from),
    /// <see cref="SafeSourceDocumentExtractor"/>,
    /// <see cref="ExamImportWorkflow"/> over the real
    /// <see cref="ExamPackageValidator"/> and the committed exam schema.
    /// Faked: the AI parser, and an in-memory draft store so no database is
    /// needed. The asset store is a no-op because a .txt source carries no
    /// embedded media.
    /// </summary>
    private static ExamPackageImportPipeline PipelineWith(
        IExamSourceParser parser, IAudioTranscriber? transcriber = null) =>
        PipelineWithStore(parser, transcriber).Pipeline;

    /// <summary>
    /// Same production pipeline as <see cref="PipelineWith"/>, but also hands
    /// back the draft store and validator it was built with — so a test that
    /// needs to run the result through <see cref="ImportReviewWorkflow"/> too
    /// (approve, resolve) does that against the very draft the pipeline
    /// saved, not a copy built by hand.
    /// </summary>
    private static (ExamPackageImportPipeline Pipeline, IImportDraftStore Drafts, IExamPackageValidator Validator)
        PipelineWithStore(IExamSourceParser parser, IAudioTranscriber? transcriber = null)
    {
        var drafts = new InMemoryDraftStore();
        var validator = new ExamPackageValidator(
            ExamPackageReader.FromSchemaFile(
                Path.Combine(RepoRoot, "contracts", "schemas", "exam.schema.json")));

        var pipeline = new ExamPackageImportPipeline(
            new ExamPackageArchiveInspector(),
            new SafeSourceDocumentExtractor(new NoAssets()),
            new ExamImportWorkflow(validator, drafts, parser),
            validator,
            drafts,
            // Generous on purpose: these fixtures are a few hundred bytes, and
            // the caps are not what this suite is testing.
            Options.Create(new ImportArchiveOptions()),
            // The production default: no transcription provider, so the stage
            // is a no-op and every existing fact in this suite is unaffected.
            transcriber ?? new UnconfiguredAudioTranscriber());

        return (pipeline, drafts, validator);
    }

    /// <summary>
    /// <b>I2: the explanation policy gate refuses an unreadable mode instead of
    /// throwing out of the pipeline.</b>
    ///
    /// <c>"mode": 3</c> parses as JSON perfectly well and then throws
    /// <see cref="InvalidOperationException"/> out of <c>GetValue&lt;string&gt;()</c>.
    /// The catch here read <c>JsonException</c> only, so that escaped a method
    /// whose entire contract is that it is a gate and never throws. It reached
    /// the worker's catch, which read it as a transient failure and retried —
    /// so it did fail safe, three times, at full price.
    ///
    /// <b>Called directly rather than through <c>ImportAsync</c>.</b> Reaching
    /// the gate needs a wired <c>ImportReviewWorkflow</c> and a configured
    /// generator; that would test the wiring, and the assertion that matters is
    /// about the gate's own answer to a value it cannot read.
    /// </summary>
    [Theory]
    [InlineData("3")]
    [InlineData("true")]
    [InlineData("[\"ai-generated\"]")]
    [InlineData("{ \"value\": \"ai-generated\" }")]
    public void An_explanation_mode_that_is_not_a_string_refuses_rather_than_throwing(string mode)
    {
        var package = $$"""{ "policyProfile": { "explanation": { "mode": {{mode}} } } }""";

        Assert.False(ExamPackageImportPipeline.RequiresAiGeneratedExplanations(package));
    }

    /// <summary>The gate still says yes to the one value that means yes.</summary>
    [Fact]
    public void The_explanation_gate_still_opens_for_a_real_ai_generated_mode()
    {
        Assert.True(ExamPackageImportPipeline.RequiresAiGeneratedExplanations(
            """{ "policyProfile": { "explanation": { "mode": "ai-generated" } } }"""));
    }

    private static string Describe(IReadOnlyList<PackageFinding> findings) =>
        findings.Count == 0
            ? "no findings"
            : string.Join("; ", findings.Select(f => $"{f.Severity} {f.Code} {f.Path}: {f.Message}"));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class NoAssets : IPrivateImportAssetStore
    {
        public Task<string> PutPrivateAsync(
            string key, Stream content, string contentType, string sha256, CancellationToken ct) =>
            Task.FromResult($"private://{key}");
    }

    private sealed class InMemoryDraftStore : IImportDraftStore
    {
        private readonly List<ExamImportDraft> saved = [];

        public Task SaveAsync(ExamImportDraft draft, CancellationToken ct)
        {
            var index = saved.FindIndex(d => d.Id == draft.Id);
            if (index >= 0) saved[index] = draft;
            else saved.Add(draft);
            return Task.CompletedTask;
        }

        public Task<ExamImportDraft?> FindAsync(Guid draftId, CancellationToken ct) =>
            Task.FromResult<ExamImportDraft?>(saved.SingleOrDefault(d => d.Id == draftId));

        public Task<ExamImportDraft?> FindBySourceAsync(
            ExamDefinitionId definitionId, int versionNumber, ExamImportRoute route,
            string sourceHash, string parsePromptVersion, CancellationToken ct) =>
            Task.FromResult(saved.FirstOrDefault(d =>
                d.DefinitionId == definitionId && d.VersionNumber == versionNumber
                && d.Route == route && d.SourceHash == sourceHash
                && d.Parser?.PromptVersion == parsePromptVersion));

        public Task<bool> ReplaceAsync(ExamImportDraft draft, int expectedRevision, CancellationToken ct)
        {
            var index = saved.FindIndex(d => d.Id == draft.Id && d.Revision == expectedRevision);
            if (index < 0) return Task.FromResult(false);
            saved[index] = draft;
            return Task.FromResult(true);
        }
    }
}
