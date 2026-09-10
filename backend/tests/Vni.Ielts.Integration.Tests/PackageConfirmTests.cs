using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Plan 05 of `phase-2-content-import-plan.md` — confirming a multi-exam
/// ZIP's `ReadyToImport` list into N real `Draft`s, through the real HTTP
/// pipeline and a real Mongo transaction.
///
/// <b>The Worker process is not part of this test host.</b>
/// `PackageIngestionProcessor` is resolved and invoked directly (the same
/// class the real Worker's poll loop calls) to drive a freshly uploaded
/// package to <c>ReadyToImport</c>, rather than waiting on a separate process
/// this suite does not start.
/// </summary>
public sealed class PackageConfirmTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";

    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

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

    private async Task GrantExactPermissionsAsync(string userId, string roleLabel, params string[] permissions)
    {
        var roles = Db().GetCollection<BsonDocument>("roles");
        var roleId = Guid.NewGuid().ToString("n");
        await roles.InsertOneAsync(new BsonDocument
        {
            ["_id"] = roleId,
            ["name"] = $"{roleLabel}-{roleId}",
            ["isSystem"] = false,
            ["permissions"] = new BsonArray(permissions),
        });

        var users = Db().GetCollection<BsonDocument>("users");
        await users.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", userId),
            Builders<BsonDocument>.Update.Set("roleIds", new BsonArray { roleId }));
    }

    private static byte[] BuildTwoExamZip()
    {
        var reading = File.ReadAllText(Path.Combine(RepoRoot, "fixtures/exams/reading-demo.json"));
        var full = File.ReadAllText(Path.Combine(RepoRoot, "fixtures/exams/full-demo.json"));

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string text)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(text);
            }

            Add("manifest.json", """{ "formatVersion": "1.0", "exams": ["exams/reading.json", "exams/full.json"], "assets": [] }""");
            Add("exams/reading.json", reading);
            Add("exams/full.json", full);
        }

        return stream.ToArray();
    }

    private static HttpRequestMessage UploadRequest(string access, byte[] bytes, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/packages");
        var form = new MultipartFormDataContent("fixed-confirm-test-boundary");
        form.Add(new ByteArrayContent(bytes), "package", "two-exams.zip");
        request.Content = form;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add(IdempotencyMiddleware.HeaderName, idempotencyKey);
        return request;
    }

    private static HttpRequestMessage ConfirmRequest(string access, string packageId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/packages/{packageId}/confirm");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add(IdempotencyMiddleware.HeaderName, idempotencyKey);
        return request;
    }

    /// <summary>Drives the package to `ReadyToImport` the same way the real Worker's poll loop would, without starting a second process.</summary>
    private async Task ProcessOnceAsync(string packageId)
    {
        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var processor = scope.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();

        var package = await repository.FindAsync(packageId, CancellationToken.None);
        await processor.ProcessAsync(package!, CancellationToken.None);
    }

    private async Task<string> UploadAndProcessAsync(string access)
    {
        var upload = await NewClient().SendAsync(
            UploadRequest(access, BuildTwoExamZip(), Guid.NewGuid().ToString("n")));
        var packageId = (await upload.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("packageId").GetString()!;

        await ProcessOnceAsync(packageId);
        return packageId;
    }

    [SkippableFact]
    public async Task Uploading_a_two_exam_zip_and_processing_it_reaches_ready_to_import()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var upload = await client.SendAsync(UploadRequest(access, BuildTwoExamZip(), Guid.NewGuid().ToString("n")));
        var packageId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("packageId").GetString()!;

        await ProcessOnceAsync(packageId);

        var stored = await Db().GetCollection<BsonDocument>("exam_packages")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", packageId))
            .FirstOrDefaultAsync();
        Assert.Equal("ReadyToImport", stored["status"].AsString);
        Assert.Equal(2, stored["entries"].AsBsonArray.Count);
    }

    [SkippableFact]
    public async Task Confirming_without_permission_is_forbidden()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);
        var packageId = await UploadAndProcessAsync(access);

        await GrantExactPermissionsAsync(userId, "no-confirm", "package.upload", "exam.create");
        var (weakerAccess, _) = await SignInAsync(client);

        var response = await client.SendAsync(ConfirmRequest(weakerAccess, packageId, Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task Confirming_creates_a_draft_per_exam_with_the_confirmer_as_creator()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "confirmer", "package.upload", "package.confirm", "exam.create");
        var (access, _) = await SignInAsync(client);
        var packageId = await UploadAndProcessAsync(access);

        var response = await client.SendAsync(ConfirmRequest(access, packageId, Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var versionIds = body.GetProperty("createdVersionIds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(2, versionIds.Count);

        foreach (var versionId in versionIds)
        {
            var version = await Db().GetCollection<BsonDocument>("exam_versions")
                .Find(Builders<BsonDocument>.Filter.Eq("_id", versionId))
                .FirstOrDefaultAsync();
            Assert.NotNull(version);
            Assert.Equal("Draft", version["status"].AsString);
            Assert.Equal(userId, version["authorId"].AsString);
        }

        var package = await Db().GetCollection<BsonDocument>("exam_packages")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", packageId))
            .FirstOrDefaultAsync();
        Assert.Equal("Imported", package["status"].AsString);
        Assert.Equal(2, package["createdVersionIds"].AsBsonArray.Count);
    }

    [SkippableFact]
    public async Task Confirming_an_already_imported_package_is_rejected()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "confirmer", "package.upload", "package.confirm", "exam.create");
        var (access, _) = await SignInAsync(client);
        var packageId = await UploadAndProcessAsync(access);

        var first = await client.SendAsync(ConfirmRequest(access, packageId, Guid.NewGuid().ToString("n")));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstIds = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("createdVersionIds").EnumerateArray().Select(e => e.GetString()!).ToList();

        var second = await client.SendAsync(ConfirmRequest(access, packageId, Guid.NewGuid().ToString("n")));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // The package still claims exactly the ids the first confirm created
        // — this test's shared class database also carries other tests'
        // packages, so the assertion is scoped to this one package rather
        // than counting exam_versions globally.
        var package = await Db().GetCollection<BsonDocument>("exam_packages")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", packageId))
            .FirstOrDefaultAsync();
        var storedIds = package["createdVersionIds"].AsBsonArray.Select(v => v.AsString).ToList();
        Assert.Equal(firstIds, storedIds);
    }

    [SkippableFact]
    public async Task A_repeated_idempotency_key_replays_the_first_confirm_without_a_second_batch()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "confirmer", "package.upload", "package.confirm", "exam.create");
        var (access, _) = await SignInAsync(client);
        var packageId = await UploadAndProcessAsync(access);

        var key = Guid.NewGuid().ToString("n");
        var first = await client.SendAsync(ConfirmRequest(access, packageId, key));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstIds = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("createdVersionIds").EnumerateArray().Select(e => e.GetString()).ToList();

        var second = await client.SendAsync(ConfirmRequest(access, packageId, key));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondIds = (await second.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("createdVersionIds").EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Equal(firstIds, secondIds);
    }

    [SkippableFact]
    public async Task Confirming_a_missing_package_is_not_found()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "confirmer", "package.upload", "package.confirm", "exam.create");
        var (access, _) = await SignInAsync(client);

        var response = await client.SendAsync(
            ConfirmRequest(access, "does-not-exist", Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task Getting_a_package_shows_its_status_findings_and_entries()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "reader", "package.upload", "package.read", "exam.create");
        var (access, _) = await SignInAsync(client);
        var packageId = await UploadAndProcessAsync(access);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/packages/{packageId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready-to-import", body.GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("entries").GetArrayLength());
        Assert.Empty(body.GetProperty("findings").EnumerateArray());
        Assert.True(body.TryGetProperty("importDraftId", out _));
        Assert.True(body.TryGetProperty("failureCode", out _));
        Assert.True(body.TryGetProperty("failureDetail", out _));
    }

    [SkippableFact]
    public async Task Listing_packages_without_permission_is_forbidden()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "no-read", "package.upload");
        var (access, _) = await SignInAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/packages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task A_rejected_package_lists_its_findings_by_stage()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "reader", "package.upload", "package.read", "exam.create");
        var (access, _) = await SignInAsync(client);

        var upload = await client.SendAsync(
            UploadJsonRequest(access, """{"not":"an exam"}"""u8.ToArray(), Guid.NewGuid().ToString("n")));
        var packageId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("packageId").GetString()!;
        await ProcessOnceAsync(packageId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/packages/{packageId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var response = await client.SendAsync(request);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rejected", body.GetProperty("status").GetString());
        var findings = body.GetProperty("findings").EnumerateArray().ToList();
        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal("schema", f.GetProperty("stage").GetString()));
    }

    private static HttpRequestMessage UploadJsonRequest(string access, byte[] bytes, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/packages");
        var form = new MultipartFormDataContent("fixed-single-json-test-boundary");
        form.Add(new ByteArrayContent(bytes), "package", "reading-demo.json");
        request.Content = form;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add(IdempotencyMiddleware.HeaderName, idempotencyKey);
        return request;
    }

    /// <summary>
    /// The other half of Plan 05 — a single-source package that imports
    /// itself with no confirm step at all, once validation passes.
    /// </summary>
    [SkippableFact]
    public async Task A_single_json_upload_is_imported_directly_with_the_uploader_as_creator()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var json = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "fixtures/exams/reading-demo.json"));
        var upload = await client.SendAsync(
            UploadJsonRequest(access, System.Text.Encoding.UTF8.GetBytes(json), Guid.NewGuid().ToString("n")));
        var packageId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("packageId").GetString()!;

        await ProcessOnceAsync(packageId);

        var package = await Db().GetCollection<BsonDocument>("exam_packages")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", packageId))
            .FirstOrDefaultAsync();
        Assert.Equal("Imported", package["status"].AsString);
        var createdIds = package["createdVersionIds"].AsBsonArray.Select(v => v.AsString).ToList();
        Assert.Single(createdIds);

        var version = await Db().GetCollection<BsonDocument>("exam_versions")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", createdIds[0]))
            .FirstOrDefaultAsync();
        Assert.NotNull(version);
        Assert.Equal("Draft", version["status"].AsString);
        Assert.Equal(userId, version["authorId"].AsString);
    }
}
