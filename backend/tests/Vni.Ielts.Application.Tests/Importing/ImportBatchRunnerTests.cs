using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Importing;

public sealed class ImportBatchRunnerTests
{
    [Fact]
    public async Task One_failure_does_not_erase_completed_drafts_and_resume_skips_them()
    {
        var validator = new Validator();
        var drafts = new Drafts();
        var parser = new Parser();
        var checkpoints = new Checkpoints();
        var runner = new ImportBatchRunner(
            new ExamImportWorkflow(validator, drafts, parser), checkpoints);
        var items = new[] { Item("one", "valid-1"), Item("two", "invalid"), Item("three", "valid-3") };

        var first = await runner.RunAsync("batch-1", items, default);

        Assert.Equal(new ImportBatchRunResult(2, 1, 0), first);
        Assert.Equal(2, drafts.Items.Count);
        Assert.Equal(ImportBatchItemState.Succeeded, checkpoints.Items[("batch-1", "one")].State);
        Assert.Equal(ImportBatchItemState.Failed, checkpoints.Items[("batch-1", "two")].State);
        Assert.Equal(ImportBatchItemState.Succeeded, checkpoints.Items[("batch-1", "three")].State);

        validator.AcceptInvalid = true;
        var resumed = await runner.RunAsync("batch-1", items, default);

        Assert.Equal(new ImportBatchRunResult(1, 0, 2), resumed);
        Assert.Equal(3, drafts.Items.Count);
        Assert.Equal(2, checkpoints.Items[("batch-1", "two")].Attempts);
        Assert.Equal(1, checkpoints.Items[("batch-1", "one")].Attempts);
    }

    [Fact]
    public async Task Repeating_same_import_has_a_stable_draft_identity()
    {
        var drafts = new Drafts();
        var workflow = new ExamImportWorkflow(new Validator(), drafts, new Parser());
        var definition = ExamDefinitionId.New();

        var first = await workflow.ImportStructuredAsync("valid", definition, 1, default);
        var second = await workflow.ImportStructuredAsync("valid", definition, 1, default);

        Assert.Equal(first.Draft!.Id, second.Draft!.Id);
        Assert.Single(drafts.Items);
    }

    private static ImportBatchItem Item(string id, string package) =>
        new(id, new ExamDefinitionId($"definition-{id}"), 1, package, null);

    private sealed class Checkpoints : IImportBatchCheckpointStore
    {
        public Dictionary<(string, string), ImportBatchCheckpoint> Items { get; } = [];
        public Task<ImportBatchCheckpoint?> FindAsync(string batch, string item, CancellationToken ct) =>
            Task.FromResult<ImportBatchCheckpoint?>(Items.GetValueOrDefault((batch, item)));
        public Task SaveAsync(ImportBatchCheckpoint checkpoint, CancellationToken ct)
        {
            Items[(checkpoint.BatchId, checkpoint.ItemId)] = checkpoint;
            return Task.CompletedTask;
        }
    }

    private sealed class Drafts : IImportDraftStore
    {
        public List<ExamImportDraft> Items { get; } = [];
        public Task SaveAsync(ExamImportDraft draft, CancellationToken ct)
        {
            if (Items.All(existing => existing.Id != draft.Id)) Items.Add(draft);
            return Task.CompletedTask;
        }
        public Task<ExamImportDraft?> FindAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ExamImportDraft?>(Items.SingleOrDefault(d => d.Id == id));
        public Task<bool> ReplaceAsync(ExamImportDraft draft, int expected, CancellationToken ct) =>
            Task.FromResult(false);
    }

    private sealed class Parser : IExamSourceParser
    {
        public Task<ParsedExamPackage> ParseAsync(ExtractedImportSource source, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class Validator : IExamPackageValidator
    {
        public bool AcceptInvalid { get; set; }
        public PackageValidationResult Validate(string json, ExamDefinitionId id, int version)
        {
            if (json == "invalid" && !AcceptInvalid)
                return new(false, null, [new PackageFinding("error", "INVALID", "/", "invalid")]);
            return new(true, Paper(id, version), []);
        }
        private static ExamVersion Paper(ExamDefinitionId id, int version) => ExamVersion.CreateDraft(
            id, version, "Paper", ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [new Section(ExamModule.Reading, 1, [])]);
    }
}
