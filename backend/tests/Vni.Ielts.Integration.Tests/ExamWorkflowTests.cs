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
/// The six-state exam lifecycle, through the real HTTP pipeline, real
/// authorization, and a real database.
///
/// <para>
/// <b>The stub SSO provider is a single fixed identity</b>
/// (<c>stub.learner@example.com</c>), so a second, genuinely different
/// account is not reachable through the HTTP sign-in flow at all. That does
/// not weaken what is tested here: ownership is decided by comparing the
/// caller against <c>ExamVersion.CreatedBy</c>, and the caller's own id is
/// known in every test — what varies is which fixture a given test seeds
/// itself as the creator of, and which role the one account is granted. An
/// author who does not own a draft is exactly as unauthorized as a second
/// author would be.
/// </para>
///
/// <para>
/// <b>Roles are granted directly in Mongo, not through the API.</b>
/// <c>POST /admin/users/{id}/roles</c> itself requires <c>role.assign</c>, so
/// a freshly signed-in account has no self-service path to it — the same
/// bootstrap problem a real first admin has on a fresh clone. Because
/// permission claims are baked into the access token at issue time (not
/// resolved per request), a role granted this way only takes effect on the
/// <i>next</i> sign-in — this file signs in again after every grant.
/// </para>
/// </summary>
public sealed class ExamWorkflowTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";

    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private IMongoDatabase Db() => new MongoClient(ConnectionString).GetDatabase(app.Database);

    /// <summary>Signs in through the stub, returning a token and the caller's own id.</summary>
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

    /// <summary>
    /// Sets the account's roles to <i>exactly</i> <paramref name="roleName"/> —
    /// not additive. <see cref="app"/> is one <see cref="SsoAppFactory"/>
    /// shared by every test in this class (<c>IClassFixture</c>), and the stub
    /// provider is a single fixed identity, so every test signs in as the
    /// same account. An additive grant would let an earlier test's role leak
    /// into a later test's "this role cannot do X" assertion — exactly the
    /// kind of cross-test contamination a permission boundary test cannot
    /// afford.
    /// </summary>
    private async Task GrantRoleAsync(string userId, string roleName)
    {
        var roles = Db().GetCollection<BsonDocument>("roles");
        var role = await roles.Find(Builders<BsonDocument>.Filter.Eq("name", roleName)).FirstOrDefaultAsync();
        Assert.NotNull(role); // a role seeded under a name that does not exist is a broken test, not a 403

        var users = Db().GetCollection<BsonDocument>("users");
        await users.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", userId),
            Builders<BsonDocument>.Update.Set("roleIds", new BsonArray { role["_id"].AsString }));
    }

    /// <summary>
    /// Builds a version through the real domain transitions (so a fixture in
    /// <c>Approved</c> looks exactly like one that reached <c>Approved</c>
    /// through the endpoints) and inserts it with <c>ExamMappers.ToDocument()</c>
    /// — the same mapping the real write path uses, not a hand-built document
    /// that could silently drift from it.
    /// </summary>
    private async Task<ExamVersion> SeedAsync(
        UserId createdBy, ExamDefinitionId? definitionId = null, Action<ExamVersion>? arrange = null)
    {
        var version = ExamVersion.CreateDraft(
            definitionId ?? ExamDefinitionId.New(), 1, "Seeded", ExamVariant.Academic, createdBy,
            new ScoringProfile(
                new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int> { [ExamModule.Reading] = 60 }, null, []),
            [new Section(ExamModule.Reading, 1, [])]);

        arrange?.Invoke(version);

        await Db().GetCollection<ExamVersionDocument>("exam_versions").InsertOneAsync(version.ToDocument());
        return version;
    }

    private static HttpRequestMessage PostRequest(string path, string access, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body ?? new { }),
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

    [SkippableFact]
    public async Task An_author_can_submit_their_own_draft_but_not_someone_elses()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "exam-author");
        var (access, _) = await SignInAsync(client); // reissue: pick up the new role's claims

        var own = await SeedAsync(new UserId(userId));
        var someoneElses = await SeedAsync(UserId.New());

        var ownResponse = await client.SendAsync(PostRequest($"/api/v1/admin/exams/{own.Id.Value}/submit", access));
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        var ownBody = await ownResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("in-review", ownBody.GetProperty("status").GetString());

        var othersResponse = await client.SendAsync(
            PostRequest($"/api/v1/admin/exams/{someoneElses.Id.Value}/submit", access));
        Assert.Equal(HttpStatusCode.Forbidden, othersResponse.StatusCode);
    }

    [SkippableFact]
    public async Task Returning_with_an_empty_note_is_rejected_before_any_state_change()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "academic-lead");
        var (access, _) = await SignInAsync(client);

        var version = await SeedAsync(UserId.New(), arrange: v => v.SubmitForReview(UserId.New(), DateTimeOffset.UtcNow));

        var response = await client.SendAsync(
            PostRequest($"/api/v1/admin/exams/{version.Id.Value}/return", access, new { note = "   " }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var detail = await client.SendAsync(GetRequest($"/api/v1/admin/exams/{version.Id.Value}", access));
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("in-review", body.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task An_academic_lead_can_approve_but_a_publish_attempt_is_forbidden()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "academic-lead");
        var (access, _) = await SignInAsync(client);

        var version = await SeedAsync(UserId.New(), arrange: v => v.SubmitForReview(UserId.New(), DateTimeOffset.UtcNow));

        var approve = await client.SendAsync(
            PostRequest($"/api/v1/admin/exams/{version.Id.Value}/approve", access));
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var approveBody = await approve.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("approved", approveBody.GetProperty("status").GetString());

        var publish = await client.SendAsync(
            PostRequest($"/api/v1/admin/exams/{version.Id.Value}/publish", access));
        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
    }

    [SkippableFact]
    public async Task Admin_publishing_flips_a_sibling_published_version_and_both_reach_the_audit_log()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "admin");
        var (access, _) = await SignInAsync(client);

        var definitionId = ExamDefinitionId.New();
        var now = DateTimeOffset.UtcNow;

        var alreadyLive = await SeedAsync(UserId.New(), definitionId, v => v.Publish(now));
        var candidate = await SeedAsync(UserId.New(), definitionId, v =>
        {
            v.SubmitForReview(UserId.New(), now);
            v.Approve(UserId.New(), now);
        });

        var publish = await client.SendAsync(
            PostRequest($"/api/v1/admin/exams/{candidate.Id.Value}/publish", access));
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var list = await client.SendAsync(GetRequest("/api/v1/admin/exams", access));
        var exams = (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("exams")
            .EnumerateArray().ToList();

        var candidateRow = exams.Single(e => e.GetProperty("examVersionId").GetString() == candidate.Id.Value);
        var supersededRow = exams.Single(e => e.GetProperty("examVersionId").GetString() == alreadyLive.Id.Value);

        Assert.Equal("published", candidateRow.GetProperty("status").GetString());
        Assert.Equal("unpublished", supersededRow.GetProperty("status").GetString());

        var audit = await client.SendAsync(GetRequest("/api/v1/admin/audit?page=1", access));
        var entries = (await audit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entries")
            .EnumerateArray().ToList();

        Assert.Contains(entries, e =>
            e.GetProperty("action").GetString() == "ExamPublished"
            && e.GetProperty("targetId").GetString() == candidate.Id.Value);
        Assert.Contains(entries, e =>
            e.GetProperty("action").GetString() == "ExamUnpublished"
            && e.GetProperty("targetId").GetString() == alreadyLive.Id.Value);
    }
}
