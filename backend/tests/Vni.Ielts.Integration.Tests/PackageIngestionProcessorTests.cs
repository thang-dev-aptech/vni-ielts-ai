using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Integration.Tests;

public sealed class PackageIngestionProcessorTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";

    private const string ValidPackageJson = """
    {
      "formatVersion": "2.0", "formatProfile": "vni-practice", "scoringProfileRef": "admin-import-test",
      "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
      "title": "Admin import HTTP test", "variant": "academic",
      "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
      "scoringProfile": { "rawToBand": { "reading": [
        { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }, { "minRaw": 2, "band": 2 } ] } },
      "sequenceProfile": { "modules": ["reading"] },
      "sections": [{ "module": "reading", "order": 1, "parts": [{ "order": 1, "kind": "passage",
        "body": "Evidence here.", "questions": [{
          "id": "q-1", "order": 1, "type": "multiple-select", "marks": 2,
          "options": [{ "key": "A", "text": "Alpha" }, { "key": "B", "text": "Beta" }],
          "group": { "id": "bank-1", "instruction": "Choose." },
          "slots": [
            { "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } },
            { "id": "slot-2", "number": 2, "answerKey": { "accepted": ["B"] } }
          ],
          "explanation": { "shortReason": "Both are stated.", "evidence": ["Evidence here."] }
        }]
      }]}]
    }
    """;

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

    private static byte[] BuildZip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    private static HttpRequestMessage UploadRequest(
        string access, string fileName, byte[] bytes, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/packages");
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        form.Add(fileContent, "package", fileName);
        request.Content = form;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add(IdempotencyMiddleware.HeaderName, idempotencyKey);
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

    [SkippableFact]
    public async Task Valid_manifest_single_exam_zip_transitions_package_to_needs_review()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var zipBytes = BuildZip(
            ("manifest.json", """{ "formatVersion": "1.0", "exams": ["exam.json"], "assets": [] }"""),
            ("exam.json", ValidPackageJson));
        var response = await client.SendAsync(
            UploadRequest(access, "manifest-one.zip", zipBytes, Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var packageId = body.GetProperty("packageId").GetString()!;

        await ProcessOnceAsync(packageId);

        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();

        var package = await repository.FindAsync(packageId, CancellationToken.None);
        Assert.NotNull(package);
        Assert.Equal(PackageImportStatus.NeedsReview, package.Status);
        Assert.False(string.IsNullOrWhiteSpace(package.ImportDraftId));

        var draft = await drafts.FindAsync(Guid.Parse(package.ImportDraftId!), CancellationToken.None);
        Assert.NotNull(draft);
        Assert.Equal(ImportApprovalState.ReviewRequired, draft.ApprovalState);
    }

    [SkippableFact]
    public async Task Valid_structured_zip_transitions_package_to_needs_review_and_links_draft()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var zipBytes = BuildZip(("reading/exam.json", ValidPackageJson));
        var response = await client.SendAsync(
            UploadRequest(access, "structured.zip", zipBytes, Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var packageId = body.GetProperty("packageId").GetString()!;

        await ProcessOnceAsync(packageId);

        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();

        var package = await repository.FindAsync(packageId, CancellationToken.None);
        Assert.NotNull(package);
        Assert.Equal(PackageImportStatus.NeedsReview, package.Status);
        Assert.False(string.IsNullOrWhiteSpace(package.ImportDraftId));

        var draft = await drafts.FindAsync(Guid.Parse(package.ImportDraftId!), CancellationToken.None);
        Assert.NotNull(draft);
        Assert.Equal(ImportApprovalState.ReviewRequired, draft.ApprovalState);
    }

    [SkippableFact]
    public async Task Invalid_structured_zip_transitions_package_to_rejected_with_findings()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        // Invalid JSON inside
        var zipBytes = BuildZip(("reading/exam.json", "{ invalid-json }"));
        var response = await client.SendAsync(
            UploadRequest(access, "invalid.zip", zipBytes, Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var packageId = body.GetProperty("packageId").GetString()!;

        await ProcessOnceAsync(packageId);

        using var scope = app.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();

        var package = await repository.FindAsync(packageId, CancellationToken.None);
        Assert.NotNull(package);
        Assert.Equal(PackageImportStatus.Rejected, package.Status);
        Assert.True(package.Findings.Count > 0);
    }

    [SkippableFact]
    public async Task Worker_concurrency_only_one_claims_uploaded_package()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var zipBytes = BuildZip(("reading/exam.json", ValidPackageJson));
        var response = await client.SendAsync(
            UploadRequest(access, "race.zip", zipBytes, Guid.NewGuid().ToString("n")));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var packageId = body.GetProperty("packageId").GetString()!;

        using var scope1 = app.Services.CreateScope();
        using var scope2 = app.Services.CreateScope();

        var repo1 = scope1.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var proc1 = scope1.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();
        var proc2 = scope2.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();

        var pkg1 = await repo1.FindAsync(packageId, CancellationToken.None);
        var pkg2 = await repo1.FindAsync(packageId, CancellationToken.None);

        // Execute concurrently
        var t1 = proc1.ProcessAsync(pkg1!, CancellationToken.None);
        var t2 = proc2.ProcessAsync(pkg2!, CancellationToken.None);

        await Task.WhenAll(t1, t2);

        var finalPkg = await repo1.FindAsync(packageId, CancellationToken.None);
        Assert.NotNull(finalPkg);
        Assert.Equal(PackageImportStatus.NeedsReview, finalPkg.Status);
    }

    [SkippableFact]
    public async Task Unexpected_exception_transitions_package_to_failed_with_sanitized_detail()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var processor = scope.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();

        var pkgId = Guid.NewGuid().ToString("n");
        // Non-existent uploadRef will cause OpenAsync to fail with FileNotFound exception
        var package = ExamPackage.Create(
            pkgId, ExamPackageSourceKind.Zip, UserId.New(),
            "sha256", "ghost.zip", "non-existent-upload-ref", DateTimeOffset.UtcNow);

        await repo.SaveAsync(package, CancellationToken.None);

        await processor.ProcessAsync(package, CancellationToken.None);

        var finalPkg = await repo.FindAsync(pkgId, CancellationToken.None);
        Assert.NotNull(finalPkg);
        Assert.Equal(PackageImportStatus.Failed, finalPkg.Status);
        Assert.Equal("INGESTION_FAILED", finalPkg.FailureCode);
        Assert.False(string.IsNullOrWhiteSpace(finalPkg.FailureDetail));
        Assert.DoesNotContain("GridFSFileNotFoundException", finalPkg.FailureDetail);
        Assert.DoesNotContain("Exception", finalPkg.FailureDetail);
    }

    [SkippableFact]
    public async Task Two_identical_uploads_with_different_keys_keep_two_distinct_package_rows_linked_to_same_draft()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var zipBytes = BuildZip(("reading/exam.json", ValidPackageJson));

        var resp1 = await client.SendAsync(UploadRequest(access, "same.zip", zipBytes, Guid.NewGuid().ToString("n")));
        var body1 = await resp1.Content.ReadFromJsonAsync<JsonElement>();
        var pkgId1 = body1.GetProperty("packageId").GetString()!;

        var resp2 = await client.SendAsync(UploadRequest(access, "same.zip", zipBytes, Guid.NewGuid().ToString("n")));
        var body2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        var pkgId2 = body2.GetProperty("packageId").GetString()!;

        Assert.NotEqual(pkgId1, pkgId2);

        await ProcessOnceAsync(pkgId1);
        await ProcessOnceAsync(pkgId2);

        using var scope = app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();

        var p1 = await repo.FindAsync(pkgId1, CancellationToken.None);
        var p2 = await repo.FindAsync(pkgId2, CancellationToken.None);

        Assert.NotNull(p1);
        Assert.NotNull(p2);
        Assert.Equal(PackageImportStatus.NeedsReview, p1.Status);
        Assert.Equal(PackageImportStatus.NeedsReview, p2.Status);
        Assert.False(string.IsNullOrWhiteSpace(p1.ImportDraftId));
        Assert.False(string.IsNullOrWhiteSpace(p2.ImportDraftId));
        Assert.NotEqual(p1.ImportDraftId, p2.ImportDraftId);

        // Review and approve package 1's draft
        var (_, reviewerId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(reviewerId, "reviewer", "exam.review", "exam.create");
        var (revAccess, _) = await SignInAsync(client);

        var checklist1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/import/packages/{p1.ImportDraftId}/checklist");
        checklist1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", revAccess);
        checklist1.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        checklist1.Content = JsonContent.Create(new
        {
            confirmed = new[] { "questions", "options", "wordlimits", "acceptedvariants", "transcriptandevidence", "assetmapping" },
        });
        var chkResp1 = await client.SendAsync(checklist1);
        Assert.Equal(HttpStatusCode.OK, chkResp1.StatusCode);

        var approve1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/import/packages/{p1.ImportDraftId}/approve");
        approve1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", revAccess);
        approve1.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        var appResp1 = await client.SendAsync(approve1);
        Assert.Equal(HttpStatusCode.OK, appResp1.StatusCode);

        // Verify package 1 is Imported, while package 2 remains NeedsReview
        var p1After = await repo.FindAsync(pkgId1, CancellationToken.None);
        var p2After = await repo.FindAsync(pkgId2, CancellationToken.None);
        Assert.NotNull(p1After);
        Assert.NotNull(p2After);
        Assert.Equal(PackageImportStatus.Imported, p1After.Status);
        Assert.Equal(PackageImportStatus.NeedsReview, p2After.Status);

        // Now review and approve package 2's draft independently
        var checklist2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/import/packages/{p2.ImportDraftId}/checklist");
        checklist2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", revAccess);
        checklist2.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        checklist2.Content = JsonContent.Create(new
        {
            confirmed = new[] { "questions", "options", "wordlimits", "acceptedvariants", "transcriptandevidence", "assetmapping" },
        });
        var chkResp2 = await client.SendAsync(checklist2);
        Assert.Equal(HttpStatusCode.OK, chkResp2.StatusCode);

        var approve2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/import/packages/{p2.ImportDraftId}/approve");
        approve2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", revAccess);
        approve2.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        var appResp2 = await client.SendAsync(approve2);
        Assert.Equal(HttpStatusCode.OK, appResp2.StatusCode);

        var p2Final = await repo.FindAsync(pkgId2, CancellationToken.None);
        Assert.NotNull(p2Final);
        Assert.Equal(PackageImportStatus.Imported, p2Final.Status);
    }

    [SkippableFact]
    public async Task Worker_crash_after_claim_another_worker_recovers_after_lease_expiry()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var zipBytes = BuildZip(("reading/exam.json", ValidPackageJson));
        var response = await client.SendAsync(
            UploadRequest(access, "crash-recovery.zip", zipBytes, Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var packageId = body.GetProperty("packageId").GetString()!;

        using var scope = app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var processor = scope.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();

        var pkg = await repo.FindAsync(packageId, CancellationToken.None);
        Assert.NotNull(pkg);

        // Worker 1 claims the package with a 2-second lease, then simulates a crash
        var worker1Now = DateTimeOffset.UtcNow;
        var claimVersion = pkg.Version;
        pkg.ClaimForValidation("worker-1", worker1Now, TimeSpan.FromSeconds(2));
        await repo.ReplaceVersionAsync(pkg, claimVersion, CancellationToken.None);

        // Verify it is in Validating state owned by worker-1
        var claimedPkg = await repo.FindAsync(packageId, CancellationToken.None);
        Assert.NotNull(claimedPkg);
        Assert.Equal(PackageImportStatus.Validating, claimedPkg.Status);
        Assert.Equal("worker-1", claimedPkg.ClaimOwner);

        // Wait until lease expires
        await Task.Delay(2500);

        // Query claimable packages as worker 2
        var claimable = await repo.ListClaimableAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Contains(claimable, p => p.Id == packageId);

        // Worker 2 recovers and processes the package
        var stalePkg = claimable.First(p => p.Id == packageId);
        await processor.ProcessAsync(stalePkg, CancellationToken.None);

        // Verify Worker 2 successfully transitioned the package to NeedsReview
        var recoveredPkg = await repo.FindAsync(packageId, CancellationToken.None);
        Assert.NotNull(recoveredPkg);
        Assert.Equal(PackageImportStatus.NeedsReview, recoveredPkg.Status);
        Assert.False(string.IsNullOrWhiteSpace(recoveredPkg.ImportDraftId));
    }
}

public sealed class MutableImportLinkageCommitHooks : IImportLinkageCommitHooks
{
    public Func<Guid, string, CancellationToken, Task>? AfterDraftInsert { get; set; }
    public Func<Guid, string, CancellationToken, Task>? AfterPackageUpdate { get; set; }

    public Task AfterDraftInsertAsync(Guid draftId, string packageId, CancellationToken ct) =>
        AfterDraftInsert?.Invoke(draftId, packageId, ct) ?? Task.CompletedTask;

    public Task AfterPackageUpdateAsync(Guid draftId, string packageId, CancellationToken ct) =>
        AfterPackageUpdate?.Invoke(draftId, packageId, ct) ?? Task.CompletedTask;
}

public sealed class PackageIngestionFaultFactory : SsoAppFactory
{
    public MutableImportLinkageCommitHooks Hooks { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IImportLinkageCommitHooks>();
            services.AddSingleton(Hooks);
            services.AddSingleton<IImportLinkageCommitHooks>(Hooks);
        });
    }
}

public sealed class PackageIngestionFaultTests(PackageIngestionFaultFactory app)
    : IClassFixture<PackageIngestionFaultFactory>
{
    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private IMongoDatabase Db() => new MongoClient("mongodb://localhost:27018/?directConnection=true").GetDatabase(app.Database);

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

    private static byte[] BuildZip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    private static HttpRequestMessage UploadRequest(
        string access, string fileName, byte[] bytes, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/packages");
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        form.Add(fileContent, "package", fileName);
        request.Content = form;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add(IdempotencyMiddleware.HeaderName, idempotencyKey);
        return request;
    }

    private const string ValidPackageJson = """
    {
      "formatVersion": "2.0", "formatProfile": "vni-practice", "scoringProfileRef": "admin-import-test",
      "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
      "title": "Admin import HTTP test", "variant": "academic",
      "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
      "scoringProfile": { "rawToBand": { "reading": [
        { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }, { "minRaw": 2, "band": 2 } ] } },
      "sequenceProfile": { "modules": ["reading"] },
      "sections": [{ "module": "reading", "order": 1, "parts": [{ "order": 1, "kind": "passage",
        "body": "Evidence here.", "questions": [{
          "id": "q-1", "order": 1, "type": "multiple-select", "marks": 2,
          "options": [{ "key": "A", "text": "Alpha" }, { "key": "B", "text": "Beta" }],
          "group": { "id": "bank-1", "instruction": "Choose." },
          "slots": [
            { "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } },
            { "id": "slot-2", "number": 2, "answerKey": { "accepted": ["B"] } }
          ],
          "explanation": { "shortReason": "Both are stated.", "evidence": ["Evidence here."] }
        }]
      }]}]
    }
    """;

    [SkippableFact]
    public async Task Failure_after_draft_insert_but_before_linkage_leaves_no_orphan()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        Guid capturedDraftId = Guid.Empty;
        app.Hooks.AfterDraftInsert = (draftId, packageId, ct) =>
        {
            capturedDraftId = draftId;
            throw new InvalidOperationException("Injected crash immediately after draft insertion");
        };

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var zipBytes = BuildZip(("reading/exam.json", ValidPackageJson));
        var response = await client.SendAsync(
            UploadRequest(access, "atomic-draft-test.zip", zipBytes, Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var packageId = body.GetProperty("packageId").GetString()!;

        using (var scope = app.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
            var proc = scope.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();
            var pkg = await repo.FindAsync(packageId, CancellationToken.None);

            // Ingestion should fail due to injected exception
            await proc.ProcessAsync(pkg!, CancellationToken.None);
        }

        // Verify transaction atomicity:
        // 1. The draft must NOT exist in the database (no orphan draft)
        Assert.NotEqual(Guid.Empty, capturedDraftId);
        using (var scope = app.Services.CreateScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();
            var draft = await drafts.FindAsync(capturedDraftId, CancellationToken.None);
            Assert.Null(draft);

            var repo = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
            var pkg = await repo.FindAsync(packageId, CancellationToken.None);
            Assert.NotNull(pkg);
            // Package must not have been moved to NeedsReview or have ImportDraftId set
            Assert.NotEqual(PackageImportStatus.NeedsReview, pkg!.Status);
            Assert.Null(pkg.ImportDraftId);
        }
    }

    [SkippableFact]
    public async Task Failure_after_package_update_before_commit_leaves_neither_side_committed()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        Guid capturedDraftId = Guid.Empty;
        app.Hooks.AfterDraftInsert = null;
        app.Hooks.AfterPackageUpdate = (draftId, packageId, ct) =>
        {
            capturedDraftId = draftId;
            throw new InvalidOperationException("Injected crash after package update, before commit");
        };

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "author", "package.upload", "exam.create");
        var (access, _) = await SignInAsync(client);

        var zipBytes = BuildZip(("reading/exam.json", ValidPackageJson.Replace("admin-import-test", $"boundary2-{Guid.NewGuid():n}")));
        var response = await client.SendAsync(
            UploadRequest(access, "boundary2.zip", zipBytes, Guid.NewGuid().ToString("n")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var packageId = body.GetProperty("packageId").GetString()!;

        using (var scope = app.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
            var proc = scope.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();
            var pkg = await repo.FindAsync(packageId, CancellationToken.None);

            await proc.ProcessAsync(pkg!, CancellationToken.None);
        }

        Assert.NotEqual(Guid.Empty, capturedDraftId);
        using (var scope = app.Services.CreateScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();
            var draft = await drafts.FindAsync(capturedDraftId, CancellationToken.None);
            Assert.Null(draft);

            var repo = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
            var pkg = await repo.FindAsync(packageId, CancellationToken.None);
            Assert.NotNull(pkg);
            Assert.NotEqual(PackageImportStatus.NeedsReview, pkg!.Status);
            Assert.Null(pkg.ImportDraftId);
        }
    }
}
