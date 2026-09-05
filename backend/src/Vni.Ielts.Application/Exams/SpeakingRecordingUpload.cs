using System.Security.Cryptography;
using Vni.Ielts.Application.Identity;
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
/// Derives the stable object key for one question's recording.
///
/// Hashed from session and question so retries and re-records replace rather
/// than accumulate orphaned learner voice data.
/// </summary>
public static class SpeakingRecordingKey
{
    public const string Prefix = "recordings/";

    public static string For(ExamSessionId sessionId, string questionId)
    {
        var material = System.Text.Encoding.UTF8.GetBytes($"{sessionId.Value}\u0000{questionId}");
        var hash = Convert.ToHexString(SHA256.HashData(material))[..32].ToLowerInvariant();
        return Prefix + hash;
    }

    /// <summary>
    /// The id stored on the answer sheet — the hash without the prefix.
    /// </summary>
    public static string RecordingIdFor(ExamSessionId sessionId, string questionId) =>
        For(sessionId, questionId)[Prefix.Length..];

    public static bool IsValidObjectKey(string key)
    {
        if (!key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var tail = key[Prefix.Length..];
        if (tail.Length != 32) return false;
        return tail.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}

public interface ISpeakingRecordingMetadataStore
{
    Task InsertAsync(SpeakingRecordingMetadata metadata, CancellationToken ct);

    Task<SpeakingRecordingMetadata?> FindAsync(string uploadId, CancellationToken ct);

    Task MarkAbandonedForQuestionAsync(
        ExamSessionId sessionId, string questionId, CancellationToken ct);

    Task UpdateAfterUploadAsync(
        string uploadId, long sizeBytes, string checksumSha256, CancellationToken ct);

    Task MarkLinkedAsync(string uploadId, DateTimeOffset at, CancellationToken ct);

    Task MarkAbandonedAsync(string uploadId, CancellationToken ct);

    Task<IReadOnlyList<SpeakingRecordingMetadata>> ListPendingOlderThanAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct);

    Task<IReadOnlyList<SpeakingRecordingMetadata>> ListBySessionAsync(
        ExamSessionId sessionId, CancellationToken ct);

    Task<IReadOnlyList<SpeakingRecordingMetadata>> ListByOwnerAsync(
        UserId ownerId, CancellationToken ct);

    Task DeleteAsync(string uploadId, CancellationToken ct);

    Task<IReadOnlyList<SpeakingRecordingMetadata>> ListOlderThanAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct);
}

public interface ISpeakingRecordingBlobStore
{
    bool IsConfigured { get; }

    Uri CreatePresignedPutUrl(
        string objectKey, string contentType, string checksumSha256, TimeSpan ttl);

    Task<SpeakingRecordingObjectHead?> HeadAsync(string objectKey, CancellationToken ct);

    Task PutAsync(
        string objectKey, Stream content, string contentType, string checksumSha256,
        CancellationToken ct);

    Task DeleteAsync(string objectKey, CancellationToken ct);
}

public sealed record SpeakingRecordingObjectHead(
    long ContentLength, string ContentType, string? ChecksumSha256);

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

        var objectKey = SpeakingRecordingKey.For(command.SessionId, command.QuestionId);
        var recordingId = SpeakingRecordingKey.RecordingIdFor(command.SessionId, command.QuestionId);
        var now = clock.UtcNow;

        // Re-record / retry: abandon any prior pending init for this question
        // so a stale complete cannot race the new revision. The object key is
        // derived, so the next PUT replaces bytes rather than orphaning them.
        await metadata.MarkAbandonedForQuestionAsync(command.SessionId, command.QuestionId, ct);

        if (command.SizeBytes > MultipartThresholdBytes)
        {
            // Large answers go through the legacy multipart body upload — the
            // resumable path when a single PUT is impractical. No pending
            // presigned row is created.
            return new InitSpeakingRecordingResult(
                string.Empty,
                recordingId,
                null,
                now,
                command.ContentType,
                "multipart",
                MultipartThresholdBytes);
        }

        var uploadId = Guid.NewGuid().ToString("N");

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

        var uploadUrl = blobs.CreatePresignedPutUrl(
            objectKey, command.ContentType, command.ChecksumSha256.ToLowerInvariant(), UploadTtl);

        return new InitSpeakingRecordingResult(
            uploadId, recordingId, uploadUrl, now.Add(UploadTtl), command.ContentType,
            "presigned", MultipartThresholdBytes);
    }

    public const long MaxRecordingBytes = 12L * 1024 * 1024;

    /// <summary>How long a pending init may wait before it is abandoned. → FS8.3</summary>
    public static TimeSpan PendingUploadTtl => UploadTtl;
}

/// <summary>
/// Verifies a private object via HEAD and links its id to the answer sheet.
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

        if (row.Status is SpeakingRecordingStatus.Abandoned or SpeakingRecordingStatus.Linked)
            throw new SpeakingRecordingUploadNotFoundException();

        var (session, version) = await sessions.LoadOwnedAsync(
            catalogue, command.SessionId, command.UserId, ct);

        SpeakingRecordingGate.EnsureUploadAllowed(
            session, version, row.QuestionId, clock.UtcNow);

        if (command.SizeBytes != row.ExpectedSizeBytes
            || !string.Equals(
                command.ChecksumSha256, row.ExpectedChecksumSha256, StringComparison.OrdinalIgnoreCase))
            throw new SpeakingRecordingChecksumMismatchException();

        var head = await blobs.HeadAsync(row.ObjectKey, ct)
            ?? throw new SpeakingRecordingVerificationFailedException();

        if (head.ContentLength != command.SizeBytes
            || !string.Equals(head.ContentType, row.ContentType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(head.ChecksumSha256, row.ExpectedChecksumSha256, StringComparison.OrdinalIgnoreCase))
            throw new SpeakingRecordingVerificationFailedException();

        var now = clock.UtcNow;
        await metadata.UpdateAfterUploadAsync(
            command.UploadId, head.ContentLength, row.ExpectedChecksumSha256!, ct);

        try
        {
            await answers.SetAnswerAsync(
                command.SessionId, ExamModule.Speaking, row.QuestionId, row.RecordingId, now, ct);
        }
        catch (SectionSheetClosedException)
        {
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

        await metadata.MarkLinkedAsync(command.UploadId, now, ct);
        return row.RecordingId;
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
