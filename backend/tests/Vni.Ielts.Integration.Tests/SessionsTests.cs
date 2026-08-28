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

    /// <summary>
    /// <b>A dropped response does not cost the learner their session.</b>
    ///
    /// Rotation marks a token used and issues its successor. If the response
    /// carrying that successor never reaches the client — a phone leaving a
    /// tunnel, a proxy timing out, a WebView the OS suspended mid-response —
    /// the client retries with the only token it has, which is the one just
    /// marked used. Reuse detection then did the one thing it must never do by
    /// accident: it revoked the whole family and signed the learner out,
    /// mid-exam, for a network blip. On a mobile network that is not an edge
    /// case; it is Tuesday.
    ///
    /// The successor is what tells the two apart: never used means nobody ever
    /// received it. → `I4.4`
    /// </summary>
    [SkippableFact]
    public async Task A_retry_after_a_lost_refresh_response_keeps_the_session()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (_, refreshToken, client) = await SignInAsync(app, NewClient(Chrome));

        // The rotation the client never hears the answer to.
        var lost = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken });

        lost.EnsureSuccessStatusCode();

        // The client retries with the only token it has.
        var retry = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken });

        Assert.True(
            retry.IsSuccessStatusCode,
            $"A retry after a lost response answered {(int)retry.StatusCode}. Before this, a "
            + "dropped packet revoked the whole family and signed the learner out mid-exam.");

        var recovered = await retry.Content.ReadFromJsonAsync<JsonElement>();
        var recoveredRefresh = recovered.GetProperty("refreshToken").GetString()!;

        // And the session it hands back actually works.
        var again = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = recoveredRefresh });

        Assert.True(again.IsSuccessStatusCode);
    }

    /// <summary>
    /// And a token replayed after its successor was genuinely used still burns
    /// the family down.
    ///
    /// <b>This is the pair that makes the recovery safe rather than a hole.</b>
    /// If the successor <i>was</i> used, the client did receive it — so a
    /// presentation of the parent is a second party holding a token from this
    /// chain, one of them stole it, and nothing can tell which. That is exactly
    /// what the family revocation is for and it still fires. → threat `T3`
    /// </summary>
    [SkippableFact]
    public async Task A_replay_after_the_successor_was_used_still_revokes_the_family()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (_, stolen, client) = await SignInAsync(app, NewClient(Chrome));

        // The legitimate client rotates, receives the successor, and uses it.
        var first = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = stolen });
        first.EnsureSuccessStatusCode();

        var successor = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("refreshToken").GetString()!;

        var second = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = successor });

        second.EnsureSuccessStatusCode();

        var live = (await second.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("refreshToken").GetString()!;

        // Now somebody presents the original. The chain has moved on, so this
        // is a second party holding a token from it.
        var replay = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = stolen });

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var code = (await replay.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString();

        Assert.Equal("REFRESH_TOKEN_REUSED", code);

        /*
         * <b>And the family is gone, not merely this request refused.</b> A
         * refusal that left the live token working would leave the thief and
         * the learner sharing a session, which is the state the whole mechanism
         * exists to end.
         */
        var afterwards = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = live });

        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    /// <summary>
    /// Two retries of one lost response recover once, not twice.
    ///
    /// The recovery is claimed atomically, so whichever request clears the
    /// successor pointer owns it and the other is an ordinary reuse. Reading
    /// and then writing would let both through — under exactly the retry
    /// pressure that produced the situation in the first place.
    /// </summary>
    [SkippableFact]
    public async Task Two_retries_of_one_lost_response_recover_once()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (_, refreshToken, client) = await SignInAsync(app, NewClient(Chrome));

        var lost = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });
        lost.EnsureSuccessStatusCode();

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<HttpResponseMessage> RetryAsync()
        {
            await gate.Task;
            return await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });
        }

        var a = RetryAsync();
        var b = RetryAsync();
        gate.SetResult();

        var results = await Task.WhenAll(a, b);

        Assert.Equal(1, results.Count(r => r.IsSuccessStatusCode));
    }


    /// <summary>
    /// <b>Signing out ends the credential, not just the browser's copy of it.</b>
    ///
    /// Until 2026-08-28 the client cleared `localStorage` and that was all of
    /// it: the refresh-token family stayed live for its full thirty days. So
    /// signing out on a shared machine, a library computer, or a phone being
    /// handed on left a working credential behind — recoverable from a browser
    /// profile backup, or from anything that had already copied the value.
    /// → `I4.6`, threat `T3`
    /// </summary>
    [SkippableFact]
    public async Task Signing_out_revokes_this_sessions_refresh_family()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (access, refreshToken, client) = await SignInAsync(app, NewClient(Chrome));

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var response = await client.SendAsync(logout);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The credential left on the machine no longer buys anything.
        var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
    }

    /// <summary>
    /// And it ends only the caller's own session.
    ///
    /// The family comes from the access token's `fam` claim, so there is no
    /// parameter through which one session could end another's. Signing out on
    /// a laptop must not sign the learner out of the phone they are sitting an
    /// exam on — ending every device is a different, deliberate act with its
    /// own screen.
    /// </summary>
    [SkippableFact]
    public async Task Signing_out_leaves_the_other_devices_alone()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var laptop = NewClient(Chrome);
        var (laptopAccess, _, _) = await SignInAsync(app, laptop);

        var phone = NewClient(AndroidFirefox);
        var (_, phoneRefresh, _) = await SignInAsync(app, phone);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", laptopAccess);

        (await laptop.SendAsync(logout)).EnsureSuccessStatusCode();

        var stillWorking = await phone.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = phoneRefresh });

        Assert.True(
            stillWorking.IsSuccessStatusCode,
            "Signing out on one device ended another device's session. Ending every device is "
            + "a different act, and it has its own screen.");
    }

    /// <summary>
    /// Signing out twice is not an error.
    ///
    /// A button press that fails the second time is a button press people press
    /// twice when the first one looked like it did nothing — and this is the one
    /// operation that must never refuse, because refusing it leaves a live
    /// credential on a machine somebody else is about to use.
    /// </summary>
    [SkippableFact]
    public async Task Signing_out_twice_is_still_a_success()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (access, _, client) = await SignInAsync(app, NewClient(Chrome));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
            logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);
        }
    }

}
