using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Importing;

namespace Vni.Ielts.Infrastructure.Tests.Persistence.Importing;

/// <summary>CRUD and the upsert-by-pair idempotency for <see cref="MongoImportBatchCheckpointStore"/>.</summary>
public sealed class MongoImportBatchCheckpointStoreTests
{
    private static MongoImportBatchCheckpointStore NewStore()
    {
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_import_batch_test_{Guid.NewGuid():n}",
        }));

        return new MongoImportBatchCheckpointStore(context);
    }

    [Fact]
    public async Task Find_returns_null_before_anything_is_saved()
    {
        var store = NewStore();
        Assert.Null(await store.FindAsync("batch-1", "item-1", default));
    }

    [Fact]
    public async Task Save_then_find_round_trips_the_checkpoint()
    {
        var store = NewStore();
        var draftId = Guid.NewGuid();
        var checkpoint = new ImportBatchCheckpoint(
            "batch-1", "item-1", ImportBatchItemState.Succeeded, draftId,
            [new PackageFinding("warning", "AI_PARSE_REVIEW", "/", "Review before approving.")], 1);

        await store.SaveAsync(checkpoint, default);
        var found = await store.FindAsync("batch-1", "item-1", default);

        Assert.NotNull(found);
        Assert.Equal(ImportBatchItemState.Succeeded, found!.State);
        Assert.Equal(draftId, found.DraftId);
        Assert.Equal(1, found.Attempts);
        Assert.Single(found.Findings);
    }

    /// <summary>
    /// Resuming a batch re-saves the same <c>(batchId, itemId)</c> pair —
    /// this is the shape <c>ImportBatchRunner</c> relies on, and it must
    /// overwrite the prior attempt rather than create a second row.
    /// </summary>
    [Fact]
    public async Task Saving_the_same_pair_again_overwrites_rather_than_duplicates()
    {
        var store = NewStore();
        await store.SaveAsync(
            new ImportBatchCheckpoint("batch-1", "item-1", ImportBatchItemState.Failed, null,
                [new PackageFinding("error", "PARSE_FAILED", "/", "transient")], 1),
            default);

        var draftId = Guid.NewGuid();
        await store.SaveAsync(
            new ImportBatchCheckpoint("batch-1", "item-1", ImportBatchItemState.Succeeded, draftId, [], 2),
            default);

        var found = await store.FindAsync("batch-1", "item-1", default);
        Assert.Equal(ImportBatchItemState.Succeeded, found!.State);
        Assert.Equal(2, found.Attempts);
        Assert.Empty(found.Findings);
    }

    [Fact]
    public async Task Different_items_in_the_same_batch_do_not_collide()
    {
        var store = NewStore();
        await store.SaveAsync(
            new ImportBatchCheckpoint("batch-1", "item-1", ImportBatchItemState.Succeeded, Guid.NewGuid(), [], 1),
            default);
        await store.SaveAsync(
            new ImportBatchCheckpoint("batch-1", "item-2", ImportBatchItemState.Failed, null, [], 1),
            default);

        Assert.Equal(ImportBatchItemState.Succeeded, (await store.FindAsync("batch-1", "item-1", default))!.State);
        Assert.Equal(ImportBatchItemState.Failed, (await store.FindAsync("batch-1", "item-2", default))!.State);
    }
}
