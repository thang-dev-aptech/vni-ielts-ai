using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests.Persistence;

public sealed class PackageUploadIdempotencyStoreTests
{
    private static (MongoPackageUploadIdempotencyStore Store, MongoContext Context) CreateStore()
    {
        var dbName = $"vni_pkg_idemp_test_{Guid.NewGuid():n}";
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = dbName,
        }));
        return (new MongoPackageUploadIdempotencyStore(context), context);
    }

    [Fact]
    public async Task FindAsync_returns_null_when_record_does_not_exist()
    {
        var (store, _) = CreateStore();
        var record = await store.FindAsync("actor-1", "key-none", CancellationToken.None);
        Assert.Null(record);
    }

    [Fact]
    public async Task SaveAsync_stores_and_FindAsync_retrieves_record()
    {
        var (store, _) = CreateStore();
        var now = DateTimeOffset.UtcNow;
        var record = new PackageUploadIdempotencyRecord(
            ActorId: "actor-1",
            IdempotencyKey: "key-1",
            Sha256: "abc123sha",
            PackageId: "pkg-123",
            Status: "uploaded",
            StatusCode: 202,
            CreatedAt: now);

        await store.SaveAsync(record, CancellationToken.None);

        var retrieved = await store.FindAsync("actor-1", "key-1", CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal("actor-1", retrieved.ActorId);
        Assert.Equal("key-1", retrieved.IdempotencyKey);
        Assert.Equal("abc123sha", retrieved.Sha256);
        Assert.Equal("pkg-123", retrieved.PackageId);
        Assert.Equal("uploaded", retrieved.Status);
        Assert.Equal(202, retrieved.StatusCode);
    }

    [Fact]
    public async Task Duplicate_save_does_not_throw_and_preserves_initial_record()
    {
        var (store, context) = CreateStore();
        await context.EnsureIndexesAsync(CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var record1 = new PackageUploadIdempotencyRecord(
            ActorId: "actor-dup",
            IdempotencyKey: "key-dup",
            Sha256: "hash1",
            PackageId: "pkg-1",
            Status: "uploaded",
            StatusCode: 202,
            CreatedAt: now);

        var record2 = new PackageUploadIdempotencyRecord(
            ActorId: "actor-dup",
            IdempotencyKey: "key-dup",
            Sha256: "hash2",
            PackageId: "pkg-2",
            Status: "uploaded",
            StatusCode: 202,
            CreatedAt: now);

        await store.SaveAsync(record1, CancellationToken.None);
        // Second insert should catch duplicate key gracefully
        await store.SaveAsync(record2, CancellationToken.None);

        var retrieved = await store.FindAsync("actor-dup", "key-dup", CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal("pkg-1", retrieved.PackageId);
    }

    [Fact]
    public async Task ClaimOrGetAsync_issues_claim_token_and_fences_stale_requests_after_reclaim()
    {
        var (store, context) = CreateStore();
        await context.EnsureIndexesAsync(CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var claim1 = await store.ClaimOrGetAsync(
            "actor-1", "key-race", "hash-1", "pkg-1",
            now: now, leaseDuration: TimeSpan.FromSeconds(1), ct: CancellationToken.None);

        Assert.Equal(PackageUploadClaimStatus.NewClaim, claim1.Status);
        Assert.False(string.IsNullOrWhiteSpace(claim1.Record.ClaimToken));
        var token1 = claim1.Record.ClaimToken!;

        // While lease is active, same actor + key + hash returns PendingWait
        var claimSame = await store.ClaimOrGetAsync(
            "actor-1", "key-race", "hash-1", "pkg-new",
            now: now.AddMilliseconds(200), leaseDuration: TimeSpan.FromSeconds(1), ct: CancellationToken.None);
        Assert.Equal(PackageUploadClaimStatus.PendingWait, claimSame.Status);

        // Different hash returns ConflictMismatch
        var claimDiff = await store.ClaimOrGetAsync(
            "actor-1", "key-race", "hash-diff", "pkg-new",
            now: now.AddMilliseconds(200), leaseDuration: TimeSpan.FromSeconds(1), ct: CancellationToken.None);
        Assert.Equal(PackageUploadClaimStatus.ConflictMismatch, claimDiff.Status);

        // After lease expires (at now + 2s), reclaim succeeds with a new claim token
        var claim2 = await store.ClaimOrGetAsync(
            "actor-1", "key-race", "hash-1", "pkg-2",
            now: now.AddSeconds(2), leaseDuration: TimeSpan.FromSeconds(5), ct: CancellationToken.None);

        Assert.Equal(PackageUploadClaimStatus.NewClaim, claim2.Status);
        Assert.NotEqual(token1, claim2.Record.ClaimToken);
        var token2 = claim2.Record.ClaimToken!;

        // Stale attempt 1 tries to release claim using token1 -> must not delete/release claim2!
        await store.ReleaseClaimAsync("actor-1", "key-race", token1, "hash-1", "pkg-1", CancellationToken.None);

        // Verify claim2 is still intact and pending with token2
        var current = await store.FindAsync("actor-1", "key-race", CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal(PackageUploadClaimState.Pending, current.State);
        Assert.Equal(token2, current.ClaimToken);
        Assert.Equal("pkg-2", current.PackageId);

        // Stale attempt 1 tries to mark completed using token1 -> must not complete claim2!
        await store.MarkCompletedAsync("actor-1", "key-race", token1, "hash-1", "pkg-1", "uploaded", 202, now.AddSeconds(3), CancellationToken.None);

        var currentAfterStaleComplete = await store.FindAsync("actor-1", "key-race", CancellationToken.None);
        Assert.NotNull(currentAfterStaleComplete);
        Assert.Equal(PackageUploadClaimState.Pending, currentAfterStaleComplete.State);

        // Valid attempt 2 marks completed with token2
        await store.MarkCompletedAsync("actor-1", "key-race", token2, "hash-1", "pkg-2", "uploaded", 202, now.AddSeconds(4), CancellationToken.None);

        var finalRecord = await store.FindAsync("actor-1", "key-race", CancellationToken.None);
        Assert.NotNull(finalRecord);
        Assert.Equal(PackageUploadClaimState.Completed, finalRecord.State);
        Assert.Equal("pkg-2", finalRecord.PackageId);
    }
}
