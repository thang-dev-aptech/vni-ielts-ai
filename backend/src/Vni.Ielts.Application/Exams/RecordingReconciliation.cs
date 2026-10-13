using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

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
    ISpeakingRecordingMetadataStore? metadata,
    ISpeakingRecordingBlobStore? blobs,
    AbortStaleSpeakingUploads? abortStale,
    IClock clock)
{
    public RecordingReconciliation(
        IRecordingStore recordings,
        IAnswerSheetStore answers,
        IClock clock)
        : this(recordings, answers, null, null, null, clock)
    {
    }

    public RecordingReconciliation(
        IRecordingStore recordings,
        IAnswerSheetStore answers,
        AbortStaleSpeakingUploads? abortStale,
        IClock clock)
        : this(recordings, answers, null, null, abortStale, clock)
    {
    }

    /// <param name="minimumAge">
    /// How long a recording must have existed before it may be considered
    /// orphaned. → the age bound above.
    /// </param>
    /// <param name="limit">
    /// How many to examine in one pass. Bounded so a sweep cannot become a scan
    /// of every recording the product has ever stored, holding a connection for
    /// as long as that takes.
    /// </param>
    /// <param name="after">
    /// <b>The cursor that keeps the sweep moving.</b> The store hands back the
    /// oldest batch each pass; rows the sheet still references are examined
    /// and left — correctly — but left at the front of that queue. Without a
    /// cursor, one full batch of referenced rows makes every later pass
    /// re-read exactly those and never reach the orphans behind them: the
    /// sweep spins forever, and its report always says it examined the same
    /// five things. The caller carries the last examined row across passes.
    /// </param>
    public async Task<ReconciliationReport> SweepAsync(
        TimeSpan minimumAge, int limit, CancellationToken ct, StoredRecording? after = null)
    {
        // Pending inits whose PUT window elapsed — before the orphan sweep so
        // a abandoned row is not later mistaken for a Linked orphan.
        StaleUploadAbortReport? stale = null;
        if (abortStale is not null)
            stale = await abortStale.HandleAsync(limit, ct);

        var cutoff = clock.UtcNow - minimumAge;

        /*
         * <b>Enumerate from the metadata when it exists.</b> The presigned flow
         * never touches the recording store — its bytes go straight to object
         * storage — so a candidate list built from the recording store would
         * never see those takes at all. The metadata rows are the complete
         * census; the recording-store listing remains the fallback for a
         * deployment without the metadata store.
         */
        IReadOnlyList<StoredRecording> candidates;
        if (metadata is not null)
        {
            var rows = await metadata.ListOlderThanSweepableAsync(cutoff, limit, ct, after);
            candidates = rows
                .Select(r => new StoredRecording(
                    r.RecordingId, r.SessionId, r.QuestionId, r.CreatedAt))
                .ToArray();
        }
        else
        {
            candidates = await recordings.ListOlderThanAsync(cutoff, limit, ct, after);
        }

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
        SpeakingRecordingMetadata? RowFor(StoredRecording recording) =>
            metadata is null
                ? null
                : metadata.ListBySessionAsync(recording.SessionId, ct).GetAwaiter().GetResult()
                    .FirstOrDefault(r => r.RecordingId == recording.RecordingId);

        foreach (var recording in candidates)
        {
            examined++;

            if (!sheets.TryGetValue(recording.SessionId.Value, out var sheet))
            {
                sheet = await answers.LoadAsync(recording.SessionId, ExamModule.Speaking, ct);
                sheets[recording.SessionId.Value] = sheet;
            }

            // Still somebody's answer. Nothing to do — but an <c>Uploaded</c>
            // row the sheet names is the crash window recovered: complete
            // verified the bytes and wrote the sheet, then died before
            // linking. Finishing that link is the sweep's cheapest act.
            if (sheet.TryGetValue(recording.QuestionId, out var filed)
                && filed == recording.RecordingId)
            {
                var row = RowFor(recording);
                if (row is { Status: SpeakingRecordingStatus.Uploaded })
                    await metadata!.MarkLinkedAsync(row.UploadId, clock.UtcNow, ct);

                continue;
            }

            orphaned++;

            try
            {
                // <b>The bytes leave from wherever they live.</b> The presigned
                // flow stores blobs the recording store has never heard of —
                // deleting only through the recording store would leave the
                // object in the bucket while reporting success.
                var row = RowFor(recording);
                var blobKey = row?.ObjectKey
                    ?? $"{Vni.Ielts.Application.Exams.SpeakingRecordingKey.Prefix}{recording.RecordingId}";
                if (blobs is { IsConfigured: true })
                {
                    try { await blobs.DeleteAsync(blobKey, ct); }
                    catch (Exception) { /* the recording-store delete is the report's source of truth */ }
                }

                await recordings.DeleteAsync(recording.RecordingId, ct);

                // An orphaned row with live bytes was in <c>Uploaded</c> — the
                // crash window whose sheet write never landed. Retire it, or
                // the row sits there forever: sweepable, unfixable, holding a
                // row that says bytes exist when they no longer do.
                if (row is { Status: SpeakingRecordingStatus.Uploaded })
                    await metadata!.MarkAbandonedAsync(row.UploadId, ct);

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

        return new ReconciliationReport(
            examined, orphaned, removed, failed,
            stale?.Abandoned ?? 0, stale?.ObjectsRemoved ?? 0,
            candidates.Count > 0 ? candidates.Last() : after);
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
public sealed record ReconciliationReport(
    int Examined,
    int Orphaned,
    int Removed,
    int Failed,
    int StaleUploadsAbandoned = 0,
    int StaleObjectsRemoved = 0,
    StoredRecording? NextCursor = null);
