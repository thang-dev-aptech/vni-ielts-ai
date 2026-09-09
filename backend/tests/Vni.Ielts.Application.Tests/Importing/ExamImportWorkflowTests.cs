using Vni.Ielts.Application.Importing;
using PackageFinding = Vni.Ielts.Application.Importing.PackageFinding;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Importing;

public sealed class ExamImportWorkflowTests
{
    private sealed class FakeValidator : IExamPackageValidator
    {
        public List<string> Seen { get; } = [];

        public PackageValidationResult Validate(
            string packageJson, ExamDefinitionId definitionId, int versionNumber)
        {
            Seen.Add(packageJson);
            if (packageJson == "invalid-ai-package")
                return new(false, null,
                [
                    new PackageFinding(
                        "error", "RESPONSE_SLOT_KEY_MISSING", "/sections/0/parts/0/questions/0",
                        "Every auto-scored response slot must carry an answer key."),
                ]);

            return new(true, Paper(definitionId, versionNumber), []);
        }
    }

    private sealed class FakeParser(string packageJson) : IExamSourceParser
    {
        public int Calls { get; private set; }

        public Task<ParsedExamPackage> ParseAsync(ExtractedImportSource source, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new ParsedExamPackage(
                packageJson,
                new ParserRunMetadata("recorded", "parser-v1", "prompt-1", "req-1")));
        }
    }

    private sealed class FakeDraftStore : IImportDraftStore
    {
        public List<ExamImportDraft> Saved { get; } = [];

        // Deliberately not part of IImportDraftStore. It proves this workflow has no publish path.
        public int PublishCalls { get; private set; }

        public Task SaveAsync(ExamImportDraft draft, CancellationToken ct)
        {
            Saved.Add(draft);
            return Task.CompletedTask;
        }

        public Task<ExamImportDraft?> FindAsync(Guid draftId, CancellationToken ct) =>
            Task.FromResult<ExamImportDraft?>(Saved.SingleOrDefault(d => d.Id == draftId));

        public Task<bool> ReplaceAsync(ExamImportDraft draft, int expectedRevision, CancellationToken ct)
        {
            var index = Saved.FindIndex(d => d.Id == draft.Id && d.Revision == expectedRevision);
            if (index < 0) return Task.FromResult(false);
            Saved[index] = draft;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<ExamImportDraft>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamImportDraft>>(Saved);
    }

    [Fact]
    public async Task Structured_package_goes_directly_to_the_shared_validator_and_stays_unapproved()
    {
        var validator = new FakeValidator();
        var parser = new FakeParser("not-used");
        var store = new FakeDraftStore();
        var workflow = new ExamImportWorkflow(validator, store, parser);

        var result = await workflow.ImportStructuredAsync(
            "valid-structured-package", ExamDefinitionId.New(), 1, true, default);

        Assert.True(result.IsAccepted);
        Assert.Equal(["valid-structured-package"], validator.Seen);
        Assert.Equal(0, parser.Calls);
        Assert.Equal(ImportApprovalState.ReviewRequired, result.Draft!.ApprovalState);
        Assert.Equal(ExamImportRoute.StructuredPackage, result.Draft.Route);
        Assert.Single(store.Saved);
        Assert.Equal(0, store.PublishCalls);
    }

    [Fact]
    public async Task Ai_parse_output_becomes_a_review_draft_after_the_same_validator()
    {
        var validator = new FakeValidator();
        var parser = new FakeParser("valid-ai-package");
        var store = new FakeDraftStore();
        var workflow = new ExamImportWorkflow(validator, store, parser);
        const string text = "Nguồn UTF-8 có dấu";
        var source = new ExtractedImportSource(
            "source.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            text, ExamImportWorkflow.Hash("source bytes"), ExamImportWorkflow.Hash(text));

        var result = await workflow.ImportExtractedAsync(source, ExamDefinitionId.New(), 1, true, default);

        Assert.True(result.IsAccepted);
        Assert.Equal(["valid-ai-package"], validator.Seen);
        Assert.Equal(ImportApprovalState.ReviewRequired, result.Draft!.ApprovalState);
        Assert.Equal(ExamImportRoute.AiParsedSource, result.Draft.Route);
        Assert.Equal("recorded", result.Draft.Parser!.Provider);
        Assert.Equal(0, store.PublishCalls);
    }

    [Fact]
    public async Task Invalid_ai_package_is_refused_by_shared_validator_and_never_saved_or_published()
    {
        var validator = new FakeValidator();
        var parser = new FakeParser("invalid-ai-package");
        var store = new FakeDraftStore();
        var workflow = new ExamImportWorkflow(validator, store, parser);
        const string text = "synthetic source";

        var result = await workflow.ImportExtractedAsync(
            new ExtractedImportSource(
                "source.pdf", "application/pdf", text,
                ExamImportWorkflow.Hash("source bytes"), ExamImportWorkflow.Hash(text)),
            ExamDefinitionId.New(), 1, true, default);

        Assert.False(result.IsAccepted);
        Assert.Equal("RESPONSE_SLOT_KEY_MISSING", Assert.Single(result.Findings).Code);
        Assert.Empty(store.Saved);
        Assert.Equal(0, store.PublishCalls);
    }

    [Fact]
    public async Task Changed_extracted_source_is_refused_before_parser_or_validator()
    {
        var validator = new FakeValidator();
        var parser = new FakeParser("valid-ai-package");
        var store = new FakeDraftStore();
        var workflow = new ExamImportWorkflow(validator, store, parser);

        var result = await workflow.ImportExtractedAsync(
            new ExtractedImportSource(
                "source.pdf", "application/pdf", "changed",
                ExamImportWorkflow.Hash("source bytes"), new string('0', 64)),
            ExamDefinitionId.New(), 1, true, default);

        Assert.False(result.IsAccepted);
        Assert.Equal("SOURCE_HASH_MISMATCH", Assert.Single(result.Findings).Code);
        Assert.Equal(0, parser.Calls);
        Assert.Empty(validator.Seen);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task Structured_import_records_the_supplied_uploader_and_time()
    {
        var store = new FakeDraftStore();
        var workflow = new ExamImportWorkflow(new FakeValidator(), store, new FakeParser("not-used"));
        var uploader = new UserId("uploader-1");
        var at = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

        var result = await workflow.ImportStructuredAsync(
            "valid-structured-package", ExamDefinitionId.New(), 1, true, default, uploader, at);

        Assert.True(result.IsAccepted);
        Assert.Equal(uploader, result.Draft!.CreatedBy);
        Assert.Equal(at, result.Draft.CreatedAt);
        Assert.Equal(uploader, store.Saved.Single().CreatedBy);
    }

    [Fact]
    public async Task Structured_import_stores_the_asset_manifest_without_changing_the_draft_id()
    {
        var definition = ExamDefinitionId.New();
        var manifest = new ImportAssetManifestEntry[]
        {
            new("assets/a.mp3", "imports/exam-drafts/x/assets/a.mp3", "audio/mpeg", 8, new string('a', 64)),
        };

        var without = await new ExamImportWorkflow(new FakeValidator(), new FakeDraftStore(), new FakeParser("not-used"))
            .ImportStructuredAsync("valid-structured-package", definition, 1, true, default);
        var withStore = new FakeDraftStore();
        var with = await new ExamImportWorkflow(new FakeValidator(), withStore, new FakeParser("not-used"))
            .ImportStructuredAsync(
                "valid-structured-package", definition, 1, true, default,
                assetManifest: manifest);

        Assert.Equal(without.Draft!.Id, with.Draft!.Id);
        Assert.Equal(manifest, with.Draft.Assets);
        Assert.Empty(without.Draft.Assets);
    }

    private static ExamVersion Paper(ExamDefinitionId definitionId, int versionNumber) =>
        ExamVersion.CreateDraft(
            definitionId,
            versionNumber,
            "Imported paper",
            ExamVariant.Academic,
            new ScoringProfile(
                new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
                AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [new Section(ExamModule.Reading, 1, [])]);
}
