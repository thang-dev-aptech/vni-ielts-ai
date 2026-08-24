using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The whole social sign-in flow, through the real HTTP pipeline and a real
/// database, driven by the stub provider.
///
/// <para>
/// The unit tests prove each decision in isolation; this proves the four
/// endpoints actually fit together — that the state survives a round trip
/// through Mongo, that the callback really does redirect with a code rather
/// than a token, and that the resulting bearer token opens <c>/me</c>. Those
/// are exactly the failures that unit tests cannot see.
/// </para>
///
/// <para>
/// <b>Skipped when no local MongoDB is listening</b>, rather than failing.
/// A developer without the infra stack up should get a passing suite and a
/// clear note, not a red build they did not cause. CI runs the stack.
/// </para>
/// </summary>
public sealed class SsoFlowTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private HttpClient NewClient() => app.CreateClient(new WebApplicationFactoryClientOptions
    {
        // Every redirect in this flow is an assertion target. Following them
        // automatically would hide the one thing worth checking.
        AllowAutoRedirect = false,
    });

    [SkippableFact]
    public async Task A_learner_can_sign_in_through_a_provider_and_use_the_token()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();

        // 1 · Start. The client receives a URL and nothing else.
        var start = await client.PostAsJsonAsync(
            "/api/v1/auth/sso/google/start", new { returnTo = "/hoc" });
        start.EnsureSuccessStatusCode();

        var authorizationUrl = new Uri(
            (await start.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("authorizationUrl").GetString()!);

        Assert.DoesNotContain("client_secret", authorizationUrl.ToString(), StringComparison.Ordinal);

        // 2 · The provider redirects the browser to our callback.
        var callback = await client.GetAsync(
            authorizationUrl.PathAndQuery, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);

        var back = callback.Headers.Location!;
        var returned = System.Web.HttpUtility.ParseQueryString(back.Query);

        var handoff = returned["code"];
        Assert.False(string.IsNullOrWhiteSpace(handoff));
        Assert.Equal("/hoc", returned["returnTo"]);

        // The point of the extra round trip: no token is ever in a URL.
        Assert.Null(returned["accessToken"]);
        Assert.Null(returned["refreshToken"]);

        // 3 · Exchange the handoff code for a session.
        var complete = await client.PostAsJsonAsync(
            "/api/v1/auth/sso/complete", new { handoffCode = handoff });
        complete.EnsureSuccessStatusCode();

        var session = await complete.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = session.GetProperty("accessToken").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("refreshToken").GetString()));

        // 4 · The token works.
        var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var meResponse = await client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();

        var body = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(session.GetProperty("userId").GetString(), body.GetProperty("userId").GetString());
        Assert.True(body.GetProperty("emailVerified").GetBoolean());

        // 5 · The handoff code is spent.
        var replay = await client.PostAsJsonAsync(
            "/api/v1/auth/sso/complete", new { handoffCode = handoff });

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("SSO_HANDOFF_INVALID", await CodeOf(replay));
    }

    [SkippableFact]
    public async Task A_replayed_callback_is_refused()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();

        var start = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
        var url = new Uri((await start.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("authorizationUrl").GetString()!);

        var first = await client.GetAsync(url.PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        var second = await client.GetAsync(url.PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);

        var query = System.Web.HttpUtility.ParseQueryString(second.Headers.Location!.Query);
        Assert.Equal("SSO_STATE_INVALID", query["error"]);
        Assert.Null(query["code"]);
    }

    [SkippableFact]
    public async Task A_second_sign_in_reuses_the_same_account()
    {
        // The identity is keyed on the provider's subject, so the second visit
        // must not produce a second learner.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var first = await SignInAsync();
        var second = await SignInAsync();

        Assert.Equal(first, second);
    }

    [SkippableFact]
    public async Task The_providers_endpoint_lists_what_is_configured()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var response = await NewClient().GetAsync("/api/v1/auth/sso/providers");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var keys = body.GetProperty("providers").EnumerateArray()
            .Select(p => p.GetProperty("key").GetString() ?? string.Empty).ToArray();

        Assert.Equal("google", Assert.Single(keys));
    }

    [SkippableFact]
    public async Task An_unconfigured_provider_is_a_404()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var response = await NewClient().PostAsJsonAsync("/api/v1/auth/sso/facebook/start", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("SSO_PROVIDER_UNKNOWN", await CodeOf(response));
    }

    private async Task<string> SignInAsync()
    {
        var client = NewClient();

        var start = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
        var url = new Uri((await start.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("authorizationUrl").GetString()!);

        var callback = await client.GetAsync(url.PathAndQuery);
        var handoff = System.Web.HttpUtility.ParseQueryString(callback.Headers.Location!.Query)["code"];

        var complete = await client.PostAsJsonAsync(
            "/api/v1/auth/sso/complete", new { handoffCode = handoff });
        complete.EnsureSuccessStatusCode();

        return (await complete.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("userId").GetString()!;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}

/// <summary>
/// The API, wired to a throwaway database and the stub sign-in provider.
/// </summary>
public sealed class SsoAppFactory : WebApplicationFactory<Program>
{
    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";

    private readonly string _database = $"vni_ielts_test_{Guid.NewGuid():n}";

    /// <summary>
    /// Probed once per run. A developer without the infra stack up gets a
    /// skipped suite and a note saying how to start it, not a red build.
    /// </summary>
    public static bool MongoAvailable => _mongoAvailable.Value;

    private static readonly Lazy<bool> _mongoAvailable = new(() =>
    {
        try
        {
            var client = new MongoClient(
                new MongoClientSettings
                {
                    Server = new MongoServerAddress("localhost", 27018),
                    DirectConnection = true,
                    ServerSelectionTimeout = TimeSpan.FromSeconds(3),
                    ConnectTimeout = TimeSpan.FromSeconds(3),
                });

            client.ListDatabaseNames().MoveNext();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    });

    public const string SkipReason =
        "No MongoDB replica set on localhost:27018. Start it with "
        + "`docker compose -f infra/docker/compose.yaml up -d`.";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        // Development, because that is the only environment where the stub
        // provider is permitted and where the API tolerates having no
        // production email sender.
        builder.UseSetting(
            Microsoft.AspNetCore.Hosting.WebHostDefaults.EnvironmentKey, Environments.Development);

        builder.UseSetting("Mongo:ConnectionString", ConnectionString);
        builder.UseSetting("Mongo:Database", _database);
        builder.UseSetting("Jwt:SigningKey", new string('k', 48));
        builder.UseSetting("Sso:EnableStubProvider", "true");

        // Blank the real credentials, deliberately.
        //
        // A developer who has configured a real Google client — which by now is
        // the normal state — loads it from user secrets, and real credentials
        // outrank the stub. Without this the suite would try to reach Google
        // from a test, and three of these went red the moment the first client
        // secret was set. A test must not depend on whose machine it runs on.
        builder.UseSetting("Sso:Google:ClientId", string.Empty);
        builder.UseSetting("Sso:Google:ClientSecret", string.Empty);
        builder.UseSetting("Sso:ClientBaseUrl", "http://localhost:5173");
        builder.UseSetting("Sso:Google:RedirectUri", "http://localhost/api/v1/auth/sso/google/callback");
    }

    public override async ValueTask DisposeAsync()
    {
        if (MongoAvailable)
        {
            // A test database per run, dropped afterwards. Sharing one would
            // make "a second sign-in reuses the account" depend on the order
            // the suite happened to run in.
            await new MongoClient(ConnectionString).DropDatabaseAsync(_database);
        }

        await base.DisposeAsync();
    }
}
