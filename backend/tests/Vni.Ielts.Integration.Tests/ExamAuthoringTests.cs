using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// In-place CMS authoring, through the real HTTP pipeline — the "third
/// producer" `exam.schema.json` names, alongside the dev seeder and the ZIP
/// importer. → `docs/development/phase-3-question-builder-plan.md` Plan 01
/// </summary>
public sealed class ExamAuthoringTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
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

    private async Task GrantRoleAsync(string userId, string roleName)
    {
        var roles = Db().GetCollection<BsonDocument>("roles");
        var role = await roles.Find(Builders<BsonDocument>.Filter.Eq("name", roleName)).FirstOrDefaultAsync();
        Assert.NotNull(role);

        var users = Db().GetCollection<BsonDocument>("users");
        await users.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", userId),
            Builders<BsonDocument>.Update.Set("roleIds", new BsonArray { role["_id"].AsString }));
    }

    private async Task<ExamVersion> SeedBlankDraftAsync(UserId createdBy)
    {
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Seeded blank draft", ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [],
            authorId: createdBy);
        await Db().GetCollection<ExamVersionDocument>("exam_versions").InsertOneAsync(version.ToDocument());
        return version;
    }

    private static HttpRequestMessage PostRequest(string path, string access, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
        return request;
    }

    /// <summary>Raw JSON body, not an object graph — the endpoints under test read the request body as text.</summary>
    private static HttpRequestMessage RawJsonRequest(HttpMethod method, string path, string access, string json)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
        return request;
    }

    private static HttpRequestMessage GetRequest(string path, string access)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return request;
    }

    private static string ValidContentJson(string questionType = "short-answer")
    {
        var options = questionType == "multiple-choice"
            ? """
            , "options": [ { "key": "A", "text": "One" }, { "key": "B", "text": "Two" } ]
            """
            : "";

        return $$"""
        {
          "formatVersion": "1.0", "title": "T", "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [ { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 5 } ] } },
          "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1, "kind": "passage",
            "questions": [
              { "id": "r-1", "order": 1, "type": "{{questionType}}", "prompt": "Sample prompt",
                "explanation": { "shortReason": "Because the text says so.", "evidence": ["line 1"] },
                "answerKey": { "accepted": ["answer"] }{{options}} }
            ] } ] } ]
        }
        """;
    }

    private const string InvalidContentJson = """
    {
      "formatVersion": "1.0", "title": "T", "variant": "academic",
      "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
      "scoringProfile": { "rawToBand": { "reading": [ { "minRaw": 0, "band": 0 } ] } },
      "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1, "kind": "passage",
        "questions": [
          { "id": "r-1", "order": 1, "type": "multiple-choice" }
        ] } ] } ]
    }
    """;

    [SkippableFact]
    public async Task An_author_can_create_a_blank_draft()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "exam-author");
        var (access, _) = await SignInAsync(client);

        var response = await client.SendAsync(
            PostRequest("/api/v1/admin/exams", access, new { title = "My New Reading Test", variant = "academic" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("draft", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("versionNumber").GetInt32());
    }

    [SkippableFact]
    public async Task Saving_valid_content_to_ones_own_draft_persists_it_and_the_preview_reflects_the_content()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "exam-author");
        var (access, _) = await SignInAsync(client);

        var draft = await SeedBlankDraftAsync(new UserId(userId));

        var save = await client.SendAsync(RawJsonRequest(
            HttpMethod.Put, $"/api/v1/admin/exams/{draft.Id.Value}/content", access, ValidContentJson()));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var saveBody = await save.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(saveBody.GetProperty("valid").GetBoolean());

        var preview = await client.SendAsync(GetRequest($"/api/v1/admin/exams/{draft.Id.Value}/preview", access));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewBody = await preview.Content.ReadFromJsonAsync<JsonElement>();
        var question = previewBody.GetProperty("sections")[0].GetProperty("parts")[0].GetProperty("questions")[0];
        Assert.Equal("shortanswer", question.GetProperty("type").GetString());
        Assert.Equal("Sample prompt", question.GetProperty("prompt").GetString());
    }

    [SkippableFact]
    public async Task Saving_content_to_someone_elses_draft_is_forbidden()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "exam-author");
        var (access, _) = await SignInAsync(client);

        var someoneElses = await SeedBlankDraftAsync(UserId.New());

        var save = await client.SendAsync(RawJsonRequest(
            HttpMethod.Put, $"/api/v1/admin/exams/{someoneElses.Id.Value}/content", access, ValidContentJson()));
        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
    }

    [SkippableFact]
    public async Task Saving_invalid_content_returns_findings_and_does_not_mutate_the_stored_version()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "exam-author");
        var (access, _) = await SignInAsync(client);

        var draft = await SeedBlankDraftAsync(new UserId(userId));

        var save = await client.SendAsync(RawJsonRequest(
            HttpMethod.Put, $"/api/v1/admin/exams/{draft.Id.Value}/content", access, InvalidContentJson));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode); // structured findings, not a generic 400
        var saveBody = await save.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(saveBody.GetProperty("valid").GetBoolean());
        Assert.True(saveBody.GetProperty("findings").GetArrayLength() > 0);

        var detail = await client.SendAsync(GetRequest($"/api/v1/admin/exams/{draft.Id.Value}", access));
        var detailBody = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, detailBody.GetProperty("modules").GetArrayLength()); // still the blank draft — rejected content never landed
    }

    [SkippableFact]
    public async Task The_validate_endpoint_reports_findings_without_changing_anything()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "exam-author");
        var (access, _) = await SignInAsync(client);

        var draft = await SeedBlankDraftAsync(new UserId(userId));

        var validate = await client.SendAsync(RawJsonRequest(
            HttpMethod.Post, $"/api/v1/admin/exams/{draft.Id.Value}/validate", access, InvalidContentJson));
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
        var body = await validate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.Equal("schema", body.GetProperty("findings")[0].GetProperty("stage").GetString());
        Assert.Equal("SCHEMA_INVALID", body.GetProperty("findings")[0].GetProperty("code").GetString());

        var detail = await client.SendAsync(GetRequest($"/api/v1/admin/exams/{draft.Id.Value}", access));
        var detailBody = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("draft", detailBody.GetProperty("status").GetString()); // untouched
    }

    [SkippableFact]
    public async Task Saving_valid_content_writes_an_exam_content_saved_audit_row()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "exam-author");
        var (access, _) = await SignInAsync(client);
        var draft = await SeedBlankDraftAsync(new UserId(userId));

        var save = await client.SendAsync(RawJsonRequest(
            HttpMethod.Put, $"/api/v1/admin/exams/{draft.Id.Value}/content", access, ValidContentJson()));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var audits = await Db().GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "ExamContentSaved"),
                Builders<BsonDocument>.Filter.Eq("targetId", draft.Id.Value)))
            .ToListAsync();
        Assert.NotEmpty(audits);
    }

    [SkippableFact]
    public async Task Getting_content_returns_the_schema_document_the_builder_will_put()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "exam-author");
        var (access, _) = await SignInAsync(client);
        var draft = await SeedBlankDraftAsync(new UserId(userId));

        var save = await client.SendAsync(RawJsonRequest(
            HttpMethod.Put, $"/api/v1/admin/exams/{draft.Id.Value}/content", access, ValidContentJson("multiple-choice")));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var content = await client.SendAsync(GetRequest($"/api/v1/admin/exams/{draft.Id.Value}/content", access));
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        var body = await content.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("1.0", body.GetProperty("formatVersion").GetString());
        Assert.Equal("multiple-choice",
            body.GetProperty("sections")[0].GetProperty("parts")[0].GetProperty("questions")[0]
                .GetProperty("type").GetString());
    }
}
