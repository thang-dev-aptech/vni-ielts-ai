using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Storage;

namespace Vni.Ielts.Infrastructure.Tests.Storage;

public sealed class ImportExamAssetStoreTests
{
    private const string RealBucket = "vni-exam-assets";

    [SkippableFact]
    public async Task Stage_promote_round_trips_exact_bytes_and_is_idempotent()
    {
        Skip.IfNot(ObjectStorageProbe.MinioAvailable, ObjectStorageProbe.SkipReason);

        var store = NewStore(out var client);
        var reference = $"assets/import-asset/{Guid.NewGuid():n}.mp3";
        var bytes = SyntheticMp3("round-trip");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var draftId = Guid.NewGuid();

        try
        {
            await using var input = new MemoryStream(bytes, writable: false);
            var staged = await store.StageAsync(draftId, reference, input, "audio/mpeg", bytes.Length, hash, default);
            Assert.StartsWith("imports/exam-drafts/", staged.StagingKey, StringComparison.Ordinal);

            var first = await store.PromoteAsync(staged, default);
            Assert.Equal(ImportAssetPromotionStatus.PromotedThisAttempt, first.Status);

            var second = await store.PromoteAsync(staged, default);
            Assert.Equal(ImportAssetPromotionStatus.AlreadyPresent, second.Status);

            var found = await store.CheckFinalAsync(reference, default);
            Assert.Equal(ImportAssetAvailabilityKind.Present, found.Kind);
            Assert.Equal(hash, found.Sha256);
            Assert.Equal(bytes.Length, found.Length);
            Assert.Equal("audio/mpeg", found.ContentType);

            await store.VerifyFinalAsync(
                [new ImportAssetManifestEntry(reference, staged.StagingKey, "audio/mpeg", bytes.Length, hash)],
                default);
        }
        finally
        {
            await client.DeleteObjectAsync(RealBucket, reference["assets/".Length..]);
            await client.DeleteObjectAsync(RealBucket, $"imports/exam-drafts/{draftId:D}/assets/{reference["assets/".Length..]}");
        }
    }

    [SkippableFact]
    public async Task Different_bytes_at_the_same_destination_conflict_and_do_not_overwrite()
    {
        Skip.IfNot(ObjectStorageProbe.MinioAvailable, ObjectStorageProbe.SkipReason);

        var store = NewStore(out var client);
        var reference = $"assets/import-asset/{Guid.NewGuid():n}.mp3";
        var original = SyntheticMp3("original");
        var conflicting = SyntheticMp3("conflict");
        var originalHash = Convert.ToHexStringLower(SHA256.HashData(original));
        var conflictHash = Convert.ToHexStringLower(SHA256.HashData(conflicting));
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        try
        {
            await using (var input = new MemoryStream(original, writable: false))
            {
                var staged = await store.StageAsync(firstId, reference, input, "audio/mpeg", original.Length, originalHash, default);
                Assert.Equal(ImportAssetPromotionStatus.PromotedThisAttempt, (await store.PromoteAsync(staged, default)).Status);
            }

            await using (var input = new MemoryStream(conflicting, writable: false))
            {
                var staged = await store.StageAsync(secondId, reference, input, "audio/mpeg", conflicting.Length, conflictHash, default);
                var result = await store.PromoteAsync(staged, default);
                Assert.Equal(ImportAssetPromotionStatus.Conflict, result.Status);
            }

            var found = await store.CheckFinalAsync(reference, default);
            Assert.Equal(originalHash, found.Sha256);
        }
        finally
        {
            await client.DeleteObjectAsync(RealBucket, reference["assets/".Length..]);
        }
    }

    [SkippableFact]
    public async Task Missing_staging_object_does_not_create_the_destination()
    {
        Skip.IfNot(ObjectStorageProbe.MinioAvailable, ObjectStorageProbe.SkipReason);

        var store = NewStore(out _);
        var reference = $"assets/import-asset/{Guid.NewGuid():n}.mp3";
        var staged = new StagedImportAsset(
            reference,
            $"imports/exam-drafts/{Guid.NewGuid():D}/assets/{reference["assets/".Length..]}",
            "audio/mpeg",
            8,
            new string('a', 64));

        var result = await store.PromoteAsync(staged, default);
        Assert.Equal(ImportAssetPromotionStatus.Missing, result.Status);
        Assert.Equal(ImportAssetAvailabilityKind.Absent, (await store.CheckFinalAsync(reference, default)).Kind);
    }

    [SkippableFact]
    public async Task Cancellation_does_not_claim_success()
    {
        Skip.IfNot(ObjectStorageProbe.MinioAvailable, ObjectStorageProbe.SkipReason);

        var store = NewStore(out _);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await store.PromoteAsync(
            new StagedImportAsset("assets/a.mp3", "imports/exam-drafts/x/assets/a.mp3", "audio/mpeg", 1, "aa"),
            cts.Token);

        Assert.Equal(ImportAssetPromotionStatus.Canceled, result.Status);
    }

    [SkippableFact]
    public async Task Concurrent_promotions_of_different_checksums_leave_exactly_one_winner()
    {
        Skip.IfNot(ObjectStorageProbe.MinioAvailable, ObjectStorageProbe.SkipReason);

        var store = NewStore(out var client);
        var reference = $"assets/import-asset/race-{Guid.NewGuid():n}.mp3";
        var left = SyntheticMp3("left");
        var right = SyntheticMp3("right");
        Assert.NotEqual(left, right);
        var leftHash = Convert.ToHexStringLower(SHA256.HashData(left));
        var rightHash = Convert.ToHexStringLower(SHA256.HashData(right));
        var leftId = Guid.NewGuid();
        var rightId = Guid.NewGuid();

        try
        {
            StagedImportAsset stagedLeft;
            StagedImportAsset stagedRight;
            await using (var input = new MemoryStream(left, writable: false))
                stagedLeft = await store.StageAsync(leftId, reference, input, "audio/mpeg", left.Length, leftHash, default);
            await using (var input = new MemoryStream(right, writable: false))
                stagedRight = await store.StageAsync(rightId, reference, input, "audio/mpeg", right.Length, rightHash, default);

            var first = store.PromoteAsync(stagedLeft, default);
            var second = store.PromoteAsync(stagedRight, default);
            await Task.WhenAll(first, second);

            var statuses = new[] { first.Result.Status, second.Result.Status };
            Assert.Equal(1, statuses.Count(s => s == ImportAssetPromotionStatus.PromotedThisAttempt));
            Assert.Equal(1, statuses.Count(s => s == ImportAssetPromotionStatus.Conflict));

            var found = await store.CheckFinalAsync(reference, default);
            var winnerHash = first.Result.Status == ImportAssetPromotionStatus.PromotedThisAttempt
                ? leftHash
                : rightHash;
            Assert.Equal(winnerHash, found.Sha256);
        }
        finally
        {
            await client.DeleteObjectAsync(RealBucket, reference["assets/".Length..]);
            await client.DeleteObjectAsync(RealBucket, $"imports/exam-drafts/{leftId:D}/assets/{reference["assets/".Length..]}");
            await client.DeleteObjectAsync(RealBucket, $"imports/exam-drafts/{rightId:D}/assets/{reference["assets/".Length..]}");
        }
    }

    [SkippableFact]
    public async Task Staging_abandoned_cleanup_deletes_staging_only_never_the_final_object()
    {
        Skip.IfNot(ObjectStorageProbe.MinioAvailable, ObjectStorageProbe.SkipReason);

        var store = NewStore(out var client);
        var reference = $"assets/import-asset/abandon-{Guid.NewGuid():n}.mp3";
        var finalBytes = SyntheticMp3("final");
        var stagedBytes = SyntheticMp3("staged");
        var finalHash = Convert.ToHexStringLower(SHA256.HashData(finalBytes));
        var stagedHash = Convert.ToHexStringLower(SHA256.HashData(stagedBytes));
        var ownerId = Guid.NewGuid();
        var abandonedId = Guid.NewGuid();
        var relative = reference["assets/".Length..];

        try
        {
            await using (var input = new MemoryStream(finalBytes, writable: false))
            {
                var staged = await store.StageAsync(ownerId, reference, input, "audio/mpeg", finalBytes.Length, finalHash, default);
                Assert.Equal(ImportAssetPromotionStatus.PromotedThisAttempt, (await store.PromoteAsync(staged, default)).Status);
            }

            await using (var input = new MemoryStream(stagedBytes, writable: false))
            {
                await store.StageAsync(abandonedId, reference, input, "audio/mpeg", stagedBytes.Length, stagedHash, default);
            }

            using var compensation = ImportAssetCompensation.Start();
            await store.RecordCleanupIntentAsync(
                abandonedId, [reference], ImportAssetCleanupReason.StagingAbandoned, compensation.Token);
            await store.ProcessPendingCleanupAsync(new EmptyCatalogue(), default);

            var found = await store.CheckFinalAsync(reference, default);
            Assert.Equal(finalHash, found.Sha256);

            await Assert.ThrowsAnyAsync<AmazonS3Exception>(() =>
                client.GetObjectMetadataAsync(RealBucket, $"imports/exam-drafts/{abandonedId:D}/assets/{relative}"));
        }
        finally
        {
            await client.DeleteObjectAsync(RealBucket, relative);
            await client.DeleteObjectAsync(RealBucket, $"imports/exam-drafts/{ownerId:D}/assets/{relative}");
            await client.DeleteObjectAsync(RealBucket, $"imports/exam-drafts/{abandonedId:D}/assets/{relative}");
        }
    }

    [SkippableFact]
    public async Task Approval_cleanup_deletes_only_newly_promoted_and_leaves_preexisting_same_checksum()
    {
        Skip.IfNot(ObjectStorageProbe.MinioAvailable, ObjectStorageProbe.SkipReason);

        var store = NewStore(out var client);
        var existingRef = $"assets/import-asset/existing-{Guid.NewGuid():n}.mp3";
        var createdRef = $"assets/import-asset/created-{Guid.NewGuid():n}.mp3";
        var existingBytes = SyntheticMp3("existing");
        var createdBytes = SyntheticMp3("created");
        var existingHash = Convert.ToHexStringLower(SHA256.HashData(existingBytes));
        var createdHash = Convert.ToHexStringLower(SHA256.HashData(createdBytes));
        var existingId = Guid.NewGuid();
        var createdId = Guid.NewGuid();

        try
        {
            await using (var input = new MemoryStream(existingBytes, writable: false))
            {
                var staged = await store.StageAsync(existingId, existingRef, input, "audio/mpeg", existingBytes.Length, existingHash, default);
                Assert.Equal(ImportAssetPromotionStatus.PromotedThisAttempt, (await store.PromoteAsync(staged, default)).Status);
            }

            await using (var input = new MemoryStream(createdBytes, writable: false))
            {
                var createdStaged = await store.StageAsync(createdId, createdRef, input, "audio/mpeg", createdBytes.Length, createdHash, default);
                Assert.Equal(ImportAssetPromotionStatus.PromotedThisAttempt, (await store.PromoteAsync(createdStaged, default)).Status);
            }

            using var compensation = ImportAssetCompensation.Start();
            await store.RecordCleanupIntentAsync(
                createdId, [createdRef], ImportAssetCleanupReason.ApprovalCommitFailed, compensation.Token);
            await store.ProcessPendingCleanupAsync(new EmptyCatalogue(), default);

            Assert.Equal(existingHash, (await store.CheckFinalAsync(existingRef, default)).Sha256);
            Assert.Equal(ImportAssetAvailabilityKind.Absent, (await store.CheckFinalAsync(createdRef, default)).Kind);
        }
        finally
        {
            await client.DeleteObjectAsync(RealBucket, existingRef["assets/".Length..]);
            await client.DeleteObjectAsync(RealBucket, createdRef["assets/".Length..]);
            await client.DeleteObjectAsync(RealBucket, $"imports/exam-drafts/{existingId:D}/assets/{existingRef["assets/".Length..]}");
            await client.DeleteObjectAsync(RealBucket, $"imports/exam-drafts/{createdId:D}/assets/{createdRef["assets/".Length..]}");
        }
    }

    private sealed class EmptyCatalogue : IExamCatalogue
    {
        public Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([]);

        public Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([]);

        public Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct) =>
            Task.FromResult<ExamVersion?>(null);

        public Task UpsertAsync(ExamVersion version, CancellationToken ct) => Task.CompletedTask;

        public Task SetStatusAsync(ExamVersionId id, ExamVersionStatus status, CancellationToken ct) =>
            Task.CompletedTask;

        public Task DeleteAsync(ExamVersionId id, CancellationToken ct) => Task.CompletedTask;
    }

    private static S3ImportExamAssetStore NewStore(out AmazonS3Client client)
    {
        client = new AmazonS3Client(
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

        var options = new ObjectStorageOptions { ExamAssetsBucket = RealBucket, ServiceUrl = "http://localhost:9000" };
        var mongo = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_import_asset_{Guid.NewGuid():n}",
        }));
        return new S3ImportExamAssetStore(client, options, mongo);
    }

    private static byte[] SyntheticMp3(string salt)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(salt + Guid.NewGuid().ToString("n"));
        var bytes = new byte[3 + payload.Length];
        bytes[0] = 0x49;
        bytes[1] = 0x44;
        bytes[2] = 0x33;
        Buffer.BlockCopy(payload, 0, bytes, 3, payload.Length);
        return bytes;
    }
}
