using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Exams;

/// <summary>
/// Reconciles stored audio against the answer sheets that reference it.
///
/// <b>Four things leave a recording nothing points at, and only a sweep can see
/// any of them.</b>
///
///   • An upload that streamed its bytes and was then refused because the
///     section had frozen. The handler deletes it, and that delete can fail —
///     deliberately, because the refusal must stand whether or not the tidying
///     worked.
///   • A crash between writing a new revision and removing the old one.
///   • A sitting or an account deleted while audio for it is still on disk.
///   • Anything written before the object key became derived, when every
///     re-record stranded its predecessor.
///
/// <b>Why this is not merely tidiness.</b> An unreferenced recording is a
/// learner's voice, which is personal data under Vietnam's PDPL. Storage
/// limitation is one of the law's stated principles, and "we kept it because
/// nothing deleted it" is not a lawful basis. → `docs/security/privacy-vietnam-pdpl.md`
///
/// <b>Two guards, and each closes a way this could destroy real work.</b>
///
/// <b>An age bound.</b> A recording written seconds ago may be seconds away
/// from being filed into a sheet. Deleting it because no sheet names it
/// <i>yet</i> would remove a learner's only copy of a spoken answer while they
/// were still uploading it.
///
/// <b>A referenced check that reads the sheet, not a cache of it.</b> The sheet
/// is the server's own index of what was recorded, so it is the only thing that
/// can say whether an object is still someone's answer.
/// </summary>
public sealed class RecordingReconciliation(
    IRecordingStore recordings,
    IAnswerSheetStore answers,
    IClock clock)
{
    /// <param name="minimumAge">
    /// How long a recording must have existed before it may be considered
    /// orphaned. → the age bound above.
    /// </param>
    /// <param name="limit">
    /// How many to examine in one pass. Bounded so a sweep cannot become a scan
    /// of every recording the product has ever stored, holding a connection for
    /// as long as that takes.
    /// </param>
    public async Task<ReconciliationReport> SweepAsync(
        TimeSpan minimumAge, int limit, CancellationToken ct)
    {
        var cutoff = clock.UtcNow - minimumAge;
        var candidates = await recordings.ListOlderThanAsync(cutoff, limit, ct);

        var examined = 0;
        var orphaned = 0;
        var removed = 0;
        var failed = 0;

        /*
         * <b>One sheet read per sitting, not per recording.</b> A Speaking
         * section is up to three answers, and a sweep over a day of sittings
         * would otherwise read the same document three times each.
         */
        var sheets = new Dictionary<string, IReadOnlyDictionary<string, string?>>();

        foreach (var recording in candidates)
        {
            examined++;

            if (!sheets.TryGetValue(recording.SessionId.Value, out var sheet))
            {
                sheet = await answers.LoadAsync(recording.SessionId, ExamModule.Speaking, ct);
                sheets[recording.SessionId.Value] = sheet;
            }

            // Still somebody's answer. Nothing to do.
            if (sheet.TryGetValue(recording.QuestionId, out var filed)
                && filed == recording.RecordingId)
            {
                continue;
            }

            orphaned++;

            try
            {
                await recordings.DeleteAsync(recording.RecordingId, ct);
                removed++;
            }
            catch (Exception)
            {
                /*
                 * <b>Counted rather than thrown.</b> One object that will not
                 * delete must not stop the sweep reaching the rest — and the
                 * count is what tells an operator that something is wrong with
                 * storage rather than with the data.
                 */
                failed++;
            }
        }

        return new ReconciliationReport(examined, orphaned, removed, failed);
    }
}

/// <summary>
/// What one sweep found.
///
/// <b>Returned rather than logged, for the same reason every other outcome in
/// this layer is.</b> Application takes no dependency on the logging
/// abstractions — an architecture test enforces it — and a number that is only
/// ever logged is a number no metric can read.
/// </summary>
/// <param name="Orphaned">
/// How many were referenced by nothing. <b>The one worth alerting on.</b> A
/// steady trickle is the ordinary consequence of refused uploads; a spike means
/// something is writing audio that never reaches a sheet.
/// </param>
public sealed record ReconciliationReport(int Examined, int Orphaned, int Removed, int Failed);
