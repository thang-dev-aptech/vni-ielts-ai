using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

internal sealed class MongoExamPackageRepository(MongoContext context) : IExamPackageRepository
{
    public async Task<ExamPackage?> FindAsync(string packageId, CancellationToken ct)
    {
        var document = await context.ExamPackages
            .Find(package => package.Id == packageId)
            .FirstOrDefaultAsync(ct);

        return document?.ToDomain();
    }

    public async Task SaveAsync(ExamPackage package, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);

        var document = package.ToDocument();
        await context.ExamPackages.ReplaceOneAsync(
            stored => stored.Id == document.Id,
            document,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }

    public async Task ReplaceVersionAsync(ExamPackage package, int expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (expectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));

        var document = package.ToDocument();
        var filter = Builders<ExamPackageDocument>.Filter.And(
            Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, document.Id),
            Builders<ExamPackageDocument>.Filter.Eq(p => p.Version, expectedVersion));

        var result = await context.ExamPackages.ReplaceOneAsync(filter, document, cancellationToken: ct);
        if (result.MatchedCount == 0)
        {
            throw new PackageConcurrencyException(package.Id, expectedVersion);
        }
    }

    public async Task<ExamPackage?> TryClaimAsync(
        string packageId, string owner, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("A claim owner is required.", nameof(owner));
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        var nowUtc = now.UtcDateTime;
        var processing = new[] { PackageImportStatus.Validating.ToString(), PackageImportStatus.Parsing.ToString() };
        var claimable = Builders<ExamPackageDocument>.Filter.Or(
            Builders<ExamPackageDocument>.Filter.Eq(p => p.Status, PackageImportStatus.Uploaded.ToString()),
            Builders<ExamPackageDocument>.Filter.And(
                Builders<ExamPackageDocument>.Filter.In(p => p.Status, processing),
                Builders<ExamPackageDocument>.Filter.Or(
                    Builders<ExamPackageDocument>.Filter.Lte(p => p.LeaseUntil, nowUtc),
                    Builders<ExamPackageDocument>.Filter.Eq(p => p.LeaseUntil, null))));
        var filter = Builders<ExamPackageDocument>.Filter.And(
            Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, packageId), claimable);
        var update = Builders<ExamPackageDocument>.Update
            .Set(p => p.Status, PackageImportStatus.Validating.ToString())
            .Set(p => p.ClaimOwner, owner)
            .Inc(p => p.ClaimFence, 1)
            .Set(p => p.ClaimedAt, nowUtc)
            .Set(p => p.LeaseUntil, (now + leaseDuration).UtcDateTime)
            .Set(p => p.UpdatedAt, nowUtc)
            .Inc(p => p.Version, 1);
        var claimed = await context.ExamPackages.FindOneAndUpdateAsync(
            filter, update,
            new FindOneAndUpdateOptions<ExamPackageDocument> { ReturnDocument = ReturnDocument.After }, ct);
        return claimed?.ToDomain();
    }

    public async Task<bool> RenewClaimAsync(
        PackageClaim claim, DateTimeOffset leaseUntil, CancellationToken ct)
    {
        var filter = ActiveClaimFilter(claim, DateTimeOffset.UtcNow);
        var update = Builders<ExamPackageDocument>.Update.Set(p => p.LeaseUntil, leaseUntil.UtcDateTime);
        var result = await context.ExamPackages.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.MatchedCount == 1;
    }

    public async Task<bool> ReleaseClaimAsync(PackageClaim claim, DateTimeOffset now, CancellationToken ct)
    {
        var filter = ActiveClaimFilter(claim, now, requireUnexpired: false);
        var update = Builders<ExamPackageDocument>.Update
            .Set(p => p.Status, PackageImportStatus.Uploaded.ToString())
            .Unset(p => p.ClaimOwner)
            .Unset(p => p.ClaimedAt)
            .Unset(p => p.LeaseUntil)
            .Set(p => p.UpdatedAt, now.UtcDateTime)
            .Inc(p => p.Version, 1);
        var result = await context.ExamPackages.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.MatchedCount == 1;
    }

    public async Task ReplaceClaimedAsync(
        ExamPackage package, int expectedVersion, PackageClaim claim, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);
        var filter = Builders<ExamPackageDocument>.Filter.And(
            ActiveClaimFilter(claim, now),
            Builders<ExamPackageDocument>.Filter.Eq(p => p.Version, expectedVersion));
        var result = await context.ExamPackages.ReplaceOneAsync(filter, package.ToDocument(), cancellationToken: ct);
        if (result.MatchedCount == 0) throw new PackageConcurrencyException(package.Id, expectedVersion);
    }

    public Task ReplaceVersionWithClaimAsync(
        ExamPackage package, int expectedVersion, string claimOwner, CancellationToken ct) =>
        ReplaceClaimedAsync(
            package,
            expectedVersion,
            new PackageClaim(package.Id, claimOwner, package.ClaimFence, package.LeaseUntil ?? DateTimeOffset.MinValue),
            DateTimeOffset.UtcNow,
            ct);

    private static FilterDefinition<ExamPackageDocument> ActiveClaimFilter(
        PackageClaim claim, DateTimeOffset now, bool requireUnexpired = true)
    {
        var filters = new List<FilterDefinition<ExamPackageDocument>>
        {
            Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, claim.PackageId),
            Builders<ExamPackageDocument>.Filter.Eq(p => p.ClaimOwner, claim.Owner),
            Builders<ExamPackageDocument>.Filter.Eq(p => p.ClaimFence, claim.Fence),
            Builders<ExamPackageDocument>.Filter.In(p => p.Status, new[]
            {
                PackageImportStatus.Validating.ToString(), PackageImportStatus.Parsing.ToString(),
            }),
        };
        if (requireUnexpired)
            filters.Add(Builders<ExamPackageDocument>.Filter.Gt(p => p.LeaseUntil, now.UtcDateTime));
        return Builders<ExamPackageDocument>.Filter.And(filters);
    }

    public async Task<ExamPackage?> FindByImportDraftIdAsync(string importDraftId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(importDraftId)) return null;

        var document = await context.ExamPackages
            .Find(package => package.ImportDraftId == importDraftId)
            .FirstOrDefaultAsync(ct);

        return document?.ToDomain();
    }

    public async Task<IReadOnlyList<ExamPackage>> ListByStatusAsync(
        PackageImportStatus status, CancellationToken ct)
    {
        var documents = await context.ExamPackages
            .Find(package => package.Status == status.ToString())
            .ToListAsync(ct);

        return [.. documents.Select(d => d.ToDomain())];
    }

    public async Task<IReadOnlyList<ExamPackage>> ListClaimableAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        var nowUtc = now.UtcDateTime;
        var filter = Builders<ExamPackageDocument>.Filter.Or(
            Builders<ExamPackageDocument>.Filter.Eq(p => p.Status, PackageImportStatus.Uploaded.ToString()),
            Builders<ExamPackageDocument>.Filter.And(
                Builders<ExamPackageDocument>.Filter.In(p => p.Status, new[]
                {
                    PackageImportStatus.Validating.ToString(),
                    PackageImportStatus.Parsing.ToString(),
                }),
                Builders<ExamPackageDocument>.Filter.Or(
                    Builders<ExamPackageDocument>.Filter.Lte(p => p.LeaseUntil, nowUtc),
                    Builders<ExamPackageDocument>.Filter.Eq(p => p.LeaseUntil, null)
                )
            )
        );

        var documents = await context.ExamPackages
            .Find(filter)
            .SortBy(p => p.CreatedAt)
            .ToListAsync(ct);

        return [.. documents.Select(d => d.ToDomain())];
    }

    public async Task<IReadOnlyList<ExamPackage>> ListAllAsync(CancellationToken ct)
    {
        var documents = await context.ExamPackages
            .Find(FilterDefinition<ExamPackageDocument>.Empty)
            .SortByDescending(package => package.CreatedAt)
            .ToListAsync(ct);

        return [.. documents.Select(d => d.ToDomain())];
    }

    public async Task<IReadOnlyList<ExamPackage>> ListUnpurgedTerminalAsync(CancellationToken ct)
    {
        var terminal = new[]
        {
            PackageImportStatus.Imported.ToString(),
            PackageImportStatus.Rejected.ToString(),
            PackageImportStatus.Failed.ToString(),
        };

        var filter = Builders<ExamPackageDocument>.Filter.And(
            Builders<ExamPackageDocument>.Filter.In(p => p.Status, terminal),
            Builders<ExamPackageDocument>.Filter.Eq(p => p.UploadPurged, false));

        var documents = await context.ExamPackages.Find(filter).ToListAsync(ct);
        return [.. documents.Select(d => d.ToDomain())];
    }

    public Task DeleteAsync(string packageId, CancellationToken ct) =>
        context.ExamPackages.DeleteOneAsync(package => package.Id == packageId, ct);
}
