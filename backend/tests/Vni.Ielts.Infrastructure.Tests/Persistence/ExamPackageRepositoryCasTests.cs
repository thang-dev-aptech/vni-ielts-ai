using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests.Persistence;

public sealed class ExamPackageRepositoryCasTests
{
    private static (MongoExamPackageRepository Repo, MongoContext Context) CreateRepo()
    {
        var dbName = $"vni_package_cas_test_{Guid.NewGuid():n}";
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = dbName,
        }));
        return (new MongoExamPackageRepository(context), context);
    }

    [Fact]
    public async Task ReplaceVersionAsync_advances_package_when_version_matches()
    {
        var (repo, _) = CreateRepo();
        var now = DateTimeOffset.UtcNow;
        var package = ExamPackage.Create(
            "pkg-cas-1", ExamPackageSourceKind.Zip, UserId.New(),
            "sha256", "test.zip", "ref-1", now);

        await repo.SaveAsync(package, CancellationToken.None);

        package.MarkValidating(now.AddSeconds(1));
        await repo.ReplaceVersionAsync(package, expectedVersion: 0, CancellationToken.None);

        var retrieved = await repo.FindAsync("pkg-cas-1", CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal(PackageImportStatus.Validating, retrieved.Status);
        Assert.Equal(1, retrieved.Version);
    }

    [Fact]
    public async Task ReplaceVersionAsync_throws_PackageConcurrencyException_when_version_is_stale()
    {
        var (repo, _) = CreateRepo();
        var now = DateTimeOffset.UtcNow;
        var package = ExamPackage.Create(
            "pkg-cas-2", ExamPackageSourceKind.Zip, UserId.New(),
            "sha256", "test.zip", "ref-2", now);

        await repo.SaveAsync(package, CancellationToken.None);

        // Move to validating (version becomes 1)
        package.MarkValidating(now.AddSeconds(1));
        await repo.ReplaceVersionAsync(package, expectedVersion: 0, CancellationToken.None);

        // Trying to replace with stale version 0 should fail
        package.MarkFailed("FAIL", "Detail", now.AddSeconds(2));
        await Assert.ThrowsAsync<PackageConcurrencyException>(() =>
            repo.ReplaceVersionAsync(package, expectedVersion: 0, CancellationToken.None));
    }

    [Fact]
    public async Task Atomic_claim_reclaims_expired_parsing_and_stale_fence_cannot_renew_or_replace()
    {
        var (repo, _) = CreateRepo();
        var now = DateTimeOffset.UtcNow;
        var package = ExamPackage.Create(
            "pkg-fenced", ExamPackageSourceKind.Zip, UserId.New(),
            "sha256", "test.zip", "ref-fenced", now);
        await repo.SaveAsync(package, CancellationToken.None);

        var first = await repo.TryClaimAsync(package.Id, "worker-a", now, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.NotNull(first);
        var firstClaim = new PackageClaim(first.Id, "worker-a", first.ClaimFence, first.LeaseUntil!.Value);
        var firstVersion = first.Version;

        first.MarkParsing(now.AddMilliseconds(100));
        await repo.ReplaceClaimedAsync(first, firstVersion, firstClaim, now.AddMilliseconds(100), CancellationToken.None);

        var second = await repo.TryClaimAsync(package.Id, "worker-b", now.AddSeconds(2), TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(second);
        Assert.Equal("worker-b", second.ClaimOwner);
        Assert.True(second.ClaimFence > firstClaim.Fence);

        Assert.False(await repo.RenewClaimAsync(firstClaim, now.AddMinutes(2), CancellationToken.None));

        first.MarkFailed("STALE", "stale worker", now.AddSeconds(3));
        await Assert.ThrowsAsync<PackageConcurrencyException>(() =>
            repo.ReplaceClaimedAsync(first, first.Version - 1, firstClaim, now.AddSeconds(3), CancellationToken.None));

        var persisted = await repo.FindAsync(package.Id, CancellationToken.None);
        Assert.Equal("worker-b", persisted!.ClaimOwner);
        Assert.Equal(PackageImportStatus.Validating, persisted.Status);
    }

    [Fact]
    public async Task RenewClaimAsync_does_not_change_business_version()
    {
        var (repo, _) = CreateRepo();
        var now = DateTimeOffset.UtcNow;
        var package = ExamPackage.Create(
            "pkg-renew", ExamPackageSourceKind.Zip, UserId.New(),
            "sha256", "test.zip", "ref-renew", now);
        await repo.SaveAsync(package, CancellationToken.None);
        var claimed = await repo.TryClaimAsync(package.Id, "worker-a", now, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(claimed);
        var version = claimed.Version;
        var claim = new PackageClaim(claimed.Id, "worker-a", claimed.ClaimFence, claimed.LeaseUntil!.Value);

        Assert.True(await repo.RenewClaimAsync(claim, now.AddMinutes(2), CancellationToken.None));

        var persisted = await repo.FindAsync(package.Id, CancellationToken.None);
        Assert.Equal(version, persisted!.Version);
        Assert.Equal(now.AddMinutes(2).ToUnixTimeMilliseconds(), persisted.LeaseUntil!.Value.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task FindByImportDraftIdAsync_finds_package_linked_to_draft()
    {
        var (repo, _) = CreateRepo();
        var now = DateTimeOffset.UtcNow;
        var package = ExamPackage.Create(
            "pkg-cas-3", ExamPackageSourceKind.Zip, UserId.New(),
            "sha256", "test.zip", "ref-3", now);

        package.MarkValidating(now.AddSeconds(1));
        package.MarkNeedsReview("draft-linked-xyz", now.AddSeconds(2));

        await repo.SaveAsync(package, CancellationToken.None);

        var found = await repo.FindByImportDraftIdAsync("draft-linked-xyz", CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal("pkg-cas-3", found.Id);
        Assert.Equal("draft-linked-xyz", found.ImportDraftId);
        Assert.Equal(PackageImportStatus.NeedsReview, found.Status);

        var missing = await repo.FindByImportDraftIdAsync("non-existent-draft", CancellationToken.None);
        Assert.Null(missing);
    }
}
