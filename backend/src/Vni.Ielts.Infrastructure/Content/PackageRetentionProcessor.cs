using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Retention processor: purges stored artefacts past their configured retention
/// window. Each class is independently nullable — null means no purge (G-11).
///
/// Every delete re-applies the full eligibility predicate inside a Mongo
/// transaction with the matching audit insert. A concurrent transition that
/// makes the row ineligible yields <c>DeletedCount == 0</c> / <c>MatchedCount == 0</c>
/// → safe skip (abort txn, leave the row and write no audit).
///
/// Raw-upload purge durably claims Pending/stale Deleting work before GridFS,
/// but sets UploadPurged and writes its audit only after idempotent deletion.
/// Field-level CAS updates never replace a stale aggregate. Grouping purge claims the package with
/// an identity write on the same predicate before deleting the proposal.
/// </summary>
public sealed class PackageRetentionProcessor(
    IExamPackageRepository packages,
    IPackageUploadStore uploads,
    MongoContext context,
    IOptions<PackageRetentionOptions> options,
    IPackageRetentionHooks hooks,
    IClock clock,
    ILogger<PackageRetentionProcessor> logger)
{
    private static readonly UserId SystemActor = new("system-retention");

    private static readonly string[] TerminalPackageStatuses =
    [
        PackageImportStatus.Imported.ToString(),
        PackageImportStatus.Rejected.ToString(),
        PackageImportStatus.Failed.ToString(),
    ];

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var config = options.Value;
        var now = clock.UtcNow;

        await PurgeUploadsAsync(config, now, ct);
        await PurgeCandidatesAsync(config, now, ct);
        await PurgeGroupingProposalsAsync(config, now, ct);
        await PurgeExtractionRunsAsync(config, now, ct);
    }

    private static readonly TimeSpan UploadClaimStaleAfter = TimeSpan.FromSeconds(15);

    private async Task PurgeUploadsAsync(PackageRetentionOptions config, DateTimeOffset now, CancellationToken ct)
    {
        var retentionDays = config.RawUploadRetentionDays;
        if (retentionDays is null)
        {
            logger.LogDebug("Package upload retention is not configured; skipping cleanup.");
            return;
        }

        var cutoff = now.AddDays(-retentionDays.Value);
        var staleClaimCutoff = now.Subtract(UploadClaimStaleAfter);
        var due = (await packages.ListUnpurgedTerminalAsync(ct))
            .Where(p => p.UpdatedAt <= cutoff)
            .ToList();

        var purged = 0;
        foreach (var package in due)
        {
            var claimId = Guid.NewGuid().ToString("n");
            try
            {
                await hooks.BeforeUploadClaimAsync(package.Id, ct);

                var claimed = await context.ExamPackages.UpdateOneAsync(
                    UploadClaimFilter(package.Id, package.Version, cutoff, staleClaimCutoff),
                    Builders<ExamPackageDocument>.Update
                        .Set(p => p.UploadPurgeState, "Deleting")
                        .Set(p => p.UploadPurgeClaimId, claimId)
                        .Set(p => p.UploadPurgeClaimedAt, now.UtcDateTime),
                    cancellationToken: ct);
                if (claimed.MatchedCount != 1) continue;

                await hooks.AfterUploadClaimedAsync(ct);
                await uploads.DeleteAsync(package.UploadRef, ct);
                await hooks.AfterUploadDeletedAsync(ct);

                using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);
                try
                {
                    session.StartTransaction();
                    var finalized = await context.ExamPackages.UpdateOneAsync(
                        session,
                        Builders<ExamPackageDocument>.Filter.And(
                            Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, package.Id),
                            Builders<ExamPackageDocument>.Filter.Eq(p => p.UploadPurged, false),
                            Builders<ExamPackageDocument>.Filter.Eq(p => p.UploadPurgeState, "Deleting"),
                            Builders<ExamPackageDocument>.Filter.Eq(p => p.UploadPurgeClaimId, claimId)),
                        Builders<ExamPackageDocument>.Update
                            .Set(p => p.UploadPurged, true)
                            .Set(p => p.UploadPurgeState, "Purged")
                            .Unset(p => p.UploadPurgeClaimId)
                            .Unset(p => p.UploadPurgeClaimedAt),
                        cancellationToken: ct);
                    if (finalized.MatchedCount != 1)
                    {
                        await session.AbortTransactionAsync(ct);
                        continue;
                    }

                    await InsertAuditAsync(
                        session, AuditAction.PackageUploadPurged, "package", package.Id,
                        package.FileName, now,
                        new Dictionary<string, string> { ["retentionDays"] = retentionDays.Value.ToString() }, ct);
                    await session.CommitTransactionAsync(ct);
                    purged++;
                }
                catch
                {
                    if (session.IsInTransaction) await session.AbortTransactionAsync(ct);
                    throw;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to purge the upload for package {PackageId}.", package.Id);
            }
        }

        logger.LogInformation(
            "Package retention cleanup purged {Purged} of {Due} due uploads.", purged, due.Count);
    }

    private async Task PurgeCandidatesAsync(PackageRetentionOptions config, DateTimeOffset now, CancellationToken ct)
    {
        var retentionDays = config.ParsedCandidateRetentionDays;
        if (retentionDays is null)
        {
            logger.LogDebug("Parsed candidate retention is not configured; skipping.");
            return;
        }

        var cutoff = now.AddDays(-retentionDays.Value).UtcDateTime;
        var eligible = CandidateEligibleFilter(cutoff);

        var dueCandidates = await context.ParsedExamCandidates
            .Find(eligible)
            .Project(c => new { c.Id, c.CandidateId, c.PackageId, c.Status })
            .ToListAsync(ct);

        if (dueCandidates.Count == 0) return;

        var purged = 0;
        foreach (var candidate in dueCandidates)
        {
            try
            {
                using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);
                try
                {
                    session.StartTransaction();

                    await hooks.BeforeCandidateDeleteAsync(candidate.Id, ct);

                    // Full eligibility recheck — never delete by Id alone.
                    var deleteFilter = Builders<ParsedExamCandidateDocument>.Filter.And(
                        Builders<ParsedExamCandidateDocument>.Filter.Eq(c => c.Id, candidate.Id),
                        eligible);

                    var deleted = await context.ParsedExamCandidates.DeleteOneAsync(
                        session, deleteFilter, cancellationToken: ct);
                    if (deleted.DeletedCount != 1)
                    {
                        await session.AbortTransactionAsync(ct);
                        continue;
                    }

                    await InsertAuditAsync(
                        session,
                        AuditAction.RetentionPurged,
                        "parsed_exam_candidate",
                        candidate.Id,
                        candidate.CandidateId,
                        now,
                        new Dictionary<string, string>
                        {
                            ["retentionDays"] = retentionDays.Value.ToString(),
                            ["status"] = candidate.Status,
                            ["packageId"] = candidate.PackageId,
                        },
                        ct);

                    await session.CommitTransactionAsync(ct);
                }
                catch
                {
                    if (session.IsInTransaction)
                        await session.AbortTransactionAsync(ct);
                    throw;
                }

                purged++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to purge candidate {CandidateId} in package {PackageId}.",
                    candidate.CandidateId, candidate.PackageId);
            }
        }

        logger.LogInformation(
            "Candidate retention purged {Purged} of {Due} due candidates.", purged, dueCandidates.Count);
    }

    /// <summary>
    /// Terminal Confirmed/Rejected, no draft link, and the status-change
    /// timestamp (ConfirmedAt / RejectedAt — the row's terminal "updated at")
    /// is at or before the retention cutoff.
    /// </summary>
    private static FilterDefinition<ParsedExamCandidateDocument> CandidateEligibleFilter(DateTime cutoff)
    {
        var draftNullOrEmpty = Builders<ParsedExamCandidateDocument>.Filter.Or(
            Builders<ParsedExamCandidateDocument>.Filter.Eq(c => c.DraftExamVersionId, null),
            Builders<ParsedExamCandidateDocument>.Filter.Eq(c => c.DraftExamVersionId, ""));

        var confirmedDue = Builders<ParsedExamCandidateDocument>.Filter.And(
            draftNullOrEmpty,
            Builders<ParsedExamCandidateDocument>.Filter.Eq(c => c.Status, "Confirmed"),
            Builders<ParsedExamCandidateDocument>.Filter.Lte(c => c.ConfirmedAt, cutoff));

        var rejectedDue = Builders<ParsedExamCandidateDocument>.Filter.And(
            draftNullOrEmpty,
            Builders<ParsedExamCandidateDocument>.Filter.Eq(c => c.Status, "Rejected"),
            Builders<ParsedExamCandidateDocument>.Filter.Lte(c => c.RejectedAt, cutoff));

        return Builders<ParsedExamCandidateDocument>.Filter.Or(confirmedDue, rejectedDue);
    }

    private async Task PurgeGroupingProposalsAsync(PackageRetentionOptions config, DateTimeOffset now, CancellationToken ct)
    {
        var retentionDays = config.GroupingProposalRetentionDays;
        if (retentionDays is null)
        {
            logger.LogDebug("Grouping proposal retention is not configured; skipping.");
            return;
        }

        var packageCutoff = now.AddDays(-retentionDays.Value);
        var duePackages = (await packages.ListUnpurgedTerminalAsync(ct))
            .Where(p => p.UpdatedAt <= packageCutoff)
            .Select(p => (p.Id, p.Version, p.UpdatedAt, p.Status))
            .ToList();

        if (duePackages.Count == 0) return;

        var purged = 0;
        foreach (var due in duePackages)
        {
            try
            {
                using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);
                try
                {
                    session.StartTransaction();

                    await hooks.BeforeGroupingDeleteAsync(due.Id, ct);

                    // Recheck package terminal state + version + retention window
                    // inside the txn before touching the grouping row.
                    var packageStillEligible = GroupingPackageClaimFilter(
                        due.Id, due.Version, packageCutoff);

                    var package = await context.ExamPackages
                        .Find(session, packageStillEligible)
                        .FirstOrDefaultAsync(ct);
                    if (package is null)
                    {
                        await session.AbortTransactionAsync(ct);
                        continue;
                    }

                    await hooks.AfterGroupingPackageRecheckAsync(due.Id, ct);

                    // Mutating claim on the same predicate. A same-value
                    // Version $set is a no-op and does not WriteConflict; this
                    // field write does, so a bump after the read recheck aborts
                    // and keeps the proposal.
                    var claimed = await context.ExamPackages.UpdateOneAsync(
                        session,
                        packageStillEligible,
                        Builders<ExamPackageDocument>.Update.Set(
                            "groupingRetentionClaimAt", now.UtcDateTime),
                        cancellationToken: ct);
                    if (claimed.MatchedCount != 1)
                    {
                        await session.AbortTransactionAsync(ct);
                        continue;
                    }

                    var result = await context.SourceDocumentGroupingProposals.DeleteOneAsync(
                        session,
                        Builders<SourceDocumentGroupingProposalDocument>.Filter.Eq(g => g.PackageId, due.Id),
                        cancellationToken: ct);

                    if (result.DeletedCount != 1)
                    {
                        await session.AbortTransactionAsync(ct);
                        continue;
                    }

                    await InsertAuditAsync(
                        session,
                        AuditAction.RetentionPurged,
                        "source_document_grouping_proposal",
                        due.Id,
                        due.Id,
                        now,
                        new Dictionary<string, string>
                        {
                            ["retentionDays"] = retentionDays.Value.ToString(),
                            ["packageVersion"] = due.Version.ToString(),
                        },
                        ct);

                    await session.CommitTransactionAsync(ct);
                }
                catch (Exception ex) when (IsWriteConflict(ex))
                {
                    if (session.IsInTransaction)
                        await session.AbortTransactionAsync(ct);
                    continue;
                }
                catch
                {
                    if (session.IsInTransaction)
                        await session.AbortTransactionAsync(ct);
                    throw;
                }

                purged++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to purge grouping proposal for package {PackageId}.", due.Id);
            }
        }

        if (purged > 0)
        {
            logger.LogInformation(
                "Grouping proposal retention purged {Purged} proposals.", purged);
        }
    }

    private async Task PurgeExtractionRunsAsync(PackageRetentionOptions config, DateTimeOffset now, CancellationToken ct)
    {
        var retentionDays = config.ExtractionRunRetentionDays;
        if (retentionDays is null)
        {
            logger.LogDebug("Extraction run retention is not configured; skipping.");
            return;
        }

        var cutoff = now.AddDays(-retentionDays.Value).UtcDateTime;
        var eligible = Builders<ExamExtractionRunDocument>.Filter.Lte(r => r.CompletedAt, cutoff);

        var dueRuns = await context.ExamExtractionRuns
            .Find(eligible)
            .Project(r => new { r.Id, r.PackageId })
            .ToListAsync(ct);

        if (dueRuns.Count == 0) return;

        var purged = 0;
        foreach (var run in dueRuns)
        {
            try
            {
                using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);
                try
                {
                    session.StartTransaction();

                    await hooks.BeforeExtractionDeleteAsync(run.Id, ct);

                    var deleteFilter = Builders<ExamExtractionRunDocument>.Filter.And(
                        Builders<ExamExtractionRunDocument>.Filter.Eq(r => r.Id, run.Id),
                        eligible);

                    var deleted = await context.ExamExtractionRuns.DeleteOneAsync(
                        session, deleteFilter, cancellationToken: ct);
                    if (deleted.DeletedCount != 1)
                    {
                        await session.AbortTransactionAsync(ct);
                        continue;
                    }

                    await InsertAuditAsync(
                        session,
                        AuditAction.RetentionPurged,
                        "exam_extraction_run",
                        run.Id,
                        run.PackageId,
                        now,
                        new Dictionary<string, string>
                        {
                            ["retentionDays"] = retentionDays.Value.ToString(),
                        },
                        ct);

                    await session.CommitTransactionAsync(ct);
                }
                catch
                {
                    if (session.IsInTransaction)
                        await session.AbortTransactionAsync(ct);
                    throw;
                }

                purged++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to purge extraction run {RunId}.", run.Id);
            }
        }

        if (purged > 0)
        {
            logger.LogInformation(
                "Extraction run retention purged {Purged} of {Due} due runs.", purged, dueRuns.Count);
        }
    }

    private static bool IsWriteConflict(Exception ex) =>
        ex is MongoCommandException command &&
            (command.Code == 112 || string.Equals(command.CodeName, "WriteConflict", StringComparison.Ordinal))
        || ex is MongoWriteException write && write.WriteError?.Code == 112
        || ex is MongoException mongo && mongo.HasErrorLabel("TransientTransactionError")
        || (ex.InnerException is not null && IsWriteConflict(ex.InnerException));

    private static FilterDefinition<ExamPackageDocument> UploadClaimFilter(
        string packageId, int version, DateTimeOffset cutoff, DateTimeOffset staleClaimCutoff)
    {
        var pendingOrStale = Builders<ExamPackageDocument>.Filter.Or(
            Builders<ExamPackageDocument>.Filter.Exists(p => p.UploadPurgeState, false),
            Builders<ExamPackageDocument>.Filter.Eq(p => p.UploadPurgeState, null),
            Builders<ExamPackageDocument>.Filter.Eq(p => p.UploadPurgeState, "Pending"),
            Builders<ExamPackageDocument>.Filter.And(
                Builders<ExamPackageDocument>.Filter.Eq(p => p.UploadPurgeState, "Deleting"),
                Builders<ExamPackageDocument>.Filter.Lte(p => p.UploadPurgeClaimedAt, staleClaimCutoff.UtcDateTime)));

        return Builders<ExamPackageDocument>.Filter.And(
            Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, packageId),
            Builders<ExamPackageDocument>.Filter.Eq(p => p.Version, version),
            Builders<ExamPackageDocument>.Filter.In(p => p.Status, TerminalPackageStatuses),
            Builders<ExamPackageDocument>.Filter.Lte(p => p.UpdatedAt, cutoff.UtcDateTime),
            Builders<ExamPackageDocument>.Filter.Eq(p => p.UploadPurged, false),
            pendingOrStale);
    }

    private static FilterDefinition<ExamPackageDocument> GroupingPackageClaimFilter(
        string packageId, int version, DateTimeOffset cutoff) =>
        Builders<ExamPackageDocument>.Filter.And(
            Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, packageId),
            Builders<ExamPackageDocument>.Filter.Eq(p => p.Version, version),
            Builders<ExamPackageDocument>.Filter.In(p => p.Status, TerminalPackageStatuses),
            Builders<ExamPackageDocument>.Filter.Lte(p => p.UpdatedAt, cutoff.UtcDateTime));

    private async Task InsertAuditAsync(
        IClientSessionHandle session,
        AuditAction action,
        string targetType,
        string targetId,
        string targetLabel,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string> detail,
        CancellationToken ct)
    {
        var entry = AuditEntry.Record(
            SystemActor, "system", action, targetType, targetId, targetLabel, now, detail);

        await context.Database.GetCollection<BsonDocument>("audit_log").InsertOneAsync(
            session,
            new BsonDocument
            {
                ["_id"] = entry.Id,
                ["at"] = entry.At.UtcDateTime,
                ["actorId"] = entry.ActorId.Value,
                ["actorEmail"] = entry.ActorEmail,
                ["action"] = entry.Action.ToString(),
                ["targetType"] = entry.TargetType,
                ["targetId"] = entry.TargetId,
                ["targetLabel"] = entry.TargetLabel,
                ["detail"] = new BsonArray(
                    entry.Detail.Select(kv => new BsonDocument { ["k"] = kv.Key, ["v"] = kv.Value })),
            },
            cancellationToken: ct);
    }
}
