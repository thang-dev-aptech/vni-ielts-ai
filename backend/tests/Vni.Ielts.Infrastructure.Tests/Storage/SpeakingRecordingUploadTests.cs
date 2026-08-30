using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;
using Vni.Ielts.Infrastructure.Storage;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Infrastructure.Tests.Storage;

/// <summary>
/// FS8.2 — init → presigned PUT → complete → HEAD → answer sheet link against MinIO.
/// </summary>
public sealed class SpeakingRecordingUploadTests
{
    private const string SpeakingBucket = "vni-speaking";
    private static readonly DateTimeOffset T0 = new(2026, 8, 29, 9, 0, 0, TimeSpan.Zero);

    public static bool MinioAvailable => ObjectStorageProbe.MinioAvailable;
    public const string SkipReason = ObjectStorageProbe.SkipReason;

    [SkippableFact]
    public async Task Init_upload_complete_links_recording_to_answer_sheet()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        await using var env = await Env.CreateAsync();
        var bytes = Encoding.UTF8.GetBytes("speaking-audio-fixture-bytes");
        var checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var init = await env.Init.HandleAsync(
            new InitSpeakingRecordingCommand(
                env.UserId,
                env.SessionId,
                env.QuestionId,
                "audio/webm",
                bytes.Length,
                checksum),
            default);

        using var http = new HttpClient();
        using var put = new ByteArrayContent(bytes);
        put.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        put.Headers.TryAddWithoutValidation("x-amz-meta-sha256", checksum);

        var putResponse = await http.PutAsync(init.UploadUrl, put);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var recordingId = await env.Complete.HandleAsync(
            new CompleteSpeakingRecordingCommand(
                env.UserId,
                env.SessionId,
                init.UploadId,
                bytes.Length,
                checksum),
            default);

        Assert.Equal(init.RecordingId, recordingId);

        var sheet = await env.Answers.ReadAsync(env.SessionId, ExamModule.Speaking, default);
        Assert.Equal(recordingId, sheet.Answers[env.QuestionId]);

        var head = await env.Blobs.HeadAsync(
            SpeakingRecordingKey.For(env.SessionId, env.QuestionId), default);
        Assert.NotNull(head);
        Assert.Equal(bytes.Length, head!.ContentLength);
        Assert.Equal(checksum, head.ChecksumSha256);
    }

    [SkippableFact]
    public async Task Complete_rejects_checksum_mismatch()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        await using var env = await Env.CreateAsync();
        var bytes = Encoding.UTF8.GetBytes("checksum-mismatch-body");
        var checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var init = await env.Init.HandleAsync(
            new InitSpeakingRecordingCommand(
                env.UserId,
                env.SessionId,
                env.QuestionId,
                "audio/webm",
                bytes.Length,
                checksum),
            default);

        using var http = new HttpClient();
        using var put = new ByteArrayContent(bytes);
        put.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        put.Headers.TryAddWithoutValidation("x-amz-meta-sha256", checksum);
        Assert.Equal(HttpStatusCode.OK, (await http.PutAsync(init.UploadUrl, put)).StatusCode);

        await Assert.ThrowsAsync<SpeakingRecordingChecksumMismatchException>(() =>
            env.Complete.HandleAsync(
                new CompleteSpeakingRecordingCommand(
                    env.UserId,
                    env.SessionId,
                    init.UploadId,
                    bytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData("wrong"u8.ToArray()))),
                default));
    }

    [SkippableFact]
    public async Task Complete_rejects_another_learners_upload()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        await using var env = await Env.CreateAsync();
        var bytes = Encoding.UTF8.GetBytes("wrong-owner");
        var checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var init = await env.Init.HandleAsync(
            new InitSpeakingRecordingCommand(
                env.UserId,
                env.SessionId,
                env.QuestionId,
                "audio/webm",
                bytes.Length,
                checksum),
            default);

        await Assert.ThrowsAsync<SpeakingRecordingUploadNotFoundException>(() =>
            env.Complete.HandleAsync(
                new CompleteSpeakingRecordingCommand(
                    UserId.New(),
                    env.SessionId,
                    init.UploadId,
                    bytes.Length,
                    checksum),
                default));
    }

    [SkippableFact]
    public async Task Object_key_rejects_traversal_segments()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        var client = NewClient();
        var options = new ObjectStorageOptions { SpeakingRecordingsBucket = SpeakingBucket };
        var blobs = new S3SpeakingRecordingBlobStore(client, options);

        Assert.Throws<ArgumentException>(() =>
            blobs.CreatePresignedPutUrl(
                "recordings/../escape",
                "audio/webm",
                Convert.ToHexStringLower(SHA256.HashData("x"u8.ToArray())),
                TimeSpan.FromMinutes(5)));
    }

    [SkippableFact]
    public async Task Re_record_replaces_object_under_same_derived_key()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        await using var env = await Env.CreateAsync();
        var first = Encoding.UTF8.GetBytes("first-take");
        var second = Encoding.UTF8.GetBytes("second-take-replaces");
        var firstChecksum = Convert.ToHexStringLower(SHA256.HashData(first));
        var secondChecksum = Convert.ToHexStringLower(SHA256.HashData(second));
        var objectKey = SpeakingRecordingKey.For(env.SessionId, env.QuestionId);

        var init1 = await env.Init.HandleAsync(
            new InitSpeakingRecordingCommand(
                env.UserId, env.SessionId, env.QuestionId, "audio/webm",
                first.Length, firstChecksum),
            default);

        using var http = new HttpClient();
        using (var put = new ByteArrayContent(first))
        {
            put.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
            put.Headers.TryAddWithoutValidation("x-amz-meta-sha256", firstChecksum);
            Assert.Equal(HttpStatusCode.OK, (await http.PutAsync(init1.UploadUrl, put)).StatusCode);
        }

        await env.Complete.HandleAsync(
            new CompleteSpeakingRecordingCommand(
                env.UserId, env.SessionId, init1.UploadId, first.Length, firstChecksum),
            default);

        var init2 = await env.Init.HandleAsync(
            new InitSpeakingRecordingCommand(
                env.UserId, env.SessionId, env.QuestionId, "audio/webm",
                second.Length, secondChecksum),
            default);

        Assert.Equal(init1.RecordingId, init2.RecordingId);
        Assert.Equal("presigned", init2.UploadMode);

        using (var put = new ByteArrayContent(second))
        {
            put.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
            put.Headers.TryAddWithoutValidation("x-amz-meta-sha256", secondChecksum);
            Assert.Equal(HttpStatusCode.OK, (await http.PutAsync(init2.UploadUrl, put)).StatusCode);
        }

        await env.Complete.HandleAsync(
            new CompleteSpeakingRecordingCommand(
                env.UserId, env.SessionId, init2.UploadId, second.Length, secondChecksum),
            default);

        var head = await env.Blobs.HeadAsync(objectKey, default);
        Assert.NotNull(head);
        Assert.Equal(second.Length, head!.ContentLength);
        Assert.Equal(secondChecksum, head.ChecksumSha256);

        var sheet = await env.Answers.ReadAsync(env.SessionId, ExamModule.Speaking, default);
        Assert.Equal(init2.RecordingId, sheet.Answers[env.QuestionId]);
    }

    [SkippableFact]
    public async Task Stale_pending_upload_is_aborted()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        await using var env = await Env.CreateAsync();
        var bytes = Encoding.UTF8.GetBytes("never-completed");
        var checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var init = await env.Init.HandleAsync(
            new InitSpeakingRecordingCommand(
                env.UserId, env.SessionId, env.QuestionId, "audio/webm",
                bytes.Length, checksum),
            default);

        using var http = new HttpClient();
        using var put = new ByteArrayContent(bytes);
        put.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        put.Headers.TryAddWithoutValidation("x-amz-meta-sha256", checksum);
        Assert.Equal(HttpStatusCode.OK, (await http.PutAsync(init.UploadUrl, put)).StatusCode);

        // Advance past the 15-minute pending TTL.
        env.Clock.Advance(TimeSpan.FromMinutes(16));

        var report = await env.AbortStale.HandleAsync(100, default);
        Assert.Equal(1, report.Abandoned);
        Assert.Equal(1, report.ObjectsRemoved);

        await Assert.ThrowsAsync<SpeakingRecordingUploadNotFoundException>(() =>
            env.Complete.HandleAsync(
                new CompleteSpeakingRecordingCommand(
                    env.UserId, env.SessionId, init.UploadId, bytes.Length, checksum),
                default));
    }

    [SkippableFact]
    public async Task Expired_presigned_put_url_is_refused_by_minio()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        var client = NewClient();
        var options = new ObjectStorageOptions
        {
            ServiceUrl = "http://localhost:9000",
            AccessKey = "vni-local",
            SecretKey = "vni-local-dev-only",
            ForcePathStyle = true,
            Region = "us-east-1",
            SpeakingRecordingsBucket = SpeakingBucket,
        };
        var blobs = new S3SpeakingRecordingBlobStore(client, options);
        var objectKey = SpeakingRecordingKey.For(ExamSessionId.New(), "s-part-1");
        var checksum = Convert.ToHexStringLower(SHA256.HashData("expired-url"u8.ToArray()));

        var url = blobs.CreatePresignedPutUrl(
            objectKey, "audio/webm", checksum, TimeSpan.FromSeconds(1));

        await Task.Delay(TimeSpan.FromSeconds(2));

        using var http = new HttpClient();
        using var put = new ByteArrayContent("expired-url"u8.ToArray());
        put.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        put.Headers.TryAddWithoutValidation("x-amz-meta-sha256", checksum);

        var response = await http.PutAsync(url, put);
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                or HttpStatusCode.BadRequest,
            $"Expected expired URL refusal, got {(int)response.StatusCode} {response.StatusCode}");
    }

    [SkippableFact]
    public async Task Complete_rejects_size_mismatch_against_init()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        await using var env = await Env.CreateAsync();
        var bytes = Encoding.UTF8.GetBytes("size-mismatch-body");
        var checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var init = await env.Init.HandleAsync(
            new InitSpeakingRecordingCommand(
                env.UserId,
                env.SessionId,
                env.QuestionId,
                "audio/webm",
                bytes.Length,
                checksum),
            default);

        using var http = new HttpClient();
        using var put = new ByteArrayContent(bytes);
        put.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        put.Headers.TryAddWithoutValidation("x-amz-meta-sha256", checksum);
        Assert.Equal(HttpStatusCode.OK, (await http.PutAsync(init.UploadUrl, put)).StatusCode);

        await Assert.ThrowsAsync<SpeakingRecordingChecksumMismatchException>(() =>
            env.Complete.HandleAsync(
                new CompleteSpeakingRecordingCommand(
                    env.UserId,
                    env.SessionId,
                    init.UploadId,
                    bytes.Length + 1,
                    checksum),
                default));
    }

    private static AmazonS3Client NewClient() =>
        new(
            new BasicAWSCredentials("vni-local", "vni-local-dev-only"),
            new AmazonS3Config
            {
                ServiceURL = "http://localhost:9000",
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
                UseHttp = true,
            });

    private sealed class Env : IAsyncDisposable
    {
        public UserId UserId { get; private init; } = UserId.New();
        public ExamSessionId SessionId { get; private init; } = ExamSessionId.New();
        public string QuestionId { get; } = "s-part-1";
        public InitSpeakingRecording Init { get; private init; } = null!;
        public CompleteSpeakingRecording Complete { get; private init; } = null!;
        public AbortStaleSpeakingUploads AbortStale { get; private init; } = null!;
        public ISpeakingRecordingBlobStore Blobs { get; private init; } = null!;
        public IAnswerSheetStore Answers { get; private init; } = null!;
        public MovableClock Clock { get; private init; } = null!;

        public static async Task<Env> CreateAsync()
        {
            var mongo = new MongoDB.Driver.MongoClient(
                "mongodb://localhost:27018/?directConnection=true");
            var database = mongo.GetDatabase($"vni-speaking-test-{Guid.NewGuid():n}");
            await MongoSpeakingRecordingMetadataStore.EnsureIndexesAsync(database, default);

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

            var options = new ObjectStorageOptions
            {
                ServiceUrl = "http://localhost:9000",
                AccessKey = "vni-local",
                SecretKey = "vni-local-dev-only",
                ForcePathStyle = true,
                Region = "us-east-1",
                SpeakingRecordingsBucket = SpeakingBucket,
                SpeakingRecordingRetentionDays = 90,
            };

            var client = NewClient();
            var blobs = new S3SpeakingRecordingBlobStore(client, options);
            var metadata = new MongoSpeakingRecordingMetadataStore(database);
            var recordings = new S3SpeakingRecordingStore(blobs, metadata);
            var answers = new MongoAnswerSheetStore(new MongoContext(
                Microsoft.Extensions.Options.Options.Create(new MongoOptions
                {
                    ConnectionString = "mongodb://localhost:27018/?directConnection=true",
                    Database = database.DatabaseNamespace.DatabaseName,
                })));
            var clock = new MovableClock(T0);

            var catalogue = new Catalogue(version);
            var sessions = new Sessions(session);

            var init = new InitSpeakingRecording(
                catalogue, sessions, blobs, metadata,
                new ObjectStorageSpeakingOptions { RetentionDays = 90 }, clock);

            var complete = new CompleteSpeakingRecording(
                catalogue, sessions, answers, blobs, metadata, recordings, clock);

            var abort = new AbortStaleSpeakingUploads(metadata, blobs, clock);

            return new Env
            {
                UserId = userId,
                SessionId = sessionId,
                Init = init,
                Complete = complete,
                AbortStale = abort,
                Blobs = blobs,
                Answers = answers,
                Clock = clock,
            };
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MovableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;
        public void Advance(TimeSpan by) => UtcNow += by;
    }

    private static ExamVersion BuildVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default);
        var timing = new TimingProfile(
            new Dictionary<ExamModule, int>(),
            null,
            []);
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Speaking upload test", ExamVariant.Academic, scoring, timing,
            [SpeakingSection()],
            new ListeningPlaybackProfile(
                new AudioPlaybackRule(false, true),
                new AudioPlaybackRule(true, false)));
        version.Publish(T0.AddDays(-1));
        return version;
    }

    private static Section SpeakingSection() =>
        new(ExamModule.Speaking, 1,
        [
            new SectionPart(
                1, "speaking", "Part 1", "Prompt", null, null, null, null, 1, null, null,
                [new Question("s-part-1", 1, QuestionType.SpeakingResponse, "Prompt", [], null, null)]),
        ]);

    private sealed class Catalogue(ExamVersion version) : IExamCatalogue
    {
        public Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([version]);

        public Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([version]);

        public Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct) =>
            Task.FromResult<ExamVersion?>(id == version.Id ? version : null);

        public Task UpsertAsync(ExamVersion version, CancellationToken ct) => Task.CompletedTask;
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
}
