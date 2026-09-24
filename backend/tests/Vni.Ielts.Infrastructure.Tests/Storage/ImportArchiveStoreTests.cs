using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure;
using Vni.Ielts.Infrastructure.Content.Import;
using Vni.Ielts.Infrastructure.Storage;

namespace Vni.Ielts.Infrastructure.Tests.Storage;

/// <summary>
/// The import archive store: the registration rule that keeps a single-machine
/// adapter out of a multi-instance deployment, and the S3 adapter itself
/// against a real MinIO.
///
/// <b>Why the S3 one is measured rather than reasoned about.</b> Its
/// <c>UseChunkEncoding = false</c> was copied from a neighbour, and the reason
/// that setting exists is that Cloudflare R2 refuses an SDK v4 chunked body —
/// something this project found out on 2026-09-04, on the first push of exam
/// audio, as an opaque "STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER not
/// implemented". A fix copied without being exercised is how the same thing
/// bites twice, and an adapter nobody runs before production is an adapter
/// nobody has tested.
/// </summary>
public sealed class ImportArchiveStoreTests
{
    private const string RealBucket = "vni-exam-assets";

    private static bool MinioAvailable => ObjectStorageProbe.MinioAvailable;
    private const string SkipReason = ObjectStorageProbe.SkipReason;

    // ── The registration rule ─────────────────────────────────────────────

    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, isDevelopment: true);

        return services.BuildServiceProvider();
    }

    private static readonly (string, string)[] ObjectStorageConfigured =
    [
        ("ObjectStorage:ServiceUrl", "http://localhost:9000"),
        ("ObjectStorage:AccessKey", "vni-local"),
        ("ObjectStorage:SecretKey", "vni-local-dev-only"),
    ];

    [Fact]
    public void With_object_storage_configured_an_upload_goes_to_the_bucket()
    {
        using var provider = Build(ObjectStorageConfigured);

        Assert.IsType<S3ImportArchiveStore>(
            provider.GetRequiredService<IImportArchiveStore>());
    }

    [Fact]
    public void With_no_object_storage_an_upload_goes_to_local_disk()
    {
        using var provider = Build();

        Assert.IsType<LocalFileImportArchiveStore>(
            provider.GetRequiredService<IImportArchiveStore>());
    }

    /// <summary>
    /// <b>The hole this closes.</b> The discard-store fallback for
    /// <see cref="IPrivateImportAssetStore"/> used to register unconditionally,
    /// after <c>AddObjectStorage</c> had already bound the real
    /// <c>S3PrivateImportAssetStore</c> — last registration wins, so the real
    /// store was never resolved in any environment, including production with
    /// object storage fully configured. Nothing that staged an asset through
    /// this port ever kept what it was given.
    /// </summary>
    [Fact]
    public void With_object_storage_configured_a_staged_import_asset_uses_the_real_store()
    {
        using var provider = Build(ObjectStorageConfigured);

        Assert.IsType<S3PrivateImportAssetStore>(
            provider.GetRequiredService<IPrivateImportAssetStore>());
    }

    [Fact]
    public void With_object_storage_configured_approval_uses_the_real_asset_promoter()
    {
        using var provider = Build(ObjectStorageConfigured);

        Assert.IsType<S3ImportAssetPromoter>(
            provider.GetRequiredService<IImportAssetPromoter>());
    }

    [Fact]
    public void With_no_object_storage_a_staged_import_asset_is_discarded()
    {
        using var provider = Build();

        Assert.IsType<DiscardedImportAssetStore>(
            provider.GetRequiredService<IPrivateImportAssetStore>());
    }

    [Fact]
    public void With_no_object_storage_approval_uses_the_noop_asset_promoter()
    {
        using var provider = Build();

        Assert.Same(
            NoOpImportAssetPromoter.Instance,
            provider.GetRequiredService<IImportAssetPromoter>());
    }

    /// <summary>
    /// <b>The hole this closes.</b> A local-disk archive store in a
    /// multi-instance deployment means the API writes the package on one
    /// machine and the worker looks for it on another — and the failure
    /// appears minutes later as "the uploaded archive is no longer in
    /// storage", which reads like an expiry, not like a misconfiguration.
    ///
    /// Nothing produces this pairing today: the startup gate refuses to boot
    /// outside Development without object storage, and <c>AddObjectStorage</c>
    /// registers the S3 store whenever it is configured. That is two true
    /// facts in two files with nothing asserting the implication between them
    /// — and the Worker never runs that startup gate at all. So the pairing is
    /// staged here deliberately, by registering the local store over the S3
    /// one, which is exactly the shape any future edit to the registration
    /// order would take.
    /// </summary>
    [Fact]
    public void Object_storage_with_a_local_disk_archive_store_refuses_to_start()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                ObjectStorageConfigured.ToDictionary(s => s.Item1, s => (string?)s.Item2))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, isDevelopment: true);

        // The regression, staged: object storage is configured and an upload
        // would still land on one machine's disk.
        services.AddSingleton<IImportArchiveStore>(sp =>
            new LocalFileImportArchiveStore(
                Path.Combine(Path.GetTempPath(), $"vni-archive-mismatch-{Guid.NewGuid():n}"),
                NullLogger<LocalFileImportArchiveStore>.Instance));

        using var provider = services.BuildServiceProvider();

        var refusal = Assert.Throws<InvalidOperationException>(
            () => DependencyInjection.AssertImportArchiveStoreMatchesObjectStorage(provider));

        Assert.Contains("local filesystem", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(LocalFileImportArchiveStore), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_s3_archive_store_with_no_object_storage_refuses_to_start()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddInfrastructure(new ConfigurationBuilder().Build(), isDevelopment: true);

        services.AddSingleton<IImportArchiveStore>(sp =>
            new S3ImportArchiveStore(
                NewClient(), new ObjectStorageOptions { ExamAssetsBucket = RealBucket },
                NullLogger<S3ImportArchiveStore>.Instance));

        using var provider = services.BuildServiceProvider();

        var refusal = Assert.Throws<InvalidOperationException>(
            () => DependencyInjection.AssertImportArchiveStoreMatchesObjectStorage(provider));

        Assert.Contains("not configured", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_matching_pairings_are_accepted()
    {
        using var configured = Build(ObjectStorageConfigured);
        using var unconfigured = Build();

        // No exception: the two registrations agree in both worlds.
        DependencyInjection.AssertImportArchiveStoreMatchesObjectStorage(configured);
        DependencyInjection.AssertImportArchiveStoreMatchesObjectStorage(unconfigured);
    }

    // ── The S3 adapter, against a real MinIO ──────────────────────────────

    /// <summary>
    /// Save, open, delete — the whole contract a worker depends on, against
    /// real object storage rather than a double.
    ///
    /// <b>The bytes are compared, not just the call's success.</b> This is the
    /// port whose write-only neighbour discards everything it is given; "the
    /// call returned a key" is precisely the weaker assertion that would not
    /// have caught it.
    /// </summary>
    [SkippableFact]
    public async Task An_archive_round_trips_through_real_object_storage_and_is_really_deleted()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        var store = NewStore();

        // Not a trivial payload: a chunked-upload refusal is a body-framing
        // failure, and a handful of bytes is the case most likely to slip past
        // one. ~1 MiB of incompressible noise is what a real package looks
        // like to the transport.
        var bytes = RandomNumberGenerator.GetBytes(1024 * 1024);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        string key;

        await using (var input = new MemoryStream(bytes, writable: false))
        {
            key = await store.SaveAsync(hash, input, default);
        }

        Assert.Equal($"imports/archives/{hash}.zip", key);

        try
        {
            await using var opened = await store.OpenAsync(key, default);
            Assert.NotNull(opened);

            using var copy = new MemoryStream();
            await opened!.CopyToAsync(copy);

            Assert.Equal(bytes.Length, copy.Length);
            Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(copy.ToArray())));
        }
        finally
        {
            await store.DeleteAsync(key, default);
        }

        // <b>Deleted means gone.</b> An uploaded exam package is third-party
        // copyright; a delete that silently did nothing would leave every
        // package this product has ever been shown sitting in a bucket, and
        // the worker's own test — which uses an in-memory double — cannot tell
        // the difference.
        Assert.Null(await store.OpenAsync(key, default));
    }

    /// <summary>
    /// A key for an object that was never written is <c>null</c>, not an
    /// exception. The worker turns that null into a terminal failure with a
    /// sentence an operator can act on; an exception out of the loop is not
    /// that.
    /// </summary>
    [SkippableFact]
    public async Task A_missing_archive_is_null_rather_than_a_throw()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        var absent = Convert.ToHexStringLower(SHA256.HashData(Guid.NewGuid().ToByteArray()));

        Assert.Null(await NewStore().OpenAsync($"imports/archives/{absent}.zip", default));
    }

    /// <summary>
    /// A key that reaches this store through a database row and a worker is
    /// attacker-influenced by the time it arrives. Anything that is not a key
    /// this store could have written is treated as "gone" — the same answer a
    /// typo gets, so a probe learns nothing — and never reaches the bucket.
    /// </summary>
    [SkippableTheory]
    [InlineData("imports/archives/../../etc/passwd.zip")]
    [InlineData("assets/listening-part-1.m4a")]
    [InlineData("imports/archives/NOTHEX0000000000000000000000000000000000000000000000000000000000.zip")]
    [InlineData("")]
    public async Task A_key_this_store_could_not_have_written_is_refused(string key)
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        var store = NewStore();

        Assert.Null(await store.OpenAsync(key, default));

        // And deleting one is a no-op rather than a wild delete.
        await store.DeleteAsync(key, default);
    }

    /// <summary>
    /// Deleting an object that is not there is not an error. A job that
    /// settles twice — a retry after a lost response — must not turn a
    /// successful import into a failure over housekeeping.
    /// </summary>
    [SkippableFact]
    public async Task Deleting_an_archive_that_is_already_gone_is_not_an_error()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        var absent = Convert.ToHexStringLower(SHA256.HashData(Guid.NewGuid().ToByteArray()));

        await NewStore().DeleteAsync($"imports/archives/{absent}.zip", default);
    }

    /// <summary>
    /// Approval copies every staged asset into the public key space
    /// <see cref="IExamAssetStore"/> reads — same bucket, different prefix —
    /// and the learner store stays read-only. Without this, ApproveAsync only
    /// flipped ApprovalState and Listening audio stayed unreachable.
    /// </summary>
    [SkippableFact]
    public async Task Approve_promotes_staged_assets_so_the_exam_asset_store_can_open_them()
    {
        Skip.IfNot(MinioAvailable, SkipReason);

        using var client = NewClient();
        var options = new ObjectStorageOptions { ExamAssetsBucket = RealBucket };
        var privateStore = new S3PrivateImportAssetStore(client, options);
        var promoter = new S3ImportAssetPromoter(client, options);
        var examStore = new S3ExamAssetStore(
            new S3ObjectStore(client, NullLogger<S3ObjectStore>.Instance), options);

        var draftId = Guid.NewGuid();
        // Unique public ref so parallel test runs do not collide on the
        // destination key (KeyFor strips assets/ → one object per relative path).
        var publicRef = $"assets/promote-{draftId:n}.mp3";
        var publicObjectKey = publicRef["assets/".Length..];
        var stagedKey = ImportReviewWorkflow.StagedKeyFor(draftId, publicRef);
        var bytes = System.Text.Encoding.UTF8.GetBytes($"promote-payload-{draftId:n}");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        await using (var input = new MemoryStream(bytes, writable: false))
        {
            await privateStore.PutPrivateAsync(stagedKey, input, "audio/mpeg", hash, default);
        }

        Assert.Null(await examStore.OpenAsync(publicRef, default));

        var draft = ApprovableDraftWithAudio(draftId, publicRef);
        var drafts = new MemoryDraftStore(draft);
        var review = new ImportReviewWorkflow(
            drafts, new AlwaysValidValidator(), assetPromoter: promoter);

        var approved = await review.ApproveAsync(
            draftId, 0, new ImportReviewActor("reviewer", false, true, false), default);

        Assert.True(approved.IsSuccess, approved.ErrorCode);
        Assert.Equal(ImportApprovalState.Approved, approved.Draft!.ApprovalState);

        try
        {
            var opened = await examStore.OpenAsync(publicRef, default);
            Assert.NotNull(opened);
            await using (opened!.Content)
            {
                using var copy = new MemoryStream();
                await opened.Content.CopyToAsync(copy);
                Assert.Equal(bytes, copy.ToArray());
                Assert.Equal("audio/mpeg", opened.ContentType);
            }
        }
        finally
        {
            await client.DeleteObjectAsync(RealBucket, stagedKey);
            await client.DeleteObjectAsync(RealBucket, publicObjectKey);
        }
    }

    /// <summary>
    /// A failed promote must refuse approval rather than leaving an Approved
    /// draft whose media never reached the learner store.
    /// </summary>
    [Fact]
    public async Task Approve_refuses_when_asset_promotion_throws()
    {
        var draftId = Guid.NewGuid();
        var draft = ApprovableDraftWithAudio(draftId, "assets/missing.mp3");
        var drafts = new MemoryDraftStore(draft);
        var review = new ImportReviewWorkflow(
            drafts, new AlwaysValidValidator(), assetPromoter: new ThrowingPromoter());

        var refused = await review.ApproveAsync(
            draftId, 0, new ImportReviewActor("reviewer", false, true, false), default);

        Assert.False(refused.IsSuccess);
        Assert.Equal("IMPORT_ASSET_PROMOTE_FAILED", refused.ErrorCode);
        Assert.Equal(ImportApprovalState.ReviewRequired, drafts.Draft.ApprovalState);
    }

    private static S3ImportArchiveStore NewStore() =>
        new(
            NewClient(),
            new ObjectStorageOptions { ExamAssetsBucket = RealBucket },
            NullLogger<S3ImportArchiveStore>.Instance);

    /// <summary>
    /// <b>The same client <c>AddObjectStorage</c> builds, checksum settings
    /// included.</b> A test client left on the SDK's defaults would sign
    /// uploads as <c>aws-chunked</c> with a trailing CRC — and would therefore
    /// pass against MinIO while the production configuration this store
    /// actually runs under is the untested one.
    /// </summary>
    private static AmazonS3Client NewClient() =>
        new(
            new BasicAWSCredentials("vni-local", "vni-local-dev-only"),
            new AmazonS3Config
            {
                ServiceURL = "http://localhost:9000",
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
                UseHttp = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });

    private static ExamImportDraft ApprovableDraftWithAudio(Guid draftId, string audioKey)
    {
        var definition = ExamDefinitionId.New();
        var paper = ExamVersion.CreateDraft(
            definition, 1, "Promote assets paper", ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [
                new Section(ExamModule.Listening, 1,
                [
                    new SectionPart(
                        1, "listening-part", "Part 1", null, audioKey, null, null,
                        null, 1, null, null, []),
                ]),
            ]);

        return new ExamImportDraft(
            draftId, definition, 1, ExamImportRoute.StructuredPackage,
            new string('a', 64), ExamImportWorkflow.Hash("valid"), paper, null,
            ImportApprovalState.ReviewRequired, [], "raw source", "valid",
            new ImportReviewChecklist(Enum.GetValues<ImportReviewCategory>().ToHashSet()),
            [], 0, null);
    }

    private sealed class MemoryDraftStore(ExamImportDraft draft) : IImportDraftStore
    {
        public ExamImportDraft Draft { get; private set; } = draft;

        public Task SaveAsync(ExamImportDraft value, CancellationToken ct)
        {
            Draft = value;
            return Task.CompletedTask;
        }

        public Task<ExamImportDraft?> FindAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ExamImportDraft?>(Draft.Id == id ? Draft : null);

        public Task<ExamImportDraft?> FindBySourceAsync(
            ExamDefinitionId definitionId, int versionNumber, ExamImportRoute route,
            string sourceHash, string parsePromptVersion, CancellationToken ct) =>
            Task.FromResult<ExamImportDraft?>(null);

        public Task<bool> ReplaceAsync(ExamImportDraft value, int expected, CancellationToken ct)
        {
            if (Draft.Id != value.Id || Draft.Revision != expected) return Task.FromResult(false);
            Draft = value;
            return Task.FromResult(true);
        }
    }

    private sealed class AlwaysValidValidator : IExamPackageValidator
    {
        public PackageValidationResult Validate(
            string json, ExamDefinitionId id, int version, UserId? authorId = null) =>
            new(true, ExamVersion.CreateDraft(
                id, version, "ok", ExamVariant.Academic,
                new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
                new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
                [new Section(ExamModule.Listening, 1, [])],
                authorId: authorId), []);
    }

    private sealed class ThrowingPromoter : IImportAssetPromoter
    {
        public Task PromoteAsync(string stagedKey, string publicKey, CancellationToken ct) =>
            throw new InvalidOperationException("staged asset missing");
    }
}
