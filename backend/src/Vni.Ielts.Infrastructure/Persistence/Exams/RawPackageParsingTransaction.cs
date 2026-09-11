using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

internal sealed class MongoRawPackageParsingTransaction(MongoContext context, IClock clock)
    : IRawPackageParsingTransaction
{
    public async Task CommitAsync(
        ExamPackage package,
        int expectedVersion,
        PackageClaim claim,
        SourceDocumentGroupingProposal proposal,
        ParsedExamCandidate candidate,
        IReadOnlyList<PackageFinding> findings,
        CancellationToken ct)
    {
        using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);
        try
        {
            session.StartTransaction();
            var now = clock.UtcNow;
            var activeClaim = Builders<ExamPackageDocument>.Filter.And(
                Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, claim.PackageId),
                Builders<ExamPackageDocument>.Filter.Eq(p => p.Version, expectedVersion),
                Builders<ExamPackageDocument>.Filter.Eq(p => p.ClaimOwner, claim.Owner),
                Builders<ExamPackageDocument>.Filter.Eq(p => p.ClaimFence, claim.Fence),
                Builders<ExamPackageDocument>.Filter.Gt(p => p.LeaseUntil, now.UtcDateTime));

            if (await context.ExamPackages.CountDocumentsAsync(session, activeClaim, cancellationToken: ct) != 1)
                throw new PackageConcurrencyException(package.Id, expectedVersion);

            await context.SourceDocumentGroupingProposals.InsertOneAsync(
                session,
                new SourceDocumentGroupingProposalDocument
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
                },
                cancellationToken: ct);
            await context.ParsedExamCandidates.InsertOneAsync(session, candidate.ToDocument(), cancellationToken: ct);

            package.MarkNeedsReview(now, findings);
            var result = await context.ExamPackages.ReplaceOneAsync(
                session, activeClaim, package.ToDocument(), cancellationToken: ct);
            if (result.MatchedCount != 1)
                throw new PackageConcurrencyException(package.Id, expectedVersion);

            await session.CommitTransactionAsync(ct);
        }
        catch
        {
            if (session.IsInTransaction) await session.AbortTransactionAsync(ct);
            throw;
        }
    }
}
