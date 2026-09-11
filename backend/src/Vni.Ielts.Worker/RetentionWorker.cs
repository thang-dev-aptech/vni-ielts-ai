using Microsoft.Extensions.Options;
using Vni.Ielts.Infrastructure.Content;
using Vni.Ielts.Infrastructure.Persistence;

namespace Vni.Ielts.Worker;

/// <summary>
/// Periodically applies independently configured retention policies. When no
/// retention window is configured, the worker remains idle rather than
/// inventing a deletion policy.
/// </summary>
public sealed class RetentionWorker(
    IServiceScopeFactory scopes,
    IOptions<PackageRetentionOptions> options,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = options.Value;
        if (!HasRetentionWindow(configured))
        {
            logger.LogInformation("Package retention is disabled because no retention window is configured.");
            return;
        }

        var interval = configured.IntervalSeconds is > 0
            ? TimeSpan.FromSeconds(configured.IntervalSeconds.Value)
            : DefaultInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider
                    .GetRequiredService<PackageRetentionProcessor>()
                    .RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Package retention processing failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private static bool HasRetentionWindow(PackageRetentionOptions options) =>
        options.RawUploadRetentionDays is not null
        || options.ParsedCandidateRetentionDays is not null
        || options.GroupingProposalRetentionDays is not null
        || options.ExtractionRunRetentionDays is not null;
}
