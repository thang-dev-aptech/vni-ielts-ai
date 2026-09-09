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
    IImportApprovalCommitHooks hooks) : IImportApprovalCommitter
{
    public async Task<ImportApprovalCommitResult> CommitAsync(
        ExamImportDraft approvedDraft,
        int expectedRevision,
        ExamVersion catalogueDraft,
        CancellationToken ct)
    {
        var mapper = new MongoImportDraftStore(context, validator);
        using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);

        try
        {
            session.StartTransaction();

            var current = await context.ImportDrafts
                .Find(session, Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Id, approvedDraft.Id.ToString("D")))
                .FirstOrDefaultAsync(ct);

            if (current is null)
            {
                await session.AbortTransactionAsync(ct);
                return ImportApprovalCommitResult.RevisionConflict();
            }

            if (string.Equals(current.ApprovalState, nameof(ImportApprovalState.Approved), StringComparison.Ordinal)
                && current.Revision == expectedRevision)
            {
                var equivalent = await CatalogueIsEquivalentAsync(session, catalogueDraft, ct);
                if (!equivalent)
                {
                    await session.AbortTransactionAsync(ct);
                    return ImportApprovalCommitResult.IdentityConflict();
                }

                await session.AbortTransactionAsync(ct);
                return ImportApprovalCommitResult.AlreadyCommitted(mapper.ToDraft(current));
            }

            if (current.Revision != expectedRevision
                || !string.Equals(current.ApprovalState, nameof(ImportApprovalState.ReviewRequired), StringComparison.Ordinal))
            {
                await session.AbortTransactionAsync(ct);
                return ImportApprovalCommitResult.RevisionConflict();
            }

            var identity = await WriteCatalogueAsync(session, catalogueDraft, ct);
            if (identity is not null)
            {
                await session.AbortTransactionAsync(ct);
                return identity;
            }

            await hooks.AfterCatalogueWriteAsync(catalogueDraft.Id, ct);

            var replaced = await context.ImportDrafts.ReplaceOneAsync(
                session,
                Builders<ExamImportDraftDocument>.Filter.And(
                    Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Id, approvedDraft.Id.ToString("D")),
                    Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Revision, expectedRevision),
                    Builders<ExamImportDraftDocument>.Filter.Eq(d => d.ApprovalState, nameof(ImportApprovalState.ReviewRequired))),
                MongoImportDraftStore.ToDocument(approvedDraft),
                cancellationToken: ct);

            if (replaced.MatchedCount == 0)
            {
                await session.AbortTransactionAsync(ct);
                return ImportApprovalCommitResult.RevisionConflict();
            }

            await session.CommitTransactionAsync(ct);
            return ImportApprovalCommitResult.Committed(approvedDraft);
        }
        catch
        {
            if (session.IsInTransaction) await session.AbortTransactionAsync(ct);
            throw;
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
}
