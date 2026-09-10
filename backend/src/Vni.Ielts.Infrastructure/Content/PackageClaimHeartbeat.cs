using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Infrastructure.Content;

internal sealed class PackageClaimHeartbeat : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _lost;
    private readonly Task _loop;

    private PackageClaimHeartbeat(
        IExamPackageRepository packages,
        PackageClaim claim,
        TimeSpan leaseDuration,
        IClock clock,
        CancellationToken caller,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _lost = CancellationTokenSource.CreateLinkedTokenSource(caller);
        _loop = RunAsync(packages, claim, leaseDuration, clock, delay);
    }

    public CancellationToken Token => _lost.Token;

    public static PackageClaimHeartbeat Start(
        IExamPackageRepository packages,
        PackageClaim claim,
        TimeSpan leaseDuration,
        IClock clock,
        CancellationToken caller) =>
        new(packages, claim, leaseDuration, clock, caller, Task.Delay);

    internal static PackageClaimHeartbeat Start(
        IExamPackageRepository packages,
        PackageClaim claim,
        TimeSpan leaseDuration,
        IClock clock,
        CancellationToken caller,
        Func<TimeSpan, CancellationToken, Task> delay) =>
        new(packages, claim, leaseDuration, clock, caller, delay);

    private async Task RunAsync(
        IExamPackageRepository packages,
        PackageClaim claim,
        TimeSpan leaseDuration,
        IClock clock,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        var interval = TimeSpan.FromTicks(Math.Max(TimeSpan.FromMilliseconds(50).Ticks, leaseDuration.Ticks / 3));
        try
        {
            while (true)
            {
                await delay(interval, _stop.Token);
                if (!await packages.RenewClaimAsync(claim, clock.UtcNow + leaseDuration, _stop.Token))
                {
                    _lost.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch
        {
            _lost.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _loop; }
        finally
        {
            _lost.Dispose();
            _stop.Dispose();
        }
    }
}
