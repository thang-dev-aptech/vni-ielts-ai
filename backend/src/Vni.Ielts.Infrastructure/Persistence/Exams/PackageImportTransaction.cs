using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

/// <summary>
/// The first real Mongo transaction in this codebase — <c>infra/docker/compose.yaml</c>'s
/// single-node replica set exists specifically so multi-document transactions
/// work identically in every environment (see its own comment on why, `H-10`).
///
/// <b>Two collections, one commit.</b> Every version in <c>versions</c> is
/// inserted and the package is replaced with a version-checked filter (the
/// optimistic-concurrency guard), all before either write is visible to
/// another reader. A crash between the two would otherwise either leave a
/// Draft nothing points back to, or a package claiming an import that never
/// happened.
/// </summary>
internal sealed class MongoPackageImportTransaction(MongoContext context, IClock clock)
    : IPackageImportTransaction
{
    public async Task<IReadOnlyList<ExamVersionId>> ImportAsync(
        ExamPackage package,
        int expectedVersion,
        IReadOnlyList<ExamVersion> versions,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (versions.Count == 0)
            throw new ArgumentException("Importing requires at least one version.", nameof(versions));

        using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);

        try
        {
            session.StartTransaction();

            foreach (var version in versions)
                await context.ExamVersions.InsertOneAsync(session, version.ToDocument(), cancellationToken: ct);

            package.MarkImported([.. versions.Select(v => v.Id.Value)], clock.UtcNow);

            var filter = Builders<ExamPackageDocument>.Filter.And(
                Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, package.Id),
                Builders<ExamPackageDocument>.Filter.Eq(p => p.Version, expectedVersion));

            var result = await context.ExamPackages.ReplaceOneAsync(
                session, filter, package.ToDocument(), cancellationToken: ct);

            if (result.MatchedCount == 0)
            {
                await session.AbortTransactionAsync(ct);
                throw new PackageConcurrencyException(package.Id, expectedVersion);
            }

            await session.CommitTransactionAsync(ct);
            return [.. versions.Select(v => v.Id)];
        }
        catch
        {
            if (session.IsInTransaction) await session.AbortTransactionAsync(ct);
            throw;
        }
    }
}
