using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

internal sealed class MongoParsedExamCandidateRepository(MongoContext context)
    : IParsedExamCandidateRepository
{
    internal static string KeyFor(string packageId, string candidateId) =>
        $"{packageId}:{candidateId}";

    public async Task<ParsedExamCandidate?> FindAsync(
        string packageId,
        string candidateId,
        CancellationToken ct)
    {
        var key = KeyFor(packageId, candidateId);
        var document = await context.ParsedExamCandidates
            .Find(candidate => candidate.Id == key)
            .FirstOrDefaultAsync(ct);

        return document?.ToDomain();
    }

    public async Task<IReadOnlyList<ParsedExamCandidate>> ListByPackageAsync(
        string packageId,
        CancellationToken ct)
    {
        var documents = await context.ParsedExamCandidates
            .Find(candidate => candidate.PackageId == packageId)
            .ToListAsync(ct);

        return documents.Select(d => d.ToDomain()).ToList();
    }

    public async Task SaveAsync(
        ParsedExamCandidate candidate,
        int expectedVersion,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (expectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        if (candidate.Version < expectedVersion)
            throw new InvalidOperationException("A candidate cannot be persisted at an older version.");

        var document = candidate.ToDocument();

        if (candidate.Version == 0 && expectedVersion == 0)
        {
            try
            {
                await context.ParsedExamCandidates.InsertOneAsync(document, cancellationToken: ct);
                return;
            }
            catch (MongoWriteException exception)
                when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                var existing = await context.ParsedExamCandidates
                    .Find(stored => stored.Id == document.Id)
                    .FirstOrDefaultAsync(ct);

                if (existing is not null && Equivalent(existing, document)) return;

                throw new ParsedCandidateConcurrencyException(
                    candidate.PackageId, candidate.Id, expectedVersion, exception);
            }
        }

        if (candidate.Version != expectedVersion + 1)
            throw new InvalidOperationException(
                "A candidate mutation must advance its version exactly once before persistence.");

        var filter = Builders<ParsedExamCandidateDocument>.Filter.And(
            Builders<ParsedExamCandidateDocument>.Filter.Eq(stored => stored.Id, document.Id),
            Builders<ParsedExamCandidateDocument>.Filter.Eq(stored => stored.PackageId, candidate.PackageId),
            Builders<ParsedExamCandidateDocument>.Filter.Eq(stored => stored.CandidateId, candidate.Id),
            Builders<ParsedExamCandidateDocument>.Filter.Eq(stored => stored.Version, expectedVersion));

        var result = await context.ParsedExamCandidates.ReplaceOneAsync(filter, document, cancellationToken: ct);
        if (result.MatchedCount == 0)
            throw new ParsedCandidateConcurrencyException(
                candidate.PackageId, candidate.Id, expectedVersion);
    }

    private static bool Equivalent(
        ParsedExamCandidateDocument existing,
        ParsedExamCandidateDocument candidate) =>
        existing.PackageId == candidate.PackageId
        && existing.CandidateId == candidate.CandidateId
        && existing.Version == candidate.Version
        && existing.ToBsonDocument().Equals(candidate.ToBsonDocument());
}
