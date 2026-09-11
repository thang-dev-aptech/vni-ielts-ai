using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Negative coverage for <c>exam.preview</c> — read alone is not enough.
/// </summary>
public sealed class ExamPreviewPermissionTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
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
        var roleId = Guid.NewGuid().ToString("n");
        await Db().GetCollection<BsonDocument>("roles").InsertOneAsync(new BsonDocument
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
            ExamDefinitionId.New(), 1, "Preview gate draft", ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [],
            authorId: authorId);
        await Db().GetCollection<ExamVersionDocument>("exam_versions").InsertOneAsync(version.ToDocument());
        return version;
    }

    [SkippableFact]
    public async Task Preview_without_exam_preview_permission_is_forbidden()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "read-no-preview",
            "exam.read.own", "exam.update.own");
        var (access, _) = await SignInAsync(client);

        var draft = await SeedBlankDraftAsync(new UserId(userId));

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/exams/{draft.Id.Value}/preview");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task Preview_of_someone_elses_draft_without_read_any_is_forbidden()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantExactPermissionsAsync(userId, "preview-own-only",
            "exam.read.own", "exam.preview");
        var (access, _) = await SignInAsync(client);

        var someoneElses = await SeedBlankDraftAsync(UserId.New());

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/exams/{someoneElses.Id.Value}/preview");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
