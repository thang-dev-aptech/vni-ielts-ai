using System.Linq.Expressions;
using MongoDB.Driver;

namespace Vni.Ielts.Infrastructure.Persistence;

/// <summary>
/// One `$in` read, split into as many round trips as the key list needs.
///
/// <b>Why this exists.</b> `W10` and its follow-up gave four stores a
/// `ListManyAsync`/`FindManyAsync` that asks for many sittings at once, and
/// every one of them built a single `$in` from whatever list it was handed.
/// That is safe today only because the one caller is bounded — the history
/// screen asks for at most `MaxLimit + 1 = 51` ids — and safety that rests on
/// a constant in a different layer is not safety, it is a coincidence with
/// good manners. It was carried as debt 7 in
/// `_workspace/queue/web-enduser-completion.md`.
///
/// <b>What goes wrong without it.</b> A query is a BSON document and a BSON
/// document is capped at 16MB, so a large enough `$in` is not slow, it is
/// rejected — and it is rejected at the database, far from the loop that built
/// the list. Long before that the query plan degrades: the server has to union
/// one index lookup per key inside a single operation it cannot yield out of.
///
/// <b>Chunking here rather than at each call site</b> so that a future caller
/// with an unbounded list is correct without having remembered anything. The
/// bound a caller still owes is on the <i>result</i> — this splits the request,
/// it does not stop an unbounded answer from being loaded into memory.
/// </summary>
internal static class BatchedKeyReads
{
    /// <summary>
    /// Keys per round trip.
    ///
    /// <b>1000, and the size is a shape rather than a measurement.</b> At a
    /// 36-character key it puts the query near 40KB — three orders of magnitude
    /// under the BSON cap, so the margin absorbs a key format nobody has
    /// invented yet. Raising it trades round trips for a worse query plan, and
    /// neither is measured here because at the sizes this code actually sees
    /// there is exactly one round trip either way.
    /// </summary>
    internal const int MaxKeysPerQuery = 1_000;

    /// <summary>
    /// Fetch every document whose <paramref name="field"/> is one of
    /// <paramref name="keys"/>, in chunks.
    ///
    /// Duplicates are removed first, so a caller that asks for the same id
    /// twice pays once and cannot receive the document twice.
    ///
    /// <paramref name="chunkSize"/> is a parameter only so a test can drive the
    /// split at a size it can assert on. Production has no reason to pass it.
    /// </summary>
    internal static async Task<List<TDocument>> FindByKeysAsync<TDocument>(
        IMongoCollection<TDocument> collection,
        Expression<Func<TDocument, string>> field,
        IEnumerable<string> keys,
        CancellationToken ct,
        int chunkSize = MaxKeysPerQuery)
    {
        var found = new List<TDocument>();

        foreach (var chunk in ChunkKeys(keys, chunkSize))
        {
            var docs = await collection
                .Find(Builders<TDocument>.Filter.In(field, chunk))
                .ToListAsync(ct);

            found.AddRange(docs);
        }

        return found;
    }

    /// <summary>
    /// The split itself, with no database in it.
    ///
    /// <b>Separated so it can be checked without one.</b> The failure this
    /// guards against needs on the order of half a million keys to reproduce,
    /// which is an outage rehearsal rather than a test. What can be checked
    /// exactly is the partitioning: nothing dropped at a boundary, the uneven
    /// tail included, a repeated key fetched once. Those are properties of a
    /// list, so they are proved against a list — and they stay provable on a
    /// machine whose dev stack is down, which is how this method came to exist.
    ///
    /// <b>Named `ChunkKeys` rather than `Chunk`</b> because `Enumerable.Chunk`
    /// is in scope at every call site: a `Chunk` here that failed to compile
    /// would silently resolve to LINQ's instead, which does no de-duplication
    /// and refuses nothing. That is not hypothetical — it is what the first
    /// version of this file did.
    ///
    /// De-duplication happens here rather than per chunk, because a key
    /// repeated across a boundary would otherwise land in two separate queries
    /// and come back twice, and a caller grouping by session id would then
    /// report that sitting's work twice.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<string>> ChunkKeys(
        IEnumerable<string> keys, int chunkSize = MaxKeysPerQuery)
    {
        // Zero is the size that turns the read into a spin rather than an error.
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        var distinct = keys.Distinct(StringComparer.Ordinal).ToList();

        // No keys, no round trip. Several callers rely on this: a learner whose
        // history holds no mock owes no marking, and being told so should be free.
        if (distinct.Count == 0) return [];

        var chunks = new List<IReadOnlyList<string>>();

        for (var offset = 0; offset < distinct.Count; offset += chunkSize)
        {
            chunks.Add(distinct.GetRange(offset, Math.Min(chunkSize, distinct.Count - offset)));
        }

        return chunks;
    }
}
