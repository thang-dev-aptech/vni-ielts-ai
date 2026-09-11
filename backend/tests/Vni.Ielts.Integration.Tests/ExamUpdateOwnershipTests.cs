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
/// <c>exam.update.any</c> must authorise independently of <c>exam.update.own</c>
/// (admin seed holds .any only). Owner-.own and non-owner-.own cover the rest.
/// </summary>
public sealed class ExamUpdateOwnershipTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
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

    private async Task<ExamVersion> SeedBlankDraftAsync(UserId authorId)
    {
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Ownership draft", ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [],
            authorId: authorId);
        await Db().GetCollection<ExamVersionDocument>("exam_versions").InsertOneAsync(version.ToDocument());
        return version;
    }

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

    private static string ValidContentJson() => """
        {
          "formatVersion": "1.0", "title": "T", "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [ { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 5 } ] } },
          "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1, "kind": "passage",
            "questions": [
              { "id": "r-1", "order": 1, "type": "short-answer", "prompt": "Sample prompt",
                "answerKey": { "accepted": ["answer"] } }
            ] } ] } ]
        }
        """;

    [SkippableFact]
    public async Task Admin_with_exam_update_any_only_can_save_another_authors_draft()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        // Seed Admin holds ExamUpdateAny without ExamUpdateOwn — exact grant mirrors that.
        await GrantExactPermissionsAsync(userId, "admin-any",
            "exam.read.any", "exam.update.any", "exam.preview");
        var (access, _) = await SignInAsync(client);

        var someoneElses = await SeedBlankDraftAsync(UserId.New());

        var save = await client.SendAsync(RawJsonRequest(
            HttpMethod.Put, $"/api/v1/admin/exams/{someoneElses.Id.Value}/content", access, ValidContentJson()));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.True((await save.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valid").GetBoolean());
    }

    [SkippableFact]
    public async Task Owner_with_exam_update_own_can_save_own_draft()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "owner-own",
            "exam.read.own", "exam.update.own", "exam.preview");
        var (access, _) = await SignInAsync(client);

        var own = await SeedBlankDraftAsync(new UserId(userId));

        var save = await client.SendAsync(RawJsonRequest(
            HttpMethod.Put, $"/api/v1/admin/exams/{own.Id.Value}/content", access, ValidContentJson()));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.True((await save.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valid").GetBoolean());
    }

    [SkippableFact]
    public async Task Non_owner_with_exam_update_own_only_cannot_save_another_authors_draft()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "non-owner-own",
            "exam.read.own", "exam.update.own", "exam.preview");
        var (access, _) = await SignInAsync(client);

        var someoneElses = await SeedBlankDraftAsync(UserId.New());

        var save = await client.SendAsync(RawJsonRequest(
            HttpMethod.Put, $"/api/v1/admin/exams/{someoneElses.Id.Value}/content", access, ValidContentJson()));
        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
    }
}
