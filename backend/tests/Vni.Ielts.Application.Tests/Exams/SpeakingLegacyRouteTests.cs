using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Tests.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// The legacy Speaking route — <c>POST …/recordings</c>, the multipart body
/// upload for large answers — files its bytes with no metadata row: playback
/// could not find them, the purge could not see them, and the only route the
/// sweeper had to a stored object was the answer sheet.
///
/// <para>
/// <b>The fix these tests demand.</b> A legacy upload that files itself also
/// files a Linked metadata row — the same shape the presigned flow writes —
/// so playback, purge and the sweep all see every stored recording; and a
/// refused upload leaves no row, because its bytes were deleted.
/// </para>
/// </summary>
public sealed class SpeakingLegacyRouteTests
{
    [Fact]
    public async Task A_legacy_upload_files_a_linked_metadata_row()
    {
        var env = LegacyEnv.Create();
        var recordingId = await env.Submit();

        var row = Assert.Single(env.Metadata.AllRows);
        Assert.Equal(SpeakingRecordingStatus.Linked, row.Status);
        Assert.Equal(recordingId, row.RecordingId);
        // The key is the sheet's id under the prefix — true for the real S3
        // and GridFS stores, and so for any fake.
        Assert.Equal(SpeakingRecordingKey.Prefix + recordingId, row.ObjectKey);
        Assert.Equal(env.SessionId, row.SessionId);
        Assert.Equal(env.QuestionId, row.QuestionId);
        Assert.Equal(env.OwnerId, row.OwnerId);
        Assert.Equal(env.SheetValue(env.QuestionId), recordingId);
    }

    [Fact]
    public async Task Playback_finds_a_recording_filed_through_the_legacy_route()
    {
        var env = LegacyEnv.Create();
        var recordingId = await env.Submit();

        var handler = new GetSpeakingRecordingPlaybackUrl(
            env.Catalogue, env.Sessions, env.Blobs, env.Metadata, env.Answers, env.Clock);
        var result = await handler.HandleAsync(
            new GetSpeakingRecordingPlaybackCommand(
                env.OwnerId, env.SessionId, env.QuestionId), default);

        // The URL targets the row's own key, and the sheet's value reconstructs it.
        Assert.Equal(
            $"https://cdn.example/{SpeakingRecordingKey.Prefix}{recordingId}",
            result.Url.ToString());
    }

    [Fact]
    public async Task Purge_finds_and_removes_a_recording_filed_through_the_legacy_route()
    {
        var env = LegacyEnv.Create();
        var recordingId = await env.Submit();

        var purge = new PurgeSpeakingRecordings(
            env.Metadata, env.Recordings, env.Blobs, audit: null, env.Clock);
        var report = await purge.ForSessionAsync(
            env.SessionId, env.OwnerId, "operator", default);

        Assert.Equal(1, report.Examined);
        Assert.Equal(1, report.Removed);
        Assert.Contains(recordingId, env.Recordings.Deleted);
        Assert.Empty(env.Metadata.AllRows);
    }

    [Fact]
    public async Task A_refused_legacy_upload_deletes_its_bytes_and_leaves_no_row()
    {
        var env = LegacyEnv.Create();
        env.Answers.Closed = true;

        await Assert.ThrowsAsync<SectionSheetClosedException>(() => env.Submit());

        // The refusal deleted the bytes; no row may survive pointing at them.
        Assert.Empty(env.Metadata.AllRows);
        Assert.NotEmpty(env.Recordings.Deleted);
    }
}

/// <summary>The legacy multipart route, wired to in-memory fakes.</summary>
internal sealed class LegacyEnv
{
    public FakeExamCatalogue Catalogue { get; }
    public FakeSessionRepository Sessions { get; } = new();
    public TakeIsolationMetadata Metadata { get; } = new();
    public TakeIsolationBlobs Blobs { get; } = new();
    public ClosingAnswers Answers { get; } = new();
    public FakeRecordingStore Recordings { get; } = new();
    public MovableClock Clock { get; } = new(SpeakingFixtures.T0);

    public SubmitSpeakingRecording Handler { get; }

    private LegacyEnv()
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

Handler = new SubmitSpeakingRecording(
            Catalogue, Sessions, Answers, Recordings, Metadata, Clock);
    }

    public static LegacyEnv Create() => new();

    public UserId OwnerId => SpeakingFixtures.Owner;
    public ExamSessionId SessionId => SpeakingFixtures.Sitting;
    public string QuestionId => SpeakingFixtures.QuestionId;

    public string? SheetValue(string questionId) => Answers.Saved.GetValueOrDefault(questionId);

    public async Task<string> Submit()
    {
        var bytes = new byte[] { 0x49, 0x44, 0x33, 0x0d };
        using var content = new MemoryStream(bytes, writable: false);
        return await Handler.HandleAsync(
            new SubmitSpeakingRecordingCommand(
                SpeakingFixtures.Owner, SpeakingFixtures.Sitting, SpeakingFixtures.QuestionId,
                content, "audio/mp4"), default);
    }

    private static ExamVersion SpeakingVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Speaking legacy route", ExamVariant.Academic, scoring, timing,
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
