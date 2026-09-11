using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

/// <summary>
/// Package + draft cascade delete in one Mongo transaction. Every draft delete
/// re-checks <c>Status == Draft</c> inside the txn; the package delete is
/// version-guarded. A mismatch with the preflight set aborts everything —
/// including any audit rows written earlier in the same txn.
/// </summary>
internal sealed class MongoPackageCascadeDelete(
    MongoContext context,
    IPackageCascadeDeleteHooks hooks) : IPackageCascadeDelete
{
    public async Task CascadeDeleteAsync(
        ExamPackage package,
        IReadOnlyList<ExamVersion> drafts,
        UserId actorId,
        string actorEmail,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);

        using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);

        try
        {
            session.StartTransaction();

            foreach (var version in drafts)
            {
                await hooks.BeforeDraftDeleteAsync(version.Id, ct);

                var draftFilter = Builders<ExamVersionDocument>.Filter.And(
                    Builders<ExamVersionDocument>.Filter.Eq(v => v.Id, version.Id.Value),
                    Builders<ExamVersionDocument>.Filter.Eq(v => v.Status, ExamVersionStatus.Draft.ToString()));

                var draftDelete = await context.ExamVersions.DeleteOneAsync(
                    session, draftFilter, cancellationToken: ct);

                if (draftDelete.DeletedCount != 1)
                {
                    throw new PackageCascadeConflictException(
                        package.Id,
                        $"Exam version '{version.Id.Value}' is no longer a Draft (or is missing); cascade aborted.");
                }

                await InsertAuditAsync(
                    session,
                    actorId,
                    actorEmail,
                    AuditAction.ExamDeleted,
                    "exam-version",
                    version.Id.Value,
                    $"{version.Title} v{version.VersionNumber}",
                    now,
                    ct);

                await hooks.AfterDraftDeletedAsync(version.Id, ct);
            }

            var packageFilter = Builders<ExamPackageDocument>.Filter.And(
                Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, package.Id),
                Builders<ExamPackageDocument>.Filter.Eq(p => p.Version, package.Version));

            var packageDelete = await context.ExamPackages.DeleteOneAsync(
                session, packageFilter, cancellationToken: ct);

            if (packageDelete.DeletedCount != 1)
            {
                throw new PackageCascadeConflictException(
                    package.Id,
                    $"Package '{package.Id}' changed concurrently (version {package.Version} no longer matches); cascade aborted.");
            }

            await InsertAuditAsync(
                session,
                actorId,
                actorEmail,
                AuditAction.PackageDeleted,
                "exam-package",
                package.Id,
                package.FileName,
                now,
                ct);

            await session.CommitTransactionAsync(ct);
        }
        catch
        {
            if (session.IsInTransaction)
                await session.AbortTransactionAsync(ct);
            throw;
        }
    }

    private async Task InsertAuditAsync(
        IClientSessionHandle session,
        UserId actorId,
        string actorEmail,
        AuditAction action,
        string targetType,
        string targetId,
        string targetLabel,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var entry = AuditEntry.Record(actorId, actorEmail, action, targetType, targetId, targetLabel, now);
        var doc = new BsonDocument
        {
            ["_id"] = entry.Id,
            ["at"] = entry.At.UtcDateTime,
            ["actorId"] = entry.ActorId.Value,
            ["actorEmail"] = entry.ActorEmail,
            ["action"] = entry.Action.ToString(),
            ["targetType"] = entry.TargetType,
            ["targetId"] = entry.TargetId,
            ["targetLabel"] = entry.TargetLabel,
            ["detail"] = new BsonArray(),
        };

        await context.Database.GetCollection<BsonDocument>("audit_log")
            .InsertOneAsync(session, doc, cancellationToken: ct);
    }
}

internal sealed class NoOpPackageCascadeDeleteHooks : IPackageCascadeDeleteHooks
{
    public Task BeforeDraftDeleteAsync(ExamVersionId versionId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task AfterDraftDeletedAsync(ExamVersionId versionId, CancellationToken ct) =>
        Task.CompletedTask;
}
