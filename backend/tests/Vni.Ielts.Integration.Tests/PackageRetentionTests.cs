using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Plan 07's retention cleanup, against real Mongo and real GridFS.
/// </summary>
public sealed class PackageRetentionTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static PackageRetentionProcessor ProcessorWith(
        IServiceProvider services, int? retentionDays, IPackageRetentionHooks? hooks = null) =>
        ProcessorWith(services, new PackageRetentionOptions { RawUploadRetentionDays = retentionDays }, hooks);

    private static PackageRetentionProcessor ProcessorWith(
        IServiceProvider services, PackageRetentionOptions config, IPackageRetentionHooks? hooks = null)
    {
        return new PackageRetentionProcessor(
            services.GetRequiredService<IExamPackageRepository>(),
            services.GetRequiredService<IPackageUploadStore>(),
            services.GetRequiredService<MongoContext>(),
            Options.Create(config),
            hooks ?? services.GetRequiredService<IPackageRetentionHooks>(),
            services.GetRequiredService<IClock>(),
            services.GetRequiredService<ILogger<PackageRetentionProcessor>>());
    }

    private async Task<ExamPackage> SeedTerminalPackageAsync(IServiceProvider services, DateTimeOffset updatedAt)
    {
        var uploads = services.GetRequiredService<IPackageUploadStore>();
        var packages = services.GetRequiredService<IExamPackageRepository>();

        var uploadRef = await uploads.SaveAsync(
            new MemoryStream("""{"title":"demo"}"""u8.ToArray()), "demo.json", "application/json", CancellationToken.None);

        var package = ExamPackage.Create(
            Guid.NewGuid().ToString("n"), ExamPackageSourceKind.Json, UserId.New(),
            sha256: "abc", fileName: "demo.json", uploadRef, updatedAt);
        package.MarkValidating(updatedAt);
        package.Reject([new PackageFinding("schema", "SCHEMA_INVALID", null, "not a real exam")], updatedAt);

        await packages.SaveAsync(package, CancellationToken.None);
        return package;
    }

    [SkippableFact]
    public async Task Retention_does_nothing_when_unconfigured()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        _ = NewClient();

        using var scope = app.Services.CreateScope();
        var old = DateTimeOffset.UtcNow.AddDays(-3650);
        var package = await SeedTerminalPackageAsync(scope.ServiceProvider, old);

        await ProcessorWith(scope.ServiceProvider, retentionDays: null).RunOnceAsync(CancellationToken.None);

        var packages = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var reloaded = await packages.FindAsync(package.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.False(reloaded.UploadPurged);

        var uploads = scope.ServiceProvider.GetRequiredService<IPackageUploadStore>();
        await using var stream = await uploads.OpenAsync(package.UploadRef, CancellationToken.None);
        Assert.True(stream.CanRead);
    }

    [SkippableFact]
    public async Task Retention_purges_a_due_terminal_packages_upload_and_records_one_audit_entry()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        _ = NewClient();

        using var scope = app.Services.CreateScope();
        var old = DateTimeOffset.UtcNow.AddDays(-30);
        var package = await SeedTerminalPackageAsync(scope.ServiceProvider, old);

        await ProcessorWith(scope.ServiceProvider, retentionDays: 7).RunOnceAsync(CancellationToken.None);

        var packages = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var reloaded = await packages.FindAsync(package.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.True(reloaded.UploadPurged);

        var uploads = scope.ServiceProvider.GetRequiredService<IPackageUploadStore>();
        await Assert.ThrowsAsync<GridFSFileNotFoundException>(
            () => uploads.OpenAsync(package.UploadRef, CancellationToken.None));

        var audits = await scope.ServiceProvider.GetRequiredService<MongoContext>().Database
            .GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "PackageUploadPurged"),
                Builders<BsonDocument>.Filter.Eq("targetId", package.Id)))
            .ToListAsync();
        Assert.NotEmpty(audits);
    }

    [SkippableFact]
    public async Task Retention_is_idempotent_when_object_already_gone_after_failed_mark()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        _ = NewClient();

        using var scope = app.Services.CreateScope();
        var old = DateTimeOffset.UtcNow.AddDays(-30);
        var package = await SeedTerminalPackageAsync(scope.ServiceProvider, old);

        var uploads = scope.ServiceProvider.GetRequiredService<IPackageUploadStore>();
        await uploads.DeleteAsync(package.UploadRef, CancellationToken.None);

        var hooks = new MutablePackageRetentionHooks
        {
            AfterUploadClaimed = _ =>
                throw new InvalidOperationException("injected: crash after durable claim"),
        };

        await ProcessorWith(scope.ServiceProvider, retentionDays: 7, hooks).RunOnceAsync(CancellationToken.None);

        var packages = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var still = await packages.FindAsync(package.Id, CancellationToken.None);
        Assert.NotNull(still);
        Assert.False(still.UploadPurged);

        await scope.ServiceProvider.GetRequiredService<MongoContext>().ExamPackages.UpdateOneAsync(
            p => p.Id == package.Id,
            Builders<ExamPackageDocument>.Update.Set(
                p => p.UploadPurgeClaimedAt, DateTime.UtcNow.AddMinutes(-1)));

        await ProcessorWith(scope.ServiceProvider, retentionDays: 7).RunOnceAsync(CancellationToken.None);

        var done = await packages.FindAsync(package.Id, CancellationToken.None);
        Assert.NotNull(done);
        Assert.True(done.UploadPurged);

        var audits = await scope.ServiceProvider.GetRequiredService<MongoContext>().Database
            .GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "PackageUploadPurged"),
                Builders<BsonDocument>.Filter.Eq("targetId", package.Id)))
            .ToListAsync();
        Assert.Single(audits);
    }

    [SkippableFact]
    public async Task Retention_crash_after_delete_leaves_retryable_deleting_claim_without_purged_audit()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        _ = NewClient();

        using var scope = app.Services.CreateScope();
        var old = DateTimeOffset.UtcNow.AddDays(-30);
        var package = await SeedTerminalPackageAsync(scope.ServiceProvider, old);

        var hooks = new MutablePackageRetentionHooks
        {
            AfterUploadDeleted = _ =>
                throw new InvalidOperationException("injected crash after GridFS delete"),
        };

        await ProcessorWith(scope.ServiceProvider, retentionDays: 7, hooks).RunOnceAsync(CancellationToken.None);

        var packages = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var still = await packages.FindAsync(package.Id, CancellationToken.None);
        Assert.NotNull(still);
        Assert.False(still.UploadPurged);

        var audits = await scope.ServiceProvider.GetRequiredService<MongoContext>().Database
            .GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "PackageUploadPurged"),
                Builders<BsonDocument>.Filter.Eq("targetId", package.Id)))
            .ToListAsync();
        Assert.Empty(audits);

        await scope.ServiceProvider.GetRequiredService<MongoContext>().ExamPackages.UpdateOneAsync(
            p => p.Id == package.Id,
            Builders<ExamPackageDocument>.Update.Set(
                p => p.UploadPurgeClaimedAt, DateTime.UtcNow.AddMinutes(-1)));
        await ProcessorWith(scope.ServiceProvider, retentionDays: 7).RunOnceAsync(CancellationToken.None);

        var retried = await packages.FindAsync(package.Id, CancellationToken.None);
        Assert.NotNull(retried);
        Assert.True(retried.UploadPurged);
        Assert.Single(await scope.ServiceProvider.GetRequiredService<MongoContext>().Database
            .GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "PackageUploadPurged"),
                Builders<BsonDocument>.Filter.Eq("targetId", package.Id)))
            .ToListAsync());
    }

    [SkippableFact]
    public async Task Retention_leaves_a_package_that_has_not_reached_its_window_alone()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        _ = NewClient();

        using var scope = app.Services.CreateScope();
        var recent = DateTimeOffset.UtcNow.AddDays(-1);
        var package = await SeedTerminalPackageAsync(scope.ServiceProvider, recent);

        await ProcessorWith(scope.ServiceProvider, retentionDays: 7).RunOnceAsync(CancellationToken.None);

        var packages = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var reloaded = await packages.FindAsync(package.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.False(reloaded.UploadPurged);
    }

    [SkippableFact]
    public async Task Retention_upload_race_after_selection_keeps_upload_and_does_not_overwrite_concurrent_update()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        _ = NewClient();

        using var scope = app.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<MongoContext>();
        var old = DateTimeOffset.UtcNow.AddDays(-30);
        var package = await SeedTerminalPackageAsync(scope.ServiceProvider, old);
        const string concurrentName = "concurrent-update.json";

        var hooks = new MutablePackageRetentionHooks
        {
            BeforeUploadClaim = async (packageId, _) =>
            {
                await mongo.ExamPackages.UpdateOneAsync(
                    p => p.Id == packageId,
                    Builders<ExamPackageDocument>.Update
                        .Inc(p => p.Version, 1)
                        .Set(p => p.FileName, concurrentName));
            },
        };

        await ProcessorWith(scope.ServiceProvider, retentionDays: 7, hooks).RunOnceAsync(CancellationToken.None);

        var packages = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var reloaded = await packages.FindAsync(package.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.False(reloaded.UploadPurged);
        Assert.Equal(concurrentName, reloaded.FileName);
        Assert.Equal(package.Version + 1, reloaded.Version);

        var uploads = scope.ServiceProvider.GetRequiredService<IPackageUploadStore>();
        await using var stream = await uploads.OpenAsync(package.UploadRef, CancellationToken.None);
        using var preserved = new MemoryStream();
        await stream.CopyToAsync(preserved);
        var expected = """{"title":"demo"}"""u8.ToArray();
        Assert.Equal(expected, preserved.ToArray());
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(expected)),
            Convert.ToHexStringLower(SHA256.HashData(preserved.ToArray())));

        Assert.Empty(await mongo.Database.GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "PackageUploadPurged"),
                Builders<BsonDocument>.Filter.Eq("targetId", package.Id)))
            .ToListAsync());
    }

    [SkippableFact]
    public async Task Candidate_race_to_pending_review_skips_delete_and_writes_no_audit()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        _ = NewClient();

        using var scope = app.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<MongoContext>();
        var old = DateTime.UtcNow.AddDays(-30);
        var docId = Guid.NewGuid().ToString("n");
        var candidateId = Guid.NewGuid().ToString("n");
        var packageId = Guid.NewGuid().ToString("n");

        await mongo.ParsedExamCandidates.InsertOneAsync(new ParsedExamCandidateDocument
        {
            Id = docId,
            CandidateId = candidateId,
            PackageId = packageId,
            Classification = "Exam",
            Status = "Rejected",
            Version = 1,
            RejectedAt = old,
            DraftExamVersionId = null,
        });

        var hooks = new MutablePackageRetentionHooks
        {
            BeforeCandidateDelete = async (id, _) =>
            {
                await mongo.ParsedExamCandidates.UpdateOneAsync(
                    c => c.Id == id,
                    Builders<ParsedExamCandidateDocument>.Update
                        .Set(c => c.Status, "PendingReview")
                        .Unset(c => c.RejectedAt));
            },
        };

        await ProcessorWith(scope.ServiceProvider, new PackageRetentionOptions
        {
            ParsedCandidateRetentionDays = 7,
        }, hooks).RunOnceAsync(CancellationToken.None);

        Assert.NotNull(await mongo.ParsedExamCandidates.Find(c => c.Id == docId).FirstOrDefaultAsync());
        Assert.Empty(await mongo.Database.GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "RetentionPurged"),
                Builders<BsonDocument>.Filter.Eq("targetId", docId)))
            .ToListAsync());
    }

    [SkippableFact]
    public async Task Grouping_race_package_version_bump_skips_delete_and_writes_no_audit()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        _ = NewClient();

        using var scope = app.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<MongoContext>();
        var old = DateTimeOffset.UtcNow.AddDays(-30);
        var package = await SeedTerminalPackageAsync(scope.ServiceProvider, old);

        await mongo.SourceDocumentGroupingProposals.InsertOneAsync(new SourceDocumentGroupingProposalDocument
        {
            PackageId = package.Id,
            NeedsReview = false,
            Version = 1,
            Groups = [],
        });

        var hooks = new MutablePackageRetentionHooks
        {
            BeforeGroupingDelete = async (packageId, _) =>
            {
                await mongo.ExamPackages.UpdateOneAsync(
                    p => p.Id == packageId,
                    Builders<ExamPackageDocument>.Update.Inc(p => p.Version, 1));
            },
        };

        await ProcessorWith(scope.ServiceProvider, new PackageRetentionOptions
        {
            GroupingProposalRetentionDays = 7,
        }, hooks).RunOnceAsync(CancellationToken.None);

        Assert.NotNull(await mongo.SourceDocumentGroupingProposals
            .Find(g => g.PackageId == package.Id).FirstOrDefaultAsync());
        Assert.Empty(await mongo.Database.GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "RetentionPurged"),
                Builders<BsonDocument>.Filter.Eq("targetType", "source_document_grouping_proposal"),
                Builders<BsonDocument>.Filter.Eq("targetId", package.Id)))
            .ToListAsync());
    }

    [SkippableFact]
    public async Task Grouping_race_version_bump_after_recheck_skips_delete_and_writes_no_audit()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        _ = NewClient();

        using var scope = app.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<MongoContext>();
        var old = DateTimeOffset.UtcNow.AddDays(-30);
        var package = await SeedTerminalPackageAsync(scope.ServiceProvider, old);

        await mongo.SourceDocumentGroupingProposals.InsertOneAsync(new SourceDocumentGroupingProposalDocument
        {
            PackageId = package.Id,
            NeedsReview = false,
            Version = 1,
            Groups = [],
        });

        var hooks = new MutablePackageRetentionHooks
        {
            AfterGroupingPackageRecheck = async (packageId, _) =>
            {
                await mongo.ExamPackages.UpdateOneAsync(
                    p => p.Id == packageId,
                    Builders<ExamPackageDocument>.Update.Inc(p => p.Version, 1));
            },
        };

        await ProcessorWith(scope.ServiceProvider, new PackageRetentionOptions
        {
            GroupingProposalRetentionDays = 7,
        }, hooks).RunOnceAsync(CancellationToken.None);

        Assert.NotNull(await mongo.SourceDocumentGroupingProposals
            .Find(g => g.PackageId == package.Id).FirstOrDefaultAsync());
        Assert.Empty(await mongo.Database.GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "RetentionPurged"),
                Builders<BsonDocument>.Filter.Eq("targetType", "source_document_grouping_proposal"),
                Builders<BsonDocument>.Filter.Eq("targetId", package.Id)))
            .ToListAsync());
    }

    [SkippableFact]
    public async Task Grouping_same_version_mutation_after_recheck_retains_proposal_and_audit_absence()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        _ = NewClient();

        using var scope = app.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<MongoContext>();
        var package = await SeedTerminalPackageAsync(scope.ServiceProvider, DateTimeOffset.UtcNow.AddDays(-30));
        await mongo.SourceDocumentGroupingProposals.InsertOneAsync(new SourceDocumentGroupingProposalDocument
        {
            PackageId = package.Id, NeedsReview = false, Version = 1, Groups = [],
        });

        const string concurrentName = "same-version-mutation.json";
        var hooks = new MutablePackageRetentionHooks
        {
            AfterGroupingPackageRecheck = async (packageId, _) =>
            {
                await mongo.ExamPackages.UpdateOneAsync(
                    p => p.Id == packageId,
                    Builders<ExamPackageDocument>.Update.Set(p => p.FileName, concurrentName));
            },
        };

        await ProcessorWith(scope.ServiceProvider, new PackageRetentionOptions
        {
            GroupingProposalRetentionDays = 7,
        }, hooks).RunOnceAsync(CancellationToken.None);

        Assert.NotNull(await mongo.SourceDocumentGroupingProposals
            .Find(g => g.PackageId == package.Id).FirstOrDefaultAsync());
        var retained = await mongo.ExamPackages.Find(p => p.Id == package.Id).SingleAsync();
        Assert.Equal(package.Version, retained.Version);
        Assert.Equal(concurrentName, retained.FileName);
        Assert.Empty(await mongo.Database.GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "RetentionPurged"),
                Builders<BsonDocument>.Filter.Eq("targetType", "source_document_grouping_proposal"),
                Builders<BsonDocument>.Filter.Eq("targetId", package.Id)))
            .ToListAsync());
    }

    [SkippableFact]
    public async Task Extraction_race_completed_at_moved_forward_skips_delete_and_writes_no_audit()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        _ = NewClient();

        using var scope = app.Services.CreateScope();
        var mongo = scope.ServiceProvider.GetRequiredService<MongoContext>();
        var runId = Guid.NewGuid().ToString("n");
        var packageId = Guid.NewGuid().ToString("n");

        await mongo.ExamExtractionRuns.InsertOneAsync(new ExamExtractionRunDocument
        {
            Id = runId,
            PackageId = packageId,
            Provider = "test",
            Model = "test",
            PromptVersion = "1",
            SchemaId = "exam-extraction",
            ContractVersion = "1",
            RequestId = Guid.NewGuid().ToString("n"),
            InputHash = "abc",
            SourceCount = 0,
            InputTokens = 0,
            OutputTokens = 0,
            RequestedAt = DateTime.UtcNow.AddDays(-40),
            CompletedAt = DateTime.UtcNow.AddDays(-30),
        });

        var hooks = new MutablePackageRetentionHooks
        {
            BeforeExtractionDelete = async (id, _) =>
            {
                await mongo.ExamExtractionRuns.UpdateOneAsync(
                    r => r.Id == id,
                    Builders<ExamExtractionRunDocument>.Update.Set(
                        r => r.CompletedAt, DateTime.UtcNow));
            },
        };

        await ProcessorWith(scope.ServiceProvider, new PackageRetentionOptions
        {
            ExtractionRunRetentionDays = 7,
        }, hooks).RunOnceAsync(CancellationToken.None);

        Assert.NotNull(await mongo.ExamExtractionRuns.Find(r => r.Id == runId).FirstOrDefaultAsync());
        Assert.Empty(await mongo.Database.GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "RetentionPurged"),
                Builders<BsonDocument>.Filter.Eq("targetId", runId)))
            .ToListAsync());
    }
}
