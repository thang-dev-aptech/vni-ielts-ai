using System.Text;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Media;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

/// <summary>
/// Resolving `media/&lt;id&gt;` refs — the form Phase 3's in-place authoring
/// produces, as opposed to `assets/&lt;path&gt;` from a ZIP import.
/// → `docs/development/phase-3-question-builder-plan.md` Plan 01
/// </summary>
public sealed class MediaBackedAssetStoreTests
{
    private static readonly UserId Uploader = UserId.New();

    private sealed class InMemoryMediaAssetRepository : IMediaAssetRepository
    {
        private readonly Dictionary<string, MediaAsset> _assets = [];

        public void Seed(MediaAsset asset) => _assets[asset.Id] = asset;

        public Task<MediaAsset?> FindAsync(string mediaId, CancellationToken ct) =>
            Task.FromResult(_assets.GetValueOrDefault(mediaId));

        public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MediaAsset>>([.. _assets.Values]);

        public Task SaveAsync(MediaAsset asset, CancellationToken ct)
        {
            _assets[asset.Id] = asset;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string mediaId, CancellationToken ct)
        {
            _assets.Remove(mediaId);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryObjectStorage : IObjectStorage
    {
        private readonly Dictionary<string, byte[]> _objects = [];

        public void Seed(string key, string content) => _objects[key] = Encoding.UTF8.GetBytes(content);

        public Task<string> PutAsync(Stream content, string contentType, CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by these tests.");

        public Task<Stream> OpenAsync(string key, CancellationToken ct) =>
            _objects.TryGetValue(key, out var bytes)
                ? Task.FromResult<Stream>(new MemoryStream(bytes))
                : throw new InvalidOperationException($"No object at key '{key}'.");

        public Task DeleteAsync(string key, CancellationToken ct) =>
            throw new NotSupportedException("Not exercised by these tests.");
    }

    private static MediaAsset Seeded(
        InMemoryMediaAssetRepository media, InMemoryObjectStorage storage,
        string id, bool retired = false)
    {
        var asset = MediaAsset.Create(
            id, MediaKind.Audio, "part1.mp3", "audio/mpeg", 1024, durationMs: 5000,
            "checksum", $"storage/{id}", Uploader, DateTimeOffset.UtcNow);
        if (retired) asset.Retire();

        media.Seed(asset);
        storage.Seed($"storage/{id}", "fake audio bytes");
        return asset;
    }

    [Fact]
    public async Task An_existing_media_reference_resolves_to_its_stored_content()
    {
        var media = new InMemoryMediaAssetRepository();
        var storage = new InMemoryObjectStorage();
        Seeded(media, storage, "0123456789abcdef0123456789abcdef");

        var store = new MediaBackedAssetStore(media, storage);
        var asset = await store.OpenAsync("media/0123456789abcdef0123456789abcdef", CancellationToken.None);

        Assert.NotNull(asset);
        Assert.Equal("audio/mpeg", asset!.ContentType);
    }

    [Fact]
    public async Task A_missing_media_id_resolves_to_null()
    {
        var store = new MediaBackedAssetStore(new InMemoryMediaAssetRepository(), new InMemoryObjectStorage());
        var asset = await store.OpenAsync("media/does-not-exist", CancellationToken.None);

        Assert.Null(asset);
    }

    [Fact]
    public async Task A_retired_asset_still_resolves()
    {
        // Retirement removes an id from the upload picker; it must not revoke
        // access for content that already references it — an already-approved
        // or published exam version's audio has to keep playing.
        var media = new InMemoryMediaAssetRepository();
        var storage = new InMemoryObjectStorage();
        Seeded(media, storage, "fedcba9876543210fedcba9876543210", retired: true);

        var store = new MediaBackedAssetStore(media, storage);
        var asset = await store.OpenAsync("media/fedcba9876543210fedcba9876543210", CancellationToken.None);

        Assert.NotNull(asset);
    }

    [Fact]
    public async Task An_assets_prefixed_reference_is_not_this_stores_job()
    {
        var store = new MediaBackedAssetStore(new InMemoryMediaAssetRepository(), new InMemoryObjectStorage());
        var asset = await store.OpenAsync("assets/listening-part-1.mp3", CancellationToken.None);

        Assert.Null(asset);
    }

    [Fact]
    public async Task The_composite_store_tries_each_inner_store_and_returns_the_first_hit()
    {
        var media = new InMemoryMediaAssetRepository();
        var storage = new InMemoryObjectStorage();
        Seeded(media, storage, "0123456789abcdef0123456789abcdef");

        var neverResolves = new AlwaysNullAssetStore();
        var composite = new CompositeAssetStore([neverResolves, new MediaBackedAssetStore(media, storage)]);

        var asset = await composite.OpenAsync("media/0123456789abcdef0123456789abcdef", CancellationToken.None);

        Assert.NotNull(asset);
    }

    private sealed class AlwaysNullAssetStore : IExamAssetStore
    {
        public Task<ExamAsset?> OpenAsync(string reference, CancellationToken ct) =>
            Task.FromResult<ExamAsset?>(null);
    }
}
