using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Registration through the real pipeline, after the 27/08/2026 decision.
///
/// <para>
/// The unit tests prove the handler returns a session. Only this proves the
/// three things that live outside it: that the token the API hands back
/// actually opens <c>/me</c>, that <c>/me</c> reports the account as
/// unverified without refusing it anything, and that the resend endpoint on
/// the profile page answers with what really happened to the message.
/// </para>
///
/// <para>
/// <b>Skipped when no local MongoDB is listening</b>, like the rest of this
/// project — a developer without the infra stack up gets a note, not a red
/// build.
/// </para>
/// </summary>
public sealed class RegistrationFlowTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>A fresh address per test, so one run cannot collide with another.</summary>
    private static string NewAddress() => $"hoc.vien.{Guid.NewGuid():n}@example.com";

    private const string Password = "mot-mat-khau-du-dai-2026";

    private static async Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string address, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(
                new { email = address, password = Password, displayName = "Học viên" }),
        };

        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path, string access)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [SkippableFact]
    public async Task Registering_returns_a_session_that_opens_the_account()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var address = NewAddress();

        var response = await RegisterAsync(client, address);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var session = body.GetProperty("session");
        var access = session.GetProperty("accessToken").GetString()!;

        Assert.False(string.IsNullOrWhiteSpace(access));
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("refreshToken").GetString()));
        Assert.False(body.GetProperty("emailVerified").GetBoolean());

        // No email provider is configured in Development, so the API must not
        // claim one went out. This is the assertion that fails the day someone
        // wires a sender and forgets to report it — which is the good failure.
        Assert.False(body.GetProperty("verificationEmailSent").GetBoolean());

        var me = await GetAsync(client, "/api/v1/me", access);

        Assert.Equal(session.GetProperty("userId").GetString(), me.GetProperty("userId").GetString());
        Assert.Equal(address, me.GetProperty("email").GetString());
        Assert.False(me.GetProperty("emailVerified").GetBoolean());
    }

    [SkippableFact]
    public async Task An_unverified_account_is_refused_nothing_it_would_otherwise_be_allowed()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        /*
         * The decision settled sign-in and nothing else, so this checks that
         * nothing *else* quietly gates on verification either — the ordinary
         * authenticated surfaces answer for a brand-new account.
         *
         * It deliberately does not assert that some named capability is
         * allowed: what an unverified account may not do is `M-38`, still with
         * the owner. This pins the current state — no restriction exists — so
         * that adding one is a visible change to a test rather than a quiet
         * drift.
         */
        var client = NewClient();

        var body = await (await RegisterAsync(client, NewAddress()))
            .Content.ReadFromJsonAsync<JsonElement>();

        var access = body.GetProperty("session").GetProperty("accessToken").GetString()!;

        var sessions = await GetAsync(client, "/api/v1/me/sessions", access);
        Assert.True(sessions.TryGetProperty("sessions", out _));

        var exams = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exams");
        exams.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(exams)).StatusCode);
    }

    [SkippableFact]
    public async Task Signing_in_with_the_password_just_created_works()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var address = NewAddress();

        await RegisterAsync(client, address);

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = address, password = Password });

        login.EnsureSuccessStatusCode();

        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("accessToken").GetString()));
    }

    [SkippableFact]
    public async Task A_retried_registration_returns_the_first_answer_rather_than_a_second_account()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        // Mobile clients retry. Without the key the second attempt would hit
        // EMAIL_ALREADY_REGISTERED and a learner who lost one response would be
        // told their own address is taken.
        var client = NewClient();
        var address = NewAddress();
        var key = Guid.NewGuid().ToString();

        var first = await RegisterAsync(client, address, key);
        var second = await RegisterAsync(client, address, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var a = await first.Content.ReadFromJsonAsync<JsonElement>();
        var b = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            a.GetProperty("session").GetProperty("userId").GetString(),
            b.GetProperty("session").GetProperty("userId").GetString());
    }

    [SkippableFact]
    public async Task The_profile_can_ask_for_the_verification_message_again()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();

        var registered = await (await RegisterAsync(client, NewAddress()))
            .Content.ReadFromJsonAsync<JsonElement>();

        var access = registered.GetProperty("session").GetProperty("accessToken").GetString()!;

        var resend = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/verify-email/resend");
        resend.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        resend.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(resend);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("emailVerified").GetBoolean());

        // 200 with the truth, not 202 with a promise: nothing was sent, and the
        // screen has to be able to say so.
        Assert.False(body.GetProperty("verificationEmailSent").GetBoolean());
    }
}
