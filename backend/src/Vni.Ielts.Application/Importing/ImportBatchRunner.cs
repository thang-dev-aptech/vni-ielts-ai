using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Importing;

public sealed record ImportBatchItem(
    string ItemId,
    ExamDefinitionId DefinitionId,
    int VersionNumber,
    string? StructuredPackage,
    ExtractedImportSource? ExtractedSource)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ItemId)
            || (StructuredPackage is null) == (ExtractedSource is null))
            throw new ArgumentException("A batch item needs an id and exactly one import source.");
    }
}

public enum ImportBatchItemState { Succeeded, Failed }

public sealed record ImportBatchCheckpoint(
    string BatchId, string ItemId, ImportBatchItemState State, Guid? DraftId,
    IReadOnlyList<PackageFinding> Findings, int Attempts);

public interface IImportBatchCheckpointStore
{
    Task<ImportBatchCheckpoint?> FindAsync(string batchId, string itemId, CancellationToken ct);
    Task SaveAsync(ImportBatchCheckpoint checkpoint, CancellationToken ct);
}

public sealed record ImportBatchRunResult(int Succeeded, int Failed, int Skipped);

/// <summary>Resumes item-by-item; an item produces only its own review draft, never a publication.</summary>
public sealed class ImportBatchRunner(
    ExamImportWorkflow imports,
    IImportBatchCheckpointStore checkpoints)
{
    public async Task<ImportBatchRunResult> RunAsync(
        string batchId, IReadOnlyList<ImportBatchItem> items, CancellationToken ct)
    {
        if (items.Select(item => item.ItemId).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new ArgumentException("Batch item ids must be unique.", nameof(items));

        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            item.Validate();
            var previous = await checkpoints.FindAsync(batchId, item.ItemId, ct);
            if (previous?.State == ImportBatchItemState.Succeeded)
            {
                skipped++;
                continue;
            }

            try
            {
                var attempt = item.StructuredPackage is { } package
                    ? await imports.ImportStructuredAsync(package, item.DefinitionId, item.VersionNumber, ct)
                    : await imports.ImportExtractedAsync(
                        item.ExtractedSource!, item.DefinitionId, item.VersionNumber, ct);
                var state = attempt.IsAccepted
                    ? ImportBatchItemState.Succeeded : ImportBatchItemState.Failed;
                await checkpoints.SaveAsync(new ImportBatchCheckpoint(
                    batchId, item.ItemId, state, attempt.Draft?.Id, attempt.Findings,
                    (previous?.Attempts ?? 0) + 1), ct);
                if (attempt.IsAccepted) succeeded++; else failed++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                await checkpoints.SaveAsync(new ImportBatchCheckpoint(
                    batchId, item.ItemId, ImportBatchItemState.Failed, null,
                    [new PackageFinding("error", "BATCH_ITEM_FAILED", "/", e.Message)],
                    (previous?.Attempts ?? 0) + 1), ct);
                failed++;
            }
        }

        return new ImportBatchRunResult(succeeded, failed, skipped);
    }
}
