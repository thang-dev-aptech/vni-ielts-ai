using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Worker;

public sealed class PackageIngestionWorker(
    IServiceScopeFactory scopes,
    ILogger<PackageIngestionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private readonly string _workerId = $"worker-{Environment.ProcessId}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var packages = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
                var processor = scope.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();
                var clock = scope.ServiceProvider.GetRequiredService<Vni.Ielts.Domain.Common.IClock>();
                foreach (var package in await packages.ListClaimableAsync(clock.UtcNow, stoppingToken))
                    await processor.ProcessAsync(package, _workerId, TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Package ingestion polling failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
