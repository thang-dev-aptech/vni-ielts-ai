using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Registration through the real pipeline, after the 08/09/2026 decision.
///
/// <para>
/// The unit tests prove the handler returns a session. Only this proves what
/// lives outside it: that the token the API hands back actually opens
/// <c>/me</c>, that <c>/me</c> reports an account with a number and no address
/// without refusing it anything, and that both handles reach the same account
/// once an address is added.
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

    /// <summary>
    /// A fresh number per test, so one run cannot collide with another — and
    /// unlike the address it used to use, this one is a unique index.
    /// </summary>
    private static string NewPhone() => $"09{Random.Shared.NextInt64(0, 100_000_000):D8}";

    private static string NewAddress() => $"hoc.vien.{Guid.NewGuid():n}@example.com";

    private const string Password = "mot-mat-khau-du-dai-2026";

    private static async Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string phone, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(
                new { phone, password = Password, displayName = "Hoc vien" }),
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

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string path, string access, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        return await client.SendAsync(request);
    }

    [SkippableFact]
    public async Task Registering_returns_a_session_that_opens_the_account()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var phone = NewPhone();

        var response = await RegisterAsync(client, phone);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var session = body.GetProperty("session");
        var access = session.GetProperty("accessToken").GetString()!;

        Assert.False(string.IsNullOrWhiteSpace(access));
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("refreshToken").GetString()));

        var me = await GetAsync(client, "/api/v1/me", access);

        Assert.Equal(session.GetProperty("userId").GetString(), me.GetProperty("userId").GetString());
        Assert.Equal($"+84{phone[1..]}", me.GetProperty("phone").GetString());
    }

    [SkippableFact]
    public async Task The_new_account_has_no_email_address_at_all()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        // The owner's instruction was "ở profile phần email bỏ trống". Null,
        // not a placeholder and not the number copied into a fake address to
        // satisfy something that used to be required.
        var client = NewClient();

        var body = await (await RegisterAsync(client, NewPhone()))
            .Content.ReadFromJsonAsync<JsonElement>();
        var access = body.GetProperty("session").GetProperty("accessToken").GetString()!;

        var me = await GetAsync(client, "/api/v1/me", access);

        Assert.Equal(JsonValueKind.Null, me.GetProperty("email").ValueKind);
    }

    [SkippableFact]
    public async Task Nothing_the_account_would_otherwise_be_allowed_is_refused_it()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        /*
         * There is no verification to gate on any more, so this pins that no
         * gate quietly grew somewhere else either: the ordinary authenticated
         * surfaces answer for a brand-new account with no address.
         */
        var client = NewClient();

        var body = await (await RegisterAsync(client, NewPhone()))
            .Content.ReadFromJsonAsync<JsonElement>();

        var access = body.GetProperty("session").GetProperty("accessToken").GetString()!;

        var sessions = await GetAsync(client, "/api/v1/me/sessions", access);
        Assert.True(sessions.TryGetProperty("sessions", out _));

        var exams = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exams");
        exams.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(exams)).StatusCode);
    }

    [SkippableFact]
    public async Task Signing_in_with_the_number_just_registered_works()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var phone = NewPhone();

        await RegisterAsync(client, phone);

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { identifier = phone, password = Password });

        login.EnsureSuccessStatusCode();

        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("accessToken").GetString()));
    }

    [SkippableFact]
    public async Task An_address_added_afterwards_becomes_a_second_way_in()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        /*
         * <b>The whole reason the password row is keyed by account id.</b> One
         * account, one password, two handles. Keyed by the handle instead, this
         * would need a second identity row holding a duplicate hash — and the
         * next password change would update one of them.
         */
        var client = NewClient();
        var phone = NewPhone();
        var address = NewAddress();

        var registered = await (await RegisterAsync(client, phone))
            .Content.ReadFromJsonAsync<JsonElement>();
        var access = registered.GetProperty("session").GetProperty("accessToken").GetString()!;
        var userId = registered.GetProperty("session").GetProperty("userId").GetString();

        var added = await PostAsync(client, "/api/v1/me/email", access, new { email = address });
        added.EnsureSuccessStatusCode();

        var byEmail = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { identifier = address, password = Password });
        byEmail.EnsureSuccessStatusCode();

        var byPhone = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { identifier = phone, password = Password });
        byPhone.EnsureSuccessStatusCode();

        var one = await byEmail.Content.ReadFromJsonAsync<JsonElement>();
        var two = await byPhone.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(userId, one.GetProperty("userId").GetString());
        Assert.Equal(userId, two.GetProperty("userId").GetString());
    }

    [SkippableFact]
    public async Task A_second_account_cannot_take_a_number_that_is_already_registered()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = NewClient();
        var phone = NewPhone();

        await RegisterAsync(client, phone);
        var again = await RegisterAsync(client, phone);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var problem = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PHONE_ALREADY_REGISTERED", problem.GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task A_retried_registration_returns_the_first_answer_rather_than_a_second_account()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        // Mobile clients retry. Without the key the second attempt would hit
        // PHONE_ALREADY_REGISTERED and a learner who lost one response would be
        // told their own number is taken.
        var client = NewClient();
        var phone = NewPhone();
        var key = Guid.NewGuid().ToString();

        var first = await RegisterAsync(client, phone, key);
        var second = await RegisterAsync(client, phone, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var a = await first.Content.ReadFromJsonAsync<JsonElement>();
        var b = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            a.GetProperty("session").GetProperty("userId").GetString(),
            b.GetProperty("session").GetProperty("userId").GetString());
    }

    [SkippableFact]
    public async Task An_account_cannot_remove_the_only_handle_it_has()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        // Registration leaves the number as the only thing anyone can type to
        // reach this account. Clearing it through the ordinary profile call
        // would lock it shut with no operator-free way back.
        var client = NewClient();

        var registered = await (await RegisterAsync(client, NewPhone()))
            .Content.ReadFromJsonAsync<JsonElement>();
        var access = registered.GetProperty("session").GetProperty("accessToken").GetString()!;

        var cleared = await PostAsync(
            client, "/api/v1/me/phone", access, new { phone = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, cleared.StatusCode);

        var problem = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SIGN_IN_METHOD_REQUIRED", problem.GetProperty("code").GetString());
    }
}
