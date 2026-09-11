using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Infrastructure.Persistence.Media;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Media upload/delete must not leave orphan objects or lose metadata on
/// partial failure. Requires MinIO (same stack as media smoke).
/// </summary>
public sealed class MediaPartialFailureTests(FaultInjectionAppFactory app)
    : IClassFixture<FaultInjectionAppFactory>
{
    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";

    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, 0x00, 0x00, 0x00,
        0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x00, 0x03, 0x00, 0x01, 0x00, 0x05, 0xFE, 0x02, 0xFE, 0xDC, 0xCC,
        0x59, 0xE7, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42,
        0x60, 0x82,
    ];

    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private IMongoDatabase Db() => new MongoClient(ConnectionString).GetDatabase(app.Database);

    private MediaFaultInjection Fault => app.MediaFault;

    private static async Task<(string Access, string UserId)> SignInAsync(HttpClient client)
    {
        var start = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
        var url = new Uri((await start.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("authorizationUrl").GetString()!);
        var callback = await client.GetAsync(url.PathAndQuery);
        var code = System.Web.HttpUtility.ParseQueryString(callback.Headers.Location!.Query)["code"];
        var complete = await client.PostAsJsonAsync("/api/v1/auth/sso/complete", new { handoffCode = code });
        complete.EnsureSuccessStatusCode();
        var access = (await complete.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;
        var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var meResponse = await client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var userId = (await meResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("userId").GetString()!;
        return (access, userId);
    }

    private async Task GrantRoleAsync(string userId, string roleName)
    {
        var role = await Db().GetCollection<BsonDocument>("roles")
            .Find(Builders<BsonDocument>.Filter.Eq("name", roleName)).FirstAsync();
        await Db().GetCollection<BsonDocument>("users").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", userId),
            Builders<BsonDocument>.Update.Set("roleIds", new BsonArray { role["_id"].AsString }));
    }

    private static HttpRequestMessage UploadRequest(string access, string fileName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/media");
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(TinyPng), "file", fileName);
        request.Content = form;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
        return request;
    }

    [SkippableFact]
    public async Task Upload_save_failure_compensates_by_deleting_storage_object()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        Fault.FailNextSaves = 1;
        Fault.PutKeys.Clear();
        Fault.DeletedKeys.Clear();
        Fault.LastDeleteTokenWasCanceled = null;

        try
        {
            var client = NewClient();
            var (_, userId) = await SignInAsync(client);
            await GrantRoleAsync(userId, "admin");
            var (access, _) = await SignInAsync(client);

            var fileName = $"orphan-check-{Guid.NewGuid():n}.png";
            var response = await client.SendAsync(UploadRequest(access, fileName));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            using var scope = app.Services.CreateScope();
            var media = scope.ServiceProvider.GetRequiredService<IMediaAssetRepository>();
            Assert.DoesNotContain(await media.ListAsync(CancellationToken.None), a => a.FileName == fileName);

            Assert.NotEmpty(Fault.PutKeys);
            Assert.Contains(Fault.PutKeys.Last(), Fault.DeletedKeys);
            Assert.False(Fault.LastDeleteTokenWasCanceled);

            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
            await Assert.ThrowsAnyAsync<Exception>(() => storage.OpenAsync(Fault.PutKeys.Last(), CancellationToken.None));
        }
        finally
        {
            Fault.FailNextSaves = 0;
        }
    }

    [SkippableFact]
    public async Task Upload_compensation_uses_cleanup_token_when_request_token_already_canceled()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        Fault.ThrowCanceledOnSave = true;
        Fault.PutKeys.Clear();
        Fault.DeletedKeys.Clear();
        Fault.LastDeleteTokenWasCanceled = null;

        try
        {
            var client = NewClient();
            var (_, userId) = await SignInAsync(client);
            await GrantRoleAsync(userId, "admin");
            var (access, _) = await SignInAsync(client);

            var fileName = $"canceled-req-{Guid.NewGuid():n}.png";
            var response = await client.SendAsync(UploadRequest(access, fileName));
            Assert.True((int)response.StatusCode >= 400, $"expected failure status, got {response.StatusCode}");

            Assert.NotEmpty(Fault.PutKeys);
            Assert.Contains(Fault.PutKeys.Last(), Fault.DeletedKeys);
            Assert.False(Fault.LastDeleteTokenWasCanceled);
        }
        finally
        {
            Fault.ThrowCanceledOnSave = false;
        }
    }

    [SkippableFact]
    public async Task Upload_cleanup_failure_records_durable_orphan_intent()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        Fault.FailNextSaves = 1;
        Fault.FailNextStorageDelete = true;
        Fault.PutKeys.Clear();
        Fault.DeletedKeys.Clear();

        try
        {
            var client = NewClient();
            var (_, userId) = await SignInAsync(client);
            await GrantRoleAsync(userId, "admin");
            var (access, _) = await SignInAsync(client);

            var fileName = $"orphan-intent-{Guid.NewGuid():n}.png";
            var response = await client.SendAsync(UploadRequest(access, fileName));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            Assert.NotEmpty(Fault.PutKeys);
            var key = Fault.PutKeys.Last();
            Assert.DoesNotContain(key, Fault.DeletedKeys);

            var intents = await Db().GetCollection<BsonDocument>("media_orphan_intents")
                .Find(Builders<BsonDocument>.Filter.Eq("storageKey", key))
                .ToListAsync();
            Assert.NotEmpty(intents);
            Assert.Equal(MediaCleanupErrorCodes.Failed, intents[0]["errorCode"].AsString);
            Assert.False(intents[0].Contains("reason"));

            var audits = await Db().GetCollection<BsonDocument>("audit_log")
                .Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("action", "MediaOrphanCleanupFailed"),
                    Builders<BsonDocument>.Filter.Eq("targetId", key)))
                .ToListAsync();
            Assert.NotEmpty(audits);
            var detail = audits[0]["detail"].AsBsonArray
                .First(d => d["k"].AsString == "errorCode");
            Assert.Equal(MediaCleanupErrorCodes.Failed, detail["v"].AsString);
        }
        finally
        {
            Fault.FailNextSaves = 0;
            Fault.FailNextStorageDelete = false;
        }
    }

    [SkippableFact]
    public async Task Upload_orphan_audit_failure_retains_intent_for_reconciliation()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        Fault.FailNextSaves = 1;
        Fault.FailNextStorageDelete = true;
        Fault.PutKeys.Clear();
        Fault.DeletedKeys.Clear();
        app.OrphanHooks.AfterIntentInserted = _ =>
            throw new InvalidOperationException("injected: abort after intent insert, before audit");

        try
        {
            var client = NewClient();
            var (_, userId) = await SignInAsync(client);
            await GrantRoleAsync(userId, "admin");
            var (access, _) = await SignInAsync(client);

            var fileName = $"orphan-rollback-{Guid.NewGuid():n}.png";
            var response = await client.SendAsync(UploadRequest(access, fileName));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            Assert.NotEmpty(Fault.PutKeys);
            var key = Fault.PutKeys.Last();

            var discoverable = await Db().GetCollection<BsonDocument>("media_orphan_intents")
                .Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("storageKey", key),
                    Builders<BsonDocument>.Filter.Eq("status", "pending")))
                .SingleAsync();
            Assert.Equal(key, discoverable["storageKey"].AsString);
            Assert.Equal(MediaCleanupErrorCodes.Failed, discoverable["errorCode"].AsString);
            Assert.Empty(await Db().GetCollection<BsonDocument>("audit_log")
                .Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("action", "MediaOrphanCleanupFailed"),
                    Builders<BsonDocument>.Filter.Eq("targetId", key)))
                .ToListAsync());
        }
        finally
        {
            Fault.FailNextSaves = 0;
            Fault.FailNextStorageDelete = false;
            app.OrphanHooks.AfterIntentInserted = null;
        }
    }

    [SkippableFact]
    public async Task Delete_metadata_failure_keeps_row_so_retry_can_finish()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        Fault.FailNextDeletes = 0;
        Fault.PutKeys.Clear();
        Fault.DeletedKeys.Clear();

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "admin");
        var (access, _) = await SignInAsync(client);

        var fileName = $"delete-retry-{Guid.NewGuid():n}.png";
        var upload = await client.SendAsync(UploadRequest(access, fileName));
        upload.EnsureSuccessStatusCode();
        var mediaId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mediaId").GetString()!;

        Fault.FailNextDeletes = 1;
        try
        {
            var del = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/media/{mediaId}");
            del.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            del.Headers.Add(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
            var failed = await client.SendAsync(del);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

            using var scope = app.Services.CreateScope();
            var still = await scope.ServiceProvider.GetRequiredService<IMediaAssetRepository>()
                .FindAsync(mediaId, CancellationToken.None);
            Assert.NotNull(still);
            Assert.Contains(still.StorageKey, Fault.DeletedKeys);
        }
        finally
        {
            Fault.FailNextDeletes = 0;
        }

        var retry = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/media/{mediaId}");
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        retry.Headers.Add(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
        var ok = await client.SendAsync(retry);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        using var verify = app.Services.CreateScope();
        Assert.Null(await verify.ServiceProvider.GetRequiredService<IMediaAssetRepository>()
            .FindAsync(mediaId, CancellationToken.None));
    }
}
