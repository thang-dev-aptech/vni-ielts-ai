using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Approval must create exactly one catalogue Draft in the same commit that
/// marks the import draft approved. A crash or a stale revision must leave
/// neither half written.
/// </summary>
public sealed class ImportApprovalAtomicityTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [SkippableFact]
    public async Task Approval_creates_one_catalogue_draft_and_a_retry_does_not_duplicate_it()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var draftId = await UploadReadyDraftAsync(client, access);

        var first = await client.SendAsync(
            Request(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/approve", access));
        var firstBody = await BodyOf(first);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var examVersionId = firstBody.GetProperty("examVersionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(examVersionId));

        var second = await client.SendAsync(
            Request(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/approve", access));
        var secondBody = await BodyOf(second);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(examVersionId, secondBody.GetProperty("examVersionId").GetString());

        using var scope = app.Services.CreateScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();
        var version = await catalogue.FindAsync(new ExamVersionId(examVersionId!), default);
        Assert.NotNull(version);
        Assert.Equal(ExamVersionStatus.Draft, version!.Status);
        Assert.Equal("synthetic-validation", version.ContentSourceId?.Value);

        var copies = (await catalogue.ListAllAsync(default))
            .Count(v => v.Id.Value == examVersionId);
        Assert.Equal(1, copies);
    }

    [SkippableFact]
    public async Task A_stale_revision_creates_no_catalogue_version()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var draftId = await UploadReadyDraftAsync(client, access);

        ExamVersionId versionId;
        ImportReviewResult stale;
        using (var scope = app.Services.CreateScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();
            var review = scope.ServiceProvider.GetRequiredService<ImportReviewWorkflow>();
            var draft = await drafts.FindAsync(Guid.Parse(draftId), default);
            versionId = draft!.Version.Id;

            stale = await review.ApproveAsync(
                draft.Id, expectedRevision: draft.Revision - 1,
                new ImportReviewActor("reviewer", false, true, false), default);
        }

        Assert.Equal("IMPORT_REVISION_CONFLICT", stale.ErrorCode);

        using var after = app.Services.CreateScope();
        var catalogue = after.ServiceProvider.GetRequiredService<IExamCatalogue>();
        Assert.Null(await catalogue.FindAsync(versionId, default));

        var draftsAfter = after.ServiceProvider.GetRequiredService<IImportDraftStore>();
        Assert.Equal(
            ImportApprovalState.ReviewRequired,
            (await draftsAfter.FindAsync(Guid.Parse(draftId), default))!.ApprovalState);
    }

    private async Task<(HttpClient Client, string Access)> SignInAsAdminAsync()
    {
        var client = NewClient();
        await SsoRoundTripAsync(client);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            var admin = await roles.FindByNameAsync(SystemRoles.Admin, default);
            var user = await users.FindByEmailAsync(Email.Create("stub.learner@example.com"), default);
            Assert.NotNull(admin);
            Assert.NotNull(user);
            if (!user!.HasRole(admin!.Id))
            {
                user.AssignRole(admin.Id);
                await users.SaveAsync(user, default);
            }
        }

        return (client, await SsoRoundTripAsync(client));
    }

    private static async Task<string> SsoRoundTripAsync(HttpClient client)
    {
        var start = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
        var url = new Uri((await start.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("authorizationUrl").GetString()!);
        var callback = await client.GetAsync(url.PathAndQuery);
        var code = System.Web.HttpUtility.ParseQueryString(callback.Headers.Location!.Query)["code"];
        var complete = await client.PostAsJsonAsync("/api/v1/auth/sso/complete", new { handoffCode = code });
        complete.EnsureSuccessStatusCode();
        return (await complete.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;
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

    private static readonly string[] FullChecklist =
    [
        "questions", "options", "wordlimits", "acceptedvariants", "transcriptandevidence", "assetmapping",
    ];

    private async Task<string> UploadReadyDraftAsync(HttpClient client, string access)
    {
        var json = ValidPackageJson.Replace("admin-import-test", $"atomic-{Guid.NewGuid():n}");
        var zip = BuildZip(("reading/exam.json", json));
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(zip);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part, "file", "package.zip");

        var upload = Request(HttpMethod.Post, "/api/v1/admin/import/packages", access);
        upload.Content = content;
        var uploaded = await client.SendAsync(upload);
        var body = await BodyOf(uploaded);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var draftId = body.GetProperty("draftId").GetString()!;

        var checklist = Request(
            HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/checklist", access);
        checklist.Content = JsonContent.Create(new { confirmed = FullChecklist });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(checklist)).StatusCode);
        return draftId;
    }

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
}

/// <summary>Production DI stays no-op. This factory alone injects a mutable approval hook.</summary>
public sealed class ImportApprovalFaultFactory : SsoAppFactory
{
    public MutableImportApprovalCommitHooks Hooks { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IImportApprovalCommitHooks>();
            services.AddSingleton(Hooks);
            services.AddSingleton<IImportApprovalCommitHooks>(Hooks);
        });
    }
}

public sealed class MutableImportApprovalCommitHooks : IImportApprovalCommitHooks
{
    public Func<ExamVersionId, CancellationToken, Task>? AfterCatalogueWrite { get; set; }
    public Func<Guid, CancellationToken, Task>? AfterAssetPromotion { get; set; }

    public Task AfterCatalogueWriteAsync(ExamVersionId versionId, CancellationToken ct) =>
        AfterCatalogueWrite?.Invoke(versionId, ct) ?? Task.CompletedTask;

    public Task AfterAssetPromotionAsync(Guid draftId, CancellationToken ct) =>
        AfterAssetPromotion?.Invoke(draftId, ct) ?? Task.CompletedTask;
}

public sealed class ImportApprovalAtomicityFaultTests(ImportApprovalFaultFactory app)
    : IClassFixture<ImportApprovalFaultFactory>
{
    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [SkippableFact]
    public async Task Failure_after_catalogue_write_rolls_back_draft_and_catalogue()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        app.Hooks.AfterCatalogueWrite = (_, _) => throw new InvalidOperationException("injected-fault");
        try
        {
            var client = NewClient();
            var start = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
            var url = new Uri((await start.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("authorizationUrl").GetString()!);
            var callback = await client.GetAsync(url.PathAndQuery);
            var code = System.Web.HttpUtility.ParseQueryString(callback.Headers.Location!.Query)["code"];
            var complete = await client.PostAsJsonAsync("/api/v1/auth/sso/complete", new { handoffCode = code });
            complete.EnsureSuccessStatusCode();

            using (var scope = app.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
                var admin = await roles.FindByNameAsync(SystemRoles.Admin, default);
                var user = await users.FindByEmailAsync(Email.Create("stub.learner@example.com"), default);
                if (admin is not null && user is not null && !user.HasRole(admin.Id))
                {
                    user.AssignRole(admin.Id);
                    await users.SaveAsync(user, default);
                }
            }

            var login = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
            var loginUrl = new Uri((await login.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("authorizationUrl").GetString()!);
            var loginCb = await client.GetAsync(loginUrl.PathAndQuery);
            var loginCode = System.Web.HttpUtility.ParseQueryString(loginCb.Headers.Location!.Query)["code"];
            var loginDone = await client.PostAsJsonAsync("/api/v1/auth/sso/complete", new { handoffCode = loginCode });
            var access = (await loginDone.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("accessToken").GetString()!;

            var json = """
            {
              "formatVersion": "2.0", "formatProfile": "vni-practice", "scoringProfileRef": "fault-test",
              "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              "title": "Fault injection", "variant": "academic",
              "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
              "scoringProfile": { "rawToBand": { "reading": [
                { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 } ] } },
              "sequenceProfile": { "modules": ["reading"] },
              "sections": [{ "module": "reading", "order": 1, "parts": [{ "order": 1, "kind": "passage",
                "body": "Evidence.", "questions": [{
                  "id": "q-1", "order": 1, "type": "multiple-select", "marks": 1,
                  "options": [{ "key": "A", "text": "Alpha" }],
                  "group": { "id": "bank-1", "instruction": "Choose." },
                  "slots": [{ "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } }],
                  "explanation": { "shortReason": "Stated.", "evidence": ["Evidence."] }
                }]
              }]}]
            }
            """.Replace("fault-test", $"fault-{Guid.NewGuid():n}");

            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("reading/exam.json", CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(json);
            }

            var upload = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/import/packages");
            upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            upload.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
            var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(zipStream.ToArray());
            part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(part, "file", "package.zip");
            upload.Content = form;
            var uploaded = await client.SendAsync(upload);
            var body = await uploaded.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
            var draftId = body.GetProperty("draftId").GetString()!;

            var checklist = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/checklist");
            checklist.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            checklist.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
            checklist.Content = JsonContent.Create(new
            {
                confirmed = new[] { "questions", "options", "wordlimits", "acceptedvariants", "transcriptandevidence", "assetmapping" },
            });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(checklist)).StatusCode);

            ExamVersionId versionId;
            using (var scope = app.Services.CreateScope())
            {
                var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();
                versionId = (await drafts.FindAsync(Guid.Parse(draftId), default))!.Version.Id;
            }

            var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/approve");
            approve.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            approve.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
            var approved = await client.SendAsync(approve);
            Assert.True((int)approved.StatusCode >= 500, $"expected 5xx, got {approved.StatusCode}");

            using var after = app.Services.CreateScope();
            var catalogue = after.ServiceProvider.GetRequiredService<IExamCatalogue>();
            Assert.Null(await catalogue.FindAsync(versionId, default));
            var still = await after.ServiceProvider.GetRequiredService<IImportDraftStore>()
                .FindAsync(Guid.Parse(draftId), default);
            Assert.Equal(ImportApprovalState.ReviewRequired, still!.ApprovalState);
        }
        finally
        {
            app.Hooks.AfterCatalogueWrite = null;
        }
    }
}
