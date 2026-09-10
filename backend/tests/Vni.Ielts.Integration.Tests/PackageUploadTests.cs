using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Api.Common;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The package upload endpoint (`POST /api/v1/admin/packages`), through the
/// real HTTP pipeline. Covers only what Plan 02 owns — receiving and
/// immutably storing an upload — not structural validation (Plan 03), which
/// does not exist yet.
/// </summary>
public sealed class PackageUploadTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";

    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private IMongoDatabase Db() => new MongoClient(ConnectionString).GetDatabase(app.Database);

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

    /// <summary>
    /// Grants exactly the given permission set through a throwaway role — not
    /// one of the seeded presets, all three of which hold both
    /// <c>package.upload</c> and <c>exam.create</c> together. Testing that
    /// either one alone is insufficient needs a role no preset has.
    /// </summary>
    private async Task GrantExactPermissionsAsync(string userId, string roleLabel, params string[] permissions)
    {
        var roles = Db().GetCollection<BsonDocument>("roles");
        var roleId = Guid.NewGuid().ToString("n");
        await roles.InsertOneAsync(new BsonDocument
        {
            ["_id"] = roleId,
            // Unique per call — `roles.name` has a uniqueness index, and every
            // test in this class shares one database (IClassFixture).
            ["name"] = $"{roleLabel}-{roleId}",
            ["isSystem"] = false,
            ["permissions"] = new BsonArray(permissions),
        });

        var users = Db().GetCollection<BsonDocument>("users");
        await users.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", userId),
            Builders<BsonDocument>.Update.Set("roleIds", new BsonArray { roleId }));
    }

    private static HttpRequestMessage UploadRequest(
        string access, string fileName, byte[] bytes, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/packages");
        // Boundary is fixed only so two test requests share a stable fixture.
        // IdempotencyMiddleware no longer hashes multipart bodies (the browser
        // boundary changes on a real retry), so this handler does not replay.
        var form = new MultipartFormDataContent("fixed-test-boundary");
        var fileContent = new ByteArrayContent(bytes);
        form.Add(fileContent, "package", fileName);
        request.Content = form;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add(IdempotencyMiddleware.HeaderName, idempotencyKey);
        return request;
    }

    private static byte[] TinyJsonPackage() => Encoding.UTF8.GetBytes("""{"title":"demo"}""");

    [SkippableFact]
    public async Task Missing_package_upload_is_forbidden_even_with_exam_create()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "creator-only", "exam.create");
        var (access, _) = await SignInAsync(client);

        var response = await client.SendAsync(
            UploadRequest(access, "demo.json", TinyJsonPackage(), Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task Missing_exam_create_is_forbidden_even_with_package_upload()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "uploader-only", "package.upload");
        var (access, _) = await SignInAsync(client);

        var response = await client.SendAsync(
            UploadRequest(access, "demo.json", TinyJsonPackage(), Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task A_holder_of_both_permissions_can_upload_a_json_package()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var response = await client.SendAsync(
            UploadRequest(access, "demo.json", TinyJsonPackage(), Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("uploaded", body.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("packageId").GetString()));

        var stored = await Db().GetCollection<BsonDocument>("exam_packages")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", body.GetProperty("packageId").GetString()))
            .FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal("Json", stored["sourceKind"].AsString);
        Assert.Equal("Uploaded", stored["status"].AsString);
        Assert.False(string.IsNullOrWhiteSpace(stored["sha256"].AsString));
    }

    [SkippableFact]
    public async Task An_unrecognised_file_extension_creates_visible_rejected_package_row()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var response = await client.SendAsync(
            UploadRequest(access, "demo.pdf", TinyJsonPackage(), Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var packageId = body.GetProperty("packageId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(packageId));
        Assert.Equal("rejected", body.GetProperty("status").GetString());

        var stored = await Db().GetCollection<BsonDocument>("exam_packages")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", packageId))
            .FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal("Rejected", stored["status"].AsString);
        Assert.True(stored["findings"].AsBsonArray.Count > 0);
    }

    [SkippableFact]
    public async Task An_idempotent_retry_replays_the_same_package()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var key = Guid.NewGuid().ToString("n");
        var first = await client.SendAsync(UploadRequest(access, "demo.json", TinyJsonPackage(), key));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var firstPkgId = firstBody.GetProperty("packageId").GetString();

        var second = await client.SendAsync(UploadRequest(access, "demo.json", TinyJsonPackage(), key));
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        var secondPkgId = secondBody.GetProperty("packageId").GetString();

        Assert.Equal(firstPkgId, secondPkgId);

        var stored = await Db().GetCollection<BsonDocument>("exam_packages")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", firstPkgId))
            .ToListAsync();
        Assert.Single(stored);
    }

    [SkippableFact]
    public async Task Same_key_with_different_bytes_returns_conflict()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var key = Guid.NewGuid().ToString("n");
        var bytes1 = Encoding.UTF8.GetBytes("""{"title":"first"}""");
        var bytes2 = Encoding.UTF8.GetBytes("""{"title":"second"}""");

        var first = await client.SendAsync(UploadRequest(access, "demo.json", bytes1, key));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var second = await client.SendAsync(UploadRequest(access, "demo.json", bytes2, key));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [SkippableFact]
    public async Task Different_keys_with_same_bytes_creates_two_distinct_packages()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var key1 = Guid.NewGuid().ToString("n");
        var key2 = Guid.NewGuid().ToString("n");

        var first = await client.SendAsync(UploadRequest(access, "demo.json", TinyJsonPackage(), key1));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var firstId = firstBody.GetProperty("packageId").GetString()!;

        var second = await client.SendAsync(UploadRequest(access, "demo.json", TinyJsonPackage(), key2));
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        var secondId = secondBody.GetProperty("packageId").GetString()!;

        Assert.NotEqual(firstId, secondId);

        var stored = await Db().GetCollection<BsonDocument>("exam_packages")
            .Find(Builders<BsonDocument>.Filter.In("_id", new[] { firstId, secondId }))
            .ToListAsync();
        Assert.Equal(2, stored.Count);
    }

    [SkippableFact]
    public async Task Empty_file_does_not_create_package_row()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var initialCount = await Db().GetCollection<BsonDocument>("exam_packages").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);

        var response = await client.SendAsync(
            UploadRequest(access, "empty.json", [], Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var finalCount = await Db().GetCollection<BsonDocument>("exam_packages").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        Assert.Equal(initialCount, finalCount);
    }

    [SkippableFact]
    public async Task Twenty_concurrent_requests_with_same_actor_key_and_bytes_returns_one_packageId_and_creates_one_package_row()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var key = Guid.NewGuid().ToString("n");
        var bytes = TinyJsonPackage();

        var tasks = Enumerable.Range(0, 20).Select(_ =>
        {
            var req = UploadRequest(access, "demo.json", bytes, key);
            return client.SendAsync(req);
        }).ToArray();

        var responses = await Task.WhenAll(tasks);
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var packageIds = new HashSet<string>();
        foreach (var response in responses)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            packageIds.Add(body.GetProperty("packageId").GetString()!);
        }

        Assert.Single(packageIds);
        var pkgId = packageIds.Single();

        var stored = await Db().GetCollection<BsonDocument>("exam_packages")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", pkgId))
            .ToListAsync();
        Assert.Single(stored);
    }

    [SkippableFact]
    public async Task Concurrent_same_key_with_different_bytes_returns_one_success_one_conflict_and_creates_one_package_row()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var key = Guid.NewGuid().ToString("n");
        var bytes1 = Encoding.UTF8.GetBytes("""{"title":"first"}""");
        var bytes2 = Encoding.UTF8.GetBytes("""{"title":"second"}""");

        var task1 = client.SendAsync(UploadRequest(access, "demo.json", bytes1, key));
        var task2 = client.SendAsync(UploadRequest(access, "demo.json", bytes2, key));

        var responses = await Task.WhenAll(task1, task2);
        var statusCodes = responses.Select(r => r.StatusCode).OrderBy(s => s).ToArray();

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Conflict], statusCodes);

        var successResponse = responses.First(r => r.StatusCode == HttpStatusCode.Accepted);
        var successBody = await successResponse.Content.ReadFromJsonAsync<JsonElement>();
        var packageId = successBody.GetProperty("packageId").GetString()!;

        var successStored = await Db().GetCollection<BsonDocument>("exam_packages")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", packageId))
            .ToListAsync();
        Assert.Single(successStored);
    }
}
