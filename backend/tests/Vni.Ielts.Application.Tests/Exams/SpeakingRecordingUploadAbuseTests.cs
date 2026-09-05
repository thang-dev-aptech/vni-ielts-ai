using System.Security.Cryptography;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// FS9.1 — upload abuse gates that must refuse before touching object storage.
/// </summary>
public sealed class SpeakingRecordingUploadAbuseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Init_rejects_oversized_recording()
    {
        var env = Env.Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            env.Init.HandleAsync(
                new InitSpeakingRecordingCommand(
                    env.UserId,
                    env.SessionId,
                    env.QuestionId,
                    "audio/webm",
                    InitSpeakingRecording.MaxRecordingBytes + 1,
                    HexChecksum("x")),
                default));

        Assert.Empty(env.Metadata.Rows);
    }

    [Fact]
    public async Task Init_rejects_non_audio_content_type()
    {
        var env = Env.Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            env.Init.HandleAsync(
                new InitSpeakingRecordingCommand(
                    env.UserId,
                    env.SessionId,
                    env.QuestionId,
                    "application/octet-stream",
                    1024,
                    HexChecksum("x")),
                default));

        Assert.Empty(env.Metadata.Rows);
    }

    [Fact]
    public async Task Init_rejects_malformed_checksum()
    {
        var env = Env.Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            env.Init.HandleAsync(
                new InitSpeakingRecordingCommand(
                    env.UserId,
                    env.SessionId,
                    env.QuestionId,
                    "audio/webm",
                    1024,
                    "not-a-sha256"),
                default));

        Assert.Empty(env.Metadata.Rows);
    }

    [Fact]
    public async Task Init_rejects_another_learners_session()
    {
        var env = Env.Create();

        await Assert.ThrowsAsync<SessionNotFoundException>(() =>
            env.Init.HandleAsync(
                new InitSpeakingRecordingCommand(
                    UserId.New(),
                    env.SessionId,
                    env.QuestionId,
                    "audio/webm",
                    1024,
                    HexChecksum("x")),
                default));

        Assert.Empty(env.Metadata.Rows);
    }

    [Fact]
    public void Derived_object_key_stays_under_recordings_prefix_without_traversal()
    {
        var key = SpeakingRecordingKey.For(ExamSessionId.New(), "../escape");

        Assert.StartsWith(SpeakingRecordingKey.Prefix, key, StringComparison.Ordinal);
        Assert.DoesNotContain("..", key, StringComparison.Ordinal);
        Assert.DoesNotContain("escape", key, StringComparison.Ordinal);
        Assert.True(SpeakingRecordingKey.IsValidObjectKey(key));
    }

    [Fact]
    public async Task Init_rejects_when_blob_store_is_unavailable()
    {
        var env = Env.Create(configured: false);

        await Assert.ThrowsAsync<SpeakingRecordingUploadUnavailableException>(() =>
            env.Init.HandleAsync(
                new InitSpeakingRecordingCommand(
                    env.UserId,
                    env.SessionId,
                    env.QuestionId,
                    "audio/webm",
                    1024,
                    HexChecksum("x")),
                default));

        Assert.Empty(env.Metadata.Rows);
    }

    [Fact]
    public async Task Complete_rejects_declared_size_mismatch()
    {
        var env = Env.Create();
        var checksum = HexChecksum("body");
        const long expected = 2048;
        var objectKey = SpeakingRecordingKey.For(env.SessionId, env.QuestionId);
        var recordingId = SpeakingRecordingKey.RecordingIdFor(env.SessionId, env.QuestionId);

        await env.Metadata.InsertAsync(
            new SpeakingRecordingMetadata(
                "upload-size",
                recordingId,
                objectKey,
                env.UserId,
                env.SessionId,
                env.QuestionId,
                "audio/webm",
                expected,
                checksum,
                null,
                null,
                SpeakingRecordingStatus.PendingUpload,
                T0,
                T0.AddDays(90),
                null),
            default);

        env.Blobs.Heads[objectKey] = new SpeakingRecordingObjectHead(expected, "audio/webm", checksum);

        await Assert.ThrowsAsync<SpeakingRecordingChecksumMismatchException>(() =>
            env.Complete.HandleAsync(
                new CompleteSpeakingRecordingCommand(
                    env.UserId,
                    env.SessionId,
                    "upload-size",
                    expected + 1,
                    checksum),
                default));
    }

    private static string HexChecksum(string seed) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)));

    private sealed class Env
    {
        public UserId UserId { get; private init; } = UserId.New();
        public ExamSessionId SessionId { get; private init; } = ExamSessionId.New();
        public string QuestionId { get; } = "s-part-1";
        public InitSpeakingRecording Init { get; private init; } = null!;
        public CompleteSpeakingRecording Complete { get; private init; } = null!;
        public FakeMetadata Metadata { get; private init; } = null!;
        public FakeBlobs Blobs { get; private init; } = null!;

        public static Env Create(bool configured = true)
        {
            var version = BuildVersion();
            var userId = UserId.New();
            var sessionId = ExamSessionId.New();
            var session = ExamSession.Rehydrate(
                sessionId,
                userId,
                version.Id,
                SessionMode.Single,
                SessionStatus.InProgress,
                T0,
                null,
                [SectionAttempt.OpenEnded(ExamModule.Speaking, T0, null, null)],
                SessionTiming.OpenEnded);

            var metadata = new FakeMetadata();
            var blobs = new FakeBlobs { Configured = configured };
            var clock = new FixedClock(T0);
            var catalogue = new Catalogue(version);
            var sessions = new Sessions(session);
            var init = new InitSpeakingRecording(
                catalogue,
                sessions,
                blobs,
                metadata,
                new ObjectStorageSpeakingOptions { RetentionDays = 90 },
                clock);
            var complete = new CompleteSpeakingRecording(
                catalogue,
                sessions,
                new Assessment.FakeAnswerSheetStore(),
                blobs,
                metadata,
                new FakeRecordingStore(),
                clock);

            return new Env
            {
                UserId = userId,
                SessionId = sessionId,
                Init = init,
                Complete = complete,
                Metadata = metadata,
                Blobs = blobs,
            };
        }
    }

    private static ExamVersion BuildVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Speaking abuse test", ExamVariant.Academic, scoring, timing,
            [
                new Section(ExamModule.Speaking, 1,
                [
                    new SectionPart(
                        1, "speaking", "Part 1", "Prompt", null, null, null, null, 1, null, null,
                        [new Question("s-part-1", 1, QuestionType.SpeakingResponse, "Prompt", [], null, null)]),
                ]),
            ]);
        version.Publish(T0.AddDays(-1));
        return version;
    }

    private sealed class Catalogue(ExamVersion version) : IExamCatalogue
    {
        public Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([version]);

        public Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([version]);

        public Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct) =>
            Task.FromResult<ExamVersion?>(id == version.Id ? version : null);

        public Task UpsertAsync(ExamVersion version, CancellationToken ct) => Task.CompletedTask;
        public Task SetStatusAsync(ExamVersionId id, ExamVersionStatus status, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Sessions(ExamSession session) : IExamSessionRepository
    {
        public Task<ExamSession?> FindAsync(ExamSessionId id, CancellationToken ct) =>
            Task.FromResult<ExamSession?>(id == session.Id ? session : null);

        public Task<ExamSession?> FindOpenForUserAsync(UserId userId, CancellationToken ct) =>
            Task.FromResult<ExamSession?>(userId == session.UserId ? session : null);

        public Task<IReadOnlyList<ExamSession>> ListForUserAsync(
            UserId userId, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamSession>>(
                userId == session.UserId ? [session] : []);

        public Task AddAsync(ExamSession session, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> TrySaveAsync(
            ExamSession session, SessionState from, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class FakeBlobs : ISpeakingRecordingBlobStore
    {
        public bool Configured { get; init; } = true;
        public bool IsConfigured => Configured;
        public Dictionary<string, SpeakingRecordingObjectHead> Heads { get; } = new();

        public Uri CreatePresignedPutUrl(
            string objectKey, string contentType, string checksumSha256, TimeSpan ttl) =>
            new($"https://storage.example.com/{objectKey}?X-Amz-Signature=should-not-be-audited");

        public Task<SpeakingRecordingObjectHead?> HeadAsync(string objectKey, CancellationToken ct) =>
            Task.FromResult(
                Heads.TryGetValue(objectKey, out var head) ? head : null);

        public Task PutAsync(
            string objectKey, Stream content, string contentType, string checksumSha256,
            CancellationToken ct) =>
            Task.CompletedTask;

        public Task DeleteAsync(string objectKey, CancellationToken ct) => Task.CompletedTask;
    }

    internal sealed class FakeMetadata : ISpeakingRecordingMetadataStore
    {
        public List<SpeakingRecordingMetadata> Rows { get; } = [];

        public Task InsertAsync(SpeakingRecordingMetadata metadata, CancellationToken ct)
        {
            Rows.Add(metadata);
            return Task.CompletedTask;
        }

        public Task<SpeakingRecordingMetadata?> FindAsync(string uploadId, CancellationToken ct) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.UploadId == uploadId));

        public Task MarkAbandonedForQuestionAsync(
            ExamSessionId sessionId, string questionId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task UpdateAfterUploadAsync(
            string uploadId, long sizeBytes, string checksumSha256, CancellationToken ct) =>
            Task.CompletedTask;

        public Task MarkLinkedAsync(string uploadId, DateTimeOffset at, CancellationToken ct) =>
            Task.CompletedTask;

        public Task MarkAbandonedAsync(string uploadId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task DeleteAsync(string uploadId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SpeakingRecordingMetadata>> ListPendingOlderThanAsync(
            DateTimeOffset olderThan, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SpeakingRecordingMetadata>>([]);

        public Task<IReadOnlyList<SpeakingRecordingMetadata>> ListBySessionAsync(
            ExamSessionId sessionId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SpeakingRecordingMetadata>>(
                Rows.Where(r => r.SessionId == sessionId).ToList());

        public Task<IReadOnlyList<SpeakingRecordingMetadata>> ListByOwnerAsync(
            UserId ownerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SpeakingRecordingMetadata>>(
                Rows.Where(r => r.OwnerId == ownerId).ToList());

        public Task<IReadOnlyList<SpeakingRecordingMetadata>> ListOlderThanAsync(
            DateTimeOffset olderThan, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SpeakingRecordingMetadata>>([]);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
