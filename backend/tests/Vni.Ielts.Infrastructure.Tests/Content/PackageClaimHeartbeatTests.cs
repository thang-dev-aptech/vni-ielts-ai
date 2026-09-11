using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

public sealed class PackageClaimHeartbeatTests
{
    [Fact]
    public async Task Lost_renewal_cancels_processing_token()
    {
        var renewalAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var packages = new Packages(renewalAttempted);
        var delayReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claim = new PackageClaim("package-1", "worker-1", 7, DateTimeOffset.UtcNow.AddMinutes(1));

        await using var heartbeat = PackageClaimHeartbeat.Start(
            packages,
            claim,
            TimeSpan.FromMinutes(1),
            new Clock(),
            CancellationToken.None,
            (_, ct) => delayReleased.Task.WaitAsync(ct));

        delayReleased.SetResult();
        await renewalAttempted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(heartbeat.Token.IsCancellationRequested);
        Assert.Equal(claim, packages.ReceivedClaim);
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class Packages(TaskCompletionSource renewalAttempted) : IExamPackageRepository
    {
        public PackageClaim? ReceivedClaim { get; private set; }

        public Task<bool> RenewClaimAsync(PackageClaim claim, DateTimeOffset leaseUntil, CancellationToken ct)
        {
            ReceivedClaim = claim;
            renewalAttempted.TrySetResult();
            return Task.FromResult(false);
        }

        public Task<ExamPackage?> FindAsync(string packageId, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveAsync(ExamPackage package, CancellationToken ct) => throw new NotSupportedException();
        public Task ReplaceVersionAsync(ExamPackage package, int expectedVersion, CancellationToken ct) => throw new NotSupportedException();
        public Task ReplaceVersionWithClaimAsync(ExamPackage package, int expectedVersion, string claimOwner, CancellationToken ct) => throw new NotSupportedException();
        public Task<ExamPackage?> FindByImportDraftIdAsync(string importDraftId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExamPackage>> ListByStatusAsync(PackageImportStatus status, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExamPackage>> ListClaimableAsync(DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExamPackage>> ListAllAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExamPackage>> ListUnpurgedTerminalAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string packageId, CancellationToken ct) => throw new NotSupportedException();
    }
}
