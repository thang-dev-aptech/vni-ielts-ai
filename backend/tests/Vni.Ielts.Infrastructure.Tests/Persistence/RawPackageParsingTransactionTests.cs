using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;
using Xunit;

namespace Vni.Ielts.Infrastructure.Tests.Persistence;

public sealed class RawPackageParsingTransactionTests
{
    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";
    private const string SkipReason = "MongoDB not available on localhost:27018";

    private static bool MongoAvailable
    {
        get
        {
            try
            {
                var context = NewContext();
                using var cursor = context.Database.ListCollectionNames();
                return cursor.MoveNext();
            }
            catch
            {
                return false;
            }
        }
    }

    [SkippableFact]
    public async Task Candidate_insert_failure_rolls_back_proposal_and_package_transition()
    {
        Skip.IfNot(MongoAvailable, SkipReason);
        var context = NewContext();
        var (package, claim, expectedVersion) = await SeedClaimedParsingPackageAsync(context);
        var candidate = Candidate(package.Id);
        await context.ParsedExamCandidates.InsertOneAsync(candidate.ToDocument());
        var transaction = new MongoRawPackageParsingTransaction(context, new Clock());

        await Assert.ThrowsAsync<MongoWriteException>(() => transaction.CommitAsync(
            package, expectedVersion, claim, Proposal(package.Id), candidate, [], CancellationToken.None));

        Assert.Equal(0, await context.SourceDocumentGroupingProposals.CountDocumentsAsync(x => x.PackageId == package.Id));
        Assert.Equal(1, await context.ParsedExamCandidates.CountDocumentsAsync(x => x.PackageId == package.Id));
        var persisted = await new MongoExamPackageRepository(context).FindAsync(package.Id, CancellationToken.None);
        Assert.Equal(PackageImportStatus.Parsing, persisted!.Status);
        Assert.Equal(expectedVersion, persisted.Version);
    }

    [SkippableFact]
    public async Task Stale_fence_rejects_commit_without_writing_proposal_or_candidate()
    {
        Skip.IfNot(MongoAvailable, SkipReason);
        var context = NewContext();
        var (package, claim, expectedVersion) = await SeedClaimedParsingPackageAsync(context);
        var staleClaim = claim with { Fence = claim.Fence - 1 };
        var transaction = new MongoRawPackageParsingTransaction(context, new Clock());

        await Assert.ThrowsAsync<PackageConcurrencyException>(() => transaction.CommitAsync(
            package, expectedVersion, staleClaim, Proposal(package.Id), Candidate(package.Id), [], CancellationToken.None));

        Assert.Equal(0, await context.SourceDocumentGroupingProposals.CountDocumentsAsync(x => x.PackageId == package.Id));
        Assert.Equal(0, await context.ParsedExamCandidates.CountDocumentsAsync(x => x.PackageId == package.Id));
        var persisted = await new MongoExamPackageRepository(context).FindAsync(package.Id, CancellationToken.None);
        Assert.Equal(PackageImportStatus.Parsing, persisted!.Status);
        Assert.Equal(claim.Fence, persisted.ClaimFence);
    }

    private static MongoContext NewContext() => new(Options.Create(new MongoOptions
    {
        ConnectionString = ConnectionString,
        Database = $"vni_raw_parsing_tx_test_{Guid.NewGuid():n}",
    }));

    private static async Task<(ExamPackage Package, PackageClaim Claim, int ExpectedVersion)> SeedClaimedParsingPackageAsync(MongoContext context)
    {
        var now = Clock.Now;
        var repository = new MongoExamPackageRepository(context);
        var package = ExamPackage.Create(
            $"package-{Guid.NewGuid():n}", ExamPackageSourceKind.Zip, UserId.New(), "sha256", "raw.zip", "upload", now);
        await repository.SaveAsync(package, CancellationToken.None);
        package = (await repository.TryClaimAsync(package.Id, "worker-1", now, TimeSpan.FromMinutes(5), CancellationToken.None))!;
        var claim = new PackageClaim(package.Id, "worker-1", package.ClaimFence, package.LeaseUntil!.Value);
        var expectedVersion = package.Version;
        package.MarkParsing(now);
        await repository.ReplaceClaimedAsync(package, expectedVersion, claim, now, CancellationToken.None);
        return (package, claim, package.Version);
    }

    private static SourceDocumentGroupingProposal Proposal(string packageId) =>
        SourceDocumentGroupingProposal.Create(packageId, [new SourceDocumentGroup("group-1", null, ["exam.docx"])]);

    private static ParsedExamCandidate Candidate(string packageId) =>
        ParsedExamCandidate.Create(
            "candidate-1", packageId, null, ParsedExamClassification.NeedsReview, null, [],
            [new ParsedSourceProvenance("exam.docx", null, null, "sha256")]);

    private sealed class Clock : IClock
    {
        public static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow => Now;
    }
}
