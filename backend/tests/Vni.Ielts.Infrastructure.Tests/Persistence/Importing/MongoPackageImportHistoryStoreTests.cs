using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Importing;

namespace Vni.Ielts.Infrastructure.Tests.Persistence.Importing;

public sealed class MongoPackageImportHistoryStoreTests
{
    private static async Task<(MongoPackageImportHistoryStore Store, MongoContext Context)> NewStoreAsync()
    {
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_package_history_test_{Guid.NewGuid():n}",
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
    public async Task A_door_rejection_is_queryable_and_carries_no_operation_id()
    {
        var (store, _) = await NewStoreAsync();
        var findings = new PackageFinding[]
        {
            new("error", "ZIP_COMPRESSION_RATIO", "reading/bomb.txt", "ratio cap"),
            new("error", "PATH_ESCAPE", "../x", "escaped"),
        };

        await store.RecordDoorRejectionAsync(
            new PackageImportHistory(
                Guid.NewGuid(),
                OperationId: "must-not-be-kept",
                ActorId: "operator-1",
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
        Assert.Equal("operator-1", row.ActorId);
        Assert.Equal("deadbeef", row.SourceSha256);
        Assert.Equal(2, row.Findings.Count);
        Assert.Equal(["ZIP_COMPRESSION_RATIO", "PATH_ESCAPE"], row.Findings.Select(f => f.Code));
    }

    [Fact]
    public async Task Finding_count_and_size_bounds_are_enforced_on_write()
    {
        var (store, _) = await NewStoreAsync();
        var oversized = Enumerable.Range(0, PackageImportHistoryBounds.MaxFindings + 9)
            .Select(i => new PackageFinding(
                "error",
                $"CODE-{i}",
                "path",
                new string('M', PackageImportHistoryBounds.MaxFindingMessageChars + 20)))
            .ToArray();

        await store.RecordDoorRejectionAsync(
            new PackageImportHistory(
                Guid.NewGuid(),
                null,
                "operator-1",
                "bomb.zip",
                new ExamDefinitionId("bounds"),
                1,
                "hash",
                null,
                null,
                PackageImportHistoryResult.DoorRejected,
                oversized,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
            default);

        var row = Assert.Single((await store.QueryAsync(new PackageImportHistoryQuery(), default)).Items);
        Assert.Equal(PackageImportHistoryBounds.MaxFindings, row.Findings.Count);
        Assert.All(
            row.Findings,
            f => Assert.Equal(PackageImportHistoryBounds.MaxFindingMessageChars, f.Message.Length));
    }

    [Fact]
    public async Task Queued_uploads_and_worker_transitions_share_one_row()
    {
        var (store, _) = await NewStoreAsync();
        var operationId = "op-lifecycle";
        var draftId = Guid.NewGuid();

        await store.RecordQueuedAsync(Queued(operationId), default);
        await store.RecordQueuedAsync(Queued(operationId, actor: "operator-1", fileName: "paper.zip"), default);

        await store.ApplyTransitionAsync(
            operationId, PackageImportHistoryResult.Running, ImportJobStage.Parsing, null, null,
            default);

        await store.ApplyTransitionAsync(
            operationId,
            PackageImportHistoryResult.Completed,
            ImportJobStage.Done,
            draftId,
            [new PackageFinding("warning", "OK", "exam.json", "accepted")],
            default);

        var row = await store.FindByOperationAsync(operationId, default);
        Assert.NotNull(row);
        Assert.Equal(PackageImportHistoryResult.Completed, row!.Result);
        Assert.Equal(ImportJobStage.Done, row.Stage);
        Assert.Equal(draftId, row.DraftId);
        Assert.Equal("operator-1", row.ActorId);
        Assert.Equal("paper.zip", row.OriginalFileName);
        Assert.Equal("cam-16", row.DefinitionId?.Value);
        Assert.Equal("abc123", row.SourceSha256);
        Assert.Equal("OK", Assert.Single(row.Findings).Code);

        var byActor = await store.QueryAsync(
            new PackageImportHistoryQuery(ActorId: "operator-1"), default);
        Assert.Equal(operationId, Assert.Single(byActor.Items).OperationId);
    }

    [Fact]
    public async Task Re_uploading_a_failed_attempt_returns_the_same_row_to_queued()
    {
        var (store, _) = await NewStoreAsync();
        var operationId = "op-reopen";

        await store.RecordQueuedAsync(Queued(operationId), default);
        await store.ApplyTransitionAsync(
            operationId,
            PackageImportHistoryResult.Rejected,
            ImportJobStage.Checking,
            null,
            [new PackageFinding("error", "SCHEMA", "exam.json", "bad")],
            default);

        await store.RecordQueuedAsync(Queued(operationId, actor: "operator-2"), default);

        var row = await store.FindByOperationAsync(operationId, default);
        Assert.Equal(PackageImportHistoryResult.Queued, row!.Result);
        Assert.Equal("operator-2", row.ActorId);
        Assert.Equal(1, (await store.QueryAsync(new PackageImportHistoryQuery(), default)).TotalCount);
    }

    [Fact]
    public async Task History_is_listed_newest_first_and_filters_by_result_and_stage()
    {
        var (store, _) = await NewStoreAsync();

        await store.RecordQueuedAsync(Queued("op-old", actor: "a", hash: "old"), default);
        await Task.Delay(15);
        await store.RecordQueuedAsync(Queued("op-new", actor: "b", hash: "new"), default);
        await store.ApplyTransitionAsync(
            "op-new", PackageImportHistoryResult.Running, ImportJobStage.Parsing, null, null,
            default);

        var newest = await store.QueryAsync(new PackageImportHistoryQuery(), default);
        Assert.Equal(["op-new", "op-old"], newest.Items.Select(i => i.OperationId));

        var running = await store.QueryAsync(
            new PackageImportHistoryQuery(Result: PackageImportHistoryResult.Running), default);
        Assert.Equal("op-new", Assert.Single(running.Items).OperationId);

        var parsing = await store.QueryAsync(
            new PackageImportHistoryQuery(Stage: ImportJobStage.Parsing), default);
        Assert.Equal("op-new", Assert.Single(parsing.Items).OperationId);
    }

    [Fact]
    public async Task Indexes_cover_newest_first_pagination_and_the_declared_filters()
    {
        var (_, context) = await NewStoreAsync();
        var listed = await (await context.PackageImportHistory.Indexes.ListAsync()).ToListAsync();
        var names = listed.Select(i => i["name"].AsString).ToArray();

        Assert.Contains("ix_package_import_history_newest", names);
        Assert.Contains("ix_package_import_history_result", names);
        Assert.Contains("ix_package_import_history_stage", names);
        Assert.Contains("ix_package_import_history_actor", names);
        Assert.Contains("ux_package_import_history_operation", names);

        var unique = listed.Single(i => i["name"] == "ux_package_import_history_operation");
        Assert.True(unique["unique"].AsBoolean);
        Assert.True(unique["sparse"].AsBoolean);
    }
}
