using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// F4.2 — a secret sent to this API does not come back out of it.
///
/// <b>Why this is a test and not a review.</b> Telemetry and logging are
/// exactly the places a secret leaks without anyone deciding to leak it: a
/// driver puts the failing command in an exception message, a framework logs
/// the request it could not bind, a span captures the statement it executed.
/// Every one of those is a default somewhere, and every one is invisible
/// until the day a log ends up somewhere it should not be. Reading the code
/// once cannot hold that; a test run on every change can.
///
/// <b>The payload is deliberately shaped like the things that matter.</b> A
/// real password, an OpenAI-style key, a bearer token and a connection string
/// — three of which appear nowhere in this product yet and will the moment an
/// AI provider is wired. → CLAUDE.md rule 6
///
/// Three sinks are checked, because a secret escaping any one of them is a
/// breach: the HTTP response, everything written through `ILogger`, and every
/// span name and tag.
/// </summary>
public sealed class RedactionTests
{
    /*
     * <b>Two groups, because they are held to different rules — and getting
     * that wrong is how the first version of this test failed for the wrong
     * reason.</b>
     *
     * CREDENTIALS are supplied to authenticate. No correct API ever echoes
     * one, so they must appear in NO sink at all, the response included.
     *
     * PLANTED CONTENT is secret-SHAPED text submitted as ordinary profile
     * data. The API legitimately echoes a display name back to the caller who
     * just set it, so the response is not held against these — but logs and
     * spans still are, because those leave the process and are read by people
     * the content was never for.
     *
     * The two groups share no value, so a failure names the right rule.
     */
    private const string Password = "Tr0ub4dor&3-horse-battery";
    private const string BearerToken = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";

    private const string ApiKeyInContent = "sk-proj-AAAABBBBCCCCDDDDEEEEFFFFGGGGHHHH";
    private const string ConnectionStringInContent = "mongodb://admin:hunter2@db.internal:27017";

    /// <summary>Never echoed by any correct API.</summary>
    private static readonly string[] Credentials = [Password, BearerToken];

    /// <summary>Must never reach a log or a span, wherever they came from.</summary>
    private static readonly string[] Secrets =
    [
        Password,
        BearerToken,
        ApiKeyInContent,
        ConnectionStringInContent,
    ];

    [SkippableFact]
    public async Task A_secret_in_a_request_reaches_no_response_no_log_and_no_span()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var logs = new RecordingLoggerProvider();

        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
            host.UseSetting(
                "Mongo:ConnectionString", "mongodb://localhost:27018/?directConnection=true");
            host.UseSetting("Mongo:Database", $"vni_ielts_redaction_{Guid.NewGuid():n}");
            host.UseSetting("Jwt:SigningKey", new string('k', 48));
            host.UseSetting("Sso:EnableStubProvider", "true");
            host.UseSetting("Sso:ClientBaseUrl", "http://localhost:5173");

            host.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs));
        });

        var client = app.CreateClient();

        // Every secret is carried on a request that really is processed: the
        // password through the field that takes one, the rest through fields
        // that accept free text, so each one genuinely passes through binding,
        // validation, the database driver and the error path.
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new
            {
                email = $"redaction-{Guid.NewGuid():n}@example.com",
                password = Password,
                displayName = $"{ApiKeyInContent} {ConnectionStringInContent}",
            }),
        };
        request.Headers.TryAddWithoutValidation("Authorization", BearerToken);
        // Registration is behind the idempotency gate; without a key the
        // request is refused before any of these secrets reaches the database
        // or the driver, and the test would pass without exercising the path
        // it exists to check. → the same trap this file's other test documents
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Give the deferred sinks a moment to flush.
        await Task.Delay(200);

        /*
         * <b>The response body is held to a narrower rule than the logs, and
         * the difference is deliberate.</b>
         *
         * A first version of this test asserted that NO secret came back in
         * the body, and it failed — on the API key, because that value was
         * planted in `displayName` and the API echoes a newly created
         * account's display name back to the caller. That is not a leak: it
         * is the caller's own submitted profile field, returned to the caller
         * who just sent it. Asserting otherwise would have meant "banning" a
         * string from user content, which no API can do and none should try.
         *
         * What must never come back is a CREDENTIAL — something the caller
         * supplied to authenticate, not to store. Those are never echoed by
         * any correct API, so they are what the body is checked for.
         *
         * Logs and spans keep the strict rule below: they leave the process,
         * they are read by operators and shipped to collectors, and user
         * content has no business in either.
         */
        foreach (var credential in Credentials)
        {
            Assert.False(
                body.Contains(credential, StringComparison.Ordinal),
                $"The HTTP response body contains {Describe(credential)}. A credential is "
                    + "supplied to authenticate, never to be stored or echoed.");
        }

        foreach (var secret in Secrets)
        {
            var leakedLog = logs.Messages.FirstOrDefault(
                m => m.Contains(secret, StringComparison.Ordinal));
            Assert.True(
                leakedLog is null,
                $"A log line contains a secret ({Describe(secret)}):\n{leakedLog}");

            foreach (var span in spans)
            {
                Assert.False(
                    span.DisplayName.Contains(secret, StringComparison.Ordinal),
                    $"A span NAME contains a secret ({Describe(secret)}): {span.DisplayName}");

                foreach (var (key, value) in span.TagObjects)
                {
                    Assert.False(
                        value?.ToString()?.Contains(secret, StringComparison.Ordinal) == true,
                        $"Span tag '{key}' on '{span.DisplayName}' contains a secret "
                            + $"({Describe(secret)}). → Telemetry.cs, MongoContext.cs");
                }
            }
        }
    }

    [SkippableFact]
    public async Task No_database_span_carries_the_command_it_executed()
    {
        // <b>The specific default this guards.</b> The MongoDB instrumentation
        // can capture command text, and a Mongo command embeds its filter
        // values — an email, a session id, a learner's answer. `MongoContext`
        // sets `CaptureCommandText = false` explicitly; this is what notices
        // if that is ever removed or if a future driver flips the default.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.Contains("MongoDB", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var marker = $"marker-{Guid.NewGuid():n}";

        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
            host.UseSetting(
                "Mongo:ConnectionString", "mongodb://localhost:27018/?directConnection=true");
            host.UseSetting("Mongo:Database", $"vni_ielts_redaction_{Guid.NewGuid():n}");
            host.UseSetting("Jwt:SigningKey", new string('k', 48));
            host.UseSetting("Sso:EnableStubProvider", "true");
            host.UseSetting("Sso:ClientBaseUrl", "http://localhost:5173");
        });

        var register = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new
            {
                email = $"{marker}@example.com",
                password = "Password123!Aa",
                displayName = marker,
            }),
        };
        // <b>Without this the test proves nothing.</b> Registration is behind
        // the idempotency gate, so a request with no `Idempotency-Key` is
        // rejected with 400 IDEMPOTENCY_KEY_MISSING before it ever reaches the
        // database — and a redaction test that never writes the value it is
        // hunting for passes for the wrong reason. Found by turning command
        // capture ON and watching this test stay green.
        register.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));

        var registered = await app.CreateClient().SendAsync(register);

        Assert.True(
            registered.IsSuccessStatusCode,
            $"the account must actually be created for this test to mean anything; "
                + $"got {(int)registered.StatusCode}: {await registered.Content.ReadAsStringAsync()}");

        await Task.Delay(200);

        // The write really happened, so a span describing it must exist.
        Assert.Contains(
            spans,
            s => s.DisplayName.Contains("users", StringComparison.Ordinal)
                || s.TagObjects.Any(t => t.Value?.ToString()?.Contains("users", StringComparison.Ordinal) == true));

        foreach (var span in spans)
        {
            foreach (var (key, value) in span.TagObjects)
            {
                Assert.False(
                    value?.ToString()?.Contains(marker, StringComparison.Ordinal) == true,
                    $"MongoDB span tag '{key}' contains the value that was written. "
                        + "Command capture must stay off — a command carries filter values, "
                        + "and a span leaves this machine. → MongoContext.cs");
            }
        }
    }

    private static string Describe(string secret) =>
        secret.StartsWith("sk-", StringComparison.Ordinal) ? "an API key"
        : secret.StartsWith("Bearer ", StringComparison.Ordinal) ? "a bearer token"
        : secret.StartsWith("mongodb://", StringComparison.Ordinal) ? "a connection string"
        : "a password";

    /// <summary>Captures everything written through ILogger, for inspection.</summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Recorder(Messages);

        public void Dispose() { }

        private sealed class Recorder(ConcurrentBag<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                // The formatted message AND the exception: an exception's own
                // message is the likeliest carrier, because it comes from a
                // driver rather than from anything written here.
                sink.Add(formatter(state, exception));
                if (exception is not null) sink.Add(exception.ToString());
            }
        }
    }
}
