using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// One learner, one sitting, from having no account to reading their bands.
///
/// <b>Written 2026-08-28 as `I7.4`. Nothing before it had ever walked all four
/// modules through HTTP.</b> The suite was thorough in a shape that hid this:
/// every refusal was covered, every race was covered, and the one thing nobody
/// had checked was the path a learner actually takes when nothing goes wrong.
/// <c>ExamRunContractTests</c> advances exactly one step, Reading to Listening,
/// and stops.
///
/// ── What a journey catches that a per-endpoint test cannot ────────────────
///
/// Every test in this project so far asks "does this endpoint answer
/// correctly", and each one sets up its own world to ask it. That is the right
/// question and it is blind to a whole class of defect, because the setup is
/// written by the same person as the assertion and they agree by construction.
/// Three things only show up when the output of one real call becomes the input
/// of the next:
///
/// <list type="bullet">
/// <item><b>State that only accumulates over a whole sitting.</b> Four sections
/// close in sequence, each writing results the next call reads. A one-step test
/// never has two closed sections at once, so it cannot see a results view that
/// reports the last one and drops the rest.</item>
/// <item><b>Deadlines derived from the wrong anchor.</b> A section's clock must
/// start when that section opens, not when the sitting did. Advance one step
/// and the two are indistinguishable; spend twenty real minutes in Reading and
/// they differ by exactly those twenty minutes.</item>
/// <item><b>Credentials that expire mid-sitting.</b> An access token lives
/// minutes and an IELTS sitting lives hours, so <i>every</i> real sitting
/// refreshes at least once. No test had ever rotated a token and then kept
/// using the same session — which is precisely the moment a mistake in
/// ownership or in refresh-token family handling would cost a learner their
/// paper.</item>
/// </list>
///
/// <b>Registration by email and password, not the SSO stub.</b> The rest of the
/// exam suite signs in through the stub provider because it is two calls and it
/// works. It is also the path that skips password hashing, email verification
/// state and the registration rate limiter. This one takes the long way in.
/// </summary>
public sealed class FullSittingJourneyTests(ExamAppFactory app) : IClassFixture<ExamAppFactory>
{
    private const string Password = "mot-mat-khau-du-dai-2026";

    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static HttpRequestMessage Authed(HttpMethod method, string path, string access)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return request;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Key() => Guid.NewGuid().ToString("n");

    // ── The journey, one step at a time ─────────────────────────────────

    /// <summary>Registers a brand-new learner and returns the pair they are handed.</summary>
    private static async Task<(string Access, string Refresh, string UserId)> RegisterAsync(
        HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new
            {
                email = $"hanh.trinh.{Guid.NewGuid():n}@example.com",
                password = Password,
                displayName = "Học viên",
            }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Key());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var session = (await BodyOf(response)).GetProperty("session");

        return (
            session.GetProperty("accessToken").GetString()!,
            session.GetProperty("refreshToken").GetString()!,
            session.GetProperty("userId").GetString()!);
    }

    /// <summary>
    /// The synthetic paper, <b>by name</b>.
    ///
    /// <b>Not "the first exam with four modules", which is what this was and
    /// what made it fail on the machine it was written on.</b> A developer who
    /// has the borrowed paper in <c>fixtures/exams</c> seeds two four-module
    /// exams; a clean checkout and CI seed one. Picking the first match makes
    /// the test read a different paper depending on whose disk it runs on —
    /// green in CI, red locally, and neither result about the code.
    ///
    /// The borrowed paper is third-party material with an unestablished licence
    /// and is deliberately not under version control, so it can never be the
    /// thing a test asserts against.
    /// </summary>
    private const string SyntheticExamTitle = "VNI Synthetic Practice Test";

    private async Task<string> FullExamIdAsync(HttpClient client, string access)
    {
        var response = await client.SendAsync(Authed(HttpMethod.Get, "/api/v1/exams", access));
        response.EnsureSuccessStatusCode();

        var full = (await BodyOf(response)).GetProperty("exams").EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("title").GetString() == SyntheticExamTitle);

        Assert.True(
            full.ValueKind == JsonValueKind.Object,
            $"'{SyntheticExamTitle}' is not in the catalogue. It is seeded only when "
            + "`Seed:IncludeSyntheticExams` is on, which this factory sets.");

        Assert.Equal(4, full.GetProperty("modules").EnumerateArray().Count());

        return full.GetProperty("examVersionId").GetString()!;
    }

    private async Task<JsonElement> StartFullTestAsync(
        HttpClient client, string access, string examVersionId)
    {
        var request = Authed(HttpMethod.Post, "/api/v1/sessions", access);
        request.Headers.Add("Idempotency-Key", Key());
        request.Content = JsonContent.Create(
            new { examVersionId, mode = "full", module = (string?)null });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await BodyOf(response);
    }

    private async Task<int> SaveAsync(
        HttpClient client, string access, string sessionId,
        string module, Dictionary<string, string?> changes, int baseRevision)
    {
        var request = Authed(HttpMethod.Put, $"/api/v1/sessions/{sessionId}/answers", access);
        request.Content = JsonContent.Create(new { module, changes, baseRevision });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (await BodyOf(response)).GetProperty("revision").GetInt32();
    }

    private async Task<JsonElement> AdvanceAsync(HttpClient client, string access, string sessionId)
    {
        var request = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/advance", access);
        request.Headers.Add("Idempotency-Key", Key());

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await BodyOf(response);
    }

    /// <summary>
    /// Exchanges a refresh token, <b>with the test clock briefly put back to
    /// real time.</b>
    ///
    /// ── Why that is necessary, and why it is a harness fact rather than a bug ──
    ///
    /// <c>JwtTokenService</c> stamps <c>nbf</c> and <c>exp</c> from
    /// <see cref="IClock"/> — this suite's movable one. The framework's bearer
    /// handler validates them against the system clock, with the standard five
    /// minutes of tolerance. In production the two are the same clock and the
    /// question never arises; here they are not, so a token minted while the
    /// test clock is twenty minutes ahead is refused as not yet valid, and the
    /// 401 lands on the <i>next</i> call rather than on the refresh — which is
    /// about as misleading as a failure gets.
    ///
    /// <b>The rule this encodes: skew the clock freely, but never while a token
    /// is being minted.</b> Validating a token already issued is unaffected,
    /// because the validator was never looking at the test's clock in the first
    /// place — which is exactly how the six-hour expiry tests in
    /// <c>ExamRunContractTests</c> get away with it.
    ///
    /// <b>Not solved by widening <c>ClockSkew</c> in test services.</b> That
    /// would make this suite the one place where an expired access token is
    /// accepted, and the first real lifetime defect would land in a test that
    /// had been configured not to see it.
    /// </summary>
    private async Task<JsonElement> RotateAsync(HttpClient client, string refresh)
    {
        var skew = app.Clock.Skew;
        app.Clock.Reset();

        try
        {
            var rotated = await client.PostAsJsonAsync(
                "/api/v1/auth/refresh", new { refreshToken = refresh });

            rotated.EnsureSuccessStatusCode();
            return await BodyOf(rotated);
        }
        finally
        {
            app.Clock.Skew = skew;
        }
    }

    private async Task<HttpResponseMessage> UploadRecordingAsync(
        HttpClient client, string access, string sessionId, string questionId)
    {
        var request = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/recordings", access);

        var audio = new ByteArrayContent(Convert.FromBase64String("T2dnUwACAAAAAAAAAAA="));
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");

        request.Content = new MultipartFormDataContent
        {
            { new StringContent(questionId), "questionId" },
            { audio, "audio", $"{questionId}.webm" },
        };

        return await client.SendAsync(request);
    }

    private static string ModuleOf(JsonElement view) =>
        view.GetProperty("current").GetProperty("module").GetString()!;

    private static int RevisionOf(JsonElement view) =>
        view.GetProperty("current").GetProperty("answerRevision").GetInt32();

    private static string[] QuestionIds(JsonElement view) =>
        view.GetProperty("current").GetProperty("parts").EnumerateArray()
            .SelectMany(p => p.GetProperty("questions").EnumerateArray())
            .Select(q => q.GetProperty("id").GetString()!)
            .ToArray();

    private static JsonElement SectionNamed(JsonElement results, string module) =>
        results.GetProperty("sections").EnumerateArray()
            .Single(s => s.GetProperty("module").GetString() == module);

    // ── 1 · The whole thing ─────────────────────────────────────────────

    /// <summary>
    /// Register, sit all four modules, refresh a token halfway, hand it in, and
    /// read the bands back.
    ///
    /// <b>One test rather than eight, deliberately.</b> Split into per-step
    /// tests, each step would rebuild the world it needed and the seam between
    /// two real steps — which is the only thing this can see and the rest of the
    /// suite cannot — would stop being exercised. The failure message is worse
    /// than eight would give; what it buys is that the failure happens at all.
    ///
    /// <b>The answers are chosen so the marker has to discriminate.</b> Reading
    /// is answered fully correctly and Listening deliberately is not: 4/4 → band
    /// 9, and 3/4 → band 6 from this paper's own equated table. A marker that
    /// awarded a constant, or read the wrong section's key, passes an
    /// all-correct sitting and fails here.
    /// </summary>
    [SkippableFact]
    public async Task A_learner_registers_sits_all_four_modules_and_reads_their_bands()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var client = NewClient();
        var (access, refresh, userId) = await RegisterAsync(client);

        // ── Reading ─────────────────────────────────────────────────────
        var examId = await FullExamIdAsync(client, access);
        var view = await StartFullTestAsync(client, access, examId);
        var sessionId = view.GetProperty("sessionId").GetString()!;

        Assert.Equal("reading", ModuleOf(view));
        Assert.Equal(
            ["syn-r-1", "syn-r-2", "syn-r-3", "syn-r-4"],
            QuestionIds(view));

        // Typed one at a time, as a learner types — each save carrying the
        // revision the last one returned. A server that ignored `baseRevision`
        // would pass a single bulk save and fail this.
        var revision = RevisionOf(view);
        revision = await SaveAsync(
            client, access, sessionId, "reading",
            new() { ["syn-r-1"] = "river depth" }, revision);
        revision = await SaveAsync(
            client, access, sessionId, "reading",
            new() { ["syn-r-2"] = "TRUE", ["syn-r-3"] = "A" }, revision);

        // Changed their mind, which is the ordinary case and the one that has to
        // overwrite rather than accumulate.
        revision = await SaveAsync(
            client, access, sessionId, "reading",
            new() { ["syn-r-2"] = "FALSE", ["syn-r-4"] = "B" }, revision);

        Assert.True(revision >= 3, "Each accepted save must move the revision forward.");

        // Twenty minutes of a sixty-minute section.
        app.Clock.Skew = TimeSpan.FromMinutes(20);

        // ── Listening ───────────────────────────────────────────────────
        view = await AdvanceAsync(client, access, sessionId);
        Assert.Equal("listening", ModuleOf(view));

        /*
         * <b>The assertion this whole test exists for.</b>
         *
         * Listening is 30 minutes plus this paper's 10-minute transfer time, so
         * a learner opening it has 2400 seconds. Twenty minutes of Reading have
         * already gone. If the deadline were anchored to the sitting rather than
         * to the section, they would open Listening with 1200 on the clock and
         * no way to tell that from a slow start.
         *
         * A one-step test cannot distinguish the two anchors at all, because at
         * step one they are the same instant. This is the first test in the
         * project where they are not.
         */
        var remaining = view.GetProperty("current").GetProperty("remainingSeconds").GetInt32();
        Assert.InRange(remaining, 2_300, 2_400);

        revision = RevisionOf(view);
        revision = await SaveAsync(
            client, access, sessionId, "listening",
            new()
            {
                ["syn-l-1"] = "9:30",
                ["syn-l-2"] = "three",
                // Two picks in one answer. `A17` was this exact value spelled
                // one way by the client and read another by the marker.
                ["syn-l-3"] = "A|C",
                // Wrong on purpose. The key accepts "registrar".
                ["syn-l-4"] = "the porter",
            },
            revision);

        /*
         * ── The access token expires mid-sitting ────────────────────────
         *
         * <b>Every real sitting does this.</b> An access token lives minutes and
         * an IELTS paper lives hours, so a learner who never refreshed would be
         * locked out of their own exam somewhere in Listening. No test had ever
         * rotated a token and then carried on using the same session, which is
         * the one moment a mistake in ownership or in refresh-family handling
         * costs somebody their paper rather than a retry.
         */
        var renewed = await RotateAsync(client, refresh);
        var secondAccess = renewed.GetProperty("accessToken").GetString()!;

        Assert.NotEqual(access, secondAccess);
        Assert.Equal(userId, renewed.GetProperty("userId").GetString());

        access = secondAccess;

        // And the sitting is still theirs, under the new credential.
        var reloaded = await client.SendAsync(
            Authed(HttpMethod.Get, $"/api/v1/sessions/{sessionId}", access));
        reloaded.EnsureSuccessStatusCode();
        Assert.Equal("listening", ModuleOf(await BodyOf(reloaded)));

        app.Clock.Skew = TimeSpan.FromMinutes(45);

        // ── Writing ─────────────────────────────────────────────────────
        view = await AdvanceAsync(client, access, sessionId);
        Assert.Equal("writing", ModuleOf(view));

        revision = RevisionOf(view);
        await SaveAsync(
            client, access, sessionId, "writing",
            new()
            {
                ["syn-w-task-1"] =
                    "The chart shows the number of visitors to three coastal towns between "
                    + "2019 and 2024. Overall, arrivals rose in every town, though the rise "
                    + "was steepest in the smallest of the three.",
                ["syn-w-task-2"] =
                    "Some argue that public transport should be free at the point of use. "
                    + "In my view the case is strong in dense cities and weak elsewhere, and "
                    + "this essay explains why the distinction matters more than the principle.",
            },
            revision);

        app.Clock.Skew = TimeSpan.FromMinutes(100);

        // ── Speaking ────────────────────────────────────────────────────
        view = await AdvanceAsync(client, access, sessionId);
        Assert.Equal("speaking", ModuleOf(view));

        var spoken = QuestionIds(view);
        Assert.Equal(3, spoken.Length);

        foreach (var questionId in spoken)
        {
            var upload = await UploadRecordingAsync(client, access, sessionId, questionId);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        // Every recording is filed against its own question, which is the half
        // of the chain that makes Speaking markable at all.
        var speaking = await BodyOf(await client.SendAsync(
            Authed(HttpMethod.Get, $"/api/v1/sessions/{sessionId}", access)));

        var filed = speaking.GetProperty("current").GetProperty("answers");
        foreach (var questionId in spoken)
            Assert.False(string.IsNullOrWhiteSpace(filed.GetProperty(questionId).GetString()));

        // ── Handing it in ───────────────────────────────────────────────
        var submitKey = Key();
        var submit = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/submit", access);
        submit.Headers.Add("Idempotency-Key", submitKey);

        var handedIn = await client.SendAsync(submit);
        Assert.Equal(HttpStatusCode.OK, handedIn.StatusCode);

        var results = await BodyOf(handedIn);

        Assert.Equal("submitted", results.GetProperty("status").GetString());
        Assert.Equal("full", results.GetProperty("mode").GetString());

        // ── The bands ───────────────────────────────────────────────────

        // 4 of 4. This paper's table puts that at 9.
        var reading = SectionNamed(results, "reading");
        Assert.Equal(4, reading.GetProperty("rawScore").GetInt32());
        Assert.Equal(4, reading.GetProperty("maxScore").GetInt32());
        Assert.Equal(9m, reading.GetProperty("band").GetDecimal());

        /*
         * <b>4 of 5, from four questions — and the mismatch is the point.</b>
         *
         * `syn-l-3` is a "choose TWO" question carrying two marks, so the
         * section is scored out of 5 rather than out of 4. Counting question
         * objects instead of marks is what once scored a 40-mark Listening
         * section out of 36 and put the top of its own equated table out of
         * reach. `syn-l-4` was answered wrongly on purpose; 4 marks is band 7.5
         * on this paper.
         *
         * <b>This is the first test in the project to read `maxScore` at all</b>,
         * which is how the difference between the two counts became visible.
         */
        var listening = SectionNamed(results, "listening");
        Assert.Equal(4, listening.GetProperty("rawScore").GetInt32());
        Assert.Equal(5, listening.GetProperty("maxScore").GetInt32());
        Assert.Equal(7.5m, listening.GetProperty("band").GetDecimal());

        // And the one wrong answer is reported as wrong, with what they typed —
        // a results screen that could not show this could not explain a band.
        var missed = listening.GetProperty("questions").EnumerateArray()
            .Single(q => q.GetProperty("questionId").GetString() == "syn-l-4");

        Assert.False(missed.GetProperty("isCorrect").GetBoolean());
        Assert.Equal("the porter", missed.GetProperty("submitted").GetString());

        // Both sections are here, together. A results view that reported only
        // the section closed last would pass every one-step test in the suite.
        Assert.Equal(2, results.GetProperty("sections").EnumerateArray().Count());

        /*
         * <b>Writing and Speaking carry no band, which is right.</b> No
         * evaluator is wired — `B-2` — so the honest answer is a dash, never a
         * zero and never an average of the two skills that happen to be marked.
         * Product law `L3`.
         */
        Assert.True(
            results.GetProperty("overallBand").ValueKind == JsonValueKind.Null,
            "An overall band before Writing and Speaking are marked would be invented.");

        Assert.Empty(results.GetProperty("markings").EnumerateArray());

        /*
         * <b>And they carry no reason either, which is not right — and this is
         * the assertion that says so.</b> `[OPEN QUESTION]` raised 28/08/2026.
         *
         * `I3.6` was built so a dash could explain itself: four states, four
         * different things for the learner to do. `MarkingWork.EnqueueAsync`
         * reaches `if (rubrics.For(module) is not { } rubric) return;` first,
         * and <b>no `Assessment` section exists in any appsettings file in this
         * repository</b> — not Development, not Production. So no rubric is
         * ever configured, no job is ever enqueued, and `markingStatuses` comes
         * back empty in every environment the product currently has.
         *
         * The seam is right and `G-11` is satisfied: an unresolved policy became
         * configuration with a null implementation rather than an invented
         * default. What is missing is the configuration itself, so the observable
         * behaviour today is the bare dash `I3.6` set out to remove — and no
         * test noticed, because no test had ever read this field after a
         * complete sitting.
         *
         * <b>Pinned as it is rather than fixed here.</b> Choosing a rubric
         * version and a descriptor source is a product decision, not a test's.
         * `A_configured_rubric_puts_writing_and_speaking_in_the_queue` below
         * proves the machinery works the moment somebody makes it.
         */
        Assert.Empty(results.GetProperty("markingStatuses").EnumerateArray());

        // ── The retry the client offers after a dropped response ────────

        /*
         * <b>Submitting is the one call a learner presses once and the network
         * may deliver twice.</b> The guard has to replay the first answer rather
         * than run a second submission — and "ran twice harmlessly" is not good
         * enough to assume, because marking runs inside submit.
         */
        var again = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/submit", access);
        again.Headers.Add("Idempotency-Key", submitKey);

        var replayed = await client.SendAsync(again);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);

        var second = await BodyOf(replayed);

        Assert.Equal(
            results.GetProperty("submittedAt").GetDateTimeOffset(),
            second.GetProperty("submittedAt").GetDateTimeOffset());

        Assert.Equal(2, second.GetProperty("sections").EnumerateArray().Count());

        // ── And it is still there on reload ─────────────────────────────
        var later = await client.SendAsync(
            Authed(HttpMethod.Get, $"/api/v1/sessions/{sessionId}/results", access));
        later.EnsureSuccessStatusCode();

        var persisted = await BodyOf(later);

        Assert.Equal("submitted", persisted.GetProperty("status").GetString());
        Assert.Equal(9m, SectionNamed(persisted, "reading").GetProperty("band").GetDecimal());
        Assert.Equal(7.5m, SectionNamed(persisted, "listening").GetProperty("band").GetDecimal());
    }

    // ── 2 · The sitting belongs to the learner who started it ───────────

    /// <summary>
    /// A second learner cannot read the first one's sitting.
    ///
    /// <b>Checked here rather than in a handler test because ownership is
    /// carried by a token, and a token only exists over HTTP.</b> A handler test
    /// passes a <c>UserId</c> the test itself chose, so it can only prove that
    /// the handler compares what it is given — never that the value it is given
    /// came from the caller's own credential.
    /// </summary>
    [SkippableFact]
    public async Task Another_learners_sitting_is_not_readable()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var owner = NewClient();
        var (ownerAccess, _, _) = await RegisterAsync(owner);

        var examId = await FullExamIdAsync(owner, ownerAccess);
        var started = await StartFullTestAsync(owner, ownerAccess, examId);
        var sessionId = started.GetProperty("sessionId").GetString()!;

        var stranger = NewClient();
        var (strangerAccess, _, _) = await RegisterAsync(stranger);

        var peek = await stranger.SendAsync(
            Authed(HttpMethod.Get, $"/api/v1/sessions/{sessionId}", strangerAccess));

        /*
         * <b>404, not 403.</b> A forbidden tells the caller the sitting exists,
         * which turns an id they cannot use into an oracle for ids that are
         * real. Not found is the same answer they would get for an id nobody
         * ever issued.
         */
        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
    }
}

/// <summary>
/// The same sitting, with a rubric configured — which is the only difference
/// between a dash that explains itself and a dash that does not.
///
/// <b>A separate class because it needs a separate process configuration</b>,
/// and configuration is what the whole finding is about. Folding it into
/// <see cref="FullSittingJourneyTests"/> would mean one factory serving two
/// worlds, and the assertion "`markingStatuses` is empty" would quietly become
/// order-dependent.
/// </summary>
public sealed class MarkingQueuedOnSubmitTests(RubricConfiguredAppFactory app)
    : IClassFixture<RubricConfiguredAppFactory>
{
    private const string Password = "mot-mat-khau-du-dai-2026";

    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static HttpRequestMessage Authed(HttpMethod method, string path, string access)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return request;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Key() => Guid.NewGuid().ToString("n");

    /// <summary>
    /// Sit the paper and hand it in; Writing and Speaking must be queued.
    ///
    /// <b>What this pins is that nothing except configuration stands between the
    /// product and a results screen that can explain itself.</b> The outbox, the
    /// worker, the four states and the view were all built in `I3`; the journey
    /// test next door shows they produce nothing, and the only difference here
    /// is two version strings and two descriptor sources.
    ///
    /// <b>`pending`, not `completed`.</b> No evaluator exists — `B-2` — so the
    /// job is queued and stays queued, which is exactly the state a learner
    /// should be shown: *waiting*, not *failed*, and certainly not a zero.
    /// </summary>
    [SkippableFact]
    public async Task A_configured_rubric_puts_writing_and_speaking_in_the_queue()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var client = NewClient();

        var register = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new
            {
                email = $"cham.bai.{Guid.NewGuid():n}@example.com",
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

        // Straight through to Speaking. What is being tested is what submit
        // queues, not what the learner wrote.
        for (var step = 0; step < 3; step++)
        {
            var advance = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/advance", access);
            advance.Headers.Add("Idempotency-Key", Key());
            (await client.SendAsync(advance)).EnsureSuccessStatusCode();
        }

        var submit = Authed(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/submit", access);
        submit.Headers.Add("Idempotency-Key", Key());

        var handedIn = await client.SendAsync(submit);
        Assert.Equal(HttpStatusCode.OK, handedIn.StatusCode);

        var statuses = (await BodyOf(handedIn)).GetProperty("markingStatuses")
            .EnumerateArray().ToArray();

        var queued = statuses.Select(s => s.GetProperty("module").GetString()).ToArray();

        Assert.Contains("writing", queued);
        Assert.Contains("speaking", queued);

        foreach (var status in statuses)
        {
            Assert.Equal("pending", status.GetProperty("state").GetString());
            Assert.Equal(0, status.GetProperty("attempts").GetInt32());

            // Nothing has gone wrong yet, so there is nothing to explain. A
            // reason invented at enqueue time would be a sentence the learner
            // reads as a failure.
            Assert.Equal(JsonValueKind.Null, status.GetProperty("reason").ValueKind);
        }
    }
}

/// <summary>
/// <see cref="ExamAppFactory"/> with the rubrics that no appsettings file in
/// this repository configures.
/// </summary>
public sealed class RubricConfiguredAppFactory : ExamAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        /*
         * <b>Version strings a test invented, and they must stay in a test.</b>
         * A rubric version is what an old band stays explicable by, so choosing
         * the real one is a product decision — `H-8b` is not settled and the
         * Task 1 : Task 2 weighting is still a value a caller has to supply.
         * `G-11`: a configured seam, never an invented default.
         */
        builder.UseSetting("Assessment:Writing:Version", "test-writing-v0");
        builder.UseSetting("Assessment:Writing:DescriptorSource", "integration-test fixture");
        builder.UseSetting("Assessment:Speaking:Version", "test-speaking-v0");
        builder.UseSetting("Assessment:Speaking:DescriptorSource", "integration-test fixture");
    }
}
