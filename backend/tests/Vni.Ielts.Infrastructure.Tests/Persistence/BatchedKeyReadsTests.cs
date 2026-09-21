using Vni.Ielts.Infrastructure.Persistence;

namespace Vni.Ielts.Infrastructure.Tests.Persistence;

/// <summary>
/// The `$in` splitter — and it runs without a database on purpose.
///
/// <b>Debt 7 in `_workspace/queue/web-enduser-completion.md`.</b> `W10` gave
/// four stores a batched read and every one built a single `$in` from whatever
/// list it was handed. That is safe only because today's one caller is bounded
/// at `MaxLimit + 1 = 51`, and a bound living in a different layer protects
/// this code by coincidence rather than by construction. The next caller with
/// an unbounded list inherits the coincidence and not the protection.
///
/// <b>What is and is not testable here.</b> The failure being prevented — a
/// query document over MongoDB's 16MB BSON cap — needs roughly half a million
/// keys to reproduce, which is an outage rehearsal rather than a test. So the
/// partitioning is separated from the querying and checked exactly: nothing
/// dropped at a boundary, the uneven tail included, a repeated key fetched
/// once, and a size that would spin refused. A database would add nothing to
/// any of those and would make them unrunnable whenever the dev stack is down
/// — which is how this file started life, and why it ended up this shape.
/// </summary>
public sealed class BatchedKeyReadsTests
{
    [Fact]
    public void Splits_the_keys_across_chunks_without_losing_the_uneven_tail()
    {
        var keys = Enumerable.Range(0, 20).Select(i => $"k-{i}").ToList();

        // 20 at 3 per round trip is 7 chunks, the last holding 2. The tail is
        // deliberately uneven: an even one hides the off-by-one that drops it.
        var chunks = BatchedKeyReads.ChunkKeys(keys, chunkSize: 3);

        Assert.Equal(7, chunks.Count);
        Assert.Equal(2, chunks[^1].Count);

        // Flattened, it is the request — same keys, same order, none repeated.
        Assert.Equal(keys, chunks.SelectMany(c => c).ToList());
    }

    [Fact]
    public void Never_exceeds_the_chunk_size_it_was_given()
    {
        var chunks = BatchedKeyReads.ChunkKeys(
            Enumerable.Range(0, 1_000).Select(i => $"k-{i}"), chunkSize: 7);

        Assert.All(chunks, chunk => Assert.InRange(chunk.Count, 1, 7));
    }

    [Fact]
    public void Asks_once_for_a_key_that_was_requested_twice()
    {
        // The repeat straddles what would be a chunk boundary. Without the
        // de-duplication it lands in two queries and comes back twice, and a
        // caller grouping by session id reports that sitting's work twice.
        var chunks = BatchedKeyReads.ChunkKeys(["k-1", "k-2", "k-1", "k-3", "k-1"], chunkSize: 2);

        Assert.Equal(["k-1", "k-2", "k-3"], chunks.SelectMany(c => c).ToList());
    }

    [Fact]
    public void Costs_no_round_trip_when_there_are_no_keys()
    {
        // Not "one empty chunk" — no chunk at all, because the loop that
        // consumes this is what decides whether the database is spoken to.
        Assert.Empty(BatchedKeyReads.ChunkKeys([]));
    }

    [Fact]
    public void Refuses_a_chunk_size_that_would_never_finish()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BatchedKeyReads.ChunkKeys(["k-0"], chunkSize: 0));
    }

    [Fact]
    public void Keeps_the_shipped_chunk_size_far_below_the_bson_document_cap()
    {
        /*
         * The reason the default is what it is, written as arithmetic rather
         * than as a comment that can drift away from the constant beside it.
         * A generous 64-byte key at 1000 per query is 64KB against a 16MB cap.
         */
        const int GenerousKeyBytes = 64;
        const int BsonDocumentCapBytes = 16 * 1024 * 1024;

        Assert.True(BatchedKeyReads.MaxKeysPerQuery * GenerousKeyBytes < BsonDocumentCapBytes / 100);
    }
}
