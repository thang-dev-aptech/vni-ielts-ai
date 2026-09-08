using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Worker;

public sealed class PackageIngestionWorker(
    IServiceScopeFactory scopes,
    ILogger<PackageIngestionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var packages = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
                var processor = scope.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();
                foreach (var package in await packages.ListByStatusAsync(PackageImportStatus.Uploaded, stoppingToken))
                    await processor.ProcessAsync(package, stoppingToken);
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
