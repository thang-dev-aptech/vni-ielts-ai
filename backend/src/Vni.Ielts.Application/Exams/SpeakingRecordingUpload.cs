using System.Security.Cryptography;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Exams;

/// <summary>Lifecycle of one presigned Speaking upload.</summary>
public enum SpeakingRecordingStatus
{
    PendingUpload,
    Uploaded,
    Linked,
    Abandoned,
}

/// <summary>Mongo metadata for one Speaking recording upload.</summary>
public sealed record SpeakingRecordingMetadata(
    string UploadId,
    string RecordingId,
    string ObjectKey,
    UserId OwnerId,
    ExamSessionId SessionId,
    string QuestionId,
    string ContentType,
    long? ExpectedSizeBytes,
    string? ExpectedChecksumSha256,
    long? ActualSizeBytes,
    string? ActualChecksumSha256,
    SpeakingRecordingStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RetentionExpiresAt,
    DateTimeOffset? LinkedAt);

public sealed record InitSpeakingRecordingCommand(
    UserId UserId,
    ExamSessionId SessionId,
    string QuestionId,
    string ContentType,
    long SizeBytes,
    string ChecksumSha256);

public sealed record InitSpeakingRecordingResult(
    string UploadId,
    string RecordingId,
    Uri? UploadUrl,
    DateTimeOffset ExpiresAt,
    string ContentType,
    /// <summary>
    /// <c>presigned</c> when the recording is under
    /// <see cref="InitSpeakingRecording.MultipartThresholdBytes"/>;
    /// <c>multipart</c> directs the client to the legacy
    /// <c>POST …/recordings</c> body upload (GridFS or S3 Put) — that path is
    /// the resumable/retry transport for large answers. → FS8.3
    /// </summary>
    string UploadMode,
    long MultipartThresholdBytes);

public sealed record CompleteSpeakingRecordingCommand(
    UserId UserId,
    ExamSessionId SessionId,
    string UploadId,
    long SizeBytes,
    string ChecksumSha256);

/// <summary>Presigned upload is unavailable — object storage or the recordings bucket is not configured.</summary>
public sealed class SpeakingRecordingUploadUnavailableException()
    : Exception("Speaking recording upload via object storage is not configured.");

public sealed class SpeakingRecordingUploadNotFoundException()
    : Exception("No such Speaking recording upload.");

public sealed class SpeakingRecordingChecksumMismatchException()
    : Exception("The uploaded recording does not match the declared checksum or size.");

public sealed class SpeakingRecordingVerificationFailedException()
    : Exception("The uploaded recording could not be verified in object storage.");

/// <summary>The uploaded bytes carry no audio signature — refused before a byte is stored.</summary>
public sealed class SpeakingRecordingNotAudioException()
    : Exception("The uploaded recording's bytes are not a recognised audio format.");

/// <summary>
/// Shared gate for every Speaking recording write — direct upload and presigned.
/// </summary>
internal static class SpeakingRecordingGate
{
    public static void EnsureUploadAllowed(
        ExamSession session, ExamVersion version, string questionId, DateTimeOffset now)
    {
        if (session.Status != SessionStatus.InProgress)
            throw new SessionNotInProgressException(session.Status.ToString());

        var current = session.Current;
        if (current is null || current.Module != ExamModule.Speaking)
            throw new SectionNotOpenException(ExamModule.Speaking, current?.Module);

        if (current.IsPastDeadline(now)) throw new SessionExpiredException();

        var section = version.Section(ExamModule.Speaking)
            ?? throw new SectionNotOpenException(ExamModule.Speaking, current.Module);

        if (!section.Questions.Any(q => q.Id == questionId))
            throw new ArgumentException(
                "That question is not part of this exam's Speaking section.",
                nameof(questionId));
    }
}

/// <summary>
/// Derives the storage locations for a Speaking recording.
///
/// <b>Two shapes, and both are load-bearing.</b> The single-segment key
/// (<c>recordings/&lt;hash&gt;</c>) is the pre-2026-09-21 layout: derived from
/// session + question, so a re-record <i>replaced</i> the previous bytes
/// rather than accumulating orphaned learner voice. That same collision was
/// the take-isolation bug — a retake could overwrite an answer already filed,
/// and the refused-take cleanup deleted the id the frozen answer held — so
/// every presigned take now files under its own key
/// (<c>recordings/&lt;hash&gt;/&lt;uploadId&gt;</c>) and the answer sheet is
/// pointed at that id on complete. The legacy shape stays valid so anything
/// already stored keeps deleting and playing back.
/// </summary>
public static class SpeakingRecordingKey
{
    public const string Prefix = "recordings/";

    /// <summary>The per-question group component — the legacy whole key.</summary>
    public static string For(ExamSessionId sessionId, string questionId)
    {
        var material = System.Text.Encoding.UTF8.GetBytes($"{sessionId.Value}\u0000{questionId}");
        var hash = Convert.ToHexString(SHA256.HashData(material))[..32].ToLowerInvariant();
        return Prefix + hash;
    }

    /// <summary>
    /// One take's own key: the question's group hash, then the upload id.
    /// Nothing else writes here, so a refused or stale take's bytes can be
    /// removed without ever touching a sibling's.
    /// </summary>
    public static string ForObject(ExamSessionId sessionId, string questionId, string uploadId) =>
        $"{Prefix}{For(sessionId, questionId)[Prefix.Length..]}/{uploadId}";

    /// <summary>
    /// The id stored on the answer sheet — the object key without the prefix,
    /// so the sheet's value reconstructs the storage location for both shapes.
    /// </summary>
    public static string RecordingIdFor(ExamSessionId sessionId, string questionId) =>
        For(sessionId, questionId)[Prefix.Length..];

    public static bool IsValidObjectKey(string key)
    {
        if (!key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var tail = key[Prefix.Length..];

        // One segment is the legacy key; two is the per-upload take. Every
        // segment must be 32 lowercase hex — nothing a question id or an
        // attacker-supplied path could smuggle through.
        var segments = tail.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length is 1 or 2
            && segments.All(s =>
                s.Length == 32
                && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }
}

public interface ISpeakingRecordingMetadataStore
{
    Task InsertAsync(SpeakingRecordingMetadata metadata, CancellationToken ct);

    Task<SpeakingRecordingMetadata?> FindAsync(string uploadId, CancellationToken ct);

    Task MarkAbandonedForQuestionAsync(
        ExamSessionId sessionId, string questionId, CancellationToken ct);

    /// <summary>
    /// Moves a row from <see cref="SpeakingRecordingStatus.PendingUpload"/> to
    /// <c>Uploaded</c>. <b>False means the row was no longer pending</b> — a
    /// stale-abort pass or a concurrent complete got there first — and the
    /// caller must treat its upload as gone, because the sweep may already
    /// have removed its bytes.
    /// </summary>
    Task<bool> UpdateAfterUploadAsync(
        string uploadId, long sizeBytes, string checksumSha256, CancellationToken ct);

    /// <summary>False when the row is no longer in the <c>Uploaded</c> state.</summary>
    Task<bool> MarkLinkedAsync(string uploadId, DateTimeOffset at, CancellationToken ct);

    /// <summary>False when the row was already terminal (Linked or deleted).</summary>
    Task<bool> MarkAbandonedAsync(string uploadId, CancellationToken ct);

    Task<IReadOnlyList<SpeakingRecordingMetadata>> ListPendingOlderThanAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct);

    Task<IReadOnlyList<SpeakingRecordingMetadata>> ListBySessionAsync(
        ExamSessionId sessionId, CancellationToken ct);

    Task<IReadOnlyList<SpeakingRecordingMetadata>> ListByOwnerAsync(
        UserId ownerId, CancellationToken ct);

    Task DeleteAsync(string uploadId, CancellationToken ct);

    Task<IReadOnlyList<SpeakingRecordingMetadata>> ListOlderThanAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct);

    /// <summary>
    /// Rows the sweep may judge — Linked <i>and</i> Uploaded — older than the
    /// cutoff, ordered by (createdAt, recordingId), at or after the cursor.
    /// <b>Uploaded is in the list on purpose:</b> a crash between the bytes
    /// being verified and the sheet being written strands a row with live
    /// bytes in that state, and only the sweep can either recover it (the
    /// sheet names it) or remove it (nothing does). The stable ordering is
    /// the contract: a cursor over an unstable order can skip a row the sort
    /// shuffled behind it.
    /// </summary>
    Task<IReadOnlyList<SpeakingRecordingMetadata>> ListOlderThanSweepableAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct, StoredRecording? after);
}

public interface ISpeakingRecordingBlobStore
{
    bool IsConfigured { get; }

    Uri CreatePresignedPutUrl(
        string objectKey, string contentType, string checksumSha256, TimeSpan ttl);

    /// <summary>
    /// A short-lived, read-only URL to play one recording back — `S2b`'s
    /// counterpart to <see cref="CreatePresignedPutUrl"/>. No checksum or
    /// content type to declare: a GET proves nothing about the bytes, it
    /// only fetches them.
    /// </summary>
    Uri CreatePresignedGetUrl(string objectKey, TimeSpan ttl);

    Task<SpeakingRecordingObjectHead?> HeadAsync(string objectKey, CancellationToken ct);

    Task PutAsync(
        string objectKey, Stream content, string contentType, string checksumSha256,
        CancellationToken ct);

    /// <summary>
    /// Opens the stored bytes for server-side reading. <b>This is the port the
    /// real checksum flows through</b> — object metadata is client-set, so a
    /// hash computed over what HeadAsync echoes proves only that the client's
    /// claim agrees with itself. Reading the bytes and hashing them here is
    /// what verifies an upload. It is also the seam any speech-to-text adapter
    /// will read through (the audit's D-item: the recording ports had no way
    /// to hand audio back).
    /// </summary>
    Task<SpeakingRecordingObject?> OpenAsync(string objectKey, CancellationToken ct);

    Task DeleteAsync(string objectKey, CancellationToken ct);
}

public sealed record SpeakingRecordingObjectHead(
    long ContentLength, string ContentType, string? ChecksumSha256);

public sealed record SpeakingRecordingObject(Stream Content, string ContentType, long Length)
    : IDisposable
{
    public void Dispose() => Content.Dispose();
}

/// <summary>
/// Opens a presigned upload for one Speaking answer.
/// </summary>
public sealed class InitSpeakingRecording(
    IExamCatalogue catalogue,
    IExamSessionRepository sessions,
    ISpeakingRecordingBlobStore blobs,
    ISpeakingRecordingMetadataStore metadata,
    ObjectStorageSpeakingOptions storage,
    IClock clock)
{
    private static readonly TimeSpan UploadTtl = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Above this size the client should use the legacy multipart
    /// <c>POST …/recordings</c> path rather than a single presigned PUT.
    /// Five megabytes — well above a typical Opus Part 2, below the hard
    /// <see cref="MaxRecordingBytes"/> cap. → FS8.3
    /// </summary>
    public const long MultipartThresholdBytes = 5L * 1024 * 1024;

    public async Task<InitSpeakingRecordingResult> HandleAsync(
        InitSpeakingRecordingCommand command, CancellationToken ct)
    {
        if (!blobs.IsConfigured) throw new SpeakingRecordingUploadUnavailableException();

        if (command.SizeBytes <= 0 || command.SizeBytes > MaxRecordingBytes)
            throw new ArgumentException("Recording size is out of range.", nameof(command));

        if (string.IsNullOrWhiteSpace(command.ChecksumSha256)
            || command.ChecksumSha256.Length != 64
            || !command.ChecksumSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Checksum must be a 64-character SHA-256 hex string.", nameof(command));

        if (string.IsNullOrWhiteSpace(command.ContentType)
            || !command.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Content type must be an audio/* MIME type.", nameof(command));

        var (session, version) = await sessions.LoadOwnedAsync(
            catalogue, command.SessionId, command.UserId, ct);

        SpeakingRecordingGate.EnsureUploadAllowed(
            session, version, command.QuestionId, clock.UtcNow);

        var now = clock.UtcNow;

        // Re-record / retry: abandon any prior pending init for this question
        // so a stale complete cannot race the new revision. Each take now
        // files under its own key, so the old take's stored bytes — the ones
        // a filed answer may point at — are never in this PUT's path.
        await metadata.MarkAbandonedForQuestionAsync(command.SessionId, command.QuestionId, ct);

        if (command.SizeBytes > MultipartThresholdBytes)
        {
            // Large answers go through the legacy multipart body upload — the
            // resumable path when a single PUT is impractical. No pending
            // presigned row is created.
            return new InitSpeakingRecordingResult(
                string.Empty,
                SpeakingRecordingKey.RecordingIdFor(command.SessionId, command.QuestionId),
                null,
                now,
                command.ContentType,
                "multipart",
                MultipartThresholdBytes);
        }

        var uploadId = Guid.NewGuid().ToString("N");

        // One take, one key. Two takes at the same question never collide —
        // which is what keeps a re-take's PUT from overwriting the audio an
        // already-filed answer points at.
        var objectKey = SpeakingRecordingKey.ForObject(
            command.SessionId, command.QuestionId, uploadId);
        var recordingId = objectKey[SpeakingRecordingKey.Prefix.Length..];

        DateTimeOffset? retention = storage.RetentionDays is { } days
            ? now.AddDays(days)
            : null;

        await metadata.InsertAsync(new SpeakingRecordingMetadata(
            uploadId,
            recordingId,
            objectKey,
            command.UserId,
            command.SessionId,
            command.QuestionId,
            command.ContentType,
            command.SizeBytes,
            command.ChecksumSha256.ToLowerInvariant(),
            null,
            null,
            SpeakingRecordingStatus.PendingUpload,
            now,
            retention,
            null), ct);

        // The presigned window cannot outlive the section: a link that survives
        // the deadline is a window in which a learner answer can still be
        // written to after the sheet is frozen. The floor keeps a deadline one
        // second away from minting a zero-lifetime URL — the gate has already
        // refused a past deadline, so the remainder is positive.
        var ttl = UploadTtl;
        if (session.Current?.DeadlineAt is { } deadlineAt)
        {
            var remaining = deadlineAt - now;
            if (remaining < ttl)
                ttl = remaining > TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
        }

        var uploadUrl = blobs.CreatePresignedPutUrl(
            objectKey, command.ContentType, command.ChecksumSha256.ToLowerInvariant(), ttl);

        return new InitSpeakingRecordingResult(
            uploadId, recordingId, uploadUrl, now.Add(ttl), command.ContentType,
            "presigned", MultipartThresholdBytes);
    }

    public const long MaxRecordingBytes = 12L * 1024 * 1024;

    /// <summary>How long a pending init may wait before it is abandoned. → FS8.3</summary>
    public static TimeSpan PendingUploadTtl => UploadTtl;
}

/// <summary>
/// Verifies the stored bytes and links the take's id to the answer sheet.
///
/// <b>Verification reads the bytes, not the metadata.</b> Object metadata
/// (<c>x-amz-meta-sha256</c>) is set by whoever held the presigned URL, so a
/// check that compares the metadata against the init declaration verifies a
/// client's claim against the same client's claim. The server opens the
/// object, hashes it itself, and sniffs the head for an audio signature — the
/// same rule the CMS media library applies to every upload. A take failing
/// any of it is refused and its bytes removed: what stays behind must be
/// either a filed answer or nothing.
/// </summary>
public sealed class CompleteSpeakingRecording(
    IExamCatalogue catalogue,
    IExamSessionRepository sessions,
    IAnswerSheetStore answers,
    ISpeakingRecordingBlobStore blobs,
    ISpeakingRecordingMetadataStore metadata,
    IRecordingStore recordings,
    IClock clock)
{
    public async Task<string> HandleAsync(
        CompleteSpeakingRecordingCommand command, CancellationToken ct)
    {
        if (!blobs.IsConfigured) throw new SpeakingRecordingUploadUnavailableException();

        var row = await metadata.FindAsync(command.UploadId, ct)
            ?? throw new SpeakingRecordingUploadNotFoundException();

        if (row.OwnerId != command.UserId || row.SessionId != command.SessionId)
            throw new SpeakingRecordingUploadNotFoundException();

        if (row.Status is not SpeakingRecordingStatus.PendingUpload)
            throw new SpeakingRecordingUploadNotFoundException();

        var (session, version) = await sessions.LoadOwnedAsync(
            catalogue, command.SessionId, command.UserId, ct);

        SpeakingRecordingGate.EnsureUploadAllowed(
            session, version, row.QuestionId, clock.UtcNow);

        if (command.SizeBytes != row.ExpectedSizeBytes
            || !string.Equals(
                command.ChecksumSha256, row.ExpectedChecksumSha256, StringComparison.OrdinalIgnoreCase))
            throw new SpeakingRecordingChecksumMismatchException();

        // <b>Declare the row mine before verifying, not after.</b> The
        // stale-abort pass and a second complete both read pending rows; a
        // conditional transition is what stops the loser from filing bytes
        // the sweeper has already judged. False means the row changed under
        // us — the upload is factually gone.
        var now = clock.UtcNow;
        var claimed = await metadata.UpdateAfterUploadAsync(
            command.UploadId, command.SizeBytes, row.ExpectedChecksumSha256!, ct);
        if (!claimed)
        {
            // <b>The row changed under us - the take is factually gone.</b> The
            // sweeper that flipped it may not have reached the bytes yet, so
            // this complete removes them: whatever is left is unfiled voice
            // with no row claiming it.
            try
            {
                await blobs.DeleteAsync(row.ObjectKey, ct);
            }
            catch (Exception)
            {
                // The refusal stands whether or not the tidying worked.
            }

            throw new SpeakingRecordingUploadNotFoundException();
        }

        try
        {
            var head = await blobs.HeadAsync(row.ObjectKey, ct)
                ?? throw new SpeakingRecordingVerificationFailedException();

            if (head.ContentLength != command.SizeBytes
                || !string.Equals(head.ContentType, row.ContentType, StringComparison.OrdinalIgnoreCase))
                throw new SpeakingRecordingVerificationFailedException();

            // <b>The hash is computed here, over the bytes.</b> Whatever the
            // stored metadata says, the object itself must hash to the value
            // the learner declared at init.
            var stored = await blobs.OpenAsync(row.ObjectKey, ct)
                ?? throw new SpeakingRecordingVerificationFailedException();

            string actualChecksum;
            using (stored)
            {
                using var buffer = new MemoryStream();
                await stored.Content.CopyToAsync(buffer, ct);
                if (buffer.Length != command.SizeBytes)
                    throw new SpeakingRecordingVerificationFailedException();

                // The head bytes decide what the file is — magic bytes, never
                // the declared type. A non-audio payload is refused however
                // honestly its checksum was declared.
                var head2 = buffer.ToArray();
                var sniffed = MediaSniffer.Sniff(head2.AsSpan(0, Math.Min(MediaSniffer.ProbeLength, head2.Length)));
                if (sniffed is not { } sniff || sniff.Kind != MediaKind.Audio)
                    throw new SpeakingRecordingVerificationFailedException();

                actualChecksum = Convert.ToHexString(SHA256.HashData(head2)).ToLowerInvariant();
            }

            if (!string.Equals(actualChecksum, row.ExpectedChecksumSha256, StringComparison.OrdinalIgnoreCase))
                throw new SpeakingRecordingVerificationFailedException();

            await answers.SetAnswerAsync(
                command.SessionId, ExamModule.Speaking, row.QuestionId, row.RecordingId, now, ct);

            var linked = await metadata.MarkLinkedAsync(command.UploadId, now, ct);
            if (!linked)
            {
                // The sweep raced the link and refused the row — its bytes are
                // already gone. The sheet write above is the casualty: undo it
                // by clearing the entry rather than leaving a dead pointer.
                await answers.SetAnswerAsync(
                    command.SessionId, ExamModule.Speaking, row.QuestionId, null, clock.UtcNow, ct);
                throw new SpeakingRecordingUploadNotFoundException();
            }

            return row.RecordingId;
        }
        catch (SpeakingRecordingVerificationFailedException)
        {
            // <b>Every refused take's bytes go, and its row is retired.</b> A
            // verification refusal leaves an object nothing will ever file -
            // the upload window is dead to this take. Leaving either behind is
            // an orphan holding a learner's voice; the sweep would reach it
            // eventually, but clearing both here is immediate and free.
            try
            {
                await blobs.DeleteAsync(row.ObjectKey, ct);
            }
            catch (Exception)
            {
                // The refusal stands whether or not the tidying worked.
            }

            try
            {
                await metadata.MarkAbandonedAsync(row.UploadId, ct);
            }
            catch (Exception)
            {
                // Same rule. The sweep is the backstop.
            }

            throw;
        }
        catch (SectionSheetClosedException)
        {
            /*
             * <b>Only this take's bytes go.</b> The id is per-upload, so the
             * tidy-up cannot reach the audio an already-filed answer points
             * at — under the shared derived key it deleted exactly the id the
             * frozen answer held, which is how a lost race erased a submitted
             * answer's voice. What is removed here is the refused take's own
             * unfiled upload — the orphan this branch exists to prevent.
             */
            try
            {
                await recordings.DeleteAsync(row.RecordingId, ct);
            }
            catch (Exception)
            {
                // Refusal stands even if tidying fails.
            }

            throw;
        }
    }
}

public sealed record GetSpeakingRecordingPlaybackCommand(
    UserId UserId, ExamSessionId SessionId, string QuestionId);

public sealed record SpeakingRecordingPlaybackResult(Uri Url, DateTimeOffset ExpiresAt);

/// <summary>
/// `S2b` — the counterpart to <see cref="InitSpeakingRecording"/> the Result/
/// Review screen needs: a short-lived URL to play one answer back. Upload
/// had this from the start; listening back never did.
/// </summary>
public sealed class GetSpeakingRecordingPlaybackUrl(
    IExamCatalogue catalogue,
    IExamSessionRepository sessions,
    ISpeakingRecordingBlobStore blobs,
    ISpeakingRecordingMetadataStore metadata,
    IAnswerSheetStore answers,
    IClock clock)
{
    /// <summary>
    /// Minutes, not the upload window's quarter hour — a playback link is
    /// consumed the moment the page renders it, never held open for a
    /// multi-megabyte transfer the way an upload PUT is.
    /// </summary>
    private static readonly TimeSpan PlaybackTtl = TimeSpan.FromMinutes(5);

    public async Task<SpeakingRecordingPlaybackResult> HandleAsync(
        GetSpeakingRecordingPlaybackCommand command, CancellationToken ct)
    {
        if (!blobs.IsConfigured) throw new SpeakingRecordingUploadUnavailableException();

        // 404, not 403 — the same ownership shape as every other sitting
        // route. A learner asking for another's recording must not learn
        // the session id was real. → `SessionProjection.LoadOwnedAsync`
        await sessions.LoadOwnedAsync(catalogue, command.SessionId, command.UserId, ct);

        var rows = await metadata.ListBySessionAsync(command.SessionId, ct);
        var linked = rows
            .Where(r => r.QuestionId == command.QuestionId && r.Status == SpeakingRecordingStatus.Linked)
            .ToArray();

        // <b>Resolve through the sheet.</b> Takes are keyed per upload now, so
        // more than one take can be Linked at a question — the re-take that
        // filed after the first. What the learner recorded <i>last and
        // submitted</i> is what the sheet names, and that is the take that
        // must play — whichever row happens to enumerate first.
        var sheet = await answers.ReadAsync(command.SessionId, ExamModule.Speaking, ct);
        var filedId = sheet.Answers.GetValueOrDefault(command.QuestionId);

        var row = (filedId is { } id
                ? linked.FirstOrDefault(r => r.RecordingId == id)
                : null)
            ?? linked
                .OrderByDescending(r => r.LinkedAt ?? r.CreatedAt)
                .FirstOrDefault()
            ?? throw new SpeakingRecordingUploadNotFoundException();

        var url = blobs.CreatePresignedGetUrl(row.ObjectKey, PlaybackTtl);
        return new SpeakingRecordingPlaybackResult(url, clock.UtcNow.Add(PlaybackTtl));
    }
}

/// <summary>
/// Retention seam for Speaking recordings — no invented default.
///
/// <b><c>null</c> means keep until an explicit purge or an operator-set lifecycle
/// rule deletes the object.</b> Zero or negative is refused at startup. The
/// orphan sweep is separately gated by <c>Recordings:SweepEnabled</c> (default
/// off). → G-11, FS8.6
/// </summary>
public sealed class ObjectStorageSpeakingOptions
{
    public int? RetentionDays { get; init; }
}

/// <summary>
/// Abandons init rows whose presigned PUT window has elapsed without a
/// complete. Does not delete a linked answer — only pending uploads. → FS8.3
/// </summary>
public sealed class AbortStaleSpeakingUploads(
    ISpeakingRecordingMetadataStore metadata,
    ISpeakingRecordingBlobStore blobs,
    IClock clock)
{
    public async Task<StaleUploadAbortReport> HandleAsync(int limit, CancellationToken ct)
    {
        var cutoff = clock.UtcNow - InitSpeakingRecording.PendingUploadTtl;
        var stale = await metadata.ListPendingOlderThanAsync(cutoff, limit, ct);

        var abandoned = 0;
        var objectsRemoved = 0;
        var failed = 0;

        foreach (var row in stale)
        {
            try
            {
                await metadata.MarkAbandonedAsync(row.UploadId, ct);
                abandoned++;

                // Only remove the object when nothing Linked still claims this
                // key — a re-record may have already completed under the same
                // derived path while this pending row sat forgotten.
                var siblings = await metadata.ListBySessionAsync(row.SessionId, ct);
                var stillClaimed = siblings.Any(s =>
                    s.ObjectKey == row.ObjectKey
                    && s.Status is SpeakingRecordingStatus.Linked or SpeakingRecordingStatus.Uploaded
                    && s.UploadId != row.UploadId);

                if (!stillClaimed && blobs.IsConfigured)
                {
                    try
                    {
                        await blobs.DeleteAsync(row.ObjectKey, ct);
                        objectsRemoved++;
                    }
                    catch (Exception)
                    {
                        failed++;
                    }
                }
            }
            catch (Exception)
            {
                failed++;
            }
        }

        return new StaleUploadAbortReport(stale.Count, abandoned, objectsRemoved, failed);
    }
}

public sealed record StaleUploadAbortReport(
    int Examined, int Abandoned, int ObjectsRemoved, int Failed);

/// <summary>
/// Deletes Speaking audio for a sitting or an owner — the object-store half of
/// account/attempt deletion. → FS8.6
/// </summary>
public sealed class PurgeSpeakingRecordings(
    ISpeakingRecordingMetadataStore metadata,
    IRecordingStore recordings,
    ISpeakingRecordingBlobStore blobs,
    IAuditLog? audit,
    IClock clock)
{
    public Task<SpeakingPurgeReport> ForSessionAsync(
        ExamSessionId sessionId, UserId actorId, string actorEmail, CancellationToken ct) =>
        PurgeAsync(
            () => metadata.ListBySessionAsync(sessionId, ct),
            () => recordings.DeleteForSessionAsync(sessionId, ct),
            actorId, actorEmail, ct);

    public Task<SpeakingPurgeReport> ForOwnerAsync(
        UserId ownerId, UserId actorId, string actorEmail, CancellationToken ct) =>
        PurgeAsync(
            () => metadata.ListByOwnerAsync(ownerId, ct),
            async () =>
            {
                var rows = await metadata.ListByOwnerAsync(ownerId, ct);
                foreach (var sessionId in rows.Select(r => r.SessionId).Distinct())
                    await recordings.DeleteForSessionAsync(sessionId, ct);
            },
            actorId, actorEmail, ct);

    private async Task<SpeakingPurgeReport> PurgeAsync(
        Func<Task<IReadOnlyList<SpeakingRecordingMetadata>>> list,
        Func<Task> deleteStore,
        UserId actorId,
        string actorEmail,
        CancellationToken ct)
    {
        var rows = await list();
        var removed = 0;
        var failed = 0;

        await deleteStore();

        foreach (var row in rows)
        {
            try
            {
                if (blobs.IsConfigured)
                {
                    try { await blobs.DeleteAsync(row.ObjectKey, ct); }
                    catch (Exception) { /* store delete above is the source of truth */ }
                }

                await recordings.DeleteAsync(row.RecordingId, ct);
                await metadata.DeleteAsync(row.UploadId, ct);
                removed++;

                if (audit is not null)
                {
                    await audit.AppendAsync(
                        AuditEntry.Record(
                            actorId,
                            actorEmail,
                            AuditAction.SpeakingRecordingPurged,
                            "speaking_recording",
                            row.RecordingId,
                            row.QuestionId,
                            clock.UtcNow,
                            SpeakingAuditDetail.ForPurge(
                                row.RecordingId, row.SessionId.Value, row.QuestionId)),
                        ct);
                }
            }
            catch (Exception)
            {
                failed++;
            }
        }

        return new SpeakingPurgeReport(rows.Count, removed, failed);
    }
}

public sealed record SpeakingPurgeReport(int Examined, int Removed, int Failed);

/// <summary>
/// Audit detail for Speaking recording lifecycle — ids only, never a URL.
/// </summary>
public static class SpeakingAuditDetail
{
    public static IReadOnlyDictionary<string, string> ForPurge(
        string recordingId, string sessionId, string questionId)
    {
        var detail = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["recordingId"] = recordingId,
            ["sessionId"] = sessionId,
            ["questionId"] = questionId,
        };

        RejectLongLivedAudioUrls(detail);
        return detail;
    }

    public static IReadOnlyDictionary<string, string> ForMetadata(
        SpeakingRecordingMetadata metadata)
    {
        var detail = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["recordingId"] = metadata.RecordingId,
            ["objectKey"] = metadata.ObjectKey,
            ["questionId"] = metadata.QuestionId,
            ["status"] = metadata.Status.ToString(),
            ["contentType"] = metadata.ContentType,
        };

        RejectLongLivedAudioUrls(detail);
        return detail;
    }

    public static IReadOnlyDictionary<string, string> ForInit(
        string uploadId,
        string recordingId,
        string objectKey,
        string questionId)
    {
        var detail = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["uploadId"] = uploadId,
            ["recordingId"] = recordingId,
            ["objectKey"] = objectKey,
            ["questionId"] = questionId,
            ["status"] = SpeakingRecordingStatus.PendingUpload.ToString(),
        };

        RejectLongLivedAudioUrls(detail);
        return detail;
    }

    /// <summary>
    /// Guard: an audit trail that stores a presigned or public audio URL turns
    /// into a second long-lived pointer to learner voice. → FS8.6 / FS9.1
    /// </summary>
    public static void RejectLongLivedAudioUrls(IReadOnlyDictionary<string, string> detail)
    {
        foreach (var (key, value) in detail)
        {
            if (LooksLikeAudioUrl(value)
                || key.Contains("url", StringComparison.OrdinalIgnoreCase)
                || key.Contains("signature", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Audit detail '{key}' must not contain an audio URL or signed credential.",
                    nameof(detail));
            }
        }
    }

    public static bool LooksLikeAudioUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Contains("://", StringComparison.Ordinal)) return true;
        if (value.Contains("X-Amz-Signature", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Contains("X-Amz-", StringComparison.OrdinalIgnoreCase) && value.Contains('?'))
            return true;
        if (value.Contains("recordings/", StringComparison.Ordinal)
            && (value.Contains('?') || value.Contains("http", StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }

    /// <summary>
    /// Negative-test helper: true when a detail bag looks like it leaked a signed URL.
    /// </summary>
    public static bool LooksLikeSignedUrlLeak(IReadOnlyDictionary<string, string> detail) =>
        detail.Keys.Any(k =>
            k.Contains("url", StringComparison.OrdinalIgnoreCase)
            || k.Contains("signature", StringComparison.OrdinalIgnoreCase))
        || detail.Values.Any(LooksLikeAudioUrl);
}
