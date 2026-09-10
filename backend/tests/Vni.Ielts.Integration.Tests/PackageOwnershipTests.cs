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
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// ZIP upload (User A) vs confirm-to-Draft (User B). Confirm requires
/// <c>package.confirm</c> (shared review), not <c>package.upload</c>.
/// <c>AuthorId</c> is the confirmer. <c>package.read</c> is a shared inbox.
/// </summary>
public sealed class PackageOwnershipTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";
    private const string Password = "mot-mat-khau-du-dai-2026";

    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private IMongoDatabase Db() => new MongoClient(ConnectionString).GetDatabase(app.Database);

    private static async Task<(string Access, string UserId)> RegisterAsync(HttpClient client, string phone)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new { phone, password = Password, displayName = "Zip owner" }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("n"));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var session = body.GetProperty("session");
        return (session.GetProperty("accessToken").GetString()!, session.GetProperty("userId").GetString()!);
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

        await Db().GetCollection<BsonDocument>("users").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", userId),
            Builders<BsonDocument>.Update.Set("roleIds", new BsonArray { roleId }));
    }

    private static string MiniExam(string title) => $$"""
        {
          "formatVersion": "1.0", "title": "{{title}}", "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [ { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 5 } ] } },
          "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1, "kind": "passage",
            "questions": [
              { "id": "r-1", "order": 1, "type": "short-answer", "prompt": "Sample",
                "answerKey": { "accepted": ["ok"] } }
            ] } ] } ]
        }
        """;

    private static byte[] TwoExamZip()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string text)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(text);
            }

            Add("manifest.json", """{ "formatVersion": "1.0", "exams": ["exams/a.json", "exams/b.json"], "assets": [] }""");
            Add("exams/a.json", MiniExam("Ownership A"));
            Add("exams/b.json", MiniExam("Ownership B"));
        }

        return stream.ToArray();
    }

    private static HttpRequestMessage UploadRequest(string access, byte[] bytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/packages");
        var form = new MultipartFormDataContent("ownership-zip-boundary");
        form.Add(new ByteArrayContent(bytes), "package", "two-exams.zip");
        request.Content = form;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
        return request;
    }

    private async Task ProcessOnceAsync(string packageId)
    {
        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var processor = scope.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();
        var package = await repository.FindAsync(packageId, CancellationToken.None);
        await processor.ProcessAsync(package!, CancellationToken.None);
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, string access)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (method != HttpMethod.Get)
            request.Headers.Add(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
        return request;
    }

    [SkippableFact]
    public async Task Zip_confirmer_owns_the_draft_uploader_does_not()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var phoneA = $"09{Random.Shared.NextInt64(0, 100_000_000):D8}";
        var phoneB = $"09{Random.Shared.NextInt64(0, 100_000_000):D8}";
        var (accessA, userA) = await RegisterAsync(client, phoneA);
        var (accessB, userB) = await RegisterAsync(client, phoneB);
        Assert.NotEqual(userA, userB);

        await GrantExactPermissionsAsync(userA, "uploader", "package.upload", "exam.create", "exam.read.own");
        await GrantExactPermissionsAsync(userB, "confirmer", "package.confirm", "exam.create", "exam.read.own");

        // Re-login after role grant so the access token carries the new permissions.
        var loginA = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = phoneA, password = Password });
        loginA.EnsureSuccessStatusCode();
        accessA = (await loginA.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        var loginB = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = phoneB, password = Password });
        loginB.EnsureSuccessStatusCode();
        accessB = (await loginB.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var upload = await client.SendAsync(UploadRequest(accessA, TwoExamZip()));
        upload.EnsureSuccessStatusCode();
        var packageId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("packageId").GetString()!;

        await ProcessOnceAsync(packageId);

        var storedPackage = await Db().GetCollection<BsonDocument>("exam_packages")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", packageId)).FirstAsync();
        Assert.Equal(userA, storedPackage["uploadedBy"].AsString);
        Assert.Equal("ReadyToImport", storedPackage["status"].AsString);

        var confirm = Authed(HttpMethod.Post, $"/api/v1/admin/packages/{packageId}/confirm", accessB);
        var confirmed = await client.SendAsync(confirm);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var versionIds = (await confirmed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("createdVersionIds").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(2, versionIds.Count);

        foreach (var versionId in versionIds)
        {
            var version = await Db().GetCollection<BsonDocument>("exam_versions")
                .Find(Builders<BsonDocument>.Filter.Eq("_id", versionId)).FirstAsync();
            Assert.Equal(userB, version["authorId"].AsString);
        }

        var listB = await client.SendAsync(Authed(HttpMethod.Get, "/api/v1/admin/exams", accessB));
        listB.EnsureSuccessStatusCode();
        var examsB = (await listB.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("exams");
        var owned = examsB.EnumerateArray().Where(e => versionIds.Contains(e.GetProperty("examVersionId").GetString()!)).ToList();
        Assert.Equal(2, owned.Count);
        Assert.All(owned, exam =>
        {
            Assert.Equal(userB, exam.GetProperty("authorId").GetString());
        });

        var listA = await client.SendAsync(Authed(HttpMethod.Get, "/api/v1/admin/exams", accessA));
        listA.EnsureSuccessStatusCode();
        var examsA = (await listA.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("exams");
        Assert.DoesNotContain(examsA.EnumerateArray(), e => versionIds.Contains(e.GetProperty("examVersionId").GetString()!));

        var hidden = await client.SendAsync(Authed(HttpMethod.Get, $"/api/v1/admin/exams/{versionIds[0]}", accessA));
        Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
    }

    [SkippableFact]
    public async Task Uploader_without_package_confirm_cannot_confirm()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var phoneA = $"09{Random.Shared.NextInt64(0, 100_000_000):D8}";
        var phoneB = $"09{Random.Shared.NextInt64(0, 100_000_000):D8}";
        var (accessA, userA) = await RegisterAsync(client, phoneA);
        var (accessB, userB) = await RegisterAsync(client, phoneB);

        await GrantExactPermissionsAsync(userA, "uploader", "package.upload", "exam.create", "exam.read.own");
        // B has upload (and create) but deliberately no package.confirm.
        await GrantExactPermissionsAsync(userB, "uploader-only", "package.upload", "exam.create", "exam.read.own");

        var loginA = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = phoneA, password = Password });
        loginA.EnsureSuccessStatusCode();
        accessA = (await loginA.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        var loginB = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = phoneB, password = Password });
        loginB.EnsureSuccessStatusCode();
        accessB = (await loginB.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var upload = await client.SendAsync(UploadRequest(accessA, TwoExamZip()));
        upload.EnsureSuccessStatusCode();
        var packageId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("packageId").GetString()!;
        await ProcessOnceAsync(packageId);

        var denied = await client.SendAsync(
            Authed(HttpMethod.Post, $"/api/v1/admin/packages/{packageId}/confirm", accessB));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [SkippableFact]
    public async Task Reviewer_holding_exam_review_can_read_package()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var phoneA = $"09{Random.Shared.NextInt64(0, 100_000_000):D8}";
        var phoneR = $"09{Random.Shared.NextInt64(0, 100_000_000):D8}";
        var (accessA, userA) = await RegisterAsync(client, phoneA);
        var (accessR, userR) = await RegisterAsync(client, phoneR);

        await GrantExactPermissionsAsync(userA, "uploader", "package.upload", "exam.create", "package.read");
        await GrantExactPermissionsAsync(userR, "reviewer", "exam.review");

        var loginA = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = phoneA, password = Password });
        loginA.EnsureSuccessStatusCode();
        accessA = (await loginA.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var loginR = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { identifier = phoneR, password = Password });
        loginR.EnsureSuccessStatusCode();
        accessR = (await loginR.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var upload = await client.SendAsync(UploadRequest(accessA, TwoExamZip()));
        upload.EnsureSuccessStatusCode();
        var packageId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("packageId").GetString()!;

        // Reviewer can read the package
        var getPkg = await client.SendAsync(Authed(HttpMethod.Get, $"/api/v1/admin/packages/{packageId}", accessR));
        Assert.Equal(HttpStatusCode.OK, getPkg.StatusCode);

        // Reviewer can list packages
        var listPkg = await client.SendAsync(Authed(HttpMethod.Get, "/api/v1/admin/packages", accessR));
        Assert.Equal(HttpStatusCode.OK, listPkg.StatusCode);
    }
}
