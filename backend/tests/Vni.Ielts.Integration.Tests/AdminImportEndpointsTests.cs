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
using MongoDB.Driver;
using Vni.Ielts.Infrastructure.Persistence;

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

    /// <summary>Posts one ZIP and returns the operation id the door handed back.</summary>
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

    /// <summary>
    /// A valid package whose bytes differ from every other test's, so an
    /// operation id derived from the hash cannot collide across tests sharing
    /// one database.
    /// </summary>
    private static string UniquePackageJson() =>
        ValidPackageJson.Replace("admin-import-test", $"admin-{Guid.NewGuid():n}");

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
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(BombBytes());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part, "file", "bomb.zip");
        content.Add(new StringContent(BombDefinitionId), "definitionId");
        return content;
    }

    /// <summary>
    /// Named rather than generated, so a test can derive the operation id the
    /// endpoint <i>would</i> have used and assert that no such job exists.
    /// </summary>
    private const string BombDefinitionId = "bomb-must-never-be-enqueued";

    /// <summary>Deterministic, so its SHA-256 — the archive key — is derivable too.</summary>
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

    /// <summary>
    /// <b>202, not 201 — a Cambridge parse takes minutes and costs money.</b>
    /// Doing it inside the POST times out and loses what was paid for, and
    /// this machine's own notes record that the API restarts. The request now
    /// hashes, stores and enqueues; the work happens in the worker.
    /// </summary>
    [SkippableFact]
    public async Task Uploading_a_package_returns_202_and_an_operation_id()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();

        var request = Request(HttpMethod.Post, "/api/v1/admin/import/packages", access);
        request.Content = ValidStructuredPackage();

        var response = await client.SendAsync(request);
        var body = await BodyOf(response);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("operationId").GetString()));

        // The server generates a definition id when the upload did not name
        // one; a caller that never learned it could not find its own exam.
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("definitionId").GetString()));

        // A handle, and where to follow it.
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("/api/v1/admin/import/jobs/", response.Headers.Location!.ToString());
    }

    /// <summary>
    /// <b>The upload must survive the request that carried it, and be readable
    /// by another process.</b> Asserting it can be OPENED is the point — a
    /// store that accepts bytes and keeps none would pass a weaker assertion,
    /// and that is not hypothetical: <c>IPrivateImportAssetStore</c>'s default
    /// implementation does exactly that, and its name invites exactly this
    /// mistake.
    /// </summary>
    [SkippableFact]
    public async Task The_archive_is_stored_readably_before_the_job_is_enqueued()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var zip = BuildZip(("reading/exam.json", UniquePackageJson()));

        var operationId = await UploadAsync(client, access, zip);

        using var scope = app.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IImportOutbox>();
        var archives = scope.ServiceProvider.GetRequiredService<IImportArchiveStore>();

        var job = await outbox.FindAsync(operationId, default);
        Assert.NotNull(job);
        Assert.False(string.IsNullOrWhiteSpace(job!.ArchiveKey));

        await using var read = await archives.OpenAsync(job.ArchiveKey, default);
        Assert.NotNull(read);

        using var buffer = new MemoryStream();
        await read!.CopyToAsync(buffer);
        Assert.Equal(zip, buffer.ToArray());
    }

    /// <summary>
    /// The operation id is derived from the bytes, so a retried upload is the
    /// same job rather than a second paid parse. The unique index decides
    /// that, not this endpoint's memory.
    /// </summary>
    [SkippableFact]
    public async Task Re_uploading_identical_bytes_does_not_start_a_second_job()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var zip = BuildZip(("reading/exam.json", UniquePackageJson()));

        var definitionId = $"cam-{Guid.NewGuid():n}";

        var first = await UploadAsync(client, access, zip, definitionId);
        var second = await UploadAsync(client, access, zip, definitionId);

        Assert.Equal(first, second);

        using var scope = app.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IImportOutbox>();
        var job = await outbox.FindAsync(first, default);

        // One job, still owed once.
        Assert.Equal(0, job!.Attempts);
        Assert.Equal(ImportJobState.Pending, job.State);
    }

    /// <summary>
    /// "What happened to my import", answered.
    ///
    /// <b>The worker is not run here, and that is deliberate.</b> This
    /// assembly cannot reference <c>Vni.Ielts.Worker</c> — both it and the API
    /// generate a top-level <c>Program</c> in the global namespace, which is
    /// why the worker has its own test project. The end-to-end "a worker turns
    /// this job into a draft" property is pinned in
    /// <c>ImportWorkerTests</c>, against the real pipeline and the real
    /// outbox; what belongs here is that the HTTP surface reports the job
    /// truthfully.
    /// </summary>
    [SkippableFact]
    public async Task The_job_endpoint_reports_the_stage_and_state_it_is_in()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var operationId = await UploadAsync(
            client, access, BuildZip(("reading/exam.json", UniquePackageJson())));

        var response = await client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/import/jobs/{Uri.EscapeDataString(operationId)}", access));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyOf(response);

        Assert.Equal(operationId, body.GetProperty("operationId").GetString());
        Assert.Equal("Extracting", body.GetProperty("stage").GetString());
        Assert.Equal("Pending", body.GetProperty("state").GetString());
        Assert.Equal(0, body.GetProperty("attempts").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("draftId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("lastError").ValueKind);
    }

    [SkippableFact]
    public async Task An_unknown_operation_id_is_a_404()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();

        var response = await client.SendAsync(
            Request(HttpMethod.Get, "/api/v1/admin/import/jobs/nothing-was-ever-enqueued", access));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// <b>A loud failure nobody can read is only half the trade.</b>
    /// <c>MongoImportOutbox</c> refuses to map a stored stage this binary does
    /// not define, rather than silently resetting the job to <c>Extracting</c>
    /// and re-buying a parse. The window in which that happens is a rollback —
    /// which is exactly when an operator is asking this endpoint what happened
    /// to their import — so it must not be an unhandled 500 with nothing in
    /// it.
    ///
    /// The stage is written straight into Mongo because there is no code path
    /// that can produce it: it is by definition a value a <i>newer</i> binary
    /// wrote.
    /// </summary>
    [SkippableFact]
    public async Task A_job_whose_stage_this_build_cannot_read_is_explained_not_a_500()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var operationId = await UploadAsync(
            client, access, BuildZip(("reading/exam.json", UniquePackageJson())));

        using (var scope = app.Services.CreateScope())
        {
            /*
             * Written through the driver rather than through the store, and
             * as a loose BSON document rather than the mapped one. The store
             * has no way to write this — that is the point of the guard — and
             * the document type is internal to Infrastructure, so reaching
             * for it here would mean widening an assembly boundary to stage a
             * corruption. The collection name and the field name are the two
             * facts this test needs, and both are stable.
             */
            var options = scope.ServiceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoOptions>>().Value;

            await new MongoClient(options.ConnectionString)
                .GetDatabase(options.Database)
                .GetCollection<MongoDB.Bson.BsonDocument>("import_jobs")
                .UpdateOneAsync(
                    new MongoDB.Bson.BsonDocument("operationId", operationId),
                    new MongoDB.Bson.BsonDocument("$set", new MongoDB.Bson.BsonDocument("stage", 99)));
        }

        var response = await client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/import/jobs/{Uri.EscapeDataString(operationId)}", access));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await BodyOf(response);
        Assert.Equal("IMPORT_JOB_STAGE_UNREADABLE", body.GetProperty("code").GetString());
        Assert.Equal(99, body.GetProperty("stage").GetInt32());

        // The detail says what to do, not just that something is wrong.
        Assert.Contains("rolled back", body.GetProperty("detail").GetString()!);
    }

    /// <summary>
    /// <b>Rule 3, kept at the door even though the work moved out of band.</b>
    /// An uploaded ZIP is validated <i>before anything is persisted</i> — and
    /// "persisted" now includes the private archive the worker reads back, not
    /// only the draft. Inspection reads the central directory and writes
    /// nothing, so it costs this request almost nothing and it means a bomb
    /// never reaches storage, never occupies a job row, and never has to be
    /// swept up after a worker refuses it minutes later.
    ///
    /// The assertions are deliberately stronger than the 422: nothing was
    /// stored, and nothing was enqueued.
    /// </summary>
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

        // No handle is even offered — there is nothing for a caller to follow.
        Assert.False(body.TryGetProperty("operationId", out _));
        Assert.False(body.TryGetProperty("draftId", out _));

        using var scope = app.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IImportOutbox>();
        var archives = scope.ServiceProvider.GetRequiredService<IImportArchiveStore>();

        // Nothing owed, and nothing kept. The operation id and the archive key
        // are both derived from the bytes, so the test can name exactly what
        // must not exist without the endpoint having told it anything.
        var bomb = BombBytes();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bomb))
            .ToLowerInvariant();

        Assert.Null(await archives.OpenAsync($"imports/archives/{hash}.zip", default));
        Assert.Null(await outbox.FindAsync(
            ImportJob.OperationIdFor(
                new Vni.Ielts.Domain.Exams.ExamDefinitionId(BombDefinitionId), 1, hash,
                ImportJob.NoParserConfigured),
            default));
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
