using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Content;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// `P-20`'s review lifecycle through the real HTTP pipeline — the same
/// reasoning as <see cref="ContentRightsPublishTests"/>: a refusal proves
/// something only on the route an operator actually calls, after
/// authentication, after the permission check, after the idempotency guard.
///
/// <b>The stub SSO provider always authenticates the same account</b>
/// (<c>stub.learner@example.com</c>), so every signed-in client in this file
/// is literally the same person. That is exactly what the reviewer ≠ author
/// case needs: "same person" is produced by setting a draft's
/// <c>AuthorId</c> to that one account's id before calling <c>/approve</c>,
/// and "different person" by leaving it as some other, unrelated id.
/// </summary>
public sealed class ExamReviewLifecycleTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(HttpClient Client, string Access, UserId UserId)> SignInAsAdminAsync()
    {
        var client = NewClient();
        await SsoRoundTripAsync(client);

        UserId userId;
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
            userId = user.Id;
        }

        // Permissions are resolved when the access token is minted, so a
        // second sign-in is taken after the role grant lands.
        var access = await SsoRoundTripAsync(client);
        return (client, access, userId);
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

    private static HttpRequestMessage PostRequest(string path, string access)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        // Every state-changing route demands one. Fresh per attempt, so a
        // replay never stands in for a fresh decision.
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        return request;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<ExamVersion> DraftAsync(
        IExamCatalogue catalogue, ExamDefinitionId? definitionId = null)
    {
        var seeded = (await catalogue.ListAllAsync(default)).FirstOrDefault();
        Assert.True(seeded is not null, "No exam is seeded, so nothing here can be reviewed.");

        var draft = ExamVersion.Rehydrate(
            ExamVersionId.New(), definitionId ?? ExamDefinitionId.New(), 1, seeded!.Title,
            seeded.Variant, ExamVersionStatus.Draft, null, seeded.Scoring, seeded.Timing,
            seeded.Sections);

        await catalogue.UpsertAsync(draft, default);
        return draft;
    }

    private static async Task<ExamVersion> InReviewVersionAsync(
        IExamCatalogue catalogue, UserId? authorId)
    {
        var seeded = (await catalogue.ListAllAsync(default)).FirstOrDefault();
        Assert.True(seeded is not null, "No exam is seeded, so nothing here can be reviewed.");

        var version = ExamVersion.Rehydrate(
            ExamVersionId.New(), ExamDefinitionId.New(), 1, seeded!.Title, seeded.Variant,
            ExamVersionStatus.InReview, null, seeded.Scoring, seeded.Timing, seeded.Sections,
            authorId: authorId);

        await catalogue.UpsertAsync(version, default);
        return version;
    }

    [SkippableFact]
    public async Task Submitting_a_draft_moves_it_to_in_review()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access, _) = await SignInAsAdminAsync();
        using var scope = app.Services.CreateScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();

        var draft = await DraftAsync(catalogue);

        var response = await client.SendAsync(
            PostRequest($"/api/v1/admin/exams/{draft.Id.Value}/submit-for-review", access));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("inreview", (await BodyOf(response)).GetProperty("status").GetString());
        Assert.Equal(
            ExamVersionStatus.InReview, (await catalogue.FindAsync(draft.Id, default))!.Status);
    }

    [SkippableFact]
    public async Task Submitting_a_version_that_is_not_a_draft_is_refused()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access, _) = await SignInAsAdminAsync();
        using var scope = app.Services.CreateScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();

        var version = await InReviewVersionAsync(catalogue, authorId: null);

        var response = await client.SendAsync(
            PostRequest($"/api/v1/admin/exams/{version.Id.Value}/submit-for-review", access));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// The HTTP-level counterpart of the domain test's primary red-when-removed
    /// case: the guard is enforced again here because nothing upstream of the
    /// domain call (permission check, idempotency) would ever catch it.
    /// </summary>
    [SkippableFact]
    public async Task A_reviewer_cannot_approve_a_version_they_authored()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access, reviewerId) = await SignInAsAdminAsync();
        using var scope = app.Services.CreateScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();

        // The draft's author is the SAME account the stub signs everyone in
        // as — the one case this harness can produce "same person" for.
        var version = await InReviewVersionAsync(catalogue, authorId: reviewerId);

        var response = await client.SendAsync(
            PostRequest($"/api/v1/admin/exams/{version.Id.Value}/approve", access));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("REVIEWER_IS_AUTHOR", (await BodyOf(response)).GetProperty("code").GetString());

        // And it really did not approve.
        Assert.Equal(
            ExamVersionStatus.InReview, (await catalogue.FindAsync(version.Id, default))!.Status);
    }

    [SkippableFact]
    public async Task A_reviewer_can_approve_a_version_a_different_author_wrote()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access, _) = await SignInAsAdminAsync();
        using var scope = app.Services.CreateScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();

        var version = await InReviewVersionAsync(catalogue, authorId: UserId.New());

        var response = await client.SendAsync(
            PostRequest($"/api/v1/admin/exams/{version.Id.Value}/approve", access));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("approved", (await BodyOf(response)).GetProperty("status").GetString());
        Assert.Equal(
            ExamVersionStatus.Approved, (await catalogue.FindAsync(version.Id, default))!.Status);
    }

    [SkippableFact]
    public async Task Returning_a_version_without_a_reason_is_refused()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access, _) = await SignInAsAdminAsync();
        using var scope = app.Services.CreateScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();

        var version = await InReviewVersionAsync(catalogue, authorId: null);

        var request = PostRequest($"/api/v1/admin/exams/{version.Id.Value}/return-to-draft", access);
        request.Content = JsonContent.Create(new { reason = "" });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            ExamVersionStatus.InReview, (await catalogue.FindAsync(version.Id, default))!.Status);
    }

    [SkippableFact]
    public async Task Returning_a_version_with_a_reason_sends_it_back_to_draft()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access, _) = await SignInAsAdminAsync();
        using var scope = app.Services.CreateScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();

        var version = await InReviewVersionAsync(catalogue, authorId: null);

        var request = PostRequest($"/api/v1/admin/exams/{version.Id.Value}/return-to-draft", access);
        request.Content = JsonContent.Create(new { reason = "Thiếu transcript phần Listening." });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("draft", (await BodyOf(response)).GetProperty("status").GetString());
        Assert.Equal(
            ExamVersionStatus.Draft, (await catalogue.FindAsync(version.Id, default))!.Status);
    }

    /// <summary>
    /// Proves the `P-20` publish precondition this slice added to
    /// <c>PublishEndpoint</c> (Approved or Unpublished only) — deliberately
    /// with the content rights already cleared, so the 409 seen here cannot
    /// be the rights gate's and must be this new check's.
    /// </summary>
    [SkippableFact]
    public async Task A_draft_cannot_be_published_even_with_content_rights_cleared()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access, _) = await SignInAsAdminAsync();
        using var scope = app.Services.CreateScope();
        var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();
        var registry = scope.ServiceProvider.GetRequiredService<IContentRightsRegistry>();

        var definition = new ExamDefinitionId($"review-gate-{Guid.NewGuid():n}");
        var slug = $"review-gate-{Guid.NewGuid():n}"[..24];

        await registry.RegisterIfAbsentAsync(
            ContentSource.Register(
                new ContentSourceId(slug), "Cleared for publication, in this test only",
                owner: "VNI Education",
                proof: new RightsProof(
                    "integration-test", "test@vni.example", DateTimeOffset.UtcNow.AddDays(-1)),
                allowedEnvironments: [ContentEnvironment.LearnerProduction],
                expiresAt: null,
                rootPath: "fixtures/exams", files: [],
                boundExamVersionIds: [], boundExamDefinitionIds: [definition]),
            default);

        var draft = await DraftAsync(catalogue, definition);

        var response = await client.SendAsync(
            PostRequest($"/api/v1/admin/exams/{draft.Id.Value}/publish", access));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Specifically not the rights gate's code — this is the new guard.
        var body = await BodyOf(response);
        Assert.NotEqual("CONTENT_RIGHT_MISSING", body.GetProperty("code").GetString());

        Assert.Equal(
            ExamVersionStatus.Draft, (await catalogue.FindAsync(draft.Id, default))!.Status);
    }
}
