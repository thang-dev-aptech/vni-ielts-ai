using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Importing;

namespace Vni.Ielts.Infrastructure.Tests.Persistence.Importing;

public sealed class MongoImportAssetCleanupLeaseTests
{
    private static bool MongoAvailable
    {
        get
        {
            try
            {
                var context = NewContext();
                using var cursor = context.Database.ListCollectionNames();
                _ = cursor.MoveNext();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private const string SkipReason = "MongoDB not available on localhost:27018";

    private static MongoContext NewContext() =>
        new(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_import_asset_lease_{Guid.NewGuid():n}",
        }));

    [SkippableFact]
    public async Task Second_caller_waits_while_first_holds_lease_without_duplicate_key_failure()
    {
        Skip.IfNot(MongoAvailable, SkipReason);
        var context = NewContext();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = MongoImportAssetCleanup.WithCoordinationLeaseAsync(
            context,
            async (_, _) =>
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
                return 0;
            },
            default);

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = MongoImportAssetCleanup.WithCoordinationLeaseAsync(
            context,
            async (_, _) =>
            {
                secondEntered.TrySetResult();
                return 0;
            },
            default);

        await Task.Delay(200);
        Assert.False(secondEntered.Task.IsCompleted);
        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.True(secondEntered.Task.IsCompleted);
    }

    [SkippableFact]
    public async Task Heartbeat_keeps_long_running_action_exclusive_past_initial_lease_duration()
    {
        Skip.IfNot(MongoAvailable, SkipReason);
        var context = NewContext();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = MongoImportAssetCleanup.WithCoordinationLeaseAsync(
            context,
            async (_, _) =>
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
                return 0;
            },
            default);

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = MongoImportAssetCleanup.WithCoordinationLeaseAsync(
            context,
            async (_, _) =>
            {
                secondEntered.TrySetResult();
                return 0;
            },
            default);

        await Task.Delay(TimeSpan.FromSeconds(16)); // > 15s base lease; heartbeat should keep ownership
        Assert.False(secondEntered.Task.IsCompleted);
        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second);
    }

    [SkippableFact]
    public async Task Replacing_owner_and_fence_cancels_the_running_action()
    {
        Skip.IfNot(MongoAvailable, SkipReason);
        var context = NewContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = MongoImportAssetCleanup.WithCoordinationLeaseAsync(
            context,
            async (_, ct) =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    canceled.TrySetResult();
                    throw;
                }

                return 0;
            },
            default);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await context.ImportAssetCleanupCoordination.UpdateOneAsync(
            Builders<ImportAssetCleanupCoordinationDocument>.Filter.Eq(d => d.Id, "import-assets"),
            Builders<ImportAssetCleanupCoordinationDocument>.Update
                .Set(d => d.Owner, "replacement-owner")
                .Set(d => d.LeaseUntil, DateTime.UtcNow.AddMinutes(1))
                .Inc(d => d.Fence, 1));

        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [SkippableFact]
    public async Task Canceled_request_still_releases_lease_for_next_caller()
    {
        Skip.IfNot(MongoAvailable, SkipReason);
        var context = NewContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        var run = MongoImportAssetCleanup.WithCoordinationLeaseAsync(
            context,
            async (_, ct) =>
            {
                entered.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return 0;
            },
            cts.Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        var next = await MongoImportAssetCleanup.WithCoordinationLeaseAsync(
            context,
            (_, _) => Task.FromResult(7),
            default);
        Assert.Equal(7, next);
    }
}
