using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Tests.Assessment;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// FS8.3 / FS8.6 / FS8.7 — stale abort, re-record replacement, purge, audit URL
/// refusal, and no-voice result state.
/// </summary>
public sealed class SpeakingRecordingRemainderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly UserId Owner = UserId.New();
    private static readonly ExamSessionId Sitting = ExamSessionId.New();

    [Fact]
    public async Task Stale_pending_upload_is_abandoned_and_object_removed()
    {
        var metadata = new FakeSpeakingMetadataStore();
        var blobs = new FakeSpeakingBlobStore();
        var clock = new MovableClock(T0);

        var objectKey = SpeakingRecordingKey.For(Sitting, "s-part-1");
        await blobs.PutAsync(objectKey, new MemoryStream([1, 2, 3]), "audio/webm", "aa", default);
        await metadata.InsertAsync(Row(
            "upload-stale", objectKey, SpeakingRecordingStatus.PendingUpload, T0.AddMinutes(-20)), default);

        var report = await new AbortStaleSpeakingUploads(metadata, blobs, clock)
            .HandleAsync(100, default);

        Assert.Equal(1, report.Abandoned);
        Assert.Equal(1, report.ObjectsRemoved);
        Assert.Equal(SpeakingRecordingStatus.Abandoned, metadata.Rows[0].Status);
        Assert.DoesNotContain(objectKey, blobs.Keys);
    }

    [Fact]
    public async Task Stale_abort_does_not_delete_object_still_claimed_by_linked_revision()
    {
        // Negative proof: a forgotten pending init must not erase the take the
        // learner already completed under the same derived key.
        var metadata = new FakeSpeakingMetadataStore();
        var blobs = new FakeSpeakingBlobStore();
        var clock = new MovableClock(T0);
        var objectKey = SpeakingRecordingKey.For(Sitting, "s-part-1");

        await blobs.PutAsync(objectKey, new MemoryStream([9]), "audio/webm", "bb", default);
        await metadata.InsertAsync(Row(
            "upload-old", objectKey, SpeakingRecordingStatus.PendingUpload, T0.AddMinutes(-30)), default);
        await metadata.InsertAsync(Row(
            "upload-linked", objectKey, SpeakingRecordingStatus.Linked, T0.AddMinutes(-5), linked: true), default);

        var report = await new AbortStaleSpeakingUploads(metadata, blobs, clock)
            .HandleAsync(100, default);

        Assert.Equal(1, report.Abandoned);
        Assert.Equal(0, report.ObjectsRemoved);
        Assert.Contains(objectKey, blobs.Keys);
    }

    [Fact]
    public async Task Re_record_init_abandons_prior_pending_without_orphaning_key()
    {
        var metadata = new FakeSpeakingMetadataStore();
        var blobs = new FakeSpeakingBlobStore { Configured = true };
        var clock = new MovableClock(T0);
        var version = SpeakingVersion();
        var session = OpenSpeaking(Sitting, Owner, version.Id, T0);

        var first = await metadata.InsertAndReturn(Row(
            "u1", SpeakingRecordingKey.For(Sitting, "s-part-1"),
            SpeakingRecordingStatus.PendingUpload, T0), default);

        var init = new InitSpeakingRecording(
            new Catalogue(version),
            new Sessions(session),
            blobs,
            metadata,
            new ObjectStorageSpeakingOptions(),
            clock);

        var checksum = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("x"u8.ToArray()));
        var second = await init.HandleAsync(
            new InitSpeakingRecordingCommand(Owner, Sitting, "s-part-1", "audio/webm", 1, checksum),
            default);

        Assert.Equal("presigned", second.UploadMode);
        Assert.Equal(SpeakingRecordingStatus.Abandoned, metadata.Find(first.UploadId)!.Status);
        Assert.Equal(first.ObjectKey, metadata.Find(second.UploadId)!.ObjectKey);
    }

    [Fact]
    public async Task Init_over_threshold_directs_client_to_multipart_path()
    {
        var metadata = new FakeSpeakingMetadataStore();
        var blobs = new FakeSpeakingBlobStore { Configured = true };
        var version = SpeakingVersion();
        var session = OpenSpeaking(Sitting, Owner, version.Id, T0);
        var checksum = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(new byte[8]));

        var result = await new InitSpeakingRecording(
                new Catalogue(version), new Sessions(session), blobs, metadata,
                new ObjectStorageSpeakingOptions(), new MovableClock(T0))
            .HandleAsync(
                new InitSpeakingRecordingCommand(
                    Owner, Sitting, "s-part-1", "audio/webm",
                    InitSpeakingRecording.MultipartThresholdBytes + 1, checksum),
                default);

        Assert.Equal("multipart", result.UploadMode);
        Assert.Null(result.UploadUrl);
        Assert.Empty(metadata.Rows);
    }

    [Fact]
    public async Task Purge_for_session_removes_object_and_metadata()
    {
        var metadata = new FakeSpeakingMetadataStore();
        var recordings = new FakeRecordingStore();
        var blobs = new FakeSpeakingBlobStore { Configured = true };
        var audit = new FakeAuditLog();
        var objectKey = SpeakingRecordingKey.For(Sitting, "s-part-1");
        var recordingId = SpeakingRecordingKey.RecordingIdFor(Sitting, "s-part-1");

        await blobs.PutAsync(objectKey, new MemoryStream([1]), "audio/webm", "cc", default);
        await recordings.SaveAsync(Sitting, "s-part-1", new MemoryStream([1]), "audio/webm", default);
        // Align fake id with derived key for purge assertions.
        recordings.Saved[0] = recordings.Saved[0] with { Id = recordingId };
        await metadata.InsertAsync(Row(
            "u-linked", objectKey, SpeakingRecordingStatus.Linked, T0, linked: true), default);

        var report = await new PurgeSpeakingRecordings(
                metadata, recordings, blobs, audit, new MovableClock(T0))
            .ForSessionAsync(Sitting, Owner, "admin@example.com", default);

        Assert.Equal(1, report.Removed);
        Assert.Empty(metadata.Rows);
        Assert.DoesNotContain(objectKey, blobs.Keys);
        Assert.Contains(recordingId, recordings.Deleted);
        Assert.Equal(AuditAction.SpeakingRecordingPurged, Assert.Single(audit.Entries).Action);
        Assert.All(audit.Entries.Single().Detail.Values, v => Assert.False(SpeakingAuditDetail.LooksLikeAudioUrl(v)));
    }

    [Fact]
    public void Audit_detail_rejects_long_lived_audio_urls()
    {
        Assert.Throws<ArgumentException>(() =>
            SpeakingAuditDetail.RejectLongLivedAudioUrls(new Dictionary<string, string>
            {
                ["url"] = "https://bucket.example/recordings/abc?X-Amz-Signature=deadbeef",
            }));
    }

    [Fact]
    public void Retention_days_null_is_the_stated_non_destructive_default()
    {
        // G-11: no invented retention that quietly destroys learner voice.
        Assert.Null(new ObjectStorageSpeakingOptions().RetentionDays);
    }

    [Fact]
    public async Task Recording_complete_without_asr_is_awaiting_voice_provider()
    {
        var store = new FakeMarkingStore();
        var outcomes = await new SectionMarkingRunner(
                new FakeRubricSource(SpeakingRubric()),
                [],
                store,
                new FakeTranscriptSource(null))
            .RunAsync(
                SpeakingVersion(),
                ExamModule.Speaking,
                Sitting,
                new FakeAnswerSheetStore(new Dictionary<ExamModule, Dictionary<string, string?>>
                {
                    [ExamModule.Speaking] = new() { ["s-part-1"] = "rec-abc" },
                }),
                default);

        var only = Assert.Single(outcomes);
        Assert.Equal(MarkingAvailability.AwaitingVoiceProvider, only.Availability);
        Assert.Null(only.Marking);
    }

    [Fact]
    public void Results_view_names_awaiting_voice_provider_and_keeps_overall_null()
    {
        var version = SpeakingVersion();
        var session = ExamSession.Rehydrate(
            Sitting, Owner, version.Id, SessionMode.Single, SessionStatus.Submitted,
            T0, T0.AddHours(1),
            [SectionAttempt.Rehydrate(ExamModule.Speaking, T0, null, T0)],
            SessionTiming.OpenEnded);

        var job = new MarkingJob(
            "op-1",
            Sitting,
            ExamModule.Speaking,
            "ielts-speaking-2023.1",
            MarkingJobState.Failed,
            5,
            T0,
            null,
            null,
            null,
            nameof(MarkingAvailability.AwaitingVoiceProvider),
            T0);

        var results = session.ToResults(version, [], [], [job]);

        Assert.Null(results.OverallBand);
        var status = Assert.Single(results.MarkingStatuses);
        Assert.Equal("speaking", status.Module);
        Assert.Equal(nameof(MarkingAvailability.AwaitingVoiceProvider), status.Code);
        Assert.Contains("ASR", status.Reason!, StringComparison.Ordinal);
    }

    private static SpeakingRecordingMetadata Row(
        string uploadId, string objectKey, SpeakingRecordingStatus status,
        DateTimeOffset created, bool linked = false) =>
        new(
            uploadId,
            objectKey[SpeakingRecordingKey.Prefix.Length..],
            objectKey,
            Owner,
            Sitting,
            "s-part-1",
            "audio/webm",
            3,
            "aa",
            linked ? 3 : null,
            linked ? "aa" : null,
            status,
            created,
            null,
            linked ? created : null);

    private static ExamVersion SpeakingVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Speaking remainder", ExamVariant.Academic, scoring, timing,
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

    private static Domain.Assessment.Rubric SpeakingRubric() =>
        Domain.Assessment.Rubric.Create(
            "ielts-speaking-2023.1", ExamModule.Speaking,
            Domain.Assessment.CriterionKeys.Speaking, "IELTS descriptors");

    private static ExamSession OpenSpeaking(
        ExamSessionId id, UserId user, ExamVersionId versionId, DateTimeOffset at) =>
        ExamSession.Rehydrate(
            id, user, versionId, SessionMode.Single, SessionStatus.InProgress, at, null,
            [SectionAttempt.OpenEnded(ExamModule.Speaking, at, null, null)],
            SessionTiming.OpenEnded);

    private sealed class Catalogue(ExamVersion version) : IExamCatalogue
    {
        public Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([version]);
        public Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([version]);
        public Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct) =>
            Task.FromResult(id == version.Id ? version : null);
        public Task UpsertAsync(ExamVersion version, CancellationToken ct) => Task.CompletedTask;
        public Task SetStatusAsync(ExamVersionId id, ExamVersionStatus status, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Sessions(ExamSession session) : IExamSessionRepository
    {
        public Task<ExamSession?> FindAsync(ExamSessionId id, CancellationToken ct) =>
            Task.FromResult(id == session.Id ? session : null);
        public Task<ExamSession?> FindOpenForUserAsync(UserId userId, CancellationToken ct) =>
            Task.FromResult(userId == session.UserId ? session : null);
        public Task<IReadOnlyList<ExamSession>> ListForUserAsync(
            UserId userId, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamSession>>(userId == session.UserId ? [session] : []);
        public Task AddAsync(ExamSession session, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> TrySaveAsync(ExamSession session, SessionState from, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class FakeSpeakingMetadataStore : ISpeakingRecordingMetadataStore
    {
        public List<SpeakingRecordingMetadata> Rows { get; } = [];

        public Task InsertAsync(SpeakingRecordingMetadata metadata, CancellationToken ct)
        {
            Rows.Add(metadata);
            return Task.CompletedTask;
        }

        public async Task<SpeakingRecordingMetadata> InsertAndReturn(
            SpeakingRecordingMetadata metadata, CancellationToken ct)
        {
            await InsertAsync(metadata, ct);
            return metadata;
        }

        public SpeakingRecordingMetadata? Find(string uploadId) =>
            Rows.FirstOrDefault(r => r.UploadId == uploadId);

        public Task<SpeakingRecordingMetadata?> FindAsync(string uploadId, CancellationToken ct) =>
            Task.FromResult(Find(uploadId));

        public Task MarkAbandonedForQuestionAsync(
            ExamSessionId sessionId, string questionId, CancellationToken ct)
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].SessionId == sessionId
                    && Rows[i].QuestionId == questionId
                    && Rows[i].Status == SpeakingRecordingStatus.PendingUpload)
                {
                    Rows[i] = Rows[i] with { Status = SpeakingRecordingStatus.Abandoned };
                }
            }

            return Task.CompletedTask;
        }

        public Task UpdateAfterUploadAsync(
            string uploadId, long sizeBytes, string checksumSha256, CancellationToken ct) =>
            Task.CompletedTask;

        public Task MarkLinkedAsync(string uploadId, DateTimeOffset at, CancellationToken ct) =>
            Task.CompletedTask;

        public Task MarkAbandonedAsync(string uploadId, CancellationToken ct)
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].UploadId == uploadId)
                    Rows[i] = Rows[i] with { Status = SpeakingRecordingStatus.Abandoned };
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SpeakingRecordingMetadata>> ListPendingOlderThanAsync(
            DateTimeOffset olderThan, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SpeakingRecordingMetadata>>(
                [.. Rows
                    .Where(r => r.Status == SpeakingRecordingStatus.PendingUpload
                        && r.CreatedAt < olderThan)
                    .Take(limit)]);

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
            Task.FromResult<IReadOnlyList<SpeakingRecordingMetadata>>(
                [.. Rows.Where(r => r.CreatedAt < olderThan).Take(limit)]);
    }

    private sealed class FakeSpeakingBlobStore : ISpeakingRecordingBlobStore
    {
        public bool Configured { get; set; } = true;
        public bool IsConfigured => Configured;
        public HashSet<string> Keys { get; } = [];

        public Uri CreatePresignedPutUrl(
            string objectKey, string contentType, string checksumSha256, TimeSpan ttl)
        {
            Keys.Add(objectKey);
            return new Uri("http://localhost:9000/" + objectKey);
        }

        public Task<SpeakingRecordingObjectHead?> HeadAsync(string objectKey, CancellationToken ct) =>
            Task.FromResult<SpeakingRecordingObjectHead?>(
                Keys.Contains(objectKey) ? new SpeakingRecordingObjectHead(1, "audio/webm", "aa") : null);

        public Task PutAsync(
            string objectKey, Stream content, string contentType, string checksumSha256,
            CancellationToken ct)
        {
            Keys.Add(objectKey);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string objectKey, CancellationToken ct)
        {
            Keys.Remove(objectKey);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditLog : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task AppendAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<AuditEntry> Entries, long Total)> ListAsync(
            string? actorId, string? action, int skip, int take, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<AuditEntry>, long)>((Entries, Entries.Count));
    }
}
