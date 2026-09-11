using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests.Persistence;

public sealed class GridFsPackageUploadStoreTests
{
    private static (GridFsPackageUploadStore Store, IMongoDatabase Db) CreateStore()
    {
        var dbName = $"vni_gridfs_pkg_test_{Guid.NewGuid():n}";
        var client = new MongoClient("mongodb://localhost:27018/?directConnection=true");
        var db = client.GetDatabase(dbName);
        var clock = new SystemClock();
        return (new GridFsPackageUploadStore(db, clock), db);
    }

    [Fact]
    public async Task SaveAsync_stores_content_OpenAsync_reads_it_and_DeleteAsync_removes_it()
    {
        var (store, _) = CreateStore();
        var bytes = "sample package content"u8.ToArray();
        using var stream = new MemoryStream(bytes);

        var uploadRef = await store.SaveAsync(stream, "test.zip", "application/zip", CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(uploadRef));

        // Open and verify content
        await using (var readStream = await store.OpenAsync(uploadRef, CancellationToken.None))
        {
            using var ms = new MemoryStream();
            await readStream.CopyToAsync(ms);
            Assert.Equal(bytes, ms.ToArray());
        }

        // Delete (used for compensation on save failure)
        await store.DeleteAsync(uploadRef, CancellationToken.None);

        // Attempting to open after delete should throw
        await Assert.ThrowsAsync<MongoDB.Driver.GridFS.GridFSFileNotFoundException>(
            () => store.OpenAsync(uploadRef, CancellationToken.None));
    }

    [Fact]
    public async Task Explicit_upload_reference_is_the_GridFS_id_and_cannot_create_duplicate_blob()
    {
        var (store, db) = CreateStore();
        const string uploadRef = "pkg-upload:deterministic";
        using var first = new MemoryStream("first"u8.ToArray());
        await store.SaveAsync(first, "test.zip", "application/zip", uploadRef, CancellationToken.None);

        using var second = new MemoryStream("second"u8.ToArray());
        await Assert.ThrowsAnyAsync<MongoException>(() =>
            store.SaveAsync(second, "test.zip", "application/zip", uploadRef, CancellationToken.None));

        var files = db.GetCollection<MongoDB.Bson.BsonDocument>("package_uploads.files");
        Assert.Equal(1, await files.CountDocumentsAsync(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("filename", uploadRef)));
    }

    [Fact]
    public async Task DeleteAsync_is_noop_for_non_existent_uploadRef()
    {
        var (store, _) = CreateStore();
        // Should not throw
        await store.DeleteAsync("non-existent-ref", CancellationToken.None);
    }
}
