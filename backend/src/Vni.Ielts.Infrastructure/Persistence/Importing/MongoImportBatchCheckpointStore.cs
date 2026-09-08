using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Infrastructure.Persistence.Importing;

[BsonIgnoreExtraElements]
internal sealed class ImportBatchCheckpointDocument
{
    /// <summary>
    /// <c>{batchId}:{itemId}</c>. A batch item is resumed by exactly this pair
    /// (<see cref="ImportBatchRunner.RunAsync"/> looks one up before running
    /// it), so making the pair the id is what makes a re-run's lookup and
    /// write both single-document operations rather than a query plus a
    /// conditional insert.
    /// </summary>
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("batchId")]
    public string BatchId { get; set; } = string.Empty;

    [BsonElement("itemId")]
    public string ItemId { get; set; } = string.Empty;

    [BsonElement("state")]
    public string State { get; set; } = string.Empty;

    [BsonElement("draftId")]
    [BsonIgnoreIfNull]
    public string? DraftId { get; set; }

    [BsonElement("findings")]
    public List<ImportFindingDocument> Findings { get; set; } = [];

    [BsonElement("attempts")]
    public int Attempts { get; set; }
}

/// <summary>
/// Resume state for <see cref="ImportBatchRunner"/>. Unlike the draft store,
/// there is no compare-and-swap here — <see cref="IImportBatchCheckpointStore"/>
/// exposes no revision, and the runner itself is the only writer of a given
/// <c>(batchId, itemId)</c> pair at a time (one batch is run by one caller;
/// re-running an in-flight batch concurrently is an operator error the
/// runner does not defend against today). A plain upsert keyed on the pair is
/// therefore the correct — and simplest — idempotency shape: replaying the
/// same item overwrites its own checkpoint with the same outcome.
/// </summary>
internal sealed class MongoImportBatchCheckpointStore(MongoContext context) : IImportBatchCheckpointStore
{
    public async Task<ImportBatchCheckpoint?> FindAsync(string batchId, string itemId, CancellationToken ct)
    {
        var doc = await context.ImportBatchCheckpoints
            .Find(Builders<ImportBatchCheckpointDocument>.Filter.Eq(d => d.Id, Key(batchId, itemId)))
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : ToCheckpoint(doc);
    }

    public async Task SaveAsync(ImportBatchCheckpoint checkpoint, CancellationToken ct) =>
        await context.ImportBatchCheckpoints.ReplaceOneAsync(
            Builders<ImportBatchCheckpointDocument>.Filter.Eq(
                d => d.Id, Key(checkpoint.BatchId, checkpoint.ItemId)),
            ToDocument(checkpoint),
            new ReplaceOptions { IsUpsert = true },
            ct);

    private static string Key(string batchId, string itemId) => $"{batchId}:{itemId}";

    private static ImportBatchCheckpoint ToCheckpoint(ImportBatchCheckpointDocument doc) => new(
        doc.BatchId,
        doc.ItemId,
        Enum.Parse<ImportBatchItemState>(doc.State),
        doc.DraftId is null ? null : Guid.Parse(doc.DraftId),
        doc.Findings.Select(f => f.ToFinding()).ToArray(),
        doc.Attempts);

    private static ImportBatchCheckpointDocument ToDocument(ImportBatchCheckpoint checkpoint) => new()
    {
        Id = Key(checkpoint.BatchId, checkpoint.ItemId),
        BatchId = checkpoint.BatchId,
        ItemId = checkpoint.ItemId,
        State = checkpoint.State.ToString(),
        DraftId = checkpoint.DraftId?.ToString("D"),
        Findings = checkpoint.Findings.Select(ImportFindingDocument.From).ToList(),
        Attempts = checkpoint.Attempts,
    };
}
