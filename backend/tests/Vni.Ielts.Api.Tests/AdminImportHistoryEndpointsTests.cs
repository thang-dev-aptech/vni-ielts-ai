using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Api.Endpoints;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure.Persistence;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Package-history HTTP surface — door rejection, package.read gating,
/// filters/pagination, and audit payloads that never carry archive contents.
///
/// Lives here (not only in Integration.Tests) so the package-verification
/// slice owns a red-when-removed suite at the declared path.
/// </summary>
public sealed class AdminImportHistoryEndpointsTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private const string BombDefinitionId = "history-bomb-must-never-enqueue";

    private static string? CachedAdminAccess;

    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(HttpClient Client, string Access)> SignInAsAdminAsync()
    {
        var client = NewClient();
        if (CachedAdminAccess is { Length: > 0 } cached)
            return (client, cached);

        await SsoRoundTripAsync(client);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

            var admin = await roles.FindByNameAsync(SystemRoles.Admin, default);
            Assert.NotNull(admin);

            var user = await users.FindByEmailAsync(
                Email.Create("stub.learner@example.com"), default);
            Assert.NotNull(user);

            user!.AssignRole(admin!.Id);
            await users.SaveAsync(user, default);
        }

        var access = await SsoRoundTripAsync(client);
        CachedAdminAccess = access;
        return (client, access);
    }

    private async Task<(HttpClient Client, string Access)> SignInWithOnlyAsync(
        params string[] permissions)
    {
        var client = NewClient();
        await SsoRoundTripAsync(client);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

            var user = await users.FindByEmailAsync(
                Email.Create("stub.learner@example.com"), default);
            Assert.NotNull(user);

            foreach (var existing in await roles.ListAsync(default))
            {
                if (user!.HasRole(existing.Id)) user.RemoveRole(existing.Id);
            }

            var limited = Role.Create($"hist-limited-{Guid.NewGuid():n}", isSystem: false, permissions);
            await roles.AddAsync(limited, default);
            user!.AssignRole(limited.Id);
            await users.SaveAsync(user, default);
        }

        return (client, await SsoRoundTripAsync(client));
    }

    private static async Task<string> SsoRoundTripAsync(HttpClient client)
    {
        JsonElement startBody = default;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var start = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
            startBody = await start.Content.ReadFromJsonAsync<JsonElement>();

            if ((int)start.StatusCode == 429)
            {
                await Task.Delay(250 * (attempt + 1));
                continue;
            }

            if (!start.IsSuccessStatusCode
                || !startBody.TryGetProperty("authorizationUrl", out var urlEl)
                || urlEl.GetString() is not { Length: > 0 } authorizationUrl)
            {
                throw new InvalidOperationException(
                    $"SSO start failed ({(int)start.StatusCode}): {startBody}");
            }

            var url = new Uri(authorizationUrl);
            var callback = await client.GetAsync(url.PathAndQuery);
            var code = System.Web.HttpUtility.ParseQueryString(callback.Headers.Location!.Query)["code"];

            var complete = await client.PostAsJsonAsync(
                "/api/v1/auth/sso/complete", new { handoffCode = code });
            complete.EnsureSuccessStatusCode();

            return (await complete.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("accessToken").GetString()!;
        }

        throw new InvalidOperationException($"SSO start still 429: {startBody}");
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string access)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        return request;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> UploadAsync(
        HttpClient client, string access, byte[] zip, string? definitionId = null)
    {
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(zip);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part, "file", "package.zip");
        if (definitionId is not null) content.Add(new StringContent(definitionId), "definitionId");

        var request = Request(HttpMethod.Post, "/api/v1/admin/import/packages", access);
        request.Content = content;

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await BodyOf(response)).GetProperty("operationId").GetString()!;
    }

    private static string UniquePackageJson() =>
        ValidPackageJson.Replace("admin-import-test", $"hist-{Guid.NewGuid():n}");

    private static MultipartFormDataContent BombPackage()
    {
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(BombBytes());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part, "file", @"..\..\evil.zip");
        content.Add(new StringContent(BombDefinitionId), "definitionId");
        return content;
    }

    private static byte[] BombBytes() => BuildZipBomb("reading/bomb.txt", 10 * 1024 * 1024);

    private static byte[] BuildZip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    private static byte[] BuildZipBomb(string name, int zeroBytes)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
            using var output = entry.Open();
            output.Write(new byte[zeroBytes]);
        }

        return stream.ToArray();
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

    private async Task<string> SeedApprovableDraftAsync()
    {
        using var scope = app.Services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ExamImportWorkflow>();
        var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();

        var attempt = await workflow.ImportStructuredAsync(
            ValidPackageJson.Replace("admin-import-test", $"approve-{Guid.NewGuid():n}"),
            Vni.Ielts.Domain.Exams.ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, string.Join("; ", attempt.Findings.Select(f => f.Message)));

        var seeded = attempt.Draft! with
        {
            Checklist = new ImportReviewChecklist(
                Enum.GetValues<ImportReviewCategory>().ToHashSet()),
            Warnings = [],
            Revision = attempt.Draft.Revision + 1,
        };

        Assert.True(await drafts.ReplaceAsync(seeded, attempt.Draft.Revision, default));
        return seeded.Id.ToString("D");
    }

    /// <summary>
    /// Door rejection stores bounded metadata and findings, never archive
    /// bytes and never a resumable import job.
    /// </summary>
    [SkippableFact]
    public async Task Door_rejection_stores_history_without_archive_bytes_or_job()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var request = Request(HttpMethod.Post, "/api/v1/admin/import/packages", access);
        request.Content = BombPackage();

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await BodyOf(response);
        Assert.Equal("PACKAGE_REJECTED", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("operationId", out _));
        Assert.False(body.TryGetProperty("draftId", out _));

        var bomb = BombBytes();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bomb))
            .ToLowerInvariant();

        using var scope = app.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IImportOutbox>();
        var archives = scope.ServiceProvider.GetRequiredService<IImportArchiveStore>();
        var history = scope.ServiceProvider.GetRequiredService<IPackageImportHistoryStore>();

        Assert.Null(await archives.OpenAsync($"imports/archives/{hash}.zip", default));
        Assert.Null(await outbox.FindAsync(
            ImportJob.OperationIdFor(
                new Vni.Ielts.Domain.Exams.ExamDefinitionId(BombDefinitionId), 1, hash,
                ImportJob.NoParserConfigured),
            default));

        var recorded = (await history.QueryAsync(
                new PackageImportHistoryQuery(Result: PackageImportHistoryResult.DoorRejected),
                default))
            .Items
            .Where(i => i.SourceSha256 == hash)
            .ToArray();

        var row = Assert.Single(recorded);
        Assert.Null(row.OperationId);
        Assert.Null(row.DraftId);
        Assert.Null(row.Stage);
        Assert.Equal("evil.zip", row.OriginalFileName);
        Assert.False(string.IsNullOrWhiteSpace(row.ActorId));
        Assert.True(row.Findings.Count > 0);
        Assert.True(row.Findings.Count <= PackageImportHistoryBounds.MaxFindings);
        Assert.All(
            row.Findings,
            f => Assert.True(f.Message.Length <= PackageImportHistoryBounds.MaxFindingMessageChars));

        var listed = await client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/admin/import/packages?result=DoorRejected&pageSize=50", access));
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var historyId = (await BodyOf(listed)).GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("sourceSha256").GetString() == hash)
            .GetProperty("historyId").GetString();

        var detail = await client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/import/package-history/{historyId}", access));
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailBody = await BodyOf(detail);
        Assert.Equal("DoorRejected", detailBody.GetProperty("result").GetString());
        Assert.True(detailBody.GetProperty("findings").GetArrayLength() > 0);
        Assert.False(detailBody.TryGetProperty("archiveKey", out _));
        Assert.DoesNotContain("imports/archives", detailBody.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Accepted uploads keep uploader and filename; package.read gates list
    /// and detail; filters and pagination stay stable.
    /// </summary>
    [SkippableFact]
    public async Task Package_read_gates_list_and_detail_with_stable_filters()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var first = await UploadAsync(client, access, BuildZip(("reading/exam.json", UniquePackageJson())));
        var second = await UploadAsync(client, access, BuildZip(("reading/exam.json", UniquePackageJson())));

        using (var scope = app.Services.CreateScope())
        {
            var history = scope.ServiceProvider.GetRequiredService<IPackageImportHistoryStore>();
            var accepted = await history.FindByOperationAsync(second, default);
            Assert.NotNull(accepted);
            Assert.Equal(PackageImportHistoryResult.Queued, accepted!.Result);
            Assert.Equal("package.zip", accepted.OriginalFileName);
            Assert.False(string.IsNullOrWhiteSpace(accepted.ActorId));
            Assert.Equal(ImportJobStage.Extracting, accepted.Stage);
        }

        var listed = await client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/admin/import/packages?result=Queued&page=1&pageSize=50", access));
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);

        var body = await BodyOf(listed);
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.True(body.GetProperty("pageSize").GetInt32() <= PackageImportHistoryQuery.MaxPageSize);

        var items = body.GetProperty("items").EnumerateArray().ToArray();
        var ids = items.Select(i => i.GetProperty("operationId").GetString()).ToArray();
        var secondIndex = Array.IndexOf(ids, second);
        var firstIndex = Array.IndexOf(ids, first);
        Assert.True(secondIndex >= 0 && firstIndex >= 0);
        Assert.True(secondIndex < firstIndex, "Queued history must list newest first.");

        using (var scope = app.Services.CreateScope())
        {
            var history = scope.ServiceProvider.GetRequiredService<IPackageImportHistoryStore>();
            var row = await history.FindByOperationAsync(second, default);
            Assert.NotNull(row);

            var filtered = await client.SendAsync(
                Request(
                    HttpMethod.Get,
                    $"/api/v1/admin/import/packages?uploader={Uri.EscapeDataString(row!.ActorId)}&result=Queued&stage=Extracting",
                    access));
            Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
            var filteredItems = (await BodyOf(filtered)).GetProperty("items").EnumerateArray().ToArray();
            Assert.Contains(filteredItems, i => i.GetProperty("operationId").GetString() == second);
            Assert.All(filteredItems, i =>
            {
                Assert.Equal(row.ActorId, i.GetProperty("actorId").GetString());
                Assert.Equal("Extracting", i.GetProperty("stage").GetString());
            });
        }

        var (reader, readAccess) = await SignInWithOnlyAsync(PermissionKeys.PackageRead);
        Assert.Equal(
            HttpStatusCode.OK,
            (await reader.SendAsync(
                Request(HttpMethod.Get, "/api/v1/admin/import/packages", readAccess))).StatusCode);

        var uploadAsReader = Request(HttpMethod.Post, "/api/v1/admin/import/packages", readAccess);
        uploadAsReader.Content = new MultipartFormDataContent
        {
            {
                new ByteArrayContent(BuildZip(("reading/exam.json", UniquePackageJson())))
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/zip") },
                },
                "file",
                "package.zip"
            },
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.SendAsync(uploadAsReader)).StatusCode);

        var (uploader, uploadAccess) = await SignInWithOnlyAsync(PermissionKeys.PackageUpload);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await uploader.SendAsync(
                Request(HttpMethod.Get, "/api/v1/admin/import/packages", uploadAccess))).StatusCode);

        var bad = await client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/admin/import/packages?result=not-a-result", access));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var missing = await client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/import/package-history/{Guid.NewGuid():D}", access));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>
    /// Upload / reject / approve audit rows carry identifiers and codes, never
    /// package JSON, finding messages, or archive keys.
    /// </summary>
    [SkippableFact]
    public async Task Lifecycle_audit_records_identifiers_without_package_contents()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var operationId = await UploadAsync(
            client, access, BuildZip(("reading/exam.json", UniquePackageJson())));

        using (var scope = app.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var (entries, _) = await audit.ListAsync(
                null, nameof(AuditAction.PackageUploadAccepted), 0, 50, default);
            var mine = entries.Single(e => e.Detail.TryGetValue("operationId", out var id) && id == operationId);
            Assert.True(mine.Detail.ContainsKey("historyId"));
            Assert.True(mine.Detail.ContainsKey("fileName"));
            Assert.False(mine.Detail.ContainsKey("archiveKey"));
            Assert.DoesNotContain("reading/exam.json", string.Join("|", mine.Detail.Values));
            Assert.DoesNotContain("Evidence here", string.Join("|", mine.Detail.Values));
        }

        var bomb = Request(HttpMethod.Post, "/api/v1/admin/import/packages", access);
        bomb.Content = BombPackage();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.SendAsync(bomb)).StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var (entries, _) = await audit.ListAsync(
                null, nameof(AuditAction.PackageUploadRejected), 0, 50, default);
            var mine = entries.First(e => e.Detail.TryGetValue("fileName", out var name) && name == "evil.zip");
            Assert.True(mine.Detail.ContainsKey("historyId"));
            Assert.True(mine.Detail.ContainsKey("findingCodes"));
            Assert.True(mine.Detail.ContainsKey("findingCount"));
            Assert.False(mine.Detail.ContainsKey("archiveKey"));
            Assert.DoesNotContain("reading/bomb.txt", string.Join("|", mine.Detail.Values));
            Assert.DoesNotContain("cap exceeded", string.Join("|", mine.Detail.Values));
        }

        var draftId = await SeedApprovableDraftAsync();
        var approved = await client.SendAsync(
            Request(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/approve", access));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var (entries, _) = await audit.ListAsync(
                null, nameof(AuditAction.PackageImportApproved), 0, 50, default);
            var mine = entries.Single(e => e.TargetId == draftId);
            Assert.Equal(draftId, mine.Detail["draftId"]);
            Assert.False(mine.Detail.ContainsKey("packageJson"));
            Assert.DoesNotContain("Evidence here", string.Join("|", mine.Detail.Values));
        }

        var worker = PackageImportAudit.WorkerRejected(
            Guid.NewGuid().ToString("D"),
            "op-worker-1",
            [new PackageFinding("error", "SCHEMA_INVALID", "exam.json", "The hall has a slate roof.")]);
        Assert.Equal("SCHEMA_INVALID", worker["findingCodes"]);
        Assert.DoesNotContain("slate roof", string.Join("|", worker.Values));
        Assert.False(worker.ContainsKey("archiveKey"));
    }
}
