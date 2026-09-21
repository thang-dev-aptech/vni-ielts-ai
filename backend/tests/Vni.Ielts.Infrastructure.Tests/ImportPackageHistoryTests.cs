using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Importing;

namespace Vni.Ielts.Infrastructure.Tests;

/// <summary>
/// Package-import history persistence — attribution, bounded findings, and the
/// hard rule that a history row never stores archive bytes or a download key.
/// </summary>
public sealed class ImportPackageHistoryTests
{
    private static async Task<(MongoPackageImportHistoryStore Store, MongoContext Context)> NewStoreAsync()
    {
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_pkg_hist_{Guid.NewGuid():n}",
        }));

        await context.EnsureIndexesAsync(default);
        return (new MongoPackageImportHistoryStore(context, new SystemClock()), context);
    }

    private static PackageImportHistory Queued(
        string operationId,
        string actor = "operator-1",
        string fileName = "paper.zip",
        string definition = "cam-16",
        string hash = "abc123") =>
        new(
            Guid.NewGuid(),
            operationId,
            actor,
            fileName,
            new ExamDefinitionId(definition),
            1,
            hash,
            DraftId: null,
            ImportJobStage.Extracting,
            PackageImportHistoryResult.Queued,
            [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task Door_rejection_persists_bounded_findings_without_archive_bytes_or_key()
    {
        var (store, context) = await NewStoreAsync();
        var findings = new PackageFinding[]
        {
            new("error", "ZIP_COMPRESSION_RATIO", "reading/bomb.txt", "ratio cap exceeded"),
            new("error", "PATH_ESCAPE", "../x", "escaped the root"),
        };

        await store.RecordDoorRejectionAsync(
            new PackageImportHistory(
                Guid.NewGuid(),
                OperationId: "must-not-be-kept",
                ActorId: "operator-door",
                OriginalFileName: @"..\..\evil.zip",
                DefinitionId: new ExamDefinitionId("bomb-def"),
                VersionNumber: 1,
                SourceSha256: "deadbeef",
                DraftId: Guid.NewGuid(),
                Stage: ImportJobStage.Extracting,
                Result: PackageImportHistoryResult.Queued,
                Findings: findings,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow),
            default);

        var page = await store.QueryAsync(
            new PackageImportHistoryQuery(Result: PackageImportHistoryResult.DoorRejected),
            default);
        var row = Assert.Single(page.Items);
        Assert.Null(row.OperationId);
        Assert.Null(row.DraftId);
        Assert.Null(row.Stage);
        Assert.Equal("evil.zip", row.OriginalFileName);
        Assert.Equal("operator-door", row.ActorId);
        Assert.Equal(2, row.Findings.Count);

        var raw = await context.PackageImportHistory
            .Database
            .GetCollection<BsonDocument>("package_import_history")
            .Find(FilterDefinition<BsonDocument>.Empty)
            .FirstAsync();

        Assert.False(raw.Contains("archiveKey"));
        Assert.False(raw.Contains("archiveBytes"));
        Assert.False(raw.Contains("bytes"));
        Assert.False(raw.Contains("packageJson"));
        Assert.False(raw.Contains("operationId"));
        Assert.Equal("DoorRejected", raw["result"].AsString);
        Assert.Equal(2, raw["findings"].AsBsonArray.Count);
    }

    [Fact]
    public async Task Accepted_and_worker_rejected_uploads_keep_attribution_and_bounded_findings()
    {
        var (store, _) = await NewStoreAsync();
        var operationId = "op-worker-reject";
        var passageQuote = new string('Q', PackageImportHistoryBounds.MaxFindingMessageChars + 40);

        await store.RecordQueuedAsync(
            Queued(operationId, actor: "uploader-accepted", fileName: "cambridge-18.zip", hash: "hash-acc"),
            default);

        var accepted = await store.FindByOperationAsync(operationId, default);
        Assert.NotNull(accepted);
        Assert.Equal(PackageImportHistoryResult.Queued, accepted!.Result);
        Assert.Equal("uploader-accepted", accepted.ActorId);
        Assert.Equal("cambridge-18.zip", accepted.OriginalFileName);
        Assert.Equal("hash-acc", accepted.SourceSha256);
        Assert.Equal(ImportJobStage.Extracting, accepted.Stage);

        await store.ApplyTransitionAsync(
            operationId,
            PackageImportHistoryResult.Running,
            ImportJobStage.Checking,
            null,
            null,
            default);

        await store.ApplyTransitionAsync(
            operationId,
            PackageImportHistoryResult.Rejected,
            ImportJobStage.Checking,
            null,
            [
                new PackageFinding("error", "SCHEMA_INVALID", "exam.json", passageQuote),
                new PackageFinding("error", "ANSWER_KEY_MISMATCH", "key.json", "slot count"),
            ],
            default);

        var rejected = await store.FindByOperationAsync(operationId, default);
        Assert.NotNull(rejected);
        Assert.Equal(PackageImportHistoryResult.Rejected, rejected!.Result);
        Assert.Equal(ImportJobStage.Checking, rejected.Stage);
        Assert.Equal("uploader-accepted", rejected.ActorId);
        Assert.Equal("cambridge-18.zip", rejected.OriginalFileName);
        Assert.Equal("hash-acc", rejected.SourceSha256);
        Assert.Null(rejected.DraftId);
        Assert.Equal(2, rejected.Findings.Count);
        Assert.Equal(
            PackageImportHistoryBounds.MaxFindingMessageChars,
            rejected.Findings[0].Message.Length);
        Assert.Equal("SCHEMA_INVALID", rejected.Findings[0].Code);
        Assert.Equal("ANSWER_KEY_MISMATCH", rejected.Findings[1].Code);
    }

    [Fact]
    public async Task Pagination_and_filters_are_stable_across_pages()
    {
        var (store, _) = await NewStoreAsync();

        for (var i = 0; i < 3; i++)
        {
            await store.RecordQueuedAsync(
                Queued($"op-page-{i}", actor: "pager", hash: $"hash-{i}"),
                default);
            await Task.Delay(15);
        }

        await store.ApplyTransitionAsync(
            "op-page-2",
            PackageImportHistoryResult.Running,
            ImportJobStage.Parsing,
            null,
            null,
            default);

        var page1 = await store.QueryAsync(
            new PackageImportHistoryQuery(ActorId: "pager", Page: 1, PageSize: 2),
            default);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(1, page1.Page);
        Assert.Equal(2, page1.PageSize);

        var page2 = await store.QueryAsync(
            new PackageImportHistoryQuery(ActorId: "pager", Page: 2, PageSize: 2),
            default);
        Assert.Equal(3, page2.TotalCount);
        Assert.Single(page2.Items);

        var allIds = page1.Items.Concat(page2.Items).Select(i => i.OperationId).ToArray();
        Assert.Equal(3, allIds.Distinct().Count());
        Assert.Contains("op-page-0", allIds);
        Assert.Contains("op-page-1", allIds);
        Assert.Contains("op-page-2", allIds);

        var running = await store.QueryAsync(
            new PackageImportHistoryQuery(
                Result: PackageImportHistoryResult.Running,
                Stage: ImportJobStage.Parsing,
                ActorId: "pager"),
            default);
        Assert.Equal("op-page-2", Assert.Single(running.Items).OperationId);
    }
}
