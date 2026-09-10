using MongoDB.Driver;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Importing;

/// <summary>
/// Catalogue promotion + import-draft approval in one Mongo transaction.
/// Production DI wires the no-op hook; tests replace only that registration.
/// </summary>
internal sealed class MongoImportApprovalCommitter(
    MongoContext context,
    IExamPackageValidator validator,
    IImportExamAssetStore examAssets,
    IImportApprovalCommitHooks hooks) : IImportApprovalCommitter
{
    public async Task<ImportApprovalCommitResult> CommitAsync(
        ExamImportDraft approvedDraft,
        int expectedRevision,
        ExamVersion catalogueDraft,
        CancellationToken ct)
    {
        return await MongoImportAssetCleanup.WithCoordinationLeaseAsync(
            context,
            async (lease, leaseCt) =>
            {
                var mapper = new MongoImportDraftStore(context, validator);
                var promotion = await PromoteAssetsAsync(approvedDraft, lease, leaseCt);
                if (promotion.Blocked is not null) return promotion.Blocked;

                using var session = await context.Database.Client.StartSessionAsync(cancellationToken: leaseCt);

                try
                {
                    await lease.EnsureCurrentAsync(leaseCt);
                    await hooks.AfterAssetPromotionAsync(approvedDraft.Id, leaseCt);
                    await lease.EnsureCurrentAsync(leaseCt);
                    await examAssets.VerifyFinalAsync(approvedDraft.Assets, leaseCt);
                    await lease.EnsureCurrentAsync(leaseCt);
                    session.StartTransaction();

                    var current = await context.ImportDrafts
                        .Find(session, Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Id, approvedDraft.Id.ToString("D")))
                        .FirstOrDefaultAsync(leaseCt);

                    if (current is null)
                    {
                        await session.AbortTransactionAsync(leaseCt);
                        await RecordCleanupBestEffortAsync(approvedDraft.Id, promotion.PromotedThisAttempt);
                        return ImportApprovalCommitResult.RevisionConflict();
                    }

                    if (string.Equals(current.ApprovalState, nameof(ImportApprovalState.Approved), StringComparison.Ordinal)
                        && current.Revision == expectedRevision)
                    {
                        var equivalent = await CatalogueIsEquivalentAsync(session, catalogueDraft, leaseCt);
                        if (!equivalent)
                        {
                            await session.AbortTransactionAsync(leaseCt);
                            return ImportApprovalCommitResult.IdentityConflict();
                        }

                        if (string.IsNullOrWhiteSpace(current.PackageId))
                        {
                            await session.AbortTransactionAsync(leaseCt);
                            return ImportApprovalCommitResult.RevisionConflict();
                        }

                        var retryPackageDoc = await context.ExamPackages
                            .Find(session, Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, current.PackageId))
                            .FirstOrDefaultAsync(leaseCt);

                        if (retryPackageDoc is null)
                        {
                            await session.AbortTransactionAsync(leaseCt);
                            return ImportApprovalCommitResult.RevisionConflict();
                        }

                        var retryPackage = retryPackageDoc.ToDomain();
                        if (retryPackage.ImportDraftId != approvedDraft.Id.ToString("D"))
                        {
                            await session.AbortTransactionAsync(leaseCt);
                            return ImportApprovalCommitResult.RevisionConflict();
                        }

                        var hasVersion = retryPackage.CreatedVersionIds.Contains(catalogueDraft.Id.Value);
                        if (retryPackage.Status == PackageImportStatus.NeedsReview && !hasVersion)
                        {
                            retryPackage.MarkImported([catalogueDraft.Id.Value], DateTimeOffset.UtcNow);
                            var repaired = await context.ExamPackages.ReplaceOneAsync(
                                session,
                                Builders<ExamPackageDocument>.Filter.And(
                                    Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, retryPackageDoc.Id),
                                    Builders<ExamPackageDocument>.Filter.Eq(p => p.Version, retryPackageDoc.Version),
                                    Builders<ExamPackageDocument>.Filter.Eq(p => p.ImportDraftId, approvedDraft.Id.ToString("D"))),
                                retryPackage.ToDocument(),
                                cancellationToken: leaseCt);
                            if (repaired.MatchedCount != 1)
                            {
                                await session.AbortTransactionAsync(leaseCt);
                                return ImportApprovalCommitResult.RevisionConflict();
                            }
                            await session.CommitTransactionAsync(leaseCt);
                            return ImportApprovalCommitResult.AlreadyCommitted(mapper.ToDraft(current));
                        }

                        if (retryPackage.Status != PackageImportStatus.Imported || !hasVersion)
                        {
                            await session.AbortTransactionAsync(leaseCt);
                            return ImportApprovalCommitResult.RevisionConflict();
                        }

                        await session.AbortTransactionAsync(leaseCt);
                        return ImportApprovalCommitResult.AlreadyCommitted(mapper.ToDraft(current));
                    }

                    if (current.Revision != expectedRevision
                        || !string.Equals(current.ApprovalState, nameof(ImportApprovalState.ReviewRequired), StringComparison.Ordinal))
                    {
                        await session.AbortTransactionAsync(leaseCt);
                        await RecordCleanupBestEffortAsync(approvedDraft.Id, promotion.PromotedThisAttempt);
                        return ImportApprovalCommitResult.RevisionConflict();
                    }

                    await lease.EnsureCurrentAsync(leaseCt);
                    var identity = await WriteCatalogueAsync(session, catalogueDraft, leaseCt);
                    if (identity is not null)
                    {
                        await session.AbortTransactionAsync(leaseCt);
                        await RecordCleanupBestEffortAsync(approvedDraft.Id, promotion.PromotedThisAttempt);
                        return identity;
                    }

                    await hooks.AfterCatalogueWriteAsync(catalogueDraft.Id, leaseCt);

                    var replaced = await context.ImportDrafts.ReplaceOneAsync(
                        session,
                        Builders<ExamImportDraftDocument>.Filter.And(
                            Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Id, approvedDraft.Id.ToString("D")),
                            Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Revision, expectedRevision),
                            Builders<ExamImportDraftDocument>.Filter.Eq(d => d.ApprovalState, nameof(ImportApprovalState.ReviewRequired))),
                        MongoImportDraftStore.ToDocument(approvedDraft),
                        cancellationToken: leaseCt);

                    if (replaced.MatchedCount == 0)
                    {
                        await session.AbortTransactionAsync(leaseCt);
                        await RecordCleanupBestEffortAsync(approvedDraft.Id, promotion.PromotedThisAttempt);
                        return ImportApprovalCommitResult.RevisionConflict();
                    }

                    if (string.IsNullOrWhiteSpace(approvedDraft.PackageId))
                    {
                        await session.AbortTransactionAsync(leaseCt);
                        await RecordCleanupBestEffortAsync(approvedDraft.Id, promotion.PromotedThisAttempt);
                        return ImportApprovalCommitResult.RevisionConflict();
                    }

                    var packageDoc = await context.ExamPackages
                        .Find(session, Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, approvedDraft.PackageId))
                        .FirstOrDefaultAsync(leaseCt);

                    if (packageDoc is null)
                    {
                        await session.AbortTransactionAsync(leaseCt);
                        await RecordCleanupBestEffortAsync(approvedDraft.Id, promotion.PromotedThisAttempt);
                        return ImportApprovalCommitResult.RevisionConflict();
                    }

                    var package = packageDoc.ToDomain();
                    if (package.ImportDraftId != approvedDraft.Id.ToString("D"))
                    {
                        await session.AbortTransactionAsync(leaseCt);
                        await RecordCleanupBestEffortAsync(approvedDraft.Id, promotion.PromotedThisAttempt);
                        return ImportApprovalCommitResult.RevisionConflict();
                    }

                    if (package.Status != PackageImportStatus.NeedsReview)
                    {
                        await session.AbortTransactionAsync(leaseCt);
                        await RecordCleanupBestEffortAsync(approvedDraft.Id, promotion.PromotedThisAttempt);
                        return ImportApprovalCommitResult.RevisionConflict();
                    }

                    {
                        package.MarkImported([catalogueDraft.Id.Value], DateTimeOffset.UtcNow);
                        var pkgReplaced = await context.ExamPackages.ReplaceOneAsync(
                            session,
                            Builders<ExamPackageDocument>.Filter.And(
                                Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, packageDoc.Id),
                                Builders<ExamPackageDocument>.Filter.Eq(p => p.Version, packageDoc.Version)),
                            package.ToDocument(),
                            cancellationToken: leaseCt);

                        if (pkgReplaced.MatchedCount == 0)
                        {
                            await session.AbortTransactionAsync(leaseCt);
                            await RecordCleanupBestEffortAsync(approvedDraft.Id, promotion.PromotedThisAttempt);
                            return ImportApprovalCommitResult.RevisionConflict();
                        }
                    }

                    await session.CommitTransactionAsync(leaseCt);
                    return ImportApprovalCommitResult.Committed(approvedDraft);
                }
                catch
                {
                    await RecordCleanupBestEffortAsync(approvedDraft.Id, promotion.PromotedThisAttempt);
                    if (session.IsInTransaction) await session.AbortTransactionAsync(leaseCt);
                    throw;
                }
            },
            ct);
    }

    private async Task<ImportAssetPromotionBatch> PromoteAssetsAsync(
        ExamImportDraft draft,
        IImportAssetCoordinationLease lease,
        CancellationToken ct)
    {
        var outcomes = new List<ImportAssetPromotionResult>();
        var manifest = draft.Assets;
        if (manifest.Count == 0)
        {
            foreach (var reference in ImportAssetPaths.ZipBindableReferences(draft.Version))
            {
                await lease.EnsureCurrentAsync(ct);
                var existing = await examAssets.CheckFinalAsync(reference, ct);
                if (existing.Kind != ImportAssetAvailabilityKind.Present)
                {
                    outcomes.Add(new ImportAssetPromotionResult(ImportAssetPromotionStatus.Missing, reference));
                    await RecordCleanupBestEffortAsync(draft.Id, outcomes
                        .Where(o => o.Status == ImportAssetPromotionStatus.PromotedThisAttempt)
                        .Select(o => o.Reference).ToArray());
                    return new ImportAssetPromotionBatch(outcomes, ImportApprovalCommitResult.AssetMissing());
                }

                outcomes.Add(new ImportAssetPromotionResult(ImportAssetPromotionStatus.AlreadyPresent, reference));
            }

            return new ImportAssetPromotionBatch(outcomes, null);
        }

        foreach (var entry in manifest)
        {
            await lease.EnsureCurrentAsync(ct);
            var result = await examAssets.PromoteAsync(
                new StagedImportAsset(entry.Reference, entry.StagingKey, entry.ContentType, entry.Length, entry.Sha256),
                ct);
            outcomes.Add(result);
            if (result.Status is ImportAssetPromotionStatus.PromotedThisAttempt or ImportAssetPromotionStatus.AlreadyPresent)
                continue;

            var promoted = outcomes
                .Where(o => o.Status == ImportAssetPromotionStatus.PromotedThisAttempt)
                .Select(o => o.Reference)
                .ToArray();
            await RecordCleanupBestEffortAsync(draft.Id, promoted);
            var blocked = result.Status switch
            {
                ImportAssetPromotionStatus.Conflict => ImportApprovalCommitResult.AssetConflict(),
                ImportAssetPromotionStatus.Canceled => ImportApprovalCommitResult.Canceled(),
                _ => ImportApprovalCommitResult.AssetMissing(),
            };
            return new ImportAssetPromotionBatch(outcomes, blocked);
        }

        return new ImportAssetPromotionBatch(outcomes, null);
    }

    private async Task RecordCleanupBestEffortAsync(Guid draftId, IReadOnlyList<string> promoted)
    {
        if (promoted.Count == 0) return;
        try
        {
            using var compensation = ImportAssetCompensation.Start();
            await examAssets.RecordCleanupIntentAsync(
                draftId,
                promoted,
                ImportAssetCleanupReason.ApprovalCommitFailed,
                compensation.Token);
        }
        catch
        {
            // Best-effort compensation path.
        }
    }

    private async Task<bool> CatalogueIsEquivalentAsync(
        IClientSessionHandle session, ExamVersion catalogueDraft, CancellationToken ct)
    {
        var existing = await context.ExamVersions
            .Find(session, Builders<ExamVersionDocument>.Filter.Eq(v => v.Id, catalogueDraft.Id.Value))
            .FirstOrDefaultAsync(ct);

        if (existing is null) return false;
        if (!string.Equals(existing.Status, nameof(ExamVersionStatus.Draft), StringComparison.Ordinal))
            return false;
        if (!string.Equals(existing.ContentHash, catalogueDraft.ContentFingerprint(), StringComparison.Ordinal))
            return false;
        if (!string.Equals(existing.AuthorId, catalogueDraft.AuthorId?.Value, StringComparison.Ordinal))
            return false;
        return true;
    }

    private async Task<ImportApprovalCommitResult?> WriteCatalogueAsync(
        IClientSessionHandle session, ExamVersion catalogueDraft, CancellationToken ct)
    {
        var existing = await context.ExamVersions
            .Find(session, Builders<ExamVersionDocument>.Filter.Eq(v => v.Id, catalogueDraft.Id.Value))
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            await context.ExamVersions.InsertOneAsync(session, catalogueDraft.ToDocument(), cancellationToken: ct);
            return null;
        }

        if (!string.Equals(existing.Status, nameof(ExamVersionStatus.Draft), StringComparison.Ordinal)
            || !string.Equals(existing.ContentHash, catalogueDraft.ContentFingerprint(), StringComparison.Ordinal)
            || !string.Equals(existing.AuthorId, catalogueDraft.AuthorId?.Value, StringComparison.Ordinal))
        {
            return ImportApprovalCommitResult.IdentityConflict();
        }

        await context.ExamVersions.ReplaceOneAsync(
            session,
            Builders<ExamVersionDocument>.Filter.Eq(v => v.Id, catalogueDraft.Id.Value),
            catalogueDraft.ToDocument(),
            cancellationToken: ct);
        return null;
    }
}

internal sealed class NoOpImportApprovalCommitHooks : IImportApprovalCommitHooks
{
    public Task AfterCatalogueWriteAsync(ExamVersionId versionId, CancellationToken ct) => Task.CompletedTask;

    public Task AfterAssetPromotionAsync(Guid draftId, CancellationToken ct) => Task.CompletedTask;
}
