using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Worker;

/// <summary>
/// Removes stored audio that no answer sheet references.
///
/// <b>An unreferenced recording is a learner's voice with nothing pointing at
/// it</b> — personal data under Vietnam's PDPL, which states storage limitation
/// as a principle. "We kept it because nothing deleted it" is not a lawful
/// basis, and it is what the product was doing.
///
/// <b>Deliberately dull, and deliberately conservative.</b> Every knob below
/// errs towards leaving audio in place: a long minimum age, a small batch, a
/// slow cadence. The failure mode of being too eager is destroying a learner's
/// only copy of a spoken answer; the failure mode of being too cautious is some
/// disk. Those are not comparable.
/// </summary>
public sealed class ReconciliationWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<ReconciliationWorker> logger) : BackgroundService
{
    /// <summary>
    /// <b>How long a recording must exist before it can be called an orphan.</b>
    ///
    /// A recording written seconds ago may be seconds away from being filed
    /// into a sheet — the object is written first and the id second, so there
    /// is always a moment when storage holds audio the sheet does not name.
    /// Sweeping inside that moment would delete the learner's answer while they
    /// were still uploading it.
    ///
    /// Six hours, and generous on purpose: nothing is gained by being quick.
    /// </summary>
    private TimeSpan MinimumAge =>
        TimeSpan.FromHours(configuration.GetValue("Recordings:OrphanMinimumAgeHours", 6));

    /// <summary>
    /// How often to sweep. Hourly by default.
    ///
    /// <b>Configuration rather than a constant, because the right cadence
    /// depends on a retention policy nobody has stated yet.</b> Audio retention
    /// is `I2.5` and belongs to the product owner; until they rule, this is a
    /// seam with a stated default rather than an invented one. → `G-11`
    /// </summary>
    private TimeSpan Interval =>
        TimeSpan.FromMinutes(configuration.GetValue("Recordings:SweepIntervalMinutes", 60));

    /// <summary>
    /// How many to examine per sweep.
    ///
    /// Bounded so this can never become a scan of every recording the product
    /// has stored, holding a connection for as long as that takes. A backlog
    /// drains over several sweeps, which is fine — nothing is waiting on it.
    /// </summary>
    private int BatchSize => configuration.GetValue("Recordings:SweepBatchSize", 200);

    /// <summary>
    /// <b>Off unless switched on.</b>
    ///
    /// A background process that deletes audio is not something to enable by
    /// default in an environment nobody has looked at. It is enabled where
    /// somebody has decided it should run — which is also the moment they
    /// decide what the retention window is.
    /// </summary>
    private bool Enabled => configuration.GetValue("Recordings:SweepEnabled", false);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!Enabled)
        {
            logger.LogInformation(
                "Recording reconciliation is off. Set Recordings:SweepEnabled to run it. "
                + "Until then, audio that no answer sheet references stays on disk.");
            return;
        }

        logger.LogInformation(
            "Recording reconciliation started: every {Interval}, {Batch} at a time, "
            + "ignoring anything younger than {Age}.",
            Interval, BatchSize, MinimumAge);

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var sweep = scope.ServiceProvider.GetRequiredService<RecordingReconciliation>();

                var report = await sweep.SweepAsync(MinimumAge, BatchSize, stopping);

                /*
                 * <b>Silent when there is nothing to say.</b> An empty sweep is
                 * the expected outcome and logging it hourly is how a log stops
                 * being read — the same lesson as the marking worker's absent
                 * heartbeat.
                 */
                if (report.Orphaned > 0 || report.Failed > 0)
                {
                    logger.LogWarning(
                        "Reconciliation examined {Examined} recordings, found {Orphaned} that "
                        + "nothing references, removed {Removed}, and could not remove {Failed}. "
                        + "A steady trickle is the ordinary consequence of refused uploads; a "
                        + "spike means something is writing audio that never reaches a sheet.",
                        report.Examined, report.Orphaned, report.Removed, report.Failed);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Recording reconciliation failed. It will run again.");
            }

            try
            {
                await Task.Delay(Interval, stopping);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Recording reconciliation stopped.");
    }
}
