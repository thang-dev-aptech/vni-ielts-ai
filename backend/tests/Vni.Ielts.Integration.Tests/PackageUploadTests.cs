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
        // A fixed boundary, not the default random one — a real network retry
        // resends identical bytes, and IdempotencyMiddleware compares a hash
        // of the raw body. Two `MultipartFormDataContent` instances with
        // different random boundaries would hash differently even though the
        // file part is byte-for-byte the same, which would make a genuine
        // replay look like key reuse with a different body (409, not 202).
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
    public async Task An_unrecognised_file_extension_is_rejected()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var response = await client.SendAsync(
            UploadRequest(access, "demo.pdf", TinyJsonPackage(), Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SkippableFact]
    public async Task A_repeated_idempotency_key_replays_the_first_response_without_a_second_package()
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

        var second = await client.SendAsync(UploadRequest(access, "demo.json", TinyJsonPackage(), key));
        var secondBodyText = await second.Content.ReadAsStringAsync();
        Assert.True(second.StatusCode == HttpStatusCode.Accepted, $"status={second.StatusCode} body={secondBodyText}");
        var secondBody = JsonDocument.Parse(secondBodyText).RootElement;

        Assert.Equal(
            firstBody.GetProperty("packageId").GetString(),
            secondBody.GetProperty("packageId").GetString());

        var count = await Db().GetCollection<BsonDocument>("exam_packages")
            .Find(Builders<BsonDocument>.Filter.Eq(
                "_id", firstBody.GetProperty("packageId").GetString()))
            .CountDocumentsAsync();
        Assert.Equal(1, count);
    }
}
