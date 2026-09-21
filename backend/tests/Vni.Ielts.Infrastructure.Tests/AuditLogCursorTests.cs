using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Identity;
using Xunit;

namespace Vni.Ielts.Infrastructure.Tests;

public sealed class AuditLogCursorTests : IAsyncLifetime
{
    private MongoContext? _ctx;

    public async Task InitializeAsync()
    {
        _ctx = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_audit_cursor_test_{Guid.NewGuid():n}",
        }));
        await MongoAuditLog.EnsureIndexesAsync(_ctx.Database, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_ctx?.Database is not null)
        {
            await _ctx.Database.Client.DropDatabaseAsync(_ctx.Database.DatabaseNamespace.DatabaseName);
        }
    }

    [Fact]
    public async Task ListCursorAsync_ReturnsEntriesAndNextCursor()
    {
        var log = new MongoAuditLog(_ctx!);

        // Add some test entries
        var entry1 = new AuditEntry(
            "id1", DateTimeOffset.UtcNow.AddSeconds(-2), new UserId("actor1"), "actor1@test.com",
            AuditAction.ExamPublished, "exam", "exam1", "Test Exam 1", new Dictionary<string, string>());
        var entry2 = new AuditEntry(
            "id2", DateTimeOffset.UtcNow.AddSeconds(-1), new UserId("actor1"), "actor1@test.com",
            AuditAction.ExamPublished, "exam", "exam2", "Test Exam 2", new Dictionary<string, string>());

        await log.AppendAsync(entry1, CancellationToken.None);
        await log.AppendAsync(entry2, CancellationToken.None);

        var (entries, nextCursor) = await log.ListCursorAsync(null, null, 1, CancellationToken.None);

        Assert.Single(entries);
        Assert.Equal("id2", entries[0].Id);
        Assert.NotNull(nextCursor);
    }

    [Fact]
    public async Task ListCursorAsync_UsesCursorToGetNextPage()
    {
        var log = new MongoAuditLog(_ctx!);

        // Add three test entries with slightly different timestamps
        var now = DateTimeOffset.UtcNow;
        var entry1 = new AuditEntry(
            "id1", now.AddSeconds(-2), new UserId("actor1"), "actor1@test.com",
            AuditAction.ExamPublished, "exam", "exam1", "Test Exam 1", new Dictionary<string, string>());
        var entry2 = new AuditEntry(
            "id2", now.AddSeconds(-1), new UserId("actor1"), "actor1@test.com",
            AuditAction.ExamPublished, "exam", "exam2", "Test Exam 2", new Dictionary<string, string>());
        var entry3 = new AuditEntry(
            "id3", now, new UserId("actor1"), "actor1@test.com",
            AuditAction.ExamPublished, "exam", "exam3", "Test Exam 3", new Dictionary<string, string>());

        await log.AppendAsync(entry1, CancellationToken.None);
        await log.AppendAsync(entry2, CancellationToken.None);
        await log.AppendAsync(entry3, CancellationToken.None);

        // Get first page
        var (page1, cursor1) = await log.ListCursorAsync(null, null, 2, CancellationToken.None);
        Assert.Equal(2, page1.Count);
        Assert.Equal("id3", page1[0].Id);
        Assert.Equal("id2", page1[1].Id);
        Assert.NotNull(cursor1);

        // Get second page using cursor
        var (page2, cursor2) = await log.ListCursorAsync(null, null, 2, CancellationToken.None, cursor1);
        Assert.Single(page2);
        Assert.Equal("id1", page2[0].Id);
        Assert.Null(cursor2);  // No more pages
    }

    [Fact]
    public async Task ListCursorAsync_StableUnderConcurrentInserts()
    {
        var log = new MongoAuditLog(_ctx!);

        // Add initial entries
        var now = DateTimeOffset.UtcNow;
        var initialEntries = new List<AuditEntry>();
        for (int i = 1; i <= 3; i++)
        {
            var entry = new AuditEntry(
                $"initial-id{i}", now.AddSeconds(-10 + i), new UserId("actor1"), "actor1@test.com",
                AuditAction.ExamPublished, "exam", $"exam{i}", $"Test Exam {i}", new Dictionary<string, string>());
            initialEntries.Add(entry);
            await log.AppendAsync(entry, CancellationToken.None);
        }

        // Get the first page
        var (page1, cursor1) = await log.ListCursorAsync(null, null, 2, CancellationToken.None);
        Assert.Equal(2, page1.Count);
        var page1Ids = page1.Select(e => e.Id).ToList();

        // Now insert new entries at the head (simulating concurrent audit activity)
        for (int i = 1; i <= 3; i++)
        {
            var entry = new AuditEntry(
                $"new-id{i}", now.AddSeconds(i), new UserId("actor1"), "actor1@test.com",
                AuditAction.UserSuspended, "user", $"user{i}", $"Test User {i}", new Dictionary<string, string>());
            await log.AppendAsync(entry, CancellationToken.None);
        }

        // Fetch the next page using the cursor from page 1
        var (page2, cursor2) = await log.ListCursorAsync(null, null, 2, CancellationToken.None, cursor1);

        // Assertions:
        // 1. No row from page 1 should reappear in page 2
        var page2Ids = page2.Select(e => e.Id).ToList();
        var overlap = page1Ids.Intersect(page2Ids);
        Assert.Empty(overlap);

        // 2. No row should be skipped between page 1 and page 2
        // Page 1 returned the 2 newest initial entries. The cursor points to the
        // oldest one retrieved (initial-id2). The next page should start with the
        // next older entry (initial-id1).
        Assert.NotEmpty(page2);
        Assert.Equal("initial-id1", page2[0].Id);
    }

    [Fact]
    public async Task ListCursorAsync_FiltersWork()
    {
        var log = new MongoAuditLog(_ctx!);

        var now = DateTimeOffset.UtcNow;
        var entry1 = new AuditEntry(
            "id1", now.AddSeconds(-2), new UserId("actor1"), "actor1@test.com",
            AuditAction.ExamPublished, "exam", "exam1", "Test Exam 1", new Dictionary<string, string>());
        var entry2 = new AuditEntry(
            "id2", now.AddSeconds(-1), new UserId("actor2"), "actor2@test.com",
            AuditAction.UserSuspended, "user", "user1", "Test User 1", new Dictionary<string, string>());

        await log.AppendAsync(entry1, CancellationToken.None);
        await log.AppendAsync(entry2, CancellationToken.None);

        // Filter by actor
        var (filteredByActor, _) = await log.ListCursorAsync("actor1", null, 10, CancellationToken.None);
        Assert.Single(filteredByActor);
        Assert.Equal("id1", filteredByActor[0].Id);

        // Filter by action
        var (filteredByAction, _) = await log.ListCursorAsync(null, "UserSuspended", 10, CancellationToken.None);
        Assert.Single(filteredByAction);
        Assert.Equal("id2", filteredByAction[0].Id);
    }
}
