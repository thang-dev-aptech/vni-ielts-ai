using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Package hard-delete with Draft cascade — fills the dead <c>package.delete</c>
/// permission, and is the unlock for <see cref="ExamDeleteTests"/> when a
/// draft is still named by <c>createdVersionIds</c>.
/// </summary>
public sealed class PackageDeleteTests(FaultInjectionAppFactory app) : IClassFixture<FaultInjectionAppFactory>
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

    private async Task<ExamVersion> SeedDraftAsync(UserId createdBy, string title = "Package cascade draft")
    {
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, title, ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [],
            authorId: createdBy);
        await Db().GetCollection<ExamVersionDocument>("exam_versions").InsertOneAsync(version.ToDocument());
        return version;
    }

    private async Task<string> SeedImportedPackageAsync(UserId uploadedBy, params string[] createdVersionIds)
    {
        var now = DateTimeOffset.UtcNow;
        var package = ExamPackage.Rehydrate(
            Guid.NewGuid().ToString("n"),
            ExamPackageSourceKind.Zip,
            uploadedBy,
            sha256: new string('a', 64),
            fileName: "cascade-test.zip",
            uploadRef: Guid.NewGuid().ToString("n"),
            PackageImportStatus.Imported,
            findings: [],
            entries: [],
            createdVersionIds: createdVersionIds,
            version: 1,
            uploadPurged: true,
            createdAt: now,
            updatedAt: now);

        await Db().GetCollection<ExamPackageDocument>("exam_packages").InsertOneAsync(package.ToDocument());
        return package.Id;
    }

    private static HttpRequestMessage DeleteRequest(string path, string access)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
        return request;
    }

    [SkippableFact]
    public async Task Deleting_package_cascades_still_draft_versions_and_audits_both()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "admin");
        var (access, _) = await SignInAsync(client);

        var owner = new UserId(userId);
        var draft = await SeedDraftAsync(owner);
        var packageId = await SeedImportedPackageAsync(owner, draft.Id.Value);

        var response = await client.SendAsync(
            DeleteRequest($"/api/v1/admin/packages/{packageId}", access));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Null(await Db().GetCollection<ExamVersionDocument>("exam_versions")
            .Find(v => v.Id == draft.Id.Value).FirstOrDefaultAsync());
        Assert.Null(await Db().GetCollection<ExamPackageDocument>("exam_packages")
            .Find(p => p.Id == packageId).FirstOrDefaultAsync());

        var packageAudits = await Db().GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "PackageDeleted"),
                Builders<BsonDocument>.Filter.Eq("targetId", packageId)))
            .ToListAsync();
        Assert.NotEmpty(packageAudits);

        var examAudits = await Db().GetCollection<BsonDocument>("audit_log")
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("action", "ExamDeleted"),
                Builders<BsonDocument>.Filter.Eq("targetId", draft.Id.Value)))
            .ToListAsync();
        Assert.NotEmpty(examAudits);
    }

    [SkippableFact]
    public async Task Mid_cascade_failure_aborts_transaction_and_rolls_back_exam_and_package_audits()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "admin");
        var (access, _) = await SignInAsync(client);

        var owner = new UserId(userId);
        var draft1 = await SeedDraftAsync(owner, "Cascade draft 1");
        var draft2 = await SeedDraftAsync(owner, "Cascade draft 2");
        var packageId = await SeedImportedPackageAsync(owner, draft1.Id.Value, draft2.Id.Value);

        var seen = 0;
        app.CascadeHooks.AfterDraftDeleted = (_, _) =>
        {
            if (Interlocked.Increment(ref seen) == 1)
                throw new InvalidOperationException("injected mid-cascade failure");
            return Task.CompletedTask;
        };

        try
        {
            var response = await client.SendAsync(
                DeleteRequest($"/api/v1/admin/packages/{packageId}", access));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            Assert.NotNull(await Db().GetCollection<ExamVersionDocument>("exam_versions")
                .Find(v => v.Id == draft1.Id.Value).FirstOrDefaultAsync());
            Assert.NotNull(await Db().GetCollection<ExamVersionDocument>("exam_versions")
                .Find(v => v.Id == draft2.Id.Value).FirstOrDefaultAsync());
            Assert.NotNull(await Db().GetCollection<ExamPackageDocument>("exam_packages")
                .Find(p => p.Id == packageId).FirstOrDefaultAsync());

            Assert.Empty(await Db().GetCollection<BsonDocument>("audit_log")
                .Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("action", "PackageDeleted"),
                    Builders<BsonDocument>.Filter.Eq("targetId", packageId)))
                .ToListAsync());

            // First draft's ExamDeleted audit must also roll back with the txn.
            Assert.Empty(await Db().GetCollection<BsonDocument>("audit_log")
                .Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("action", "ExamDeleted"),
                    Builders<BsonDocument>.Filter.Eq("targetId", draft1.Id.Value)))
                .ToListAsync());
        }
        finally
        {
            app.CascadeHooks.AfterDraftDeleted = null;
        }
    }

    [SkippableFact]
    public async Task Race_draft_to_inreview_between_preflight_and_txn_leaves_everything_untouched()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "admin");
        var (access, _) = await SignInAsync(client);

        var owner = new UserId(userId);
        var draft = await SeedDraftAsync(owner, "Race draft");
        var packageId = await SeedImportedPackageAsync(owner, draft.Id.Value);

        app.CascadeHooks.BeforeDraftDelete = async (versionId, _) =>
        {
            // Simulate a concurrent SubmitForReview after preflight saw Draft.
            await Db().GetCollection<ExamVersionDocument>("exam_versions").UpdateOneAsync(
                v => v.Id == versionId.Value,
                Builders<ExamVersionDocument>.Update.Set(v => v.Status, ExamVersionStatus.InReview.ToString()));
        };

        try
        {
            var response = await client.SendAsync(
                DeleteRequest($"/api/v1/admin/packages/{packageId}", access));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var version = await Db().GetCollection<ExamVersionDocument>("exam_versions")
                .Find(v => v.Id == draft.Id.Value).FirstOrDefaultAsync();
            Assert.NotNull(version);
            Assert.Equal(ExamVersionStatus.InReview.ToString(), version.Status);

            Assert.NotNull(await Db().GetCollection<ExamPackageDocument>("exam_packages")
                .Find(p => p.Id == packageId).FirstOrDefaultAsync());

            Assert.Empty(await Db().GetCollection<BsonDocument>("audit_log")
                .Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("action", "PackageDeleted"),
                    Builders<BsonDocument>.Filter.Eq("targetId", packageId)))
                .ToListAsync());
            Assert.Empty(await Db().GetCollection<BsonDocument>("audit_log")
                .Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("action", "ExamDeleted"),
                    Builders<BsonDocument>.Filter.Eq("targetId", draft.Id.Value)))
                .ToListAsync());
        }
        finally
        {
            app.CascadeHooks.BeforeDraftDelete = null;
        }
    }

    [SkippableFact]
    public async Task Deleting_package_with_published_version_is_conflict_and_touches_nothing()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var (_, userId) = await SignInAsync(client);
        await GrantRoleAsync(userId, "admin");
        var (access, _) = await SignInAsync(client);

        var owner = new UserId(userId);
        var reviewer = UserId.New();
        var now = DateTimeOffset.UtcNow;
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Published from package", ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [],
            authorId: owner);
        version.SubmitForReview();
        version.Approve(reviewer);
        version.Publish(now);
        await Db().GetCollection<ExamVersionDocument>("exam_versions").InsertOneAsync(version.ToDocument());

        var packageId = await SeedImportedPackageAsync(owner, version.Id.Value);

        var response = await client.SendAsync(
            DeleteRequest($"/api/v1/admin/packages/{packageId}", access));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        Assert.NotNull(await Db().GetCollection<ExamVersionDocument>("exam_versions")
            .Find(v => v.Id == version.Id.Value).FirstOrDefaultAsync());
        Assert.NotNull(await Db().GetCollection<ExamPackageDocument>("exam_packages")
            .Find(p => p.Id == packageId).FirstOrDefaultAsync());
    }
}
