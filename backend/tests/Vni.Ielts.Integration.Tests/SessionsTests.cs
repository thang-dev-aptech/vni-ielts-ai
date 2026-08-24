using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Device management, end to end.
///
/// The device label is derived from a User-Agent, and the "is this me" rule
/// depends on a claim that only exists in a real token — neither can be
/// checked without going through the pipeline.
/// </summary>
public sealed class SessionsTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private HttpClient NewClient(string? userAgent = null)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (userAgent is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        }

        return client;
    }

    private const string Chrome =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/140.0.0.0 Safari/537.36";

    private const string AndroidFirefox =
        "Mozilla/5.0 (Android 15; Mobile; rv:130.0) Gecko/130.0 Firefox/130.0";

    private static async Task<(string Access, string Refresh, HttpClient Client)> SignInAsync(
        SsoAppFactory app, HttpClient client)
    {
        var start = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
        var url = new Uri((await start.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("authorizationUrl").GetString()!);

        var callback = await client.GetAsync(url.PathAndQuery);
        var code = System.Web.HttpUtility.ParseQueryString(callback.Headers.Location!.Query)["code"];

        var complete = await client.PostAsJsonAsync("/api/v1/auth/sso/complete", new { handoffCode = code });
        complete.EnsureSuccessStatusCode();

        var session = await complete.Content.ReadFromJsonAsync<JsonElement>();

        return (
            session.GetProperty("accessToken").GetString()!,
            session.GetProperty("refreshToken").GetString()!,
            client);
    }

    private static async Task<JsonElement> SessionsFor(HttpClient client, string access)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessions");
    }

    [SkippableFact]
    public async Task Each_sign_in_appears_as_a_device_the_owner_can_recognise()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (accessOnMac, _, mac) = await SignInAsync(app, NewClient(Chrome));
        await SignInAsync(app, NewClient(AndroidFirefox));

        var sessions = await SessionsFor(mac, accessOnMac);
        var labels = sessions.EnumerateArray().Select(s => s.GetProperty("device").GetString()).ToArray();

        // Chrome claims to be Safari and Firefox on Android claims to be Linux;
        // both labels being right is the point of checking through HTTP.
        Assert.Contains("Chrome trên macOS", labels);
        Assert.Contains("Firefox trên Android", labels);
    }

    [SkippableFact]
    public async Task Exactly_one_session_is_marked_as_the_calling_device()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (access, _, client) = await SignInAsync(app, NewClient(Chrome));
        await SignInAsync(app, NewClient(AndroidFirefox));

        var sessions = await SessionsFor(client, access);
        var current = sessions.EnumerateArray().Where(s => s.GetProperty("isCurrent").GetBoolean()).ToArray();

        Assert.Single(current);
        Assert.Equal("Chrome trên macOS", current[0].GetProperty("device").GetString());
    }

    [SkippableFact]
    public async Task Signing_another_device_out_removes_it_and_kills_its_token()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (accessOnMac, _, mac) = await SignInAsync(app, NewClient(Chrome));
        var (_, refreshOnPhone, phone) = await SignInAsync(app, NewClient(AndroidFirefox));

        var target = (await SessionsFor(mac, accessOnMac))
            .EnumerateArray()
            .First(s => !s.GetProperty("isCurrent").GetBoolean())
            .GetProperty("id")
            .GetString();

        var revoke = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/me/sessions/{target}");
        revoke.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessOnMac);
        Assert.Equal(HttpStatusCode.NoContent, (await mac.SendAsync(revoke)).StatusCode);

        // Gone from the list…
        var remaining = await SessionsFor(mac, accessOnMac);
        Assert.DoesNotContain(
            remaining.EnumerateArray().Select(s => s.GetProperty("id").GetString()), id => id == target);

        // …and the refresh token from that very sign-in no longer works, which
        // is the part that actually signs the other device out rather than
        // merely hiding it from a list.
        //
        // It has to be the token issued by that sign-in. An earlier version of
        // this test fetched a fresh one by signing in again — which starts a
        // new family, so of course it still worked, and the test passed while
        // proving nothing.
        var refresh = await phone.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = refreshOnPhone });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [SkippableFact]
    public async Task A_device_cannot_sign_itself_out_from_the_list()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (access, _, client) = await SignInAsync(app, NewClient(Chrome));

        var mine = (await SessionsFor(client, access))
            .EnumerateArray()
            .First(s => s.GetProperty("isCurrent").GetBoolean())
            .GetProperty("id")
            .GetString();

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/me/sessions/{mine}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "SESSION_IS_CURRENT",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task Me_reports_which_providers_the_account_can_use()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (access, _, client) = await SignInAsync(app, NewClient(Chrome));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var me = await (await client.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();

        // The stub signs in through Google, so there is no password to change —
        // which is exactly what the profile screen needs to know before telling
        // someone to enter their current one.
        Assert.Contains(
            "google", me.GetProperty("providers").EnumerateArray().Select(p => p.GetString()));
        Assert.False(me.GetProperty("hasPassword").GetBoolean());
    }
}
