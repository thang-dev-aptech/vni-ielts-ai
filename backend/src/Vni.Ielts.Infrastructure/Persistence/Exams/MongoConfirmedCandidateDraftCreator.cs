using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

internal sealed class MongoConfirmedCandidateDraftCreator(
    MongoContext context,
    IConfirmedCandidatePackageBuilder packageBuilder,
    ExamPackageReader examReader,
    IClock clock)
    : IConfirmedCandidateDraftCreator
{
    public async Task<ExamVersion> CreateOnceAsync(CandidateDraftCreation request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var candidate = request.Candidate;

        if (candidate.Status != ParsedCandidateStatus.Confirmed ||
            candidate.Classification is ParsedExamClassification.Unclassified or ParsedExamClassification.NeedsReview)
        {
            throw new InvalidOperationException("Only confirmed candidates with a resolved classification can create a Draft.");
        }

        // Idempotency: if already drafted, return existing Draft
        if (!string.IsNullOrWhiteSpace(candidate.DraftExamVersionId))
        {
            var existingDoc = await context.ExamVersions
                .Find(v => v.Id == candidate.DraftExamVersionId)
                .FirstOrDefaultAsync(ct);
            if (existingDoc is not null)
                return existingDoc.ToDomain();
        }

        var candidateKey = MongoParsedExamCandidateRepository.KeyFor(candidate.PackageId, candidate.Id);
        var definitionId = new ExamDefinitionId(candidate.Id);
        const int versionNumber = 1;

        // Build canonical JSON
        var canonicalJson = packageBuilder.BuildCanonicalJson(candidate, request.Completion, definitionId, versionNumber);

        // Validate against exam.schema.json & invariants via ExamPackageReader
        var readResult = examReader.Read(canonicalJson, definitionId, versionNumber);
        if (!readResult.IsValid || readResult.Version is null)
        {
            var findings = (readResult.Findings ?? [])
                .Select(f => new CanonicalValidationFinding(f.Severity, f.Code, f.Path, f.Message))
                .ToList();
            throw new CandidateCanonicalValidationException(findings);
        }

        var builtVersion = readResult.Version;
        // Main names the ownership field AuthorId (feature used CreatedBy).
        // ContentSource is not on main's ExamVersion — drop it at this boundary.
        var draft = ExamVersion.CreateDraft(
            builtVersion.DefinitionId,
            builtVersion.VersionNumber,
            builtVersion.Title,
            builtVersion.Variant,
            builtVersion.Scoring,
            builtVersion.Timing,
            builtVersion.Sections,
            builtVersion.ListeningPlayback,
            builtVersion.ModuleSequence,
            builtVersion.Description,
            authorId: request.CreatedBy);

        using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);

        try
        {
            session.StartTransaction();

            var candidateDoc = await context.ParsedExamCandidates
                .Find(session, c => c.Id == candidateKey)
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException($"Parsed candidate '{candidate.Id}' not found.");

            if (!string.IsNullOrWhiteSpace(candidateDoc.DraftExamVersionId))
            {
                var existingVersion = await context.ExamVersions
                    .Find(session, v => v.Id == candidateDoc.DraftExamVersionId)
                    .FirstOrDefaultAsync(ct);
                if (existingVersion is not null)
                {
                    await session.AbortTransactionAsync(ct);
                    return existingVersion.ToDomain();
                }
            }

            await context.ExamVersions.InsertOneAsync(session, draft.ToDocument(), cancellationToken: ct);

            candidate.MarkDraftCreated(draft.Id.Value);
            var candidateFilter = Builders<ParsedExamCandidateDocument>.Filter.And(
                Builders<ParsedExamCandidateDocument>.Filter.Eq(c => c.Id, candidateKey),
                Builders<ParsedExamCandidateDocument>.Filter.Eq(c => c.Version, candidateDoc.Version));

            var candidateResult = await context.ParsedExamCandidates.ReplaceOneAsync(
                session, candidateFilter, candidate.ToDocument(), cancellationToken: ct);

            if (candidateResult.MatchedCount == 0)
            {
                await session.AbortTransactionAsync(ct);
                throw new ParsedCandidateConcurrencyException(candidate.PackageId, candidate.Id, candidateDoc.Version);
            }

            var packageDoc = await context.ExamPackages
                .Find(session, p => p.Id == candidate.PackageId)
                .FirstOrDefaultAsync(ct);

            if (packageDoc is not null)
            {
                var package = packageDoc.ToDomain();
                var expectedPkgVersion = package.Version;
                package.AddCreatedVersion(draft.Id.Value, clock.UtcNow);

                var allPackageCandidates = await context.ParsedExamCandidates
                    .Find(session, c => c.PackageId == candidate.PackageId)
                    .ToListAsync(ct);

                var allResolved = allPackageCandidates.Count > 0 && allPackageCandidates.All(c =>
                    c.CandidateId == candidate.Id
                        ? true
                        : !string.IsNullOrWhiteSpace(c.DraftExamVersionId) || c.Status == "Rejected");

                if (allResolved)
                {
                    package.MarkImported([.. package.CreatedVersionIds], clock.UtcNow);
                }

                var packageFilter = Builders<ExamPackageDocument>.Filter.And(
                    Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, package.Id),
                    Builders<ExamPackageDocument>.Filter.Eq(p => p.Version, expectedPkgVersion));

                var pkgResult = await context.ExamPackages.ReplaceOneAsync(
                    session, packageFilter, package.ToDocument(), cancellationToken: ct);

                if (pkgResult.MatchedCount == 0)
                {
                    await session.AbortTransactionAsync(ct);
                    throw new PackageConcurrencyException(package.Id, expectedPkgVersion);
                }
            }

            await session.CommitTransactionAsync(ct);
            return draft;
        }
        catch
        {
            if (session.IsInTransaction) await session.AbortTransactionAsync(ct);
            throw;
        }
    }
}
