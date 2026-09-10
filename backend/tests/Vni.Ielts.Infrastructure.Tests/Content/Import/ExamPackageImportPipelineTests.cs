using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;
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
        public string? LastSourceText { get; private set; }

        public Task<ParsedExamPackage> ParseAsync(ExtractedImportSource source, CancellationToken ct)
        {
            LastSourceText = source.Text;
            // Metadata is non-nullable on ParsedExamPackage; a fake still has to
            // name a provider, because the draft records what produced it.
            return Task.FromResult(new ParsedExamPackage(
                packageJson ?? OneReadingQuestion(),
                new ParserRunMetadata("fake", "recording-parser", "test", "req-1")));
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
    }

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
    /// With a key document in hand, an answer the model invented is worthless and
    /// is about to be overwritten. Stripping first is what stops a model that
    /// solved the paper anyway from leaving a guess on a question the key does
    /// not cover. → FabricatedAnswerKeyGuard.Strip
    /// </summary>
    [Fact]
    public async Task Model_written_answers_are_stripped_before_the_real_key_is_applied()
    {
        var parser = new RecordingParser();
        var pipeline = PipelineWith(parser);
        var archive = Build(
            File("reading/de/passage.txt", "The roof is made of slate."),
            File("reading/dap-an/key.txt", "Câu số 1: TRUE"));

        var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
        Assert.DoesNotContain(
            attempt.Draft!.Warnings,
            w => w.Id.StartsWith("FABRICATED_ANSWER_KEY", StringComparison.Ordinal));
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

    // ── Wiring ───────────────────────────────────────────────────────────

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
    private static ExamPackageImportPipeline PipelineWith(IExamSourceParser parser)
    {
        var drafts = new InMemoryDraftStore();
        var validator = new ExamPackageValidator(
            ExamPackageReader.FromSchemaFile(
                Path.Combine(RepoRoot, "contracts", "schemas", "exam.schema.json")));

        return new ExamPackageImportPipeline(
            new ExamPackageArchiveInspector(),
            new SafeSourceDocumentExtractor(new NoAssets()),
            new ExamImportWorkflow(validator, drafts, parser),
            drafts,
            // Generous on purpose: these fixtures are a few hundred bytes, and
            // the caps are not what this suite is testing.
            Options.Create(new ImportArchiveOptions()));
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

        public Task<bool> ReplaceAsync(ExamImportDraft draft, int expectedRevision, CancellationToken ct)
        {
            var index = saved.FindIndex(d => d.Id == draft.Id && d.Revision == expectedRevision);
            if (index < 0) return Task.FromResult(false);
            saved[index] = draft;
            return Task.FromResult(true);
        }
    }
}
