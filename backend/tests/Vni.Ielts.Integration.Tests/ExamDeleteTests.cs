using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// Hard-delete of draft exam versions — fills the dead
/// <c>exam.delete.own</c> / <c>exam.delete.any</c> permissions.
/// Ownership uses <see cref="ExamVersion.AuthorId"/> (not feature <c>CreatedBy</c>).
/// Status wire for review is <c>inreview</c> (no hyphen).
/// </summary>
public sealed class ExamDeleteTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
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

    private async Task<ExamVersion> SeedAsync(UserId authorId, Action<ExamVersion>? configure = null)
    {
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Delete-me draft", ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [],
            authorId: authorId);
        configure?.Invoke(version);
        await Db().GetCollection<ExamVersionDocument>("exam_versions").InsertOneAsync(version.ToDocument());
        return version;
    }

    private static HttpRequestMessage DeleteRequest(string path, string access)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
        return request;
    }

    [SkippableFact]
    public async Task Author_can_delete_own_draft_and_audit_records_ExamDeleted()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "exam-author");
        var (access, _) = await SignInAsync(client);

        var draft = await SeedAsync(new UserId(userId));

        var response = await client.SendAsync(
            DeleteRequest($"/api/v1/admin/exams/{draft.Id.Value}", access));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var gone = await Db().GetCollection<ExamVersionDocument>("exam_versions")
            .Find(v => v.Id == draft.Id.Value).FirstOrDefaultAsync();
        Assert.Null(gone);

        var audits = await Db().GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "ExamDeleted"),
                Builders<BsonDocument>.Filter.Eq("targetId", draft.Id.Value)))
            .ToListAsync();
        Assert.NotEmpty(audits);
    }

    [SkippableTheory]
    [InlineData("inreview")]
    [InlineData("approved")]
    [InlineData("published")]
    public async Task Non_draft_statuses_are_conflict_even_for_the_owner(string shape)
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "exam-author");
        var (access, _) = await SignInAsync(client);

        var owner = new UserId(userId);
        var now = DateTimeOffset.UtcNow;
        var version = await SeedAsync(owner, v =>
        {
            v.SubmitForReview();
            if (shape == "inreview") return;
            // INT enforces reviewer ≠ author on Approve.
            v.Approve(UserId.New());
            if (shape == "approved") return;
            v.Publish(now);
        });

        var response = await client.SendAsync(
            DeleteRequest($"/api/v1/admin/exams/{version.Id.Value}", access));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var stillThere = await Db().GetCollection<ExamVersionDocument>("exam_versions")
            .Find(v => v.Id == version.Id.Value).FirstOrDefaultAsync();
        Assert.NotNull(stillThere);
    }

    [SkippableFact]
    public async Task Delete_own_scoped_caller_cannot_see_someone_elses_draft()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "exam-author");
        var (access, _) = await SignInAsync(client);

        var someoneElses = await SeedAsync(UserId.New());

        var response = await client.SendAsync(
            DeleteRequest($"/api/v1/admin/exams/{someoneElses.Id.Value}", access));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var stillThere = await Db().GetCollection<ExamVersionDocument>("exam_versions")
            .Find(v => v.Id == someoneElses.Id.Value).FirstOrDefaultAsync();
        Assert.NotNull(stillThere);
    }
}
