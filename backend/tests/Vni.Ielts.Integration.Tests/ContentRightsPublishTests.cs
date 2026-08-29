using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Content;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// FS0's phase gate, through the real HTTP pipeline:
///
/// <blockquote>a source may be imported without publish rights, but the publish
/// endpoint must refuse it.</blockquote>
///
/// <b>Why this has to be an integration test and not a handler test.</b> The
/// refusal is only worth anything if it happens on the route an operator
/// actually calls, after authentication, after the permission check, after the
/// idempotency guard — and with the status and error code a client branches
/// on. Every one of those is invisible to a test of the use case alone.
///
/// <b>Both directions are covered on purpose.</b> A gate that refuses
/// everything passes every refusal test ever written; the last case here
/// grants a right through the registry and shows the same request succeed.
/// </summary>
public sealed class ContentRightsPublishTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>
    /// A signed-in operator holding <c>exam.publish</c>.
    ///
    /// The stub provider always returns the same account, so the role is
    /// granted directly and a <i>second</i> sign-in is taken — permissions are
    /// resolved when the access token is minted, so the first token would not
    /// carry it.
    /// </summary>
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

        var complete = await client.PostAsJsonAsync(
            "/api/v1/auth/sso/complete", new { handoffCode = code });
        complete.EnsureSuccessStatusCode();

        return (await complete.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> PublishAsync(
        HttpClient client, string access, ExamVersionId id)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/admin/exams/{id.Value}/publish");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        // Every state-changing route on this API demands one. A fresh key per
        // attempt, so a replay never stands in for a fresh decision.
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>
    /// A fresh draft built from a seeded paper's shape, under a given
    /// definition id.
    ///
    /// Fresh every time: these cases publish, and sharing an id would make each
    /// depend on what the previous one left behind.
    /// </summary>
    private static async Task<ExamVersion> DraftAsync(
        IExamCatalogue catalogue, ExamDefinitionId definitionId)
    {
        var seeded = (await catalogue.ListAllAsync(default)).FirstOrDefault();
        Assert.True(seeded is not null, "No exam is seeded, so nothing here can be published.");

        var draft = ExamVersion.Rehydrate(
            ExamVersionId.New(), definitionId, 1, seeded!.Title, seeded.Variant,
            ExamVersionStatus.Draft, null, seeded.Scoring, seeded.Timing, seeded.Sections);

        await catalogue.UpsertAsync(draft, default);
        return draft;
    }

    [SkippableFact]
    public async Task An_exam_whose_source_has_no_registry_entry_is_refused_at_publish()
    {
        /*
         * The default-deny case, and the one that keeps the registry honest as
         * material arrives. Presence of a paper in the catalogue is not
         * permission to ship it — nobody has looked at where it came from.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();

        using var scope = app.Services.CreateScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();

        var draft = await DraftAsync(catalogue, new ExamDefinitionId($"unregistered-{Guid.NewGuid():n}"));

        var response = await PublishAsync(client, access, draft.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await BodyOf(response);
        Assert.Equal("CONTENT_RIGHT_MISSING", body.GetProperty("code").GetString());
        Assert.Equal("no-registry-entry", body.GetProperty("reason").GetString());

        // And the version really did not publish.
        var stored = await catalogue.FindAsync(draft.Id, default);
        Assert.Equal(ExamVersionStatus.Draft, stored!.Status);
    }

    [SkippableFact]
    public async Task An_imported_fixture_only_source_is_refused_at_publish()
    {
        /*
         * The FS0 phase gate in one sentence: importing is allowed, publishing
         * is not. The registry knows this material — it is seeded, with an
         * owner and a root path — and grants it `fixture` and nothing more,
         * because `M-53` has not said which papers are cleared.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();

        using var scope = app.Services.CreateScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();
        var registry = scope.ServiceProvider.GetRequiredService<IContentRightsRegistry>();

        var seededSource = await registry.FindAsync(new ContentSourceId("synthetic-full-1"), default);
        Assert.True(
            seededSource is not null,
            "The content rights seed did not run, so this case would prove nothing.");

        Assert.DoesNotContain(
            ContentEnvironment.LearnerProduction, seededSource!.AllowedEnvironments);

        var draft = await DraftAsync(catalogue, new ExamDefinitionId("seed-synthetic-full-1"));

        var response = await PublishAsync(client, access, draft.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await BodyOf(response);
        Assert.Equal("CONTENT_RIGHT_MISSING", body.GetProperty("code").GetString());
        Assert.Equal("environment-not-granted", body.GetProperty("reason").GetString());
        Assert.Equal("synthetic-full-1", body.GetProperty("sourceId").GetString());

        Assert.Equal(
            ExamVersionStatus.Draft, (await catalogue.FindAsync(draft.Id, default))!.Status);
    }

    [SkippableFact]
    public async Task A_source_that_holds_the_right_publishes()
    {
        /*
         * <b>Without this case the two above prove nothing.</b> An endpoint
         * that refused every publish outright would satisfy both, and the
         * product would look correct until the day `M-53` is answered and
         * nothing could be shipped at all.
         *
         * The grant is made here through the registry, which is where a grant
         * belongs — a reviewer's act with a licence reference behind it — and
         * never from a seed file.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();

        using var scope = app.Services.CreateScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();
        var registry = scope.ServiceProvider.GetRequiredService<IContentRightsRegistry>();

        var definition = new ExamDefinitionId($"cleared-{Guid.NewGuid():n}");
        var slug = $"cleared-{Guid.NewGuid():n}"[..24];

        await registry.RegisterIfAbsentAsync(
            ContentSource.Register(
                new ContentSourceId(slug),
                "A source cleared for publication in this test only",
                owner: "VNI Education",
                proof: new RightsProof(
                    "integration-test", "test@vni.example", DateTimeOffset.UtcNow.AddDays(-1)),
                allowedEnvironments:
                    [ContentEnvironment.Fixture, ContentEnvironment.LearnerProduction],
                expiresAt: null,
                rootPath: "fixtures/exams",
                files: [],
                boundExamVersionIds: [],
                boundExamDefinitionIds: [definition]),
            default);

        var draft = await DraftAsync(catalogue, definition);

        var response = await PublishAsync(client, access, draft.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("published", (await BodyOf(response)).GetProperty("status").GetString());

        Assert.Equal(
            ExamVersionStatus.Published, (await catalogue.FindAsync(draft.Id, default))!.Status);
    }

    [SkippableFact]
    public async Task An_expired_grant_stops_publishing_again()
    {
        // A licence with an end date is the ordinary commercial shape, and the
        // day it lapses is the day nobody is looking.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();

        using var scope = app.Services.CreateScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();
        var registry = scope.ServiceProvider.GetRequiredService<IContentRightsRegistry>();

        var definition = new ExamDefinitionId($"lapsed-{Guid.NewGuid():n}");
        var slug = $"lapsed-{Guid.NewGuid():n}"[..24];

        await registry.RegisterIfAbsentAsync(
            ContentSource.Register(
                new ContentSourceId(slug), "A grant that has run out",
                owner: null,
                proof: new RightsProof(
                    "integration-test", "test@vni.example", DateTimeOffset.UtcNow.AddYears(-2)),
                allowedEnvironments: [ContentEnvironment.LearnerProduction],
                expiresAt: DateTimeOffset.UtcNow.AddDays(-1),
                rootPath: "fixtures/exams", files: [],
                boundExamVersionIds: [], boundExamDefinitionIds: [definition]),
            default);

        var draft = await DraftAsync(catalogue, definition);

        var response = await PublishAsync(client, access, draft.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("right-expired", (await BodyOf(response)).GetProperty("reason").GetString());
    }

    [SkippableFact]
    public async Task The_registry_is_readable_by_an_operator_and_shows_nothing_publishable()
    {
        /*
         * A refusal an operator cannot investigate reads as a bug. The listing
         * is what turns "publish refused" into "here is the source, here is
         * what it is registered for, here is who reviewed it — nobody".
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/content-sources");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await BodyOf(response);
        var sources = body.GetProperty("sources").EnumerateArray().ToArray();

        Assert.NotEmpty(sources);

        // Every source the seed put there names its environments, and the
        // seeded ones name only `fixture`. The test-only grants made by the
        // cases above may also be present, so this asserts on the seeded ids
        // rather than on the whole collection.
        var seededIds = sources
            .Where(s => s.GetProperty("sourceId").GetString() is "exam1"
                or "cambridge-ielts-16" or "vol9-test-1" or "synthetic-full-1")
            .ToArray();

        Assert.Equal(4, seededIds.Length);

        foreach (var source in seededIds)
        {
            var environments = source.GetProperty("allowedEnvironments")
                .EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();

            Assert.Equal(["fixture"], environments);
            Assert.False(source.GetProperty("mayReachLearners").GetBoolean());
        }
    }

    [SkippableFact]
    public async Task An_operator_without_package_read_cannot_read_the_registry()
    {
        // The registry names where third-party material sits on disk. That is
        // not a learner's business, and the permission check is here rather
        // than in the CMS's sidebar.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var access = await SsoRoundTripAsync(client);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

            var admin = await roles.FindByNameAsync(SystemRoles.Admin, default);
            var user = await users.FindByEmailAsync(
                Vni.Ielts.Domain.Identity.Email.Create("stub.learner@example.com"), default);

            if (user is not null && admin is not null && user.HasRole(admin!.Id))
            {
                user!.RemoveRole(admin.Id);
                await users.SaveAsync(user, default);
            }
        }

        // A token minted after the role was dropped.
        var plain = await SsoRoundTripAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/content-sources");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", plain);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
