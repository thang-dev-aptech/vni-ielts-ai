using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The exam engine's refusals, through the real HTTP pipeline.
///
/// <b>These exist because the client and the server were built by people who
/// could not see each other's work.</b> Every unit test on either side passes
/// against the contract its own author had in mind; only a request that
/// actually travels can show that the two contracts are not the same one.
///
/// Three of the things checked here are reachable in no other way:
///
/// <list type="bullet">
/// <item>The <b>idempotency guard runs before the endpoint</b>, so an endpoint
/// that never decided whether it wants a key answers 400 to a request that is
/// otherwise perfectly formed. A handler test cannot see that, because a
/// handler test never passes through the middleware.</item>
/// <item>The <b>error code</b> a refusal carries. Clients branch on it, and the
/// exception a handler raises is not the code the endpoint maps it to.</item>
/// <item>The <b>status</b> that goes with it. `SECTION_NOT_OPEN` at 409 and
/// `SESSION_EXPIRED` at 409 look the same to anything that reads only the
/// status, which is what the client's generic error branch does.</item>
/// </list>
/// </summary>
public sealed class ExamRunContractTests(ExamAppFactory app) : IClassFixture<ExamAppFactory>
{
    // ── Driving the API ─────────────────────────────────────────────────

    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>A signed-in learner. The stub provider, the same route the SSO suite uses.</summary>
    private async Task<(HttpClient Client, string Access)> SignInAsync()
    {
        var client = NewClient();

        var start = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
        var url = new Uri((await start.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("authorizationUrl").GetString()!);

        var callback = await client.GetAsync(url.PathAndQuery);
        var code = System.Web.HttpUtility.ParseQueryString(callback.Headers.Location!.Query)["code"];

        var complete = await client.PostAsJsonAsync(
            "/api/v1/auth/sso/complete", new { handoffCode = code });
        complete.EnsureSuccessStatusCode();

        return (client, (await complete.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!);
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, string access)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return request;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await BodyOf(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The seeded four-module fixture. Full Test needs all four.</summary>
    private async Task<string> FullExamIdAsync(HttpClient client, string access)
    {
        var response = await client.SendAsync(Authed(HttpMethod.Get, "/api/v1/exams", access));
        response.EnsureSuccessStatusCode();

        var exams = (await BodyOf(response)).GetProperty("exams").EnumerateArray().ToArray();

        var full = exams.FirstOrDefault(e =>
            e.GetProperty("modules").EnumerateArray().Count() == 4);

        Assert.True(
            full.ValueKind == JsonValueKind.Object,
            "No seeded exam carries all four modules, so nothing here can sit a Full Test.");

        return full.GetProperty("examVersionId").GetString()!;
    }

    private async Task<JsonElement> StartAsync(
        HttpClient client, string access, string examVersionId, string mode, string? module = null)
    {
        var request = Authed(HttpMethod.Post, "/api/v1/sessions", access);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        request.Content = JsonContent.Create(new { examVersionId, mode, module });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await BodyOf(response);
    }

    private async Task<HttpResponseMessage> AdvanceAsync(
        HttpClient client, string access, string sessionId, string key)
    {
        var request = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/advance", access);
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// An autosave, shaped exactly as <c>ExamRunnerPage.tsx</c> shapes it: the
    /// questions that changed, not the whole sheet.
    ///
    /// <b>The field name is the contract, and it is the field name that was
    /// wrong before.</b> The client sent one key and the server read another,
    /// each side had passing tests, and the disagreement lived in the gap
    /// between them — which is why this helper mirrors the client rather than
    /// the server's record type.
    /// </summary>
    private async Task<HttpResponseMessage> SaveAnswersAsync(
        HttpClient client, string access, string sessionId,
        string module, Dictionary<string, string?> changes, int? baseRevision = null)
    {
        var request = Authed(HttpMethod.Put, $"/api/v1/sessions/{sessionId}/answers", access);
        request.Content = baseRevision is { } known
            ? JsonContent.Create(new { module, changes, baseRevision = known })
            : JsonContent.Create(new { module, changes });
        return await client.SendAsync(request);
    }

    /// <summary>
    /// A Speaking upload, shaped exactly as <c>SpeakingRecorder.tsx</c> shapes
    /// it: a multipart form with <c>questionId</c> and <c>audio</c>, over
    /// <c>fetch</c>, carrying an <c>Authorization</c> header and nothing else.
    ///
    /// <b><paramref name="idempotencyKey"/> defaults to none because the client
    /// sends none.</b> Every other POST in this product goes through
    /// <c>request()</c> in <c>packages/auth/src/http.ts</c>, which threads a
    /// key through; the upload cannot, because that helper serialises its body
    /// as JSON. So the one endpoint that bypasses the shared transport is the
    /// one endpoint whose headers nobody reviewed.
    /// </summary>
    private async Task<HttpResponseMessage> UploadRecordingAsync(
        HttpClient client, string access, string sessionId, string questionId,
        string? idempotencyKey = null, string contentType = "audio/webm",
        int sizeBytes = 0)
    {
        var request = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/recordings", access);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);

        var audio = new ByteArrayContent(
            sizeBytes > 0
                ? new byte[sizeBytes]
                : Convert.FromBase64String("T2dnUwACAAAAAAAAAAA="));
        audio.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var form = new MultipartFormDataContent
        {
            { new StringContent(questionId), "questionId" },
            { audio, "audio", $"{questionId}.webm" },
        };

        request.Content = form;
        return await client.SendAsync(request);
    }

    private async Task<JsonElement> GetSessionAsync(
        HttpClient client, string access, string sessionId)
    {
        var response = await client.SendAsync(
            Authed(HttpMethod.Get, $"/api/v1/sessions/{sessionId}", access));
        response.EnsureSuccessStatusCode();
        return await BodyOf(response);
    }

    private static string ModuleOf(JsonElement view) =>
        view.GetProperty("current").GetProperty("module").GetString()!;

    private static IEnumerable<string> QuestionIds(JsonElement view) =>
        view.GetProperty("current").GetProperty("parts").EnumerateArray()
            .SelectMany(p => p.GetProperty("questions").EnumerateArray())
            .Select(q => q.GetProperty("id").GetString()!);

    private static string FirstQuestionId(JsonElement view) => QuestionIds(view).First();

    /// <summary>Walks a Full Test forward until the named module is the open one.</summary>
    private async Task<JsonElement> AdvanceToAsync(
        HttpClient client, string access, string sessionId, JsonElement view, string module)
    {
        for (var step = 0; step < 4 && ModuleOf(view) != module; step++)
        {
            var response = await AdvanceAsync(
                client, access, sessionId, Guid.NewGuid().ToString("n"));
            response.EnsureSuccessStatusCode();
            view = await BodyOf(response);
        }

        Assert.Equal(module, ModuleOf(view));
        return view;
    }

    private async Task<(HttpClient Client, string Access, string SessionId, JsonElement View)>
        SittingOnSpeakingAsync()
    {
        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "full");
        var sessionId = started.GetProperty("sessionId").GetString()!;

        var view = await AdvanceToAsync(client, access, sessionId, started, "speaking");
        return (client, access, sessionId, view);
    }

    // ── 1 · The Speaking upload never reaches its own rules ──────────────

    /// <summary>
    /// <b>A correct Speaking upload is refused before a single exam rule runs.</b>
    ///
    /// The sitting is in progress, Speaking is the open section, the deadline is
    /// an hour away and the question id came out of this very exam's payload —
    /// every one of the four gates <c>SubmitSpeakingRecording</c> was written on
    /// 27/08 to enforce would pass. None of them is consulted.
    ///
    /// <c>IdempotencyMiddleware</c> guards every POST that is not on its exempt
    /// list, and <c>POST /sessions/{id}/recordings</c> is not on it. The middleware's
    /// own documentation names this exact failure — "a 400 from an endpoint that
    /// appeared not to exist" — and lists three endpoints it had already caught.
    /// This is the fourth, and it is the only one that makes a whole IELTS skill
    /// impossible to answer.
    ///
    /// The client cannot fix it from its side: <c>request()</c> in
    /// <c>packages/auth/src/http.ts</c> is the only helper that threads an
    /// <c>Idempotency-Key</c>, and it serialises its body as JSON, so a
    /// multipart upload has to use raw <c>fetch</c>. Which is what
    /// <c>SpeakingRecorder</c> does.
    /// </summary>
    [SkippableFact]
    public async Task A_speaking_recording_uploads_without_an_idempotency_key()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, view) = await SittingOnSpeakingAsync();
        var questionId = FirstQuestionId(view);

        var response = await UploadRecordingAsync(client, access, sessionId, questionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(
            string.IsNullOrWhiteSpace(
                (await BodyOf(response)).GetProperty("recordingId").GetString()));
    }

    /// <summary>
    /// And the id the server generated is on the Speaking sheet, filed by the
    /// server against the question — which is the half of the chain that makes
    /// the recording markable at all.
    /// </summary>
    [SkippableFact]
    public async Task A_stored_recording_is_filed_against_its_question_by_the_server()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, view) = await SittingOnSpeakingAsync();
        var questionId = FirstQuestionId(view);

        var upload = await UploadRecordingAsync(client, access, sessionId, questionId);
        upload.EnsureSuccessStatusCode();

        var recordingId = (await BodyOf(upload)).GetProperty("recordingId").GetString();

        var reloaded = await GetSessionAsync(client, access, sessionId);
        var answers = reloaded.GetProperty("current").GetProperty("answers");

        Assert.Equal(recordingId, answers.GetProperty(questionId).GetString());
    }

    // ── 2 · The gates the upload was given on 27/08 ──────────────────────

    /// <summary>
    /// A question id from outside this exam's Speaking section is refused.
    ///
    /// It becomes a key on the answer sheet and later the thing marking looks
    /// up. An unchecked one writes rows nobody reads.
    /// </summary>
    [SkippableFact]
    public async Task A_recording_for_a_question_this_exam_does_not_have_is_refused()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, _) = await SittingOnSpeakingAsync();

        var response = await UploadRecordingAsync(
            client, access, sessionId, "s-part-borrowed-from-another-paper");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await CodeOf(response));
    }

    /// <summary>
    /// A recording filed while Reading is the open section is refused as the
    /// wrong section — and, specifically, <b>not</b> as an expired sitting. The
    /// sitting is fine; the request went to the wrong place.
    /// </summary>
    [SkippableFact]
    public async Task A_recording_uploaded_while_another_section_is_open_is_the_wrong_section()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "full");
        var sessionId = started.GetProperty("sessionId").GetString()!;
        Assert.Equal("reading", ModuleOf(started));

        var response = await UploadRecordingAsync(client, access, sessionId, "s-part-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SECTION_NOT_OPEN", await CodeOf(response));

        // And the sitting is untouched: a mis-routed upload does not end an exam.
        Assert.Equal("inprogress", (await GetSessionAsync(client, access, sessionId))
            .GetProperty("status").GetString());
    }

    /// <summary>
    /// A recording the size of real speech reaches the handler.
    ///
    /// <b>Kestrel capped every request body in the application at 1 MB, and a
    /// minute of speech is several.</b> So the endpoint's own 12 MB allowance
    /// was unreachable: the transport refused the request before application
    /// code ran, and the check enforcing 12 MB was guarding a door nothing
    /// could arrive at. Speaking could not be answered at all — a short
    /// recording was refused by the idempotency middleware, and a real one was
    /// refused by Kestrel after that was fixed. Two separate walls, one behind
    /// the other, each invisible while the other stood.
    ///
    /// Two megabytes rather than twelve: enough to prove the 1 MB cap is gone,
    /// small enough that the suite is not pushing ten megabytes through a
    /// loopback socket to make a point.
    /// </summary>
    [SkippableFact]
    public async Task A_recording_larger_than_the_global_body_cap_reaches_the_handler()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, view) = await SittingOnSpeakingAsync();

        var response = await UploadRecordingAsync(
            client, access, sessionId, FirstQuestionId(view), sizeBytes: 2 * 1024 * 1024);

        // Whatever the handler decides, it got to decide. A 413 here would mean
        // the request died in the transport again.
        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    /// <summary>
    /// And the endpoint's own ceiling still refuses what is genuinely too big.
    ///
    /// Lifting a cap for one route is not removing it. This route accepts
    /// multipart and writes it to storage, so an unbounded one is a way to fill
    /// a disk with a single request.
    /// </summary>
    [SkippableFact]
    public async Task A_recording_beyond_the_endpoints_own_ceiling_is_still_refused()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, view) = await SittingOnSpeakingAsync();

        var response = await UploadRecordingAsync(
            client, access, sessionId, FirstQuestionId(view), sizeBytes: 13 * 1024 * 1024);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }


    /// <summary>
    /// A recording of <b>exactly</b> the ceiling is accepted.
    ///
    /// <b>The two caps were the same number, and that made the larger one
    /// unreachable.</b> A multipart body is not just the file: boundaries, a
    /// `Content-Disposition` and a `Content-Type` per part, and the
    /// `questionId` field all ride on top. So a recording of exactly the
    /// allowance produced a body a few hundred bytes over it, Kestrel refused
    /// it before any application code ran, and the learner got a transport
    /// error instead of the endpoint's own message — for a recording that is
    /// within the documented limit.
    ///
    /// Off-by-one on a limit is not usually interesting. It is here, because
    /// the thing being refused is somebody's spoken answer and the refusal
    /// arrives with no explanation.
    /// </summary>
    [SkippableFact]
    public async Task A_recording_of_exactly_the_ceiling_is_accepted()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, view) = await SittingOnSpeakingAsync();

        var response = await UploadRecordingAsync(
            client, access, sessionId, FirstQuestionId(view), sizeBytes: 12 * 1024 * 1024);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"A recording of exactly the ceiling was refused ({(int)response.StatusCode}). "
            + "The transport cap must exceed the file cap by the multipart envelope.");
    }

    /// <summary>
    /// One byte over is refused, by the endpoint and with the endpoint's own code.
    ///
    /// <b>The pair matters more than either half.</b> A transport cap raised
    /// far enough to make the accepted case work would also swallow the refused
    /// one — the request would be read in full and then rejected, which is the
    /// same answer bought at the price of reading an unbounded upload. This
    /// says the limit still bites, and that the caller is told which limit it
    /// was.
    /// </summary>
    [SkippableFact]
    public async Task A_recording_one_byte_over_the_ceiling_is_refused_by_name()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, view) = await SittingOnSpeakingAsync();

        var response = await UploadRecordingAsync(
            client, access, sessionId, FirstQuestionId(view), sizeBytes: 12 * 1024 * 1024 + 1);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("RECORDING_TOO_LARGE", await CodeOf(response));
    }


    /// <summary>
    /// <b>Uploading the same answer twice leaves one object, not two.</b>
    ///
    /// This route carries no idempotency key, deliberately — demanding one made
    /// Speaking unanswerable, because the shared transport helper that threads
    /// keys serialises its body as JSON and a recording is multipart. So a
    /// retry after a lost response reaches the handler a second time, and with
    /// a random object key it wrote a <i>second</i> object and pointed the
    /// sheet at it, stranding the first.
    ///
    /// Re-recording did exactly the same thing, every single time.
    ///
    /// Both left an unreferenced blob holding a learner's voice, which is
    /// personal data under PDPL and not something to accumulate by accident.
    /// → `I2.1`, `docs/security/privacy-vietnam-pdpl.md`
    /// </summary>
    [SkippableFact]
    public async Task Re_uploading_one_answer_replaces_the_recording_rather_than_stranding_it()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, view) = await SittingOnSpeakingAsync();
        var questionId = FirstQuestionId(view);

        var first = await UploadRecordingAsync(client, access, sessionId, questionId);
        first.EnsureSuccessStatusCode();

        var second = await UploadRecordingAsync(client, access, sessionId, questionId);
        second.EnsureSuccessStatusCode();

        var firstId = (await BodyOf(first)).GetProperty("recordingId").GetString();
        var secondId = (await BodyOf(second)).GetProperty("recordingId").GetString();

        // Same key, so the second upload replaced the first rather than joining it.
        Assert.Equal(firstId, secondId);

        var db = app.Services.GetRequiredService<MongoDB.Driver.IMongoDatabase>();
        var files = db.GetCollection<MongoDB.Bson.BsonDocument>("speaking_recordings.files");

        var stored = await files
            .CountDocumentsAsync(
                MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("filename", firstId));

        Assert.Equal(1, stored);
    }

    /// <summary>
    /// Two questions do not collide, which is the other half of a derived key.
    ///
    /// A key that made every recording replace every other would be a much
    /// worse bug than the leak it was fixing: Part 1 would overwrite Part 2 and
    /// the learner would be marked on one answer given three times.
    /// </summary>
    [SkippableFact]
    public async Task Two_questions_keep_two_recordings()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, view) = await SittingOnSpeakingAsync();

        var questions = view
            .GetProperty("current").GetProperty("parts").EnumerateArray()
            .SelectMany(p => p.GetProperty("questions").EnumerateArray())
            .Select(q => q.GetProperty("id").GetString()!)
            .Take(2)
            .ToArray();

        Assert.Equal(2, questions.Length);

        var a = await UploadRecordingAsync(client, access, sessionId, questions[0]);
        var b = await UploadRecordingAsync(client, access, sessionId, questions[1]);

        Assert.NotEqual(
            (await BodyOf(a)).GetProperty("recordingId").GetString(),
            (await BodyOf(b)).GetProperty("recordingId").GetString());
    }

    /// <summary>
    /// A recording that arrives after the Speaking deadline is refused, on the
    /// server's clock and nothing the client sent. → ADR-0007
    /// </summary>
    [SkippableFact]
    public async Task A_recording_uploaded_after_the_deadline_is_refused()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, view) = await SittingOnSpeakingAsync();
        var questionId = FirstQuestionId(view);

        // Past every Speaking budget the fixture configures, and past a Full
        // Test's whole run — nothing here reads a client timestamp, so this is
        // the only way to be late.
        app.Clock.Skew = TimeSpan.FromHours(6);

        var response = await UploadRecordingAsync(client, access, sessionId, questionId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SESSION_EXPIRED", await CodeOf(response));

        app.Clock.Reset();
    }

    // ── 3 · Speaking's sheet belongs to the server ───────────────────────

    /// <summary>
    /// <b>An autosave naming Speaking is refused, and this is the refusal the
    /// web client walks into on every recording.</b>
    ///
    /// `ExamRunnerPage` passes `onStored={(id) => change(question.id, id)}` to
    /// `SpeakingRecorder`, and `change` marks the sheet dirty and schedules the
    /// ordinary autosave — which sends `module: "speaking"` and gets this.
    ///
    /// The server is right to refuse. A recording id a caller supplies is an id
    /// they can take from another sitting, which is a way to be marked on
    /// somebody else's performance. The fix belongs on the client, and it is
    /// that the Speaking sheet is not the client's to send.
    /// </summary>
    [SkippableFact]
    public async Task An_autosave_naming_speaking_is_refused()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, view) = await SittingOnSpeakingAsync();
        var questionId = FirstQuestionId(view);

        var response = await SaveAnswersAsync(
            client, access, sessionId, "speaking",
            new Dictionary<string, string?> { [questionId] = "a recording id the client chose" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await CodeOf(response));
    }

    /// <summary>
    /// And the refusal does not disturb what the server already filed. A client
    /// that keeps retrying its rejected autosave must not be able to blank the
    /// recording index by trying often enough.
    /// </summary>
    [SkippableFact]
    public async Task A_refused_speaking_autosave_leaves_the_recording_index_alone()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, view) = await SittingOnSpeakingAsync();
        var questionId = FirstQuestionId(view);

        var upload = await UploadRecordingAsync(client, access, sessionId, questionId);
        upload.EnsureSuccessStatusCode();
        var recordingId = (await BodyOf(upload)).GetProperty("recordingId").GetString();

        await SaveAnswersAsync(
            client, access, sessionId, "speaking",
            new Dictionary<string, string?> { [questionId] = null });

        var reloaded = await GetSessionAsync(client, access, sessionId);
        Assert.Equal(
            recordingId,
            reloaded.GetProperty("current").GetProperty("answers")
                .GetProperty(questionId).GetString());
    }

    // ── 4 · A late autosave for a section already left ───────────────────

    /// <summary>
    /// <b>The refusal the client is most likely to meet on a flaky connection,
    /// and the one whose code changed on 27/08.</b>
    ///
    /// An autosave for Reading that lands after the sitting moved to Listening
    /// used to be answered `SESSION_EXPIRED`, whose documented client handling
    /// is to stop the exam and go to the results screen. It is now
    /// `SECTION_NOT_OPEN`, and the sitting is still running — which is the
    /// property asserted at the end here, and the one the client must not
    /// mistake for an ending.
    /// </summary>
    [SkippableFact]
    public async Task A_late_autosave_for_the_section_just_left_does_not_end_the_sitting()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "full");
        var sessionId = started.GetProperty("sessionId").GetString()!;
        var readingQuestion = FirstQuestionId(started);

        var advanced = await AdvanceAsync(client, access, sessionId, Guid.NewGuid().ToString("n"));
        advanced.EnsureSuccessStatusCode();
        Assert.Equal("listening", ModuleOf(await BodyOf(advanced)));

        // The autosave that was in flight when "Tiếp theo" was pressed.
        var late = await SaveAnswersAsync(
            client, access, sessionId, "reading",
            new Dictionary<string, string?> { [readingQuestion] = "too late" });

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("SECTION_NOT_OPEN", await CodeOf(late));

        // The whole point of the split: nothing about the sitting has ended.
        var reloaded = await GetSessionAsync(client, access, sessionId);
        Assert.Equal("inprogress", reloaded.GetProperty("status").GetString());
        Assert.Equal("listening", ModuleOf(reloaded));
    }

    /// <summary>
    /// A second tab's answers come back to the first, instead of being deleted
    /// by it.
    ///
    /// <b>This is the whole of A3, end to end.</b> Two writers on different
    /// questions used to be a lost update; then a compare made it a 409, which
    /// the client answered by re-sending its whole sheet and completing the
    /// overwrite anyway. Now the second writer's patch lands, and because its
    /// base revision was behind it is handed the merged sheet — so it can show
    /// what the other tab typed rather than erase it.
    /// </summary>
    [SkippableFact]
    public async Task An_autosave_composed_against_an_older_revision_gets_the_merged_sheet_back()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "full");
        var sessionId = started.GetProperty("sessionId").GetString()!;
        var questions = QuestionIds(started).Take(2).ToList();

        // Tab one answers the first question and is now at revision 1.
        var first = await SaveAnswersAsync(
            client, access, sessionId, "reading",
            new Dictionary<string, string?> { [questions[0]] = "from tab one" },
            baseRevision: 0);

        first.EnsureSuccessStatusCode();
        var firstBody = await BodyOf(first);
        Assert.Equal(1, firstBody.GetProperty("revision").GetInt32());

        // A caught-up caller is told nothing it already knows.
        Assert.False(firstBody.TryGetProperty("answers", out _));

        // Tab two never saw that, and answers the second question against 0.
        var second = await SaveAnswersAsync(
            client, access, sessionId, "reading",
            new Dictionary<string, string?> { [questions[1]] = "from tab two" },
            baseRevision: 0);

        second.EnsureSuccessStatusCode();
        var body = await BodyOf(second);

        Assert.Equal(2, body.GetProperty("revision").GetInt32());

        var merged = body.GetProperty("answers");
        Assert.Equal("from tab one", merged.GetProperty(questions[0]).GetString());
        Assert.Equal("from tab two", merged.GetProperty(questions[1]).GetString());
    }

    /// <summary>
    /// A question the paper does not contain is refused, not filed.
    ///
    /// <b>The id is a database path now.</b> It keys the answer document, so an
    /// unchecked one lets a caller grow a sheet no section can account for —
    /// and a <c>.</c> or a leading <c>$</c> in it would decide where the value
    /// landed. The exam names its own questions; that is what this is checked
    /// against.
    /// </summary>
    [SkippableFact]
    public async Task An_autosave_naming_a_question_this_exam_does_not_have_is_refused()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "full");
        var sessionId = started.GetProperty("sessionId").GetString()!;

        var refused = await SaveAnswersAsync(
            client, access, sessionId, "reading",
            new Dictionary<string, string?> { ["$where"] = "anything" });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await CodeOf(refused));
    }

    /// <summary>
    /// An autosave with no <c>changes</c> at all is a bad request, not a
    /// silent success.
    ///
    /// A client that has stopped sending the field is a client that has stopped
    /// saving, and the one thing it must not get back is a revision that makes
    /// its chip read "Đã lưu".
    /// </summary>
    [SkippableFact]
    public async Task An_autosave_with_no_changes_field_is_refused()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "full");
        var sessionId = started.GetProperty("sessionId").GetString()!;

        var request = Authed(HttpMethod.Put, $"/api/v1/sessions/{sessionId}/answers", access);
        request.Content = JsonContent.Create(new { module = "reading" });

        var refused = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await CodeOf(refused));
    }


    /// <summary>
    /// A refused autosave names the questions it refused, and nothing else.
    ///
    /// <b>Written 2026-08-27, after a batch refusal was found to cost every
    /// answer in the batch.</b> An autosave carries every question edited since
    /// the last acknowledgement, so a batch is normally several answers. This
    /// endpoint refuses the whole batch when one entry is bad — correctly, a
    /// partly applied autosave is a sheet nobody can reason about — but it said
    /// so in prose only.
    ///
    /// The client, given no detail, could do only one thing: throw the whole
    /// batch away and open its submit gate. So a learner who typed a good
    /// answer alongside a bad one lost both and the paper was marked without
    /// either, with every visible signal reporting success.
    ///
    /// <c>errors</c> carries one row per refused question id so the client can
    /// drop exactly those and keep offering the rest. The assertion that
    /// matters is the second one: the <b>good</b> question must not appear.
    /// → <c>useAnswerSheet</c>, <c>exam-flow.test.tsx</c>
    /// </summary>
    [SkippableFact]
    public async Task A_refused_autosave_names_the_bad_question_and_not_the_good_one()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "full");
        var sessionId = started.GetProperty("sessionId").GetString()!;

        var good = started
            .GetProperty("current").GetProperty("parts").EnumerateArray().First()
            .GetProperty("questions").EnumerateArray().First()
            .GetProperty("id").GetString()!;

        var refused = await SaveAnswersAsync(
            client, access, sessionId, "reading",
            new Dictionary<string, string?>
            {
                [good] = "a perfectly ordinary answer",
                ["no-such-question"] = "and one the paper never asked",
            });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // One read: `CodeOf` and `BodyOf` both consume the response stream, and
        // the second of them meets a closed one.
        var body = await BodyOf(refused);
        Assert.Equal("VALIDATION_FAILED", body.GetProperty("code").GetString());

        var errors = body.GetProperty("errors").EnumerateArray().ToArray();
        var named = errors.Select(e => e.GetProperty("path").GetString()).ToArray();

        Assert.Equal(new[] { "no-such-question" }, named);
        Assert.DoesNotContain(good, named);
        Assert.Equal("QUESTION_UNKNOWN", errors[0].GetProperty("code").GetString());
    }

    /// <summary>
    /// The same, for an answer too long to store. Different exception, same
    /// contract: the client has to be able to tell which entry to drop.
    /// </summary>
    [SkippableFact]
    public async Task An_over_long_answer_is_refused_by_name()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "full");
        var sessionId = started.GetProperty("sessionId").GetString()!;

        var questions = started
            .GetProperty("current").GetProperty("parts").EnumerateArray().First()
            .GetProperty("questions").EnumerateArray()
            .Select(q => q.GetProperty("id").GetString()!)
            .ToArray();

        Assert.True(questions.Length >= 2, "This test needs two questions in the first part.");

        var refused = await SaveAnswersAsync(
            client, access, sessionId, "reading",
            new Dictionary<string, string?>
            {
                [questions[0]] = new string('x', 60_001),
                [questions[1]] = "short and fine",
            });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var errors = (await BodyOf(refused)).GetProperty("errors").EnumerateArray().ToArray();

        Assert.Equal(new[] { questions[0] }, errors.Select(e => e.GetProperty("path").GetString()));
        Assert.Equal("ANSWER_TOO_LONG", errors[0].GetProperty("code").GetString());
    }


    /// <summary>
    /// <b>An answer accepted after its section was marked is data loss the
    /// interface reports as success.</b>
    ///
    /// The sitting and the answer sheet are different collections. The
    /// transition compare-and-swap guards the session document and says nothing
    /// about a write to the sheet, so an autosave that had already passed its
    /// "is this section open" check could land after the submit and after
    /// marking read the sheet. The learner's chip said "Đã lưu" and the result
    /// was computed without the answer — nothing threw, nothing was logged, and
    /// the only evidence was a band one mark low.
    ///
    /// This runs the whole thing over HTTP: save, submit, then save again. The
    /// second save is the one that used to be accepted.
    /// </summary>
    [SkippableFact]
    public async Task An_autosave_after_a_submit_is_refused_and_the_result_does_not_move()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "single", "reading");
        var sessionId = started.GetProperty("sessionId").GetString()!;

        var questions = started
            .GetProperty("current").GetProperty("parts").EnumerateArray()
            .SelectMany(p => p.GetProperty("questions").EnumerateArray())
            .Select(q => q.GetProperty("id").GetString()!)
            .ToArray();

        var accepted = await SaveAnswersAsync(
            client, access, sessionId, "reading",
            new Dictionary<string, string?> { [questions[0]] = "in before the close" });

        Assert.True(
            accepted.StatusCode == HttpStatusCode.OK,
            $"The first save was refused ({accepted.StatusCode}): "
            + await accepted.Content.ReadAsStringAsync());

        var submit = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/submit", access);
        submit.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));

        var submitted = await client.SendAsync(submit);

        Assert.True(
            submitted.StatusCode == HttpStatusCode.OK,
            $"Submit was refused ({submitted.StatusCode}): "
            + await submitted.Content.ReadAsStringAsync());

        // The write the closure protocol exists to refuse.
        var late = await SaveAnswersAsync(
            client, access, sessionId, "reading",
            new Dictionary<string, string?> { [questions[1]] = "after the close" });

        Assert.NotEqual(HttpStatusCode.OK, late.StatusCode);

        /*
         * <b>And it is refused rather than merely unreported.</b> A refusal
         * that still wrote would be the same loss wearing an error code: the
         * result was computed before this, so an answer in the sheet and not in
         * the result is exactly the state that must not exist.
         */
        var results = await client.SendAsync(
            Authed(HttpMethod.Get, $"/api/v1/sessions/{sessionId}/results", access));

        var body = await BodyOf(results);
        var reading = body.GetProperty("sections").EnumerateArray()
            .Single(s => s.GetProperty("module").GetString() == "reading");

        var submittedAnswers = reading.GetProperty("questions").EnumerateArray()
            .ToDictionary(
                q => q.GetProperty("questionId").GetString()!,
                q => q.TryGetProperty("submitted", out var v) ? v.GetString() : null);

        Assert.Equal("in before the close", submittedAnswers[questions[0]]);
        Assert.Null(submittedAnswers[questions[1]]);
    }

    /// <summary>
    /// The same barrier, on the advance path.
    ///
    /// A Full Test closes the section it is leaving inside the same call that
    /// opens the next one, so an autosave for the section being left has the
    /// same window — and it is worse here, because the sitting carries on and
    /// nobody looks at the results until the end.
    /// </summary>
    [SkippableFact]
    public async Task An_autosave_for_a_section_left_behind_by_an_advance_is_refused()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "full");
        var sessionId = started.GetProperty("sessionId").GetString()!;

        var reading = started
            .GetProperty("current").GetProperty("parts").EnumerateArray()
            .SelectMany(p => p.GetProperty("questions").EnumerateArray())
            .Select(q => q.GetProperty("id").GetString()!)
            .ToArray();

        var advanced = await AdvanceAsync(client, access, sessionId, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);

        var late = await SaveAnswersAsync(
            client, access, sessionId, "reading",
            new Dictionary<string, string?> { [reading[0]] = "too late" });

        Assert.NotEqual(HttpStatusCode.OK, late.StatusCode);
    }

    // ── 5 · Advance, and advancing off the end ───────────────────────────

    /// <summary>
    /// Advancing out of the last section completes the sitting rather than
    /// failing.
    ///
    /// The client labels the footer button from `SKILL_ORDER` and only calls
    /// `/advance` when it believes another skill follows. That belief is a
    /// guess about the exam's shape; this is what makes the guess free.
    /// </summary>
    [SkippableFact]
    public async Task Advancing_out_of_the_last_section_completes_the_sitting()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access, sessionId, _) = await SittingOnSpeakingAsync();

        var response = await AdvanceAsync(client, access, sessionId, Guid.NewGuid().ToString("n"));
        response.EnsureSuccessStatusCode();

        var view = await BodyOf(response);
        Assert.Equal("submitted", view.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("current").ValueKind);
    }

    /// <summary>
    /// A single-skill sitting refuses to advance, with its own code.
    /// "Tiếp theo" there would be a button that cannot work. → CLAUDE.md rule 10
    /// </summary>
    [SkippableFact]
    public async Task A_single_skill_sitting_refuses_to_advance()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "single", "reading");
        var sessionId = started.GetProperty("sessionId").GetString()!;

        var response = await AdvanceAsync(client, access, sessionId, Guid.NewGuid().ToString("n"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("NOT_A_FULL_TEST", await CodeOf(response));
    }

    /// <summary>
    /// <b>A replayed "Tiếp theo" does not open a third section.</b>
    ///
    /// The client holds one key per section left behind and regenerates it only
    /// after a section actually closes, so a retry after a lost response carries
    /// the same key. This is the server half of that arrangement: the second
    /// call is answered from the store and the sitting does not move again.
    /// </summary>
    [SkippableFact]
    public async Task A_replayed_advance_returns_the_first_answer_and_moves_nothing()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "full");
        var sessionId = started.GetProperty("sessionId").GetString()!;

        var key = Guid.NewGuid().ToString("n");

        var first = await AdvanceAsync(client, access, sessionId, key);
        first.EnsureSuccessStatusCode();
        Assert.Equal("listening", ModuleOf(await BodyOf(first)));

        var replay = await AdvanceAsync(client, access, sessionId, key);
        replay.EnsureSuccessStatusCode();

        Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal("listening", ModuleOf(await BodyOf(replay)));

        // The state, not just the answer: Writing must not have been opened.
        Assert.Equal("listening", ModuleOf(await GetSessionAsync(client, access, sessionId)));
    }

    // ── 6 · Reading a sitting that ran out of time ───────────────────────

    /// <summary>
    /// <b>An overdue sitting is closed and marked when somebody reads it.</b>
    ///
    /// This is the behaviour the client depends on to recover a sitting that was
    /// abandoned mid-exam: it loads `GET /sessions/{id}`, sees a status that is
    /// not `inprogress`, and redirects to the results. If the read did not close
    /// the sitting, the runner would open a paper whose clock reads 00:00 with
    /// every input disabled and no way forward.
    ///
    /// And the answers saved before the deadline are marked, not merely kept.
    /// Marking used to run from "advance" and "submit" only, so a learner who
    /// ran out of time ended with a full sheet and no band. → `A-11`
    /// </summary>
    [SkippableFact]
    public async Task A_sitting_read_after_its_deadline_comes_back_closed_and_marked()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "single", "reading");
        var sessionId = started.GetProperty("sessionId").GetString()!;
        var questionId = FirstQuestionId(started);

        var saved = await SaveAnswersAsync(
            client, access, sessionId, "reading",
            new Dictionary<string, string?> { [questionId] = "something" });

        // <b>200 with a revision, where this used to be 204.</b> A client that
        // cannot learn the sheet's new version has to re-read before every save
        // or go on writing blind — and writing blind is what compare-and-swap
        // exists to stop.
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(1, (await BodyOf(saved)).GetProperty("revision").GetInt32());

        app.Clock.Skew = TimeSpan.FromHours(6);

        var reloaded = await GetSessionAsync(client, access, sessionId);

        Assert.Equal("expired", reloaded.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, reloaded.GetProperty("current").ValueKind);

        var results = await client.SendAsync(
            Authed(HttpMethod.Get, $"/api/v1/sessions/{sessionId}/results", access));
        results.EnsureSuccessStatusCode();

        var sections = (await BodyOf(results)).GetProperty("sections").EnumerateArray().ToArray();
        var reading = Assert.Single(sections, s => s.GetProperty("module").GetString() == "reading");

        // A band, from the answer key. Not a placeholder, and never a zero
        // standing in for a mark that was never awarded. → product law L3
        Assert.True(reading.GetProperty("maxScore").GetInt32() > 0);
        Assert.True(reading.TryGetProperty("band", out _));

        app.Clock.Reset();
    }

    /// <summary>
    /// The learner pressing submit after the deadline is refused, and the
    /// refusal costs them the request and nothing else — the sitting is closed
    /// and what they had saved is still marked.
    /// </summary>
    [SkippableFact]
    public async Task A_submission_after_the_deadline_is_refused_but_keeps_what_was_saved()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await StartAsync(client, access, examId, "single", "reading");
        var sessionId = started.GetProperty("sessionId").GetString()!;
        var questionId = FirstQuestionId(started);

        await SaveAnswersAsync(
            client, access, sessionId, "reading",
            new Dictionary<string, string?> { [questionId] = "something" });

        app.Clock.Skew = TimeSpan.FromHours(6);

        var submit = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/submit", access);
        submit.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));

        var response = await client.SendAsync(submit);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SESSION_EXPIRED", await CodeOf(response));

        // Closed by the server rather than lost.
        Assert.Equal("expired", (await GetSessionAsync(client, access, sessionId))
            .GetProperty("status").GetString());

        app.Clock.Reset();
    }
}

/// <summary>
/// A clock the test can move.
///
/// <b>Only the exam engine reads it.</b> Bearer tokens are validated by
/// `JwtBearer` against the process clock, so moving this forward expires a
/// section without expiring the sign-in that is asking about it — which is the
/// combination a real learner produces and a real clock cannot be made to.
/// </summary>
public sealed class MovableClock : IClock
{
    public TimeSpan Skew { get; set; } = TimeSpan.Zero;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + Skew;

    public void Reset() => Skew = TimeSpan.Zero;
}

/// <summary>
/// The API, wired to a throwaway database, the stub sign-in provider and a
/// clock the test owns.
///
/// Separate from <see cref="SsoAppFactory"/> rather than a setting on it: this
/// one hands every test in its class a mutable singleton, and sharing that with
/// the identity suites would make their results depend on the order xUnit
/// happened to run in.
/// </summary>
public class ExamAppFactory : WebApplicationFactory<Program>
{
    /*
     * <b>Not sealed, so a suite can add one setting without copying twelve.</b>
     * `FullSittingJourneyTests` needs the same wiring with a rubric configured,
     * and `KestrelTransportTests` needs it on a real socket. A second
     * hand-maintained copy of this configuration is a copy that drifts, and the
     * drift shows up as a test asserting against a world the product is not in.
     */

    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";

    private readonly string _database = $"vni_ielts_exam_test_{Guid.NewGuid():n}";

    public MovableClock Clock { get; } = new();

    public static bool MongoAvailable => SsoAppFactory.MongoAvailable;

    public static string SkipReason => SsoAppFactory.SkipReason;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development: the only environment where the stub provider is allowed,
        // and the only one that seeds `fixtures/exams` — which is where the
        // four-module paper these tests need comes from.
        builder.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);

        builder.UseSetting("Mongo:ConnectionString", ConnectionString);
        builder.UseSetting("Mongo:Database", _database);
        builder.UseSetting("Jwt:SigningKey", new string('k', 48));
        builder.UseSetting("Sso:EnableStubProvider", "true");

        // The synthetic four-module paper, which is the only exam a clean
        // checkout has. Off by default so it never reaches a learner's
        // catalogue. → DevelopmentExamSeeder.IncludeSynthetic
        builder.UseSetting("Seed:IncludeSyntheticExams", "true");

        // Blanked deliberately: a developer with real Google credentials in user
        // secrets would otherwise have this suite reach Google from a test.
        builder.UseSetting("Sso:Google:ClientId", string.Empty);
        builder.UseSetting("Sso:Google:ClientSecret", string.Empty);
        builder.UseSetting("Sso:ClientBaseUrl", "http://localhost:5173");
        builder.UseSetting("Sso:Google:RedirectUri", "http://localhost/api/v1/auth/sso/google/callback");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IClock>(Clock);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        if (MongoAvailable)
        {
            await new MongoClient(ConnectionString).DropDatabaseAsync(_database);
        }

        await base.DisposeAsync();
    }
}
