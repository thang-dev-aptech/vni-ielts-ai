using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

internal sealed class MongoSourceDocumentGroupingProposalRepository(MongoContext context)
    : ISourceDocumentGroupingProposalRepository
{
    public async Task<SourceDocumentGroupingProposal?> FindAsync(string packageId, CancellationToken ct)
    {
        var document = await context.SourceDocumentGroupingProposals
            .Find(candidate => candidate.PackageId == packageId)
            .FirstOrDefaultAsync(ct);
        return document is null ? null : ToDomain(document);
    }

    public async Task SaveAsync(SourceDocumentGroupingProposal proposal, int expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (expectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        if (proposal.Version < expectedVersion)
            throw new InvalidOperationException("A grouping proposal cannot be persisted at an older version.");

        var document = ToDocument(proposal);
        if (proposal.Version == 0 && expectedVersion == 0)
        {
            try
            {
                await context.SourceDocumentGroupingProposals.InsertOneAsync(document, cancellationToken: ct);
                return;
            }
            catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                var existing = await context.SourceDocumentGroupingProposals
                    .Find(stored => stored.PackageId == proposal.PackageId)
                    .FirstOrDefaultAsync(ct);

                if (existing is not null && Equivalent(existing, document)) return;

                throw new SourceDocumentGroupingConcurrencyException(proposal.PackageId, expectedVersion, exception);
            }
        }

        if (proposal.Version != expectedVersion + 1)
            throw new InvalidOperationException(
                "A grouping proposal mutation must advance its version exactly once before persistence.");

        var result = await context.SourceDocumentGroupingProposals.ReplaceOneAsync(
            stored => stored.PackageId == proposal.PackageId && stored.Version == expectedVersion,
            document,
            cancellationToken: ct);
        if (result.MatchedCount != 1)
            throw new SourceDocumentGroupingConcurrencyException(proposal.PackageId, expectedVersion);
    }

    private static SourceDocumentGroupingProposalDocument ToDocument(SourceDocumentGroupingProposal proposal) => new()
    {
        PackageId = proposal.PackageId,
        NeedsReview = proposal.NeedsReview,
        Version = proposal.Version,
        Groups = [.. proposal.Groups.Select(group => new SourceDocumentGroupDocument
        {
            Id = group.Id,
            DirectoryPath = group.DirectoryPath,
            EntryPaths = [.. group.EntryPaths],
        })],
    };

    private static SourceDocumentGroupingProposal ToDomain(SourceDocumentGroupingProposalDocument document) => new(
        document.PackageId,
        [.. document.Groups.Select(group => new SourceDocumentGroup(group.Id, group.DirectoryPath, [.. group.EntryPaths]))],
        document.NeedsReview,
        document.Version);

    private static bool Equivalent(
        SourceDocumentGroupingProposalDocument existing,
        SourceDocumentGroupingProposalDocument proposal) =>
        existing.PackageId == proposal.PackageId
        && existing.NeedsReview == proposal.NeedsReview
        && existing.Version == proposal.Version
        && existing.ToBsonDocument().Equals(proposal.ToBsonDocument());
}
