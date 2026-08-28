using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The one layer <c>TestServer</c> cannot see: Kestrel's own request-body cap.
///
/// <b>Written 2026-08-28 as the second half of `I7.4`, and it closes a hole the
/// suite had documented in a code comment rather than covered.</b>
///
/// ── What was blind, and how blind ─────────────────────────────────────────
///
/// <c>UploadRecordingEndpoint</c> begins by lifting the per-request body cap:
///
/// <code>
/// if (request.HttpContext.Features.Get&lt;IHttpMaxRequestBodySizeFeature&gt;() is
///     { IsReadOnly: false } cap)
///     cap.MaxRequestBodySize = MaxRecordingBytes + MultipartOverheadBytes;
/// </code>
///
/// It has to, because <c>Program.cs</c> sets Kestrel's global limit to 1 MB —
/// right for JSON, and roughly a minute of Speaking audio. Every real Part 2
/// answer is longer than that.
///
/// <b>Under <c>TestServer</c> that feature is <c>null</c>, measured.</b> So the
/// block never executed in any test, and every upload assertion in
/// <c>ExamRunContractTests</c> — a dozen of them — was exercising the
/// endpoint's own <c>file.Length</c> check and nothing else. A regression that
/// deleted those three lines would have left the whole suite green and refused
/// every real Speaking recording in production with a 413 the client has no
/// branch for.
///
/// <b>A real Kestrel, on a real loopback socket.</b> <c>WebApplicationFactory</c>
/// has supported this since .NET 9 (<c>UseKestrel()</c>), so it costs a port
/// rather than a Docker image — and it runs the same <c>Program</c> the
/// container does, limits and all.
/// </summary>
public sealed class KestrelTransportTests(KestrelExamAppFactory app)
    : IClassFixture<KestrelExamAppFactory>
{
    private const string Password = "mot-mat-khau-du-dai-2026";

    /// <summary>
    /// Comfortably over Kestrel's 1 MB global limit and comfortably under the
    /// endpoint's 12 MB one — so the only thing that can refuse it is the cap
    /// this test exists to check.
    /// </summary>
    private const int RealisticRecordingBytes = 3 * 1024 * 1024;

    private static HttpRequestMessage Authed(HttpMethod method, string path, string access)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return request;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Key() => Guid.NewGuid().ToString("n");

    /// <summary>A learner sitting on the Speaking section, over a real socket.</summary>
    private async Task<(HttpClient Client, string Access, string SessionId, string[] Questions)>
        OnSpeakingAsync()
    {
        /*
         * <b>No <c>WebApplicationFactoryClientOptions</c> here, deliberately.</b>
         * Handing the factory a fresh options object replaces the base address
         * it worked out from the listener with the default <c>http://localhost</c>
         * — which is port 80, which nothing is listening on. The failure is a
         * connection refused, and it reads as "the server did not start" rather
         * than "the client was pointed at the wrong place".
         */
        var client = app.CreateClient();

        var register = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new
            {
                email = $"thu.am.{Guid.NewGuid():n}@example.com",
                password = Password,
                displayName = "Học viên",
            }),
        };

        register.Headers.TryAddWithoutValidation("Idempotency-Key", Key());

        var registered = await client.SendAsync(register);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var access = (await BodyOf(registered))
            .GetProperty("session").GetProperty("accessToken").GetString()!;

        var catalogue = await client.SendAsync(Authed(HttpMethod.Get, "/api/v1/exams", access));
        catalogue.EnsureSuccessStatusCode();

        var examId = (await BodyOf(catalogue)).GetProperty("exams").EnumerateArray()
            .First(e => e.GetProperty("title").GetString() == "VNI Synthetic Practice Test")
            .GetProperty("examVersionId").GetString()!;

        var start = Authed(HttpMethod.Post, "/api/v1/sessions", access);
        start.Headers.Add("Idempotency-Key", Key());
        start.Content = JsonContent.Create(
            new { examVersionId = examId, mode = "full", module = (string?)null });

        var started = await client.SendAsync(start);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        var sessionId = (await BodyOf(started)).GetProperty("sessionId").GetString()!;

        JsonElement view = default;
        for (var step = 0; step < 3; step++)
        {
            var advance = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/advance", access);
            advance.Headers.Add("Idempotency-Key", Key());

            var moved = await client.SendAsync(advance);
            moved.EnsureSuccessStatusCode();
            view = await BodyOf(moved);
        }

        Assert.Equal("speaking", view.GetProperty("current").GetProperty("module").GetString());

        var questions = view.GetProperty("current").GetProperty("parts").EnumerateArray()
            .SelectMany(p => p.GetProperty("questions").EnumerateArray())
            .Select(q => q.GetProperty("id").GetString()!)
            .ToArray();

        return (client, access, sessionId, questions);
    }

    private static MultipartFormDataContent FormFor(string questionId, int bytes)
    {
        var audio = new ByteArrayContent(new byte[bytes]);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");

        return new MultipartFormDataContent
        {
            { new StringContent(questionId), "questionId" },
            { audio, "audio", $"{questionId}.webm" },
        };
    }

    // ── 1 · The cap is actually lifted ──────────────────────────────────

    /// <summary>
    /// A three-megabyte recording is accepted by a real server.
    ///
    /// <b>Kestrel's configured limit is 1 MB, so without the endpoint raising it
    /// this request is refused with a 413 before any application code runs.</b>
    /// That is the failure the lifting block was written for, and until now
    /// nothing could tell whether the block worked — it never executed under
    /// test at all.
    /// </summary>
    [SkippableFact]
    public async Task A_recording_larger_than_kestrels_global_limit_is_accepted()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, questions) = await OnSpeakingAsync();

        var request = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/recordings", access);
        request.Content = FormFor(questions[0], RealisticRecordingBytes);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(
            string.IsNullOrWhiteSpace(
                (await BodyOf(response)).GetProperty("recordingId").GetString()));
    }

    /// <summary>
    /// And the raise is scoped to the one request that asked for it.
    ///
    /// <b>A cap lifted per-request must not leak into the next one.</b>
    /// <c>IHttpMaxRequestBodySizeFeature</c> is per-request by contract, and a
    /// connection is reused — so if the raise ever became a connection-level
    /// change, a three-megabyte JSON body would start being accepted on an
    /// endpoint that expects kilobytes. Same client, same connection, straight
    /// after a large upload.
    /// </summary>
    [SkippableFact]
    public async Task Raising_the_cap_for_an_upload_does_not_raise_it_for_the_next_request()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, questions) = await OnSpeakingAsync();

        var upload = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/recordings", access);
        upload.Content = FormFor(questions[0], RealisticRecordingBytes);
        (await client.SendAsync(upload)).EnsureSuccessStatusCode();

        // An oversized autosave down the same connection. Nothing about this
        // endpoint asked for a larger body.
        var save = Authed(HttpMethod.Put, $"/api/v1/sessions/{sessionId}/answers", access);
        save.Content = JsonContent.Create(new
        {
            module = "speaking",
            changes = new Dictionary<string, string?> { [questions[0]] = new string('x', 2_000_000) },
        });

        var refused = await SendExpectingRefusalAsync(client, save);

        Assert.NotEqual(HttpStatusCode.OK, refused);
    }

    // ── 2 · The endpoint's own ceiling still holds ──────────────────────

    /// <summary>
    /// Thirteen megabytes is refused.
    ///
    /// <b>The ceiling matters because <c>file.Length</c> cannot be consulted
    /// until the whole body has been buffered.</b> A size limit enforced only
    /// there lets a caller make the server hold whatever they send in memory
    /// first; raising the transport cap to the same number instead means an
    /// oversized upload dies on the socket.
    ///
    /// <b>Honest about what this one proves on its own: not much.</b> It passes
    /// whether or not the endpoint raises the cap, because 13 MB is over both
    /// the raised ceiling and Kestrel's un-raised 1 MB default — measured, by
    /// deleting the raise and watching it stay green. Its discriminating partner
    /// is <see cref="A_recording_larger_than_kestrels_global_limit_is_accepted"/>;
    /// what this adds is the other side of the bracket, so a raise that was
    /// accidentally made unbounded would be caught.
    /// </summary>
    [SkippableFact]
    public async Task A_recording_past_the_ceiling_is_refused_by_the_transport()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, questions) = await OnSpeakingAsync();

        var request = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/recordings", access);
        request.Content = FormFor(questions[0], 13 * 1024 * 1024);

        var status = await SendExpectingRefusalAsync(client, request);

        Assert.NotEqual(HttpStatusCode.OK, status);
    }

    /// <summary>
    /// Sends a request that the server is expected to cut short, and reports the
    /// status it managed to return.
    ///
    /// <b>A body the server refuses mid-stream does not always come back as a
    /// status code.</b> Kestrel answers 413 and closes; whether the client sees
    /// the response or a broken pipe depends on how much of the body it had
    /// already written, which is a race with the operating system's socket
    /// buffers. Both outcomes are the same refusal, and a test that demanded one
    /// of them would be flaky on a schedule nobody controls.
    /// </summary>
    private static async Task<HttpStatusCode> SendExpectingRefusalAsync(
        HttpClient client, HttpRequestMessage request)
    {
        try
        {
            return (await client.SendAsync(request)).StatusCode;
        }
        catch (HttpRequestException)
        {
            // The connection went away mid-body. That is a refusal.
            return HttpStatusCode.RequestEntityTooLarge;
        }
    }
}

/// <summary>
/// <see cref="ExamAppFactory"/> on a real Kestrel listener rather than
/// <c>TestServer</c>.
///
/// <b>Everything else is inherited on purpose.</b> The database name, the stub
/// provider, the synthetic seed and the movable clock all come from the base —
/// a second copy of that configuration would drift, and a drifted harness makes
/// a test assert against a world the product is not in.
/// </summary>
public sealed class KestrelExamAppFactory : ExamAppFactory
{
    /// <summary>
    /// <b>Called in the constructor because the server is chosen before it is
    /// built.</b> The factory's own diagnostic says so: <i>"UseKestrel should be
    /// called before server initialization"</i> — after that it is silently
    /// ignored, and a test that believed it was on a real socket would be back
    /// on <c>TestServer</c> and green for the wrong reason.
    ///
    /// Port 0: the operating system picks a free one, so parallel runs and a
    /// developer's own API on 5099 never collide.
    /// </summary>
    public KestrelExamAppFactory() => UseKestrel(0);
}
