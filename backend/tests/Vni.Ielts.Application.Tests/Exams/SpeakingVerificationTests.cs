using System.Security.Cryptography;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Tests.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// What the server verifies about a stored recording, and when.
///
/// <para>
/// <b>The model this file pins.</b> Object metadata (<c>x-amz-meta-sha256</c>)
/// is client-set, so "the metadata matches" has always meant "the client's
/// claim agrees with the client's claim". The server verifies the <i>bytes</i>:
/// it opens the stored object, hashes it itself, and sniffs the head for an
/// audio signature — and a take that fails any of that is refused and its
/// bytes removed. A row that crashed between <c>Uploaded</c> and
/// <c>Linked</c> is not stranded: the sweep reads the sheet and either
/// recovers it or deletes it, and a status write never flips a row the sweep
/// has already refused.
/// </para>
/// </summary>
public sealed class SpeakingVerificationTests
{
    private static readonly byte[] Truthful = [0x49, 0x44, 0x33, 0x04]; // ID3 — a real MP3 head
    private static readonly byte[] Swapped = [0x09, 0x09, 0x09];
    private static readonly byte[] NotAudio =
        [0x4d, 0x5a, 0x90, 0x00, 0x01, 0x02, 0x03, 0x04]; // "MZ" — an executable header

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public async Task Bytes_that_differ_from_the_declared_checksum_are_caught_by_the_server_hash()
    {
        var env = VerificationEnv.Create();
        var declared = Hash(Truthful); // what init declared
        var put = await env.Init("audio/webm", declared);

        // The attacker PUTs different bytes — the metadata header still carries
        // the declared value, because the presigned URL signed it that way.
        env.Blobs.Store(SpeakingRecordingKey.Prefix + put.RecordingId, Swapped, "audio/webm", declared);

        await Assert.ThrowsAsync<SpeakingRecordingVerificationFailedException>(() =>
            env.Complete(put.UploadId, Truthful.Length, declared));

        // And the refused take's bytes go — they file nowhere.
        Assert.False(env.Blobs.Exists(SpeakingRecordingKey.Prefix + put.RecordingId));
        Assert.Null(env.SheetValue(SpeakingFixtures.QuestionId));
    }

    [Fact]
    public async Task Bytes_that_are_not_audio_are_refused_and_removed()
    {
        var env = VerificationEnv.Create();
        var payload = NotAudio; // a real checksum of a real non-audio file
        var declared = Hash(payload);
        var put = await env.Init("audio/webm", declared, payload.Length);
        env.Blobs.Store(SpeakingRecordingKey.Prefix + put.RecordingId, payload, "audio/webm", declared);

        await Assert.ThrowsAsync<SpeakingRecordingVerificationFailedException>(() =>
            env.Complete(put.UploadId, payload.Length, declared));

        Assert.False(env.Blobs.Exists(SpeakingRecordingKey.Prefix + put.RecordingId));
    }

    [Fact]
    public async Task Truthful_bytes_pass_the_server_hash_and_sniff()
    {
        var env = VerificationEnv.Create();
        var declared = Hash(Truthful);
        var put = await env.Init("audio/webm", declared, Truthful.Length);
        env.Blobs.Store(SpeakingRecordingKey.Prefix + put.RecordingId, Truthful, "audio/webm", declared);

        var recordingId = await env.Complete(put.UploadId, Truthful.Length, declared);

        Assert.Equal(recordingId, env.SheetValue(SpeakingFixtures.QuestionId));
        Assert.True(env.Blobs.Exists(SpeakingRecordingKey.Prefix + put.RecordingId));
    }

    [Fact]
    public async Task An_uploaded_but_unlinked_take_is_swept_when_nothing_files_it()
    {
        var env = VerificationEnv.Create();
        var declared = Hash(Truthful);
        var put = await env.Init("audio/webm", declared, Truthful.Length);
        env.Blobs.Store(SpeakingRecordingKey.Prefix + put.RecordingId, Truthful, "audio/webm", declared);

        // The crash window: bytes verified, row moved to Uploaded — then the
        // process died before the sheet write and the link.
        env.Metadata.SetStatusDirect(
            put.UploadId, SpeakingRecordingStatus.Uploaded);

        var report = await env.Sweep();

        // The sweep sees the row (it never did — only Linked rows were listed),
        // finds no sheet entry naming it, removes the bytes and retires the row.
        Assert.Equal(1, report.Orphaned);
        Assert.Equal(1, report.Removed);
        Assert.False(env.Blobs.Exists(SpeakingRecordingKey.Prefix + put.RecordingId));
        Assert.Equal(
            SpeakingRecordingStatus.Abandoned,
            env.Metadata.AllRows.Single(r => r.UploadId == put.UploadId).Status);
    }

    [Fact]
    public async Task An_uploaded_but_filed_take_is_recovered_by_the_sweep()
    {
        var env = VerificationEnv.Create();
        var declared = Hash(Truthful);
        var put = await env.Init("audio/webm", declared, Truthful.Length);
        env.Blobs.Store(SpeakingRecordingKey.Prefix + put.RecordingId, Truthful, "audio/webm", declared);

        env.Metadata.SetStatusDirect(
            put.UploadId, SpeakingRecordingStatus.Uploaded);
        // The crash happened after the sheet write: the take IS the answer.
        await env.Answers.SetAnswerAsync(
            SpeakingFixtures.Sitting, ExamModule.Speaking, SpeakingFixtures.QuestionId,
            put.RecordingId, SpeakingFixtures.T0, default);

        var report = await env.Sweep();

        Assert.Equal(0, report.Orphaned);
        Assert.True(env.Blobs.Exists(SpeakingRecordingKey.Prefix + put.RecordingId));
        Assert.Equal(
            SpeakingRecordingStatus.Linked,
            env.Metadata.AllRows.Single(r => r.UploadId == put.UploadId).Status);
    }

    [Fact]
    public async Task A_row_the_sweeper_refused_cannot_be_relinked_by_a_late_complete()
    {
        var env = VerificationEnv.Create();
        var declared = Hash(Truthful);
        var put = await env.Init("audio/webm", declared, Truthful.Length);
        env.Blobs.Store(SpeakingRecordingKey.Prefix + put.RecordingId, Truthful, "audio/webm", declared);

        // The racer: the stale-abort pass abandons the row between the
        // handler's read and its write. The store's update must refuse to
        // flip a row that is no longer pending — and the handler must treat
        // that refusal as the 404 it factually is.
        env.Metadata.RacerAbandonsOnUpdate = true;

        await Assert.ThrowsAsync<SpeakingRecordingUploadNotFoundException>(() =>
            env.Complete(put.UploadId, Truthful.Length, declared));

        // The late complete's bytes were unfiled — they go too.
        Assert.False(env.Blobs.Exists(SpeakingRecordingKey.Prefix + put.RecordingId));
        Assert.Equal(
            SpeakingRecordingStatus.Abandoned,
            env.Metadata.AllRows.Single(r => r.UploadId == put.UploadId).Status);
    }
}

/// <summary>The presigned flow against a blob store that keeps real bytes.</summary>
internal sealed class VerificationEnv
{
    public FakeExamCatalogue Catalogue { get; }
    public FakeSessionRepository Sessions { get; } = new();
    public RecordingMetadataStoreFake Metadata { get; } = new();
    public RealBlobs Blobs { get; } = new();
    public ClosingAnswers Answers { get; } = new();
    public FakeRecordingStore Recordings { get; } = new();
    public MovableClock Clock { get; } = new(SpeakingFixtures.T0);

    public InitSpeakingRecording InitHandler { get; }
    public CompleteSpeakingRecording CompleteHandler { get; }
    public RecordingReconciliation SweepHandler { get; }

    private VerificationEnv()
    {
        Catalogue = new FakeExamCatalogue(SpeakingVersion());
        var attempt = SectionAttempt.Rehydrate(
            ExamModule.Speaking, SpeakingFixtures.T0, null, null, 0, null, null, null);
        var session = ExamSession.Rehydrate(
            SpeakingFixtures.Sitting, SpeakingFixtures.Owner,
            Catalogue.ListAllAsync(default).GetAwaiter().GetResult()[0].Id,
            SessionMode.Single, SessionStatus.InProgress, SpeakingFixtures.T0, null, [attempt],
            SessionTiming.OpenEnded);
        Sessions.AddAsync(session, default).GetAwaiter().GetResult();

        InitHandler = new InitSpeakingRecording(
            Catalogue, Sessions, Blobs, Metadata, new ObjectStorageSpeakingOptions(), Clock);
        CompleteHandler = new CompleteSpeakingRecording(
            Catalogue, Sessions, Answers, Blobs, Metadata, Recordings, Clock);
        SweepHandler = new RecordingReconciliation(
            Recordings, Answers, Metadata, Blobs, new AbortStaleSpeakingUploads(Metadata, Blobs, Clock), Clock);
    }

    public static VerificationEnv Create() => new();

    public UserId OwnerId => SpeakingFixtures.Owner;
    public ExamSessionId SessionId => SpeakingFixtures.Sitting;
    public string QuestionId => SpeakingFixtures.QuestionId;
    public string? SheetValue(string questionId) => Answers.Saved.GetValueOrDefault(questionId);

    /// <summary>Sweeps with the six-hour default age bound, after advancing the clock.</summary>
    public Task<ReconciliationReport> Sweep()
    {
        Clock.Advance(TimeSpan.FromHours(7));
        return SweepHandler.SweepAsync(TimeSpan.FromHours(6), 100, default);
    }

    public async Task<InitSpeakingRecordingResult> Init(
        string contentType, string checksum, long? size = null)
    {
        var bytes = size ?? 4;
        return await InitHandler.HandleAsync(
            new InitSpeakingRecordingCommand(
                SpeakingFixtures.Owner, SpeakingFixtures.Sitting, SpeakingFixtures.QuestionId,
                contentType, bytes, checksum),
            default);
    }

    public async Task<string> Complete(string uploadId, long size, string checksum) =>
        await CompleteHandler.HandleAsync(
            new CompleteSpeakingRecordingCommand(
                SpeakingFixtures.Owner, SpeakingFixtures.Sitting, uploadId, size, checksum), default);

    private static ExamVersion SpeakingVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Speaking verification", ExamVariant.Academic, scoring, timing,
            [
                new Section(ExamModule.Speaking, 1,
                [
                    new SectionPart(
                        1, "speaking", "Part 1", "Prompt", null, null, null, null, 1, null, null,
                        [new Question(
                            SpeakingFixtures.QuestionId, 1, QuestionType.SpeakingResponse, "Prompt",
                            [], null, null)]),
                ]),
            ]);
        version.Publish(SpeakingFixtures.T0.AddDays(-1));
        return version;
    }
}

/// <summary>
/// Metadata store with race hooks: direct status writes model a crash, and
/// RacerAbandonsOnUpdate models the stale-abort pass arriving between the
/// handler's read and its write.
/// </summary>
internal sealed class RecordingMetadataStoreFake : ISpeakingRecordingMetadataStore
{
    public List<SpeakingRecordingMetadata> AllRows { get; } = [];
    public bool RacerAbandonsOnUpdate { get; set; }

    private List<SpeakingRecordingMetadata> Rows => AllRows;

    public Task InsertAsync(SpeakingRecordingMetadata metadata, CancellationToken ct)
    {
        Rows.Add(metadata);
        return Task.CompletedTask;
    }

    public Task<SpeakingRecordingMetadata?> FindAsync(string uploadId, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.UploadId == uploadId));

    public Task MarkAbandonedForQuestionAsync(
        ExamSessionId sessionId, string questionId, CancellationToken ct) => Task.CompletedTask;

    public async Task<bool> UpdateAfterUploadAsync(
        string uploadId, long sizeBytes, string checksumSha256, CancellationToken ct)
    {
        if (RacerAbandonsOnUpdate)
        {
            // The racer wins its own transition: the row is Abandoned when the
            // handler's update lands, exactly what the real conditional filter
            // sees.
            Replace(uploadId, r => r with { Status = SpeakingRecordingStatus.Abandoned });
            return false;
        }
        Replace(uploadId, r => r with
        {
            Status = SpeakingRecordingStatus.Uploaded,
            ActualSizeBytes = sizeBytes,
            ActualChecksumSha256 = checksumSha256,
        });
        return true;
    }

    public async Task<bool> MarkLinkedAsync(string uploadId, DateTimeOffset at, CancellationToken ct)
    {
        if (RacerAbandonsOnUpdate) return false;
        Replace(uploadId, r => r with { Status = SpeakingRecordingStatus.Linked, LinkedAt = at });
        return true;
    }

    public async Task<bool> MarkAbandonedAsync(string uploadId, CancellationToken ct)
    {
        Replace(uploadId, r => r with { Status = SpeakingRecordingStatus.Abandoned });
        return true;
    }

    private void Replace(string uploadId, Func<SpeakingRecordingMetadata, SpeakingRecordingMetadata> change)
    {
        var index = Rows.FindIndex(r => r.UploadId == uploadId);
        if (index >= 0) Rows[index] = change(Rows[index]);
    }

    public Task<IReadOnlyList<SpeakingRecordingMetadata>> ListPendingOlderThanAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SpeakingRecordingMetadata>>(
            [.. Rows.Where(r => r.Status == SpeakingRecordingStatus.PendingUpload && r.CreatedAt < olderThan)
                .OrderBy(r => r.CreatedAt).Take(limit)]);

    public Task<IReadOnlyList<SpeakingRecordingMetadata>> ListBySessionAsync(
        ExamSessionId sessionId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SpeakingRecordingMetadata>>(
            [.. Rows.Where(r => r.SessionId == sessionId)]);

    public Task<IReadOnlyList<SpeakingRecordingMetadata>> ListByOwnerAsync(
        UserId ownerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SpeakingRecordingMetadata>>(
            [.. Rows.Where(r => r.OwnerId == ownerId)]);

    public Task DeleteAsync(string uploadId, CancellationToken ct)
    {
        Rows.RemoveAll(r => r.UploadId == uploadId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SpeakingRecordingMetadata>> ListOlderThanAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SpeakingRecordingMetadata>>([]);

    public Task<IReadOnlyList<SpeakingRecordingMetadata>> ListOlderThanSweepableAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct, StoredRecording? after) =>
        Task.FromResult<IReadOnlyList<SpeakingRecordingMetadata>>(
            [.. Rows
                .Where(r => r.Status is SpeakingRecordingStatus.Linked or SpeakingRecordingStatus.Uploaded
                    && r.CreatedAt < olderThan)
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.RecordingId, StringComparer.Ordinal)
                .Skip(after is null ? 0 : 1)
                .Take(limit)]);

    public void SetStatusDirect(string uploadId, SpeakingRecordingStatus status)
    {
        var index = Rows.FindIndex(r => r.UploadId == uploadId);
        if (index >= 0) Rows[index] = Rows[index] with { Status = status };
    }
}

/// <summary>
/// A blob store that keeps the <i>bytes</i> and echoes the client-declared
/// metadata - the shape that hides a swapped payload from the old complete
/// check. OpenAsync is what the server-side hash reads through.
/// </summary>
internal sealed class RealBlobs : ISpeakingRecordingBlobStore
{
    private readonly Dictionary<string, (byte[] Bytes, string ContentType, string DeclaredChecksum)> _objects = [];
    private readonly HashSet<string> _deleted = [];

    public bool IsConfigured => true;

    public void Store(string objectKey, byte[] bytes, string contentType, string declaredChecksum) =>
        _objects[objectKey] = (bytes, contentType, declaredChecksum);

    public bool Exists(string objectKey) => _objects.ContainsKey(objectKey);
    public IReadOnlyCollection<string> Deleted => _deleted;

    public Uri CreatePresignedPutUrl(
        string objectKey, string contentType, string checksumSha256, TimeSpan ttl) =>
        new($"https://uploads.example/{objectKey}");

    public Uri CreatePresignedGetUrl(string objectKey, TimeSpan ttl) =>
        new($"https://cdn.example/{objectKey}");

    public Task<SpeakingRecordingObjectHead?> HeadAsync(string objectKey, CancellationToken ct) =>
        Task.FromResult<SpeakingRecordingObjectHead?>(
            _objects.TryGetValue(objectKey, out var stored)
                ? new SpeakingRecordingObjectHead(stored.Bytes.Length, stored.ContentType, stored.DeclaredChecksum)
                : null);

    public Task PutAsync(
        string objectKey, Stream content, string contentType, string checksumSha256,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        _objects[objectKey] = (buffer.ToArray(), contentType, checksumSha256);
        return Task.CompletedTask;
    }

    public Task<SpeakingRecordingObject?> OpenAsync(string objectKey, CancellationToken ct) =>
        Task.FromResult<SpeakingRecordingObject?>(
            _objects.TryGetValue(objectKey, out var stored)
                ? new SpeakingRecordingObject(
                    new MemoryStream(stored.Bytes, writable: false), stored.ContentType, stored.Bytes.Length)
                : null);

    public Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        _deleted.Add(objectKey);
        _objects.Remove(objectKey);
        return Task.CompletedTask;
    }
}




