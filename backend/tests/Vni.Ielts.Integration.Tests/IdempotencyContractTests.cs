using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The idempotency guard, through the real pipeline and against a real server.
///
/// <b>The middleware used to look the key up, run the handler, and only then
/// store the record.</b> Two requests carrying the same key both missed the
/// lookup — because the first had not finished writing — and both ran. Two
/// sittings started, two sections marked, two evaluations bought. The duplicate
/// <c>_id</c> then failed on the second insert, and that failure was caught and
/// logged as "a replay that arrived a moment too early", which is the one thing
/// it was not.
///
/// <b>Only a race can show that, and only a real database can lose one.</b> An
/// in-memory dictionary under a lock produces a single winner however the code
/// is ordered, so a suite built on one would have agreed with the bug for as
/// long as it existed.
/// </summary>
public sealed class IdempotencyContractTests(IdempotencyAppFactory app)
    : IClassFixture<IdempotencyAppFactory>
{
    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

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

        var body = await complete.Content.ReadFromJsonAsync<JsonElement>();

        return (client, body.GetProperty("accessToken").GetString()!);
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string access,
        object? body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> CodeOf(HttpResponseMessage response) =>
        (await BodyOf(response)).GetProperty("code").GetString()!;

    private async Task<string> FullExamIdAsync(HttpClient client, string access)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/api/v1/exams", access, null, null);
        response.EnsureSuccessStatusCode();

        var full = (await BodyOf(response)).GetProperty("exams").EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("modules").EnumerateArray().Count() == 4);

        Assert.True(
            full.ValueKind == JsonValueKind.Object,
            "No seeded exam carries all four modules, so nothing here can sit a Full Test.");

        return full.GetProperty("examVersionId").GetString()!;
    }

    private IMongoCollection<BsonDocument> Keys =>
        app.Services.GetRequiredService<IMongoDatabase>()
            .GetCollection<BsonDocument>("idempotency_keys");

    /// <summary>
    /// The stored record for one key.
    ///
    /// <b>Matched on the key's own suffix rather than rebuilt from the storage
    /// format.</b> The middleware scopes a key by subject, method and path, and
    /// a test that reassembled that string would be asserting on the format
    /// instead of on the behaviour — and would break the day the format gains a
    /// field, for no reason a reader could see. The key is a fresh GUID, so the
    /// suffix identifies exactly one record.
    /// </summary>
    private FilterDefinition<BsonDocument> RecordFor(string key) =>
        Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression($":{key}$"));

    /// <summary>
    /// Two requests, one key, at the same instant: the handler runs once.
    ///
    /// <b>A barrier, not two sequential calls.</b> Sequential requests exercise
    /// the stored-record branch, which worked before and works now; they do not
    /// exercise the window between the lookup and the write, which is the whole
    /// bug. Both threads are held until both are ready, so only the database's
    /// own atomicity separates them.
    ///
    /// The proof is counted where it costs something. Starting a sitting is
    /// not a read that happens twice harmlessly — it is a row, a deadline, and
    /// in time an entitlement. One key must produce one sitting.
    /// </summary>
    [SkippableFact]
    public async Task Two_requests_with_one_key_at_the_same_instant_start_one_sitting()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var key = Guid.NewGuid().ToString("n");
        var body = new { examVersionId = examId, mode = "full", module = (string?)null };

        using var barrier = new Barrier(2);

        async Task<HttpResponseMessage> Racer()
        {
            barrier.SignalAndWait();
            return await SendAsync(client, HttpMethod.Post, "/api/v1/sessions", access, body, key);
        }

        var responses = await Task.WhenAll(Task.Run(Racer), Task.Run(Racer));

        /*
         * One of three outcomes is acceptable for the loser, and none of them
         * is a second sitting: the stored response replayed, the same 201
         * because it was the winner, or `IDEMPOTENCY_KEY_IN_FLIGHT` because the
         * winner had not finished. What is not acceptable is two sittings.
         */
        var sittings = await SendAsync(
            client, HttpMethod.Get, "/api/v1/sessions/", access, null, null);

        var open = (await BodyOf(sittings)).GetProperty("sittings").EnumerateArray()
            .Count(s => s.GetProperty("status").GetString() == "inprogress");

        Assert.Equal(1, open);

        /*
         * <b>And exactly one of the two responses is a fresh execution.</b>
         *
         * Counting sittings proves the outcome; this proves the mechanism, and
         * it holds whether or not the two requests genuinely overlapped. A
         * replay carries `Idempotency-Replayed`; a caller told to wait carries
         * a 409. Only the request that actually ran the handler carries a 201
         * without the header — and there must be one of those, and one only.
         */
        var fresh = responses.Count(r =>
            r.StatusCode == HttpStatusCode.Created && !r.Headers.Contains("Idempotency-Replayed"));

        Assert.Equal(1, fresh);

        // And whichever of them was refused was refused for the right reason.
        foreach (var response in responses)
        {
            if (response.IsSuccessStatusCode) continue;

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("IDEMPOTENCY_KEY_IN_FLIGHT", await CodeOf(response));
        }
    }

    /// <summary>
    /// A key still being executed is answered with a wait, not with a second
    /// execution and not with a guess.
    ///
    /// <b>The in-flight record is planted rather than raced for</b>, because a
    /// race cannot be made to land on this branch on demand. What is under test
    /// is what the branch does, and it has to do exactly one thing: refuse
    /// without running anything.
    /// </summary>
    [SkippableFact]
    public async Task A_key_that_is_still_in_flight_is_answered_with_a_wait()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var key = Guid.NewGuid().ToString("n");
        var body = new { examVersionId = examId, mode = "full", module = (string?)null };

        // The hash is over the serialised body, so the claim is planted by
        // making the request once — and then rewinding it to in-flight. That
        // way the stored hash is genuinely this body's, without this test
        // having to reimplement the hashing.
        var first = await SendAsync(client, HttpMethod.Post, "/api/v1/sessions", access, body, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var rewound = await Keys.UpdateOneAsync(
            RecordFor(key),
            Builders<BsonDocument>.Update
                .Set("state", "in-flight")
                .Set("leaseUntil", DateTime.UtcNow.AddMinutes(2))
                .Unset("status")
                .Unset("body"));

        Assert.Equal(1, rewound.ModifiedCount);

        var waiting = await SendAsync(client, HttpMethod.Post, "/api/v1/sessions", access, body, key);

        Assert.Equal(HttpStatusCode.Conflict, waiting.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_IN_FLIGHT", await CodeOf(waiting));
        Assert.Equal("1", waiting.Headers.RetryAfter?.ToString());
    }

    /// <summary>
    /// A claim whose holder died is taken over, not honoured for ever.
    ///
    /// <b>Without this the lease is a lock with no key.</b> A process that
    /// crashes between claiming and finishing leaves the record in flight, and
    /// a client that keeps retrying is told to wait until the twenty-four hour
    /// TTL removes it. During an exam that is the submit button ceasing to work
    /// with no way to explain it.
    /// </summary>
    [SkippableFact]
    public async Task A_claim_whose_lease_has_expired_is_taken_over()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var key = Guid.NewGuid().ToString("n");
        var body = new { examVersionId = examId, mode = "single", module = "reading" };

        var first = await SendAsync(client, HttpMethod.Post, "/api/v1/sessions", access, body, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstId = (await BodyOf(first)).GetProperty("sessionId").GetString();

        // The shape a crashed handler leaves: claimed, never completed, and
        // the lease long past.
        await Keys.UpdateOneAsync(
            RecordFor(key),
            Builders<BsonDocument>.Update
                .Set("state", "in-flight")
                .Set("leaseUntil", DateTime.UtcNow.AddMinutes(-5))
                .Unset("status")
                .Unset("body"));

        var retry = await SendAsync(client, HttpMethod.Post, "/api/v1/sessions", access, body, key);

        // It ran, because nobody was holding it any more.
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.NotEqual(firstId, (await BodyOf(retry)).GetProperty("sessionId").GetString());
    }

    /// <summary>
    /// A refused request does not leave its key claimed.
    ///
    /// <b>A key that outlived a failure would make the failure permanent.</b>
    /// The client retries with the same key — that is what the key is for — and
    /// would be handed the same refusal for twenty-four hours without anything
    /// ever running again. Releasing it is what makes the retry mean something.
    /// </summary>
    [SkippableFact]
    public async Task A_refused_request_releases_its_key()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();

        var key = Guid.NewGuid().ToString("n");
        var body = new { examVersionId = "no-such-version", mode = "full", module = (string?)null };

        var refused = await SendAsync(client, HttpMethod.Post, "/api/v1/sessions", access, body, key);
        Assert.False(refused.IsSuccessStatusCode);

        var held = await Keys.Find(RecordFor(key)).FirstOrDefaultAsync();

        Assert.Null(held);
    }

    /// <summary>
    /// A completed key replays its own stored response, byte for byte.
    ///
    /// <b>Not a fresh answer that happens to look the same.</b> Recomputing
    /// would run the handler, which is the whole thing being prevented; and for
    /// anything carrying a generated id, a recomputed answer is a different
    /// answer.
    /// </summary>
    [SkippableFact]
    public async Task A_completed_key_replays_the_response_it_stored()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var key = Guid.NewGuid().ToString("n");
        var body = new { examVersionId = examId, mode = "single", module = "listening" };

        var first = await SendAsync(client, HttpMethod.Post, "/api/v1/sessions", access, body, key);
        var replay = await SendAsync(client, HttpMethod.Post, "/api/v1/sessions", access, body, key);

        Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());

        Assert.Equal(
            (await BodyOf(first)).GetProperty("sessionId").GetString(),
            (await BodyOf(replay)).GetProperty("sessionId").GetString());

        /*
         * <b>And the record itself is complete, not merely present.</b>
         *
         * The completion write is the step that used to be skippable: it ran
         * inside the same try as the handler, on the request's own cancellation
         * token, with a catch that <i>deleted</i> the claim. So a phone that
         * changed network after its submit succeeded cancelled the token, the
         * read of the buffered body threw, the claim was removed, and the retry
         * ran the whole handler again — a second sitting, a second marking, the
         * entitlement spent twice, in the exact case the key exists for.
         *
         * Everything after the handler now runs on `CancellationToken.None`.
         * Asserting the stored state is what notices if that is ever undone:
         * a record stuck at `in-flight` after a request that plainly finished
         * means the caller going away can still erase what was decided.
         */
        var stored = await Keys.Find(RecordFor(key)).FirstOrDefaultAsync();

        Assert.NotNull(stored);
        Assert.Equal("done", stored["state"].AsString);
        Assert.Equal((int)HttpStatusCode.Created, stored["status"].AsInt32);
        Assert.Contains("sessionId", stored["body"].AsString);
    }

    /// <summary>
    /// The same key with a different body is a conflict, not a replay.
    ///
    /// Returning the first response would tell the caller their second,
    /// genuinely different, operation had succeeded — when it never ran at all.
    /// </summary>
    [SkippableFact]
    public async Task The_same_key_with_a_different_body_is_refused()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var key = Guid.NewGuid().ToString("n");

        var first = await SendAsync(
            client, HttpMethod.Post, "/api/v1/sessions", access,
            new { examVersionId = examId, mode = "single", module = "reading" }, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var different = await SendAsync(
            client, HttpMethod.Post, "/api/v1/sessions", access,
            new { examVersionId = examId, mode = "single", module = "listening" }, key);

        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", await CodeOf(different));
    }

    /// <summary>
    /// <b>A caller that goes away after the operation committed does not get to
    /// have it run again.</b>
    ///
    /// The middleware runs the handler inside a <c>try</c> whose <c>catch</c>
    /// released the claim, on the reasoning that an exception means the
    /// operation failed and the retry must be able to run. That is true for an
    /// exception thrown before anything committed, and exactly false for one
    /// thrown after — and the common shape of the second is a phone changing
    /// network while the response is being written. The transition had landed,
    /// <c>OperationCanceledException</c> unwound through the middleware, the
    /// claim was deleted, and the retry advanced the sitting a second time.
    ///
    /// This drives it through the real pipeline: submit with a key and abort
    /// the request as it is answered, then retry with the same key. The second
    /// call must not submit again.
    /// </summary>
    [SkippableFact]
    public async Task A_request_cancelled_after_it_committed_is_not_run_again_by_a_retry()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await SendAsync(
            client, HttpMethod.Post, "/api/v1/sessions", access,
            new { examVersionId = examId, mode = "single", module = "reading" },
            Guid.NewGuid().ToString("n"));

        started.EnsureSuccessStatusCode();
        var sessionId = (await BodyOf(started)).GetProperty("sessionId").GetString()!;

        var key = Guid.NewGuid().ToString("n");

        /*
         * <b>Cancelled once the commit is confirmed, not after a guessed
         * delay.</b> This used to race a hard-coded one-millisecond client
         * timeout against however long the real HTTP round trip took —
         * sometimes landing before the handler had committed anything at
         * all, sometimes long after the response had already been fully
         * received, and only sometimes in the window the test claimed to
         * cover. `app.CommitSignal` fires the instant the handler sets
         * `IdempotencyMiddleware.CommittedMarker` — the same moment the
         * production code itself treats as the point of no return — so the
         * cancellation below is guaranteed to land no earlier than that,
         * every run. → `ICommitSignal`
         */
        app.CommitSignal.Reset();

        using (var abort = new CancellationTokenSource())
        {
            var submit = new HttpRequestMessage(
                HttpMethod.Post, $"/api/v1/sessions/{sessionId}/submit");
            submit.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            submit.Headers.Add("Idempotency-Key", key);

            var sending = client.SendAsync(submit, abort.Token);

            var signalled = await Task.WhenAny(app.CommitSignal.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(app.CommitSignal.Task, signalled);

            await abort.CancelAsync();

            try
            {
                await sending;
            }
            catch (OperationCanceledException)
            {
                // Expected: this is the case under test.
            }
            catch (HttpRequestException)
            {
                // The connection was torn down mid-response. Also the case.
            }
        }

        /*
         * <b>The sitting was submitted exactly once, whichever way the race
         * fell.</b> Read the state rather than trusting the response we may
         * never have received — which is exactly the move the API now tells a
         * client to make when it reports `OPERATION_OUTCOME_UNKNOWN`.
         */
        var view = await SendAsync(
            client, HttpMethod.Get, $"/api/v1/sessions/{sessionId}", access, null, null);

        var status = (await BodyOf(view)).GetProperty("status").GetString();

        // Retry with the same key. It must not submit a second time, whether it
        // replays a stored response or refuses as undetermined.
        var retry = await SendAsync(
            client, HttpMethod.Post, $"/api/v1/sessions/{sessionId}/submit", access, null, key);

        Assert.True(
            retry.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"A retry after a cancelled submit answered {(int)retry.StatusCode}.");

        if (retry.StatusCode == HttpStatusCode.Conflict)
        {
            var code = await CodeOf(retry);

            Assert.True(
                code is "OPERATION_OUTCOME_UNKNOWN" or "SESSION_NOT_IN_PROGRESS"
                    or "SESSION_MOVED_ON" or "SESSION_EXPIRED",
                $"A retry after a cancelled submit answered {code}, which does not describe "
                + "either a completed operation or an undetermined one.");
        }

        // And whatever happened, the sitting is not still running: either the
        // first attempt landed, or the retry did — never both, and never
        // neither.
        var after = await SendAsync(
            client, HttpMethod.Get, $"/api/v1/sessions/{sessionId}", access, null, null);

        Assert.NotEqual("inprogress", (await BodyOf(after)).GetProperty("status").GetString());
        Assert.NotNull(status);
    }

    /// <summary>
    /// A claim left undetermined refuses a retry inside its lease, by name.
    ///
    /// <b>Driven by writing the state directly, because the window it models
    /// cannot be produced on demand.</b> What is being tested is the guard's
    /// answer to that state, not the odds of reaching it — and the answer has
    /// to be a refusal a client can act on rather than a silent re-run.
    /// </summary>
    [SkippableFact]
    public async Task An_undetermined_claim_refuses_a_retry_and_says_why()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        // A Full Test, because a single-skill sitting does not advance at all
        // — its refusal would arrive before the guard was ever consulted.
        var started = await SendAsync(
            client, HttpMethod.Post, "/api/v1/sessions", access,
            new { examVersionId = examId, mode = "full" },
            Guid.NewGuid().ToString("n"));

        var sessionId = (await BodyOf(started)).GetProperty("sessionId").GetString()!;
        var key = Guid.NewGuid().ToString("n");

        // A first attempt, so a record exists with this request's exact body hash.
        var first = await SendAsync(
            client, HttpMethod.Post, $"/api/v1/sessions/{sessionId}/advance", access, null, key);

        first.EnsureSuccessStatusCode();

        // Now put it in the state a cancelled-after-commit attempt leaves behind.
        var updated = await Keys.UpdateOneAsync(
            RecordFor(key),
            Builders<BsonDocument>.Update
                .Set("state", "unknown")
                .Unset("status")
                .Unset("body")
                .Set("leaseUntil", DateTime.UtcNow.AddMinutes(5)));

        Assert.Equal(1, updated.MatchedCount);

        var retry = await SendAsync(
            client, HttpMethod.Post, $"/api/v1/sessions/{sessionId}/advance", access, null, key);

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal("OPERATION_OUTCOME_UNKNOWN", await CodeOf(retry));

        // Actionable, not just correct: the client is told when to come back.
        Assert.NotNull(retry.Headers.RetryAfter);
    }

    /// <summary>
    /// And an undetermined claim whose lease has run out lets a retry through.
    ///
    /// <b>Without this the refusal above is a trap.</b> A key stuck in
    /// `unknown` for the twenty-four hours a record lives would leave the
    /// learner unable to advance or submit at all — a worse outcome than the
    /// double execution it was avoiding, and one nothing would recover from.
    /// </summary>
    [SkippableFact]
    public async Task An_undetermined_claim_past_its_lease_can_be_taken_over()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);
        app.Clock.Reset();

        var (client, access) = await SignInAsync();
        var examId = await FullExamIdAsync(client, access);

        var started = await SendAsync(
            client, HttpMethod.Post, "/api/v1/sessions", access,
            new { examVersionId = examId, mode = "full" },
            Guid.NewGuid().ToString("n"));

        var sessionId = (await BodyOf(started)).GetProperty("sessionId").GetString()!;
        var key = Guid.NewGuid().ToString("n");

        var first = await SendAsync(
            client, HttpMethod.Post, $"/api/v1/sessions/{sessionId}/advance", access, null, key);

        first.EnsureSuccessStatusCode();

        await Keys.UpdateOneAsync(
            RecordFor(key),
            Builders<BsonDocument>.Update
                .Set("state", "unknown")
                .Unset("status")
                .Unset("body")
                // Already expired.
                .Set("leaseUntil", DateTime.UtcNow.AddMinutes(-1)));

        var retry = await SendAsync(
            client, HttpMethod.Post, $"/api/v1/sessions/{sessionId}/advance", access, null, key);

        // It ran — which for a Full Test on its second section is an ordinary
        // advance. The point is that it was not refused for ever.
        Assert.NotEqual(HttpStatusCode.Conflict, retry.StatusCode);
    }


    /// <summary>
    /// <b>The write half of the rule, driven deterministically.</b>
    ///
    /// The tests above put a claim into <c>unknown</c> by hand and check what
    /// the guard does when it reads one. That leaves the more important half
    /// unproven: that a handler which commits and is then cancelled <i>produces</i>
    /// that state instead of releasing the claim.
    ///
    /// It cannot be produced on demand through HTTP — the window is
    /// microseconds wide and losing the race makes the test pass for the wrong
    /// reason. So the middleware is driven directly, with a handler that does
    /// exactly what the failure does: commits, then throws
    /// <c>OperationCanceledException</c> because the caller went away.
    ///
    /// <b>Removing the rule turns this red, which is the property a test has to
    /// have to be worth writing.</b>
    /// </summary>
    [SkippableFact]
    public async Task A_handler_cancelled_after_committing_leaves_the_claim_undetermined()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);

        var key = Guid.NewGuid().ToString("n");
        var db = app.Services.GetRequiredService<IMongoDatabase>();
        var clock = app.Services.GetRequiredService<Vni.Ielts.Domain.Common.IClock>();

        var aborted = new CancellationTokenSource();

        var middleware = new Vni.Ielts.Api.Common.IdempotencyMiddleware(
            _ =>
            {
                // The caller has gone. Everything the handler was going to do
                // has already been done.
                aborted.Cancel();
                throw new OperationCanceledException(aborted.Token);
            },
            db,
            clock,
            app.Services.GetRequiredService<ILogger<Vni.Ielts.Api.Common.IdempotencyMiddleware>>());

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/sessions/sit-cancelled/submit";
        context.Request.Headers["Idempotency-Key"] = key;
        context.Request.Body = new MemoryStream();
        context.Response.Body = new MemoryStream();
        context.RequestAborted = aborted.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(context));

        var record = await Keys.Find(RecordFor(key)).FirstOrDefaultAsync();

        Assert.True(
            record is not null,
            "The claim was deleted. A retry with this key would run an operation that may "
            + "already have taken effect — which for a submit means handing in one paper twice.");

        Assert.Equal("unknown", record!.GetValue("state").AsString);
    }

    /// <summary>
    /// And a handler that fails <i>before</i> committing still releases its claim.
    ///
    /// <b>The pair is the rule.</b> Marking every failure undetermined would
    /// make a transient 500 permanent for the life of the key: the client could
    /// retry for twenty-four hours and be refused each time without anything
    /// ever running. The retry is the entire point of an idempotency key, and
    /// this is what keeps it working.
    /// </summary>
    [SkippableFact]
    public async Task A_handler_that_fails_without_committing_still_releases_its_claim()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);

        var key = Guid.NewGuid().ToString("n");
        var db = app.Services.GetRequiredService<IMongoDatabase>();
        var clock = app.Services.GetRequiredService<Vni.Ielts.Domain.Common.IClock>();

        var middleware = new Vni.Ielts.Api.Common.IdempotencyMiddleware(
            _ => throw new InvalidOperationException("the handler fell over"),
            db,
            clock,
            app.Services.GetRequiredService<ILogger<Vni.Ielts.Api.Common.IdempotencyMiddleware>>());

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/sessions/sit-failed/submit";
        context.Request.Headers["Idempotency-Key"] = key;
        context.Request.Body = new MemoryStream();
        context.Response.Body = new MemoryStream();

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Null(await Keys.Find(RecordFor(key)).FirstOrDefaultAsync());
    }


    /// <summary>
    /// A handler that outlives its lease keeps its claim, because it says so.
    ///
    /// <b>Without a heartbeat the five-minute lease is a bet on how long the
    /// slowest handler takes, and the losing side of that bet is a second paid
    /// evaluation.</b> The day an evaluator is wired, `/submit` becomes two
    /// sequential model calls plus an ASR pass over up to fourteen minutes of
    /// audio, and nobody knows yet whether that is three minutes or twelve.
    ///
    /// Driven with a short heartbeat and a handler that takes longer than one
    /// interval, so the renewal has to have happened for the assertion to hold.
    /// </summary>
    [SkippableFact]
    public async Task A_running_handler_pushes_its_lease_forward()
    {
        Skip.IfNot(ExamAppFactory.MongoAvailable, ExamAppFactory.SkipReason);

        var key = Guid.NewGuid().ToString("n");
        var db = app.Services.GetRequiredService<IMongoDatabase>();
        var clock = app.Services.GetRequiredService<Vni.Ielts.Domain.Common.IClock>();

        // A five-second lease, so one heartbeat is a second rather than a
        // minute. The interval is derived from the lease, so shortening one
        // shortens the other and nothing else about the behaviour changes.
        var lease = TimeSpan.FromSeconds(5);

        async Task<DateTime?> LeaseNowAsync() =>
            (await db.GetCollection<BsonDocument>("idempotency_keys")
                .Find(Builders<BsonDocument>.Filter.Regex("_id", new BsonRegularExpression($":{key}$")))
                .FirstOrDefaultAsync())
            ?.GetValue("leaseUntil", BsonNull.Value) is BsonDateTime at
                ? at.ToUniversalTime()
                : null;

        DateTime? first = null;
        DateTime? renewed = null;

        var middleware = new Vni.Ielts.Api.Common.IdempotencyMiddleware(
            async _ =>
            {
                first = await LeaseNowAsync();

                // Outlive one heartbeat interval and watch the field move. Read
                // from what the heartbeat writes rather than from a timer, so a
                // slow machine makes this take longer rather than fail.
                for (var i = 0; i < 60 && renewed is null; i++)
                {
                    await Task.Delay(100);

                    var now = await LeaseNowAsync();
                    if (now is { } value && first is { } start && value > start) renewed = value;
                }
            },
            db,
            clock,
            app.Services.GetRequiredService<ILogger<Vni.Ielts.Api.Common.IdempotencyMiddleware>>(),
            lease);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/sessions/sit-slow/submit";
        context.Request.Headers["Idempotency-Key"] = key;
        context.Request.Body = new MemoryStream();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.NotNull(first);

        /*
         * <b>The lease moved while the handler was still inside it.</b>
         *
         * Asserting only that it was "in the future" would pass with no
         * heartbeat at all — the initial claim already sets it a whole lease
         * ahead. What has to be true is that it <i>advanced</i>, because that
         * is the only thing that stops a takeover from firing into a handler
         * that is simply slow rather than dead.
         */
        Assert.True(
            renewed is not null,
            $"The lease never moved past {first:O} while the handler ran for six seconds "
            + "against a five-second lease. Without a heartbeat, a handler slower than its "
            + "lease has its claim taken over and its operation run a second time.");
    }

}

/// <summary>
/// A deterministic stand-in for <c>await Task.Delay(1)</c>.
///
/// <b>F0.2.</b> Exposed on <see cref="IdempotencyAppFactory"/> as a mutable
/// singleton the same way <c>ExamAppFactory.Clock</c> is: each test that needs
/// it calls <see cref="Reset"/> before firing the request it will race against
/// the signal, so an earlier test's completed signal can never be mistaken for
/// this one's.
/// </summary>
public sealed class TestCommitSignal : ICommitSignal
{
    private TaskCompletionSource _source = New();

    /// <summary>Completes the instant a guarded handler commits.</summary>
    public Task Task => _source.Task;

    public void Reset() => _source = New();

    public void Signal(HttpContext context) => _source.TrySetResult();

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Mints a fresh account on every call, instead of the fixed
/// <c>stub-google-subject</c> the ordinary Development stub returns.
///
/// <b>F0.2 — why this exists instead of changing the ordinary stub.</b>
/// <c>SsoFlowTests.A_second_sign_in_reuses_the_same_account</c> depends on the
/// production stub's fixed identity — that is the exact property under test
/// there ("the identity is keyed on the provider's subject, so the second
/// visit must not produce a second learner"). Changing it globally would fix
/// this suite's isolation by breaking that one's assertion.
///
/// <see cref="IdempotencyAppFactory"/> is a wholly separate DI container from
/// <c>SsoAppFactory</c>, so replacing the provider only here reaches nothing
/// else: <c>ExamRunContractTests</c> and <c>FullSittingJourneyTests</c> keep
/// the fixed-identity stub they were built against, unchanged.
/// </summary>
internal sealed class PerCallStubIdentityProvider(
    Microsoft.Extensions.Options.IOptions<Vni.Ielts.Infrastructure.Security.Sso.SsoOptions> options)
    : IExternalIdentityProvider
{
    public IdentityProvider Provider => IdentityProvider.Google;

    public bool AssertsEmailVerification => true;

    public Task<Uri> BuildAuthorizationUrlAsync(AuthorizationRequest request, CancellationToken ct)
    {
        var redirectUri = options.Value.Google.RedirectUri;
        var separator = redirectUri.Contains('?') ? '&' : '?';

        return System.Threading.Tasks.Task.FromResult(new Uri(
            $"{redirectUri}{separator}code=stub-code"
            + $"&state={Uri.EscapeDataString(request.State)}"));
    }

    public Task<Result<ExternalIdentity>> ExchangeCodeAsync(
        string code, string codeVerifier, string nonce, CancellationToken ct)
    {
        var unique = Guid.NewGuid().ToString("n");

        return System.Threading.Tasks.Task.FromResult<Result<ExternalIdentity>>(
            new ExternalIdentity(
                IdentityProvider.Google,
                Subject: $"stub-subject-{unique}",
                Email: $"stub-{unique}@example.com",
                EmailVerified: true,
                DisplayName: "Học viên thử"));
    }
}

/// <summary>
/// <see cref="ExamAppFactory"/>, with two overrides <c>IdempotencyContractTests</c>
/// needs and nothing else does:
///
/// <list type="bullet">
/// <item>a fresh account per sign-in (<see cref="PerCallStubIdentityProvider"/>),
/// so its thirteen tests do not share a learner — and with it, do not share
/// that learner's open sittings, which was the source of the run-order
/// dependence F0.2 exists to remove;</item>
/// <item>a commit signal (<see cref="TestCommitSignal"/>) so the one test
/// modelling a cancellation-after-commit can wait for the real moment instead
/// of guessing at one with a timeout.</item>
/// </list>
///
/// A separate <c>WebApplicationFactory</c> instance and DI container from
/// every other suite's — inheriting <c>ExamAppFactory</c> for its Mongo,
/// clock and stub-provider *wiring pattern* only. Nothing here is visible to
/// <c>ExamRunContractTests</c> or <c>FullSittingJourneyTests</c>.
/// </summary>
public sealed class IdempotencyAppFactory : ExamAppFactory
{
    public TestCommitSignal CommitSignal { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IExternalIdentityProvider>();
            services.AddSingleton<IExternalIdentityProvider, PerCallStubIdentityProvider>();

            services.RemoveAll<ICommitSignal>();
            services.AddSingleton<ICommitSignal>(CommitSignal);
        });
    }
}
