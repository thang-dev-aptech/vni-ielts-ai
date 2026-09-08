using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// `S2b` — a short-lived, read-only URL to play one Speaking recording back.
/// Upload had this from the start (`CreatePresignedPutUrl`); listening back
/// never did until this slice.
/// </summary>
public sealed class SpeakingRecordingPlaybackTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Owner = UserId.New();
    private static readonly ExamSessionId Sitting = ExamSessionId.New();
    private const string QuestionId = "s-part-1";

    [Fact]
    public async Task Happy_path_returns_a_presigned_url_for_the_linked_recording()
    {
        var env = Env.Create(linked: true);

        var result = await env.Handler.HandleAsync(
            new GetSpeakingRecordingPlaybackCommand(Owner, Sitting, QuestionId), default);

        Assert.Equal($"https://cdn.example/{env.ObjectKey}", result.Url.ToString());
        Assert.Equal(T0.AddMinutes(5), result.ExpiresAt);
    }

    /// <summary>
    /// 404, not 403 — the same ownership shape as every other sitting route,
    /// so another learner's valid-looking session id teaches an attacker
    /// nothing about whether it exists. → `SessionProjection.LoadOwnedAsync`
    /// </summary>
    [Fact]
    public async Task Another_learners_session_is_not_found()
    {
        var env = Env.Create(linked: true);
        var intruder = UserId.New();

        await Assert.ThrowsAsync<SessionNotFoundException>(() =>
            env.Handler.HandleAsync(
                new GetSpeakingRecordingPlaybackCommand(intruder, Sitting, QuestionId), default));
    }

    [Fact]
    public async Task A_recording_still_pending_upload_has_nothing_to_play_back()
    {
        var env = Env.Create(linked: false);

        await Assert.ThrowsAsync<SpeakingRecordingUploadNotFoundException>(() =>
            env.Handler.HandleAsync(
                new GetSpeakingRecordingPlaybackCommand(Owner, Sitting, QuestionId), default));
    }

    [Fact]
    public async Task An_unconfigured_blob_store_refuses_rather_than_reading_metadata()
    {
        var env = Env.Create(linked: true);
        env.Blobs.Configured = false;

        await Assert.ThrowsAsync<SpeakingRecordingUploadUnavailableException>(() =>
            env.Handler.HandleAsync(
                new GetSpeakingRecordingPlaybackCommand(Owner, Sitting, QuestionId), default));
    }

    // ── Fixtures ──────────────────────────────────────────────────────────

    private sealed class Env
    {
        public required GetSpeakingRecordingPlaybackUrl Handler { get; init; }
        public required FakeBlobs Blobs { get; init; }
        public required string ObjectKey { get; init; }

        public static Env Create(bool linked)
        {
            var version = SpeakingVersion();
            var session = ExamSession.Rehydrate(
                Sitting, Owner, version.Id, SessionMode.Single, SessionStatus.Submitted,
                T0, T0.AddHours(1),
                [SectionAttempt.Rehydrate(ExamModule.Speaking, T0, null, T0)],
                SessionTiming.OpenEnded);

            var catalogue = new FakeExamCatalogue(version);
            var sessions = new FakeSessionRepository();
            sessions.AddAsync(session, default).GetAwaiter().GetResult();

            var objectKey = SpeakingRecordingKey.For(Sitting, QuestionId);
            var metadata = new FakeMetadata();
            metadata.Rows.Add(new SpeakingRecordingMetadata(
                UploadId: "upload-1",
                RecordingId: objectKey[SpeakingRecordingKey.Prefix.Length..],
                ObjectKey: objectKey,
                OwnerId: Owner,
                SessionId: Sitting,
                QuestionId: QuestionId,
                ContentType: "audio/webm",
                ExpectedSizeBytes: 3,
                ExpectedChecksumSha256: "aa",
                ActualSizeBytes: linked ? 3 : null,
                ActualChecksumSha256: linked ? "aa" : null,
                Status: linked ? SpeakingRecordingStatus.Linked : SpeakingRecordingStatus.PendingUpload,
                CreatedAt: T0,
                RetentionExpiresAt: null,
                LinkedAt: linked ? T0 : null));

            var blobs = new FakeBlobs();
            var clock = new MovableClock(T0);

            return new Env
            {
                Handler = new GetSpeakingRecordingPlaybackUrl(catalogue, sessions, blobs, metadata, clock),
                Blobs = blobs,
                ObjectKey = objectKey,
            };
        }
    }

    private static ExamVersion SpeakingVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Speaking playback", ExamVariant.Academic, scoring, timing,
            [
                new Section(ExamModule.Speaking, 1,
                [
                    new SectionPart(
                        1, "speaking", "Part 1", "Prompt", null, null, null, null, 1, null, null,
                        [new Question(QuestionId, 1, QuestionType.SpeakingResponse, "Prompt", [], null, null)]),
                ]),
            ]);
        version.Publish(T0.AddDays(-1));
        return version;
    }

    private sealed class FakeBlobs : ISpeakingRecordingBlobStore
    {
        public bool Configured { get; set; } = true;
        public bool IsConfigured => Configured;

        public Uri CreatePresignedPutUrl(
            string objectKey, string contentType, string checksumSha256, TimeSpan ttl) =>
            throw new NotSupportedException("Playback never puts.");

        public Uri CreatePresignedGetUrl(string objectKey, TimeSpan ttl) =>
            new($"https://cdn.example/{objectKey}");

        public Task<SpeakingRecordingObjectHead?> HeadAsync(string objectKey, CancellationToken ct) =>
            throw new NotSupportedException("Playback never heads.");

        public Task PutAsync(
            string objectKey, Stream content, string contentType, string checksumSha256,
            CancellationToken ct) =>
            throw new NotSupportedException("Playback never puts.");

        public Task DeleteAsync(string objectKey, CancellationToken ct) =>
            throw new NotSupportedException("Playback never deletes.");
    }

    private sealed class FakeMetadata : ISpeakingRecordingMetadataStore
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
            ExamSessionId sessionId, string questionId, CancellationToken ct) => Task.CompletedTask;

        public Task UpdateAfterUploadAsync(
            string uploadId, long sizeBytes, string checksumSha256, CancellationToken ct) =>
            Task.CompletedTask;

        public Task MarkLinkedAsync(string uploadId, DateTimeOffset at, CancellationToken ct) =>
            Task.CompletedTask;

        public Task MarkAbandonedAsync(string uploadId, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<SpeakingRecordingMetadata>> ListPendingOlderThanAsync(
            DateTimeOffset olderThan, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SpeakingRecordingMetadata>>([]);

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
    }
}
