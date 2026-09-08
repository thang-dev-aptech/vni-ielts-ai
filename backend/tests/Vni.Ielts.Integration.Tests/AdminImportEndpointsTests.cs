using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// S6b's exam-import front door through the real HTTP pipeline — the same
/// reasoning as <see cref="ExamReviewLifecycleTests"/>: a refusal proves
/// something only on the route an operator actually calls, after
/// authentication, after the permission check, after the idempotency guard.
///
/// <b>Every ZIP here is built in memory</b>, following the exact pattern
/// <c>HostileArchives</c> (S6a, Infrastructure.Tests) established rather than
/// checking in a binary — that type lives in a different test project and is
/// off limits to modify, so this file builds its own small ZIPs the same way.
/// </summary>
public sealed class AdminImportEndpointsTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(HttpClient Client, string Access)> SignInAsAdminAsync()
    {
        var client = NewClient();
        await SsoRoundTripAsync(client);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

            var admin = await roles.FindByNameAsync(SystemRoles.Admin, default);
            Assert.NotNull(admin);

            var user = await users.FindByEmailAsync(
                Vni.Ielts.Domain.Identity.Email.Create("stub.learner@example.com"), default);
            Assert.NotNull(user);

            user!.AssignRole(admin!.Id);
            await users.SaveAsync(user, default);
        }

        // Permissions are resolved when the access token is minted, so a
        // second sign-in is taken after the role grant lands.
        var access = await SsoRoundTripAsync(client);
        return (client, access);
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

    // ── ZIP fixtures, built in memory — same technique as HostileArchives ──

    /// <summary>A structured package: one ready exam.json, valid, with a real answer key.</summary>
    private static MultipartFormDataContent ValidStructuredPackage()
    {
        var zip = BuildZip(("reading/exam.json", ValidPackageJson));
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(zip);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part, "file", "package.zip");
        return content;
    }

    /// <summary>
    /// A compression bomb: many zero bytes that deflate shrinks by hundreds
    /// to one — S6a's inspector refuses this on the ratio cap before a single
    /// byte is extracted.
    /// </summary>
    private static MultipartFormDataContent BombPackage()
    {
        var zip = BuildZipBomb("reading/bomb.txt", 10 * 1024 * 1024);
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(zip);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part, "file", "bomb.zip");
        return content;
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

    /// <summary>
    /// One reading question with a real, schema-valid answer key — copied
    /// from <c>ExamPackageReaderTests.ValidV2Json</c>'s baseline (Infrastructure.Tests,
    /// proven valid there: <c>Assert.True(result.IsValid, ...)</c>) rather than
    /// re-derived, so this test does not carry its own guess at the schema.
    /// </summary>
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
    public async Task Uploading_a_valid_structured_package_creates_an_unapproved_draft()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();

        var request = Request(HttpMethod.Post, "/api/v1/admin/import/packages", access);
        request.Content = ValidStructuredPackage();

        var response = await client.SendAsync(request);
        var body = await BodyOf(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("structuredpackage", body.GetProperty("route").GetString());
        Assert.Equal("reviewrequired", body.GetProperty("approvalState").GetString());
        Assert.Equal(0, body.GetProperty("findings").GetArrayLength());
        Assert.Equal(0, body.GetProperty("warnings").GetArrayLength());

        var draftId = body.GetProperty("draftId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(draftId));

        // The draft the upload just made is really persisted and reachable
        // by a caller with package.read — not just echoed in the response.
        var getResponse = await client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/import/packages/{draftId}", access));
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [SkippableFact]
    public async Task Uploaded_package_keeps_the_json_content_source_id_on_the_draft_version()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var draftId = await UploadDraftAsync(client, access);

        using var scope = app.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();
        var draft = await drafts.FindAsync(Guid.Parse(draftId), default);

        Assert.Equal("synthetic-validation", draft!.Version.ContentSourceId?.Value);
    }

    [SkippableFact]
    public async Task A_compression_bomb_is_refused_before_anything_is_persisted()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();

        var request = Request(HttpMethod.Post, "/api/v1/admin/import/packages", access);
        request.Content = BombPackage();

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await BodyOf(response);
        Assert.Equal("PACKAGE_REJECTED", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("findings").GetArrayLength() > 0);

        // No draft id is even offered — there is nothing for a caller to look up.
        Assert.False(body.TryGetProperty("draftId", out _));
    }

    [SkippableFact]
    public async Task Uploading_without_the_upload_permission_is_forbidden()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        // A signed-in learner has no package.upload — the server enforces
        // this, not just the CMS hiding a button. The stub SSO provider
        // always authenticates the same account, and another test in this
        // class may already have promoted it to Admin, so the role is
        // explicitly stripped here rather than assumed absent — this test
        // must hold regardless of what ran before it.
        var client = NewClient();
        await SsoRoundTripAsync(client);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            var admin = await roles.FindByNameAsync(SystemRoles.Admin, default);
            var user = await users.FindByEmailAsync(
                Vni.Ielts.Domain.Identity.Email.Create("stub.learner@example.com"), default);

            if (admin is not null && user is not null && user.HasRole(admin.Id))
            {
                user.RemoveRole(admin.Id);
                await users.SaveAsync(user, default);
            }
        }

        // Permissions are resolved when the token is minted, so a fresh
        // sign-in is taken after the role removal lands.
        var access = await SsoRoundTripAsync(client);

        var request = Request(HttpMethod.Post, "/api/v1/admin/import/packages", access);
        request.Content = ValidStructuredPackage();

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// `P-19`'s core rule at the HTTP boundary — the primary red-when-removed
    /// target for this slice: overriding a warning without a reason is
    /// refused, overriding one with a reason succeeds and lands an audited
    /// <c>WarningOverridden</c> entry carrying that reason.
    ///
    /// <b>The draft is seeded directly through <c>IImportDraftStore</c>,
    /// not through the upload endpoint.</b> The only route that produces a
    /// warning today is the AI-parsed one, and no AI parser is wired into this
    /// deployment (<c>UnconfiguredExamSourceParser</c> — see
    /// <c>ExamPackageImportPipeline</c>'s remarks); a structured upload never
    /// carries a warning to override. Seeding preconditions the HTTP surface
    /// cannot yet produce is the same technique
    /// <c>ExamReviewLifecycleTests.InReviewVersionAsync</c> already uses for
    /// the review lifecycle, for the same reason: there is no HTTP path to
    /// reach that state yet.
    /// </summary>
    [SkippableFact]
    public async Task Overriding_a_warning_requires_a_reason_and_is_audited()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var draftId = await SeedDraftWithWarningAsync();

        // 1. No reason — refused, nothing changes.
        var blank = Request(
            HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/warnings/w1/override", access);
        blank.Content = JsonContent.Create(new { reason = "" });
        var blankResponse = await client.SendAsync(blank);
        Assert.Equal(HttpStatusCode.Conflict, blankResponse.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();
            var stillUnresolved = await drafts.FindAsync(Guid.Parse(draftId), default);
            Assert.False(stillUnresolved!.Warnings.Single().Resolved);
        }

        // 2. With a reason — succeeds, and the reason is what gets audited.
        const string reason = "Thiếu transcript, đã đối chiếu thủ công với bản ghi âm gốc.";
        var withReason = Request(
            HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/warnings/w1/override", access);
        withReason.Content = JsonContent.Create(new { reason });
        var withReasonResponse = await client.SendAsync(withReason);
        var body = await BodyOf(withReasonResponse);

        Assert.Equal(HttpStatusCode.OK, withReasonResponse.StatusCode);
        var warning = body.GetProperty("warnings").EnumerateArray().Single();
        Assert.True(warning.GetProperty("resolved").GetBoolean());
        Assert.Equal(reason, warning.GetProperty("overrideReason").GetString());

        using (var scope = app.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var (entries, _) = await audit.ListAsync(null, nameof(AuditAction.WarningOverridden), 0, 50, default);
            var mine = entries.Single(e => e.TargetId == draftId);
            Assert.Equal(reason, mine.Detail["reason"]);
            Assert.Equal("w1", mine.Detail["warningId"]);
        }
    }

    [SkippableFact]
    public async Task A_draft_with_an_unresolved_warning_cannot_be_approved()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var draftId = await SeedDraftWithWarningAsync();

        var response = await client.SendAsync(
            Request(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/approve", access));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = app.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();
        Assert.Equal(
            ImportApprovalState.ReviewRequired, (await drafts.FindAsync(Guid.Parse(draftId), default))!.ApprovalState);
    }

    private static readonly string[] FullChecklist =
    [
        "questions",
        "options",
        "wordlimits",
        "acceptedvariants",
        "transcriptandevidence",
        "assetmapping",
    ];

    [SkippableFact]
    public async Task Setting_the_full_checklist_unblocks_approval()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var draftId = await UploadDraftAsync(client, access);
        await ResolveOpenWarningsAsync(client, access, draftId);

        var checklist = Request(
            HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/checklist", access);
        checklist.Content = JsonContent.Create(new { confirmed = FullChecklist });
        var checklistResponse = await client.SendAsync(checklist);
        var checklistBody = await BodyOf(checklistResponse);

        Assert.Equal(HttpStatusCode.OK, checklistResponse.StatusCode);
        Assert.True(checklistBody.GetProperty("checklistComplete").GetBoolean());

        var approve = await client.SendAsync(
            Request(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/approve", access));
        var approveBody = await BodyOf(approve);

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal("approved", approveBody.GetProperty("approvalState").GetString());
    }

    [SkippableFact]
    public async Task An_unknown_checklist_category_is_refused_with_400()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var draftId = await UploadDraftAsync(client, access);

        var before = await client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/import/packages/{draftId}", access));
        var beforeBody = await BodyOf(before);
        var confirmedBefore = beforeBody.GetProperty("checklistConfirmed")
            .EnumerateArray().Select(v => v.GetString()).ToArray();

        var checklist = Request(
            HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/checklist", access);
        checklist.Content = JsonContent.Create(new { confirmed = new[] { "not-a-real-category" } });
        var checklistResponse = await client.SendAsync(checklist);
        var checklistBody = await BodyOf(checklistResponse);

        Assert.Equal(HttpStatusCode.BadRequest, checklistResponse.StatusCode);
        Assert.Equal("VALIDATION_FAILED", checklistBody.GetProperty("code").GetString());

        var after = await client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/import/packages/{draftId}", access));
        var afterBody = await BodyOf(after);
        var confirmedAfter = afterBody.GetProperty("checklistConfirmed")
            .EnumerateArray().Select(v => v.GetString()).ToArray();

        Assert.Equal(confirmedBefore, confirmedAfter);
        Assert.False(afterBody.GetProperty("checklistComplete").GetBoolean());
    }

    [SkippableFact]
    public async Task A_numeric_undefined_checklist_category_is_refused_with_400()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var draftId = await UploadDraftAsync(client, access);

        var before = await client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/import/packages/{draftId}", access));
        var beforeBody = await BodyOf(before);
        var confirmedBefore = beforeBody.GetProperty("checklistConfirmed")
            .EnumerateArray().Select(v => v.GetString()).ToArray();

        var checklist = Request(
            HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/checklist", access);
        checklist.Content = JsonContent.Create(new { confirmed = new[] { "999" } });
        var checklistResponse = await client.SendAsync(checklist);
        var checklistBody = await BodyOf(checklistResponse);

        Assert.Equal(HttpStatusCode.BadRequest, checklistResponse.StatusCode);
        Assert.Equal("VALIDATION_FAILED", checklistBody.GetProperty("code").GetString());

        var after = await client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/import/packages/{draftId}", access));
        var afterBody = await BodyOf(after);
        var confirmedAfter = afterBody.GetProperty("checklistConfirmed")
            .EnumerateArray().Select(v => v.GetString()).ToArray();

        Assert.Equal(confirmedBefore, confirmedAfter);
        Assert.False(afterBody.GetProperty("checklistComplete").GetBoolean());
    }

    private async Task<string> UploadDraftAsync(HttpClient client, string access)
    {
        var request = Request(HttpMethod.Post, "/api/v1/admin/import/packages", access);
        request.Content = ValidStructuredPackage();
        var response = await client.SendAsync(request);
        var body = await BodyOf(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draftId = body.GetProperty("draftId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(draftId));
        return draftId!;
    }

    private static async Task ResolveOpenWarningsAsync(HttpClient client, string access, string draftId)
    {
        var get = await client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/import/packages/{draftId}", access));
        var body = await BodyOf(get);
        foreach (var warning in body.GetProperty("warnings").EnumerateArray())
        {
            if (warning.GetProperty("resolved").GetBoolean()) continue;

            var overrideRequest = Request(
                HttpMethod.Post,
                $"/api/v1/admin/import/packages/{draftId}/warnings/{warning.GetProperty("id").GetString()}/override",
                access);
            overrideRequest.Content = JsonContent.Create(new
            {
                reason = "Resolved in the HTTP checklist-unblocks-approval fixture.",
            });
            var overrideResponse = await client.SendAsync(overrideRequest);
            Assert.Equal(HttpStatusCode.OK, overrideResponse.StatusCode);
        }
    }

    /// <summary>Seeds a structured, valid draft carrying one unresolved warning "w1".</summary>
    private async Task<string> SeedDraftWithWarningAsync()
    {
        using var scope = app.Services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ExamImportWorkflow>();
        var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();

        var attempt = await workflow.ImportStructuredAsync(
            ValidPackageJson.Replace("admin-import-test", $"seed-{Guid.NewGuid():n}"),
            Vni.Ielts.Domain.Exams.ExamDefinitionId.New(), 1, default);

        Assert.True(attempt.IsAccepted, string.Join("; ", attempt.Findings.Select(f => f.Message)));

        var seeded = attempt.Draft! with
        {
            Warnings = [new ImportReviewWarning(
                "w1", ImportReviewCategory.TranscriptAndEvidence, "/sections/0", "Missing transcript.", false)],
            Revision = attempt.Draft.Revision + 1,
        };

        var replaced = await drafts.ReplaceAsync(seeded, attempt.Draft.Revision, default);
        Assert.True(replaced);

        return seeded.Id.ToString("D");
    }
}
