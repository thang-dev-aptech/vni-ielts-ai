using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Media;

namespace Vni.Ielts.Application.Tests.Exams;

public sealed class ExamAssetInventoryTests
{
    private const string MediaId = "0123456789abcdef0123456789abcdef";
    private const string MediaRef = "media/" + MediaId;
    private const string PackageRef = "assets/listening-part-1.m4a";

    [Fact]
    public void Collect_walks_audio_image_and_group_images()
    {
        var version = ListeningVersion(
            audio: MediaRef,
            image: "assets/map.png",
            groupImage: "media/fedcba9876543210fedcba9876543210");

        var collected = ExamAssetInventory.Collect(version);

        Assert.Equal(
            [
                MediaRef,
                "assets/map.png",
                "media/fedcba9876543210fedcba9876543210",
            ],
            collected.Select(c => c.Ref));
        Assert.Equal(["audio", "image", "image"], collected.Select(c => c.Kind));
        Assert.Contains(collected, c => c.UsedAt == "Listening · Part 1");
        Assert.Contains(collected, c => c.UsedAt.Contains("câu 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Collect_skips_blank_drafts()
    {
        var version = ExamVersion.CreateBlankDraft(
            ExamDefinitionId.New(), 1, "Empty", ExamVariant.Academic, UserId.New());

        Assert.Empty(ExamAssetInventory.Collect(version));
    }

    [Fact]
    public async Task Resolve_marks_a_store_hit_present_even_when_it_is_not_in_the_library()
    {
        var version = ListeningVersion(audio: PackageRef);
        var assets = new FakeAssetStore(PackageRef);
        var media = new FakeMedia();

        var resolved = await ExamAssetInventory.ResolveAsync(version, assets, media, CancellationToken.None);

        var asset = Assert.Single(resolved);
        Assert.True(asset.Resolved);
        Assert.Null(asset.MediaId);
        Assert.Equal("listening-part-1.m4a", asset.FileName);
        Assert.Equal(4, asset.SizeBytes);
    }

    [Fact]
    public async Task Resolve_marks_a_missing_media_library_ref_unresolved()
    {
        var version = ListeningVersion(audio: MediaRef);
        var assets = new FakeAssetStore();
        var media = new FakeMedia();

        var resolved = await ExamAssetInventory.ResolveAsync(version, assets, media, CancellationToken.None);

        var asset = Assert.Single(resolved);
        Assert.False(asset.Resolved);
        Assert.Equal(MediaId, asset.MediaId);
        Assert.Equal(MediaId, asset.FileName);
    }

    [Fact]
    public async Task Resolve_takes_library_metadata_when_the_bytes_open()
    {
        var version = ListeningVersion(audio: MediaRef);
        var library = MediaAsset.Create(
            MediaId, MediaKind.Audio, "part-1.m4a", "audio/mp4", 2048, 60_000,
            "abc123", "objects/part-1", UserId.New(), DateTimeOffset.UtcNow);
        var assets = new FakeAssetStore(MediaRef);
        var media = new FakeMedia(library);

        var resolved = await ExamAssetInventory.ResolveAsync(version, assets, media, CancellationToken.None);

        var asset = Assert.Single(resolved);
        Assert.True(asset.Resolved);
        Assert.Equal("part-1.m4a", asset.FileName);
        Assert.Equal(2048, asset.SizeBytes);
        Assert.Equal("abc123", asset.Checksum);
        Assert.Equal(MediaId, asset.MediaId);
    }

    private static ExamVersion ListeningVersion(string? audio, string? image = null, string? groupImage = null)
    {
        Question[] questions = groupImage is null
            ? []
            :
            [
                new Question(
                    "q1", 1, QuestionType.Labelling, "label", [], null,
                    new AnswerKey([new AcceptedAnswer("A", null, null)], null),
                    new QuestionGroup("g1", null, null, groupImage, null, false)),
            ];

        return ExamVersion.CreateDraft(
            ExamDefinitionId.New(),
            1,
            "Listening sample",
            ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int> { [ExamModule.Listening] = 60 }, null, []),
            [
                new Section(
                    ExamModule.Listening,
                    1,
                    [
                        new SectionPart(
                            1, "recording", "Part 1", null, audio, image, null,
                            null, null, null, null, questions),
                    ]),
            ]);
    }

    private sealed class FakeAssetStore(params string[] present) : IExamAssetStore
    {
        public Task<ExamAsset?> OpenAsync(string reference, CancellationToken ct)
        {
            if (!present.Contains(reference, StringComparer.Ordinal))
                return Task.FromResult<ExamAsset?>(null);

            var bytes = new MemoryStream("test"u8.ToArray());
            return Task.FromResult<ExamAsset?>(new ExamAsset(bytes, "audio/mpeg", 4, "etag-1"));
        }
    }

    private sealed class FakeMedia(MediaAsset? asset = null) : IMediaAssetRepository
    {
        public Task<MediaAsset?> FindAsync(string mediaId, CancellationToken ct) =>
            Task.FromResult(asset is not null && asset.Id == mediaId ? asset : null);

        public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MediaAsset>>(asset is null ? [] : [asset]);

        public Task SaveAsync(MediaAsset next, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteAsync(string mediaId, CancellationToken ct) => Task.CompletedTask;
    }
}
