using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Infrastructure.Persistence.Identity;
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
    public Task<IReadOnlyList<ExamVersionId>> ImportAsync(
        ExamPackage package,
        int expectedVersion,
        IReadOnlyList<ExamVersion> versions,
        CancellationToken ct) =>
        ImportCoreAsync(package, expectedVersion, null, versions, null, ct);

    public Task<IReadOnlyList<ExamVersionId>> ImportClaimedAsync(
        ExamPackage package,
        int expectedVersion,
        PackageClaim claim,
        IReadOnlyList<ExamVersion> versions,
        AuditEntry audit,
        CancellationToken ct) =>
        ImportCoreAsync(package, expectedVersion, claim, versions, audit, ct);

    private async Task<IReadOnlyList<ExamVersionId>> ImportCoreAsync(
        ExamPackage package,
        int expectedVersion,
        PackageClaim? claim,
        IReadOnlyList<ExamVersion> versions,
        AuditEntry? audit,
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
            if (claim is not null)
            {
                filter = Builders<ExamPackageDocument>.Filter.And(
                    filter,
                    Builders<ExamPackageDocument>.Filter.Eq(p => p.ClaimOwner, claim.Owner),
                    Builders<ExamPackageDocument>.Filter.Eq(p => p.ClaimFence, claim.Fence),
                    Builders<ExamPackageDocument>.Filter.Gt(p => p.LeaseUntil, clock.UtcNow.UtcDateTime));
            }

            var result = await context.ExamPackages.ReplaceOneAsync(
                session, filter, package.ToDocument(), cancellationToken: ct);

            if (result.MatchedCount == 0)
            {
                await session.AbortTransactionAsync(ct);
                throw new PackageConcurrencyException(package.Id, expectedVersion);
            }

            if (audit is not null)
            {
                await context.Database.GetCollection<AuditDocument>("audit_log").InsertOneAsync(
                    session,
                    new AuditDocument
                    {
                        Id = audit.Id,
                        At = audit.At.UtcDateTime,
                        ActorId = audit.ActorId.Value,
                        ActorEmail = audit.ActorEmail,
                        Action = audit.Action.ToString(),
                        TargetType = audit.TargetType,
                        TargetId = audit.TargetId,
                        TargetLabel = audit.TargetLabel,
                        Detail = [.. audit.Detail.Select(kv => new DetailPair { Key = kv.Key, Value = kv.Value })],
                    },
                    cancellationToken: ct);
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
