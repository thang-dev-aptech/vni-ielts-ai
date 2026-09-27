using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Tests.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>Shared fixtures for the Speaking take-isolation tests.</summary>
internal static class SpeakingFixtures
{
    public static readonly DateTimeOffset T0 = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    public static readonly UserId Owner = UserId.New();
    public static readonly ExamSessionId Sitting = ExamSessionId.New();
    public const string QuestionId = "s-part-1";
}

/// <summary>
/// Take isolation — one Speaking upload owns one storage key.
///
/// <para>
/// <b>The bug these tests hold shut.</b> The object key was derived from
/// session + question alone, so every take at a question wrote to the same
/// bytes. Two confirmed consequences: a presigned PUT whose window outlived
/// the section could <i>replace the audio of an answer already filed</i> (the
/// server refuses the late complete, but the bytes had already changed), and
/// the refused-take cleanup deleted by the id the frozen answer also held,
/// leaving the sheet pointing at audio that no longer exists.
/// </para>
///
/// <para>
/// <b>The fix these tests demand.</b> Each upload files under its own key
/// (<c>recordings/&lt;question-hash&gt;/&lt;uploadId&gt;</c>), the answer
/// sheet is pointed at that take's id on complete, playback resolves through
/// the sheet rather than through any linked row, and the presigned window
/// never outlives the section deadline.
/// </para>
/// </summary>
public sealed class SpeakingTakeIsolationTests
{
    [Fact]
    public async Task Each_presigned_take_gets_its_own_object_key_and_id()
    {
        var env = Env.Create(deadline: null);
        var first = await env.Init("audio/webm");
        var second = await env.Init("audio/webm");

        // The ids come from the two results themselves — the first row was
        // abandoned by the second init, which is expected.
        Assert.NotEqual(first.RecordingId, second.RecordingId);

        var rows = env.Metadata.AllRows;
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.ObjectKey).Distinct().Count()); // was one shared key
    }

    [Fact]
    public async Task The_presigned_url_cannot_outlive_the_section_deadline()
    {
        var env = Env.Create(deadline: SpeakingFixtures.T0.AddMinutes(5));

        var result = await env.Init("audio/webm");

        Assert.True(
            result.ExpiresAt <= SpeakingFixtures.T0.AddMinutes(5),
            $"The upload window ({result.ExpiresAt - SpeakingFixtures.T0}) must not exceed the section deadline (5 min).");
    }

    [Fact]
    public async Task An_open_ended_sitting_keeps_the_full_fifteen_minute_window()
    {
        var env = Env.Create(deadline: null);

        var result = await env.Init("audio/webm");

        Assert.Equal(SpeakingFixtures.T0.AddMinutes(15), result.ExpiresAt);
    }

    [Fact]
    public async Task A_refused_retake_deletes_only_its_own_bytes_and_never_the_filed_take()
    {
        var env = Env.Create(deadline: null);

        // Take one is completed and filed; the sheet carries its id.
        var first = await env.Init("audio/webm");
        await env.Complete(first.UploadId, 3, Env.Checksum);
        var filedId = env.SheetValue(SpeakingFixtures.QuestionId);

        // The section freezes behind the handler's back — the retake that was
        // already open now arrives at a closed sheet.
        env.Answers.Closed = true;
        var second = await env.Init("audio/webm");

        await Assert.ThrowsAsync<SectionSheetClosedException>(() =>
            env.Complete(second.UploadId, 3, Env.Checksum));

        // <b>The property: the cleanup can only reach the refused take.</b>
        // Under the shared derived key this same call deleted the id the
        // frozen answer held. Per-upload ids mean the two differ, so the
        // filed take's audio survives the refused take's tidy-up. (The
        // bytes themselves live in the blob store in the presigned flow;
        // the recording store's delete list is the observable here.)
        Assert.NotEqual(filedId, second.RecordingId);
        Assert.Contains(second.RecordingId, env.Recordings.Deleted);
        Assert.DoesNotContain(filedId, env.Recordings.Deleted);
    }

    [Fact]
    public async Task Playback_resolves_through_the_answer_sheet_when_two_takes_are_linked()
    {
        var env = Env.Create(deadline: null);

        var first = await env.Init("audio/webm");
        await env.Complete(first.UploadId, 3, Env.Checksum);

        // A second take files after the first — both rows are Linked, and the
        // sheet points at the second. Playback must follow the sheet.
        var second = await env.Init("audio/webm");
        await env.Complete(second.UploadId, 3, Env.Checksum);

        var handler = new GetSpeakingRecordingPlaybackUrl(
            env.Catalogue, env.Sessions, env.Blobs, env.Metadata, env.Answers, env.Clock);
        var result = await handler.HandleAsync(
            new GetSpeakingRecordingPlaybackCommand(
                SpeakingFixtures.Owner, SpeakingFixtures.Sitting, SpeakingFixtures.QuestionId), default);

        var current = env.Metadata.AllRows.Single(
            r => r.RecordingId == env.SheetValue(SpeakingFixtures.QuestionId));
        Assert.Equal($"https://cdn.example/{current.ObjectKey}", result.Url.ToString());
    }

    [Fact]
    public void The_key_shape_stays_bounded()
    {
        var sessionId = ExamSessionId.New();
        var uploadId = Guid.NewGuid().ToString("N");

        var key = SpeakingRecordingKey.ForObject(sessionId, SpeakingFixtures.QuestionId, uploadId);

        Assert.True(SpeakingRecordingKey.IsValidObjectKey(key), "A per-upload key is valid.");
        Assert.True(
            SpeakingRecordingKey.IsValidObjectKey(
                SpeakingRecordingKey.For(sessionId, SpeakingFixtures.QuestionId)),
            "The legacy single-segment key stays valid — old rows keep deleting.");

        Assert.False(SpeakingRecordingKey.IsValidObjectKey("recordings/../../etc"));
        Assert.False(SpeakingRecordingKey.IsValidObjectKey("recordings/short"));
        Assert.False(SpeakingRecordingKey.IsValidObjectKey("assets/elsewhere"));
    }
}

/// <summary>The whole Speaking upload surface, wired to in-memory fakes.</summary>
internal sealed class Env
{
    public FakeExamCatalogue Catalogue { get; } = new(SpeakingVersion());
    public FakeSessionRepository Sessions { get; } = new();
    public TakeIsolationMetadata Metadata { get; } = new();
    public TakeIsolationBlobs Blobs { get; } = new();
    public ClosingAnswers Answers { get; } = new();
    public FakeRecordingStore Recordings { get; } = new();
    public MovableClock Clock { get; } = new(SpeakingFixtures.T0);

    public InitSpeakingRecording InitHandler { get; }
    public CompleteSpeakingRecording CompleteHandler { get; }

    private Env(DateTimeOffset? deadline)
    {
        var attempt = SectionAttempt.Rehydrate(
            ExamModule.Speaking, SpeakingFixtures.T0, deadline, null, 0, null, null, null);
        var session = ExamSession.Rehydrate(
            SpeakingFixtures.Sitting, SpeakingFixtures.Owner,
            Catalogue.ListAllAsync(default).GetAwaiter().GetResult()[0].Id,
            SessionMode.Single, SessionStatus.InProgress, SpeakingFixtures.T0, null, [attempt],
            deadline is null ? SessionTiming.OpenEnded : SessionTiming.Deadline);
        Sessions.AddAsync(session, default).GetAwaiter().GetResult();

        InitHandler = new InitSpeakingRecording(
            Catalogue, Sessions, Blobs, Metadata, new ObjectStorageSpeakingOptions(), Clock);
        CompleteHandler = new CompleteSpeakingRecording(
            Catalogue, Sessions, Answers, Blobs, Metadata, Recordings, Clock);
    }

    public static Env Create(DateTimeOffset? deadline) => new(deadline);

    public async Task<InitSpeakingRecordingResult> Init(string contentType)
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03 };
        return await InitHandler.HandleAsync(
            new InitSpeakingRecordingCommand(
                SpeakingFixtures.Owner, SpeakingFixtures.Sitting, SpeakingFixtures.QuestionId,
                contentType, bytes.Length,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()),
            default);
    }

    /// <summary>Completes with the same declared values — the real flow.</summary>
    public async Task Complete(string uploadId, long size, string checksum) =>
        await CompleteHandler.HandleAsync(
            new CompleteSpeakingRecordingCommand(
                SpeakingFixtures.Owner, SpeakingFixtures.Sitting, uploadId, size, checksum), default);

    /// <summary>The declared checksum for a take — 64-hex SHA-256 of the test bytes.</summary>
    public static string Checksum =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([0x01, 0x02, 0x03]))
            .ToLowerInvariant();

    public string? SheetValue(string questionId) => Answers.Saved.GetValueOrDefault(questionId);

    private static ExamVersion SpeakingVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Speaking take isolation", ExamVariant.Academic, scoring, timing,
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

    public ExamSession PreparedSession(DateTimeOffset? deadline)
    {
        var attempt = SectionAttempt.Rehydrate(
            ExamModule.Speaking, SpeakingFixtures.T0, deadline, null, 0, null, null, null);
        return ExamSession.Rehydrate(
            SpeakingFixtures.Sitting, SpeakingFixtures.Owner,
            Catalogue.ListAllAsync(default).GetAwaiter().GetResult()[0].Id,
            SessionMode.Single, SessionStatus.InProgress, SpeakingFixtures.T0, null, [attempt],
            deadline is null ? SessionTiming.OpenEnded : SessionTiming.Deadline);
    }
}

internal sealed class TakeIsolationMetadata : ISpeakingRecordingMetadataStore
{
    public List<SpeakingRecordingMetadata> Rows { get; } = [];

    public IReadOnlyList<SpeakingRecordingMetadata> AllRows => Rows;

    public IReadOnlyList<SpeakingRecordingMetadata> AllPending(ExamSessionId sessionId) =>
        [.. Rows.Where(r => r.SessionId == sessionId && r.Status == SpeakingRecordingStatus.PendingUpload)];

    public Task InsertAsync(SpeakingRecordingMetadata metadata, CancellationToken ct)
    {
        Rows.Add(metadata);
        return Task.CompletedTask;
    }

    public Task<SpeakingRecordingMetadata?> FindAsync(string uploadId, CancellationToken ct) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.UploadId == uploadId));

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

    public Task MarkLinkedAsync(string uploadId, DateTimeOffset at, CancellationToken ct)
    {
        Replace(uploadId, r => r with { Status = SpeakingRecordingStatus.Linked, LinkedAt = at });
        return Task.CompletedTask;
    }

    public Task MarkAbandonedAsync(string uploadId, CancellationToken ct)
    {
        Replace(uploadId, r => r with { Status = SpeakingRecordingStatus.Abandoned });
        return Task.CompletedTask;
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
}

internal sealed class TakeIsolationBlobs : ISpeakingRecordingBlobStore
{
    public bool IsConfigured => true;

    /// <summary>
    /// The checksum each presign declared — echoed back by HEAD, the way the
    /// object metadata echoes <c>x-amz-meta-sha256</c> in the real store.
    /// </summary>
    private readonly Dictionary<string, string> _declared = [];

    public Uri CreatePresignedPutUrl(
        string objectKey, string contentType, string checksumSha256, TimeSpan ttl)
    {
        _declared[objectKey] = checksumSha256;
        return new Uri($"https://uploads.example/{objectKey}");
    }

    public Uri CreatePresignedGetUrl(string objectKey, TimeSpan ttl) =>
        new($"https://cdn.example/{objectKey}");

    public Task<SpeakingRecordingObjectHead?> HeadAsync(
        string objectKey, CancellationToken ct) =>
        Task.FromResult<SpeakingRecordingObjectHead?>(
            _declared.TryGetValue(objectKey, out var checksum)
                ? new SpeakingRecordingObjectHead(3, "audio/webm", checksum)
                : null);

    public Task PutAsync(
        string objectKey, Stream content, string contentType, string checksumSha256,
        CancellationToken ct) => Task.CompletedTask;

    public Task DeleteAsync(string objectKey, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// The answer sheet with a freeze switch — the race the audit describes,
/// modelled: the sheet closes between the complete handler's gate check and
/// its write.
/// </summary>
internal sealed class ClosingAnswers : IAnswerSheetStore
{
    public bool Closed { get; set; }
    public Dictionary<string, string?> Saved { get; } = [];

    public Task<IReadOnlyDictionary<string, string?>> LoadAsync(
        ExamSessionId sessionId, ExamModule module, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, string?>>(Saved);

    public Task<AnswerSheet> ReadAsync(
        ExamSessionId sessionId, ExamModule module, CancellationToken ct) =>
        Task.FromResult(new AnswerSheet(Saved, 1));

    public Task SetAnswerAsync(
        ExamSessionId sessionId, ExamModule module, string questionId, string? value,
        DateTimeOffset at, CancellationToken ct)
    {
        if (Closed) throw new SectionSheetClosedException(module);
        Saved[questionId] = value;
        return Task.CompletedTask;
    }

    public Task<PatchedSheet> PatchAsync(
        ExamSessionId sessionId, ExamModule module,
        IReadOnlyDictionary<string, string?> changes,
        DateTimeOffset at, CancellationToken ct,
        IReadOnlyDictionary<string, long>? sequences = null)
    {
        if (Closed) throw new SectionSheetClosedException(module);
        foreach (var (question, value) in changes) Saved[question] = value;
        return Task.FromResult(new PatchedSheet(new AnswerSheet(Saved, 1), 0));
    }

    public Task<AnswerSheet> CloseAsync(
        ExamSessionId sessionId, ExamModule module, DateTimeOffset at, CancellationToken ct)
    {
        Closed = true;
        return Task.FromResult(new AnswerSheet(Saved, 1));
    }
}


