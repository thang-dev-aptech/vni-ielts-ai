using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Security;
using Vni.Ielts.Infrastructure.Security.Sso;
using Vni.Ielts.Infrastructure.Storage;

namespace Vni.Ielts.Api.Common;

/// <summary>
/// Refuses to start on a configuration that cannot work.
///
/// <b>Written 2026-08-28. Before it, one setting was checked and the rest were
/// discovered at runtime by a user.</b>
///
/// The signing key had a guard because somebody had been bitten by it. Nothing
/// else did — so a wrong `Jwt:Issuer` surfaced as every sign-in failing with a
/// 401 nobody could explain, an empty `Cors:Origins` as a web app whose every
/// request failed in the browser with no server-side trace at all, a Mongo
/// connection string pointing at a standalone node as token deduction being
/// silently non-atomic, and a missing `Sso:ClientBaseUrl` as a callback that
/// redirected to nowhere.
///
/// Each of those is a production incident that presents as a user problem.
/// Refusing to boot is a deployment failure, which is loud, immediate, and
/// costs one rollback.
///
/// ── Two rules this follows ────────────────────────────────────────────────
///
/// <b>Every problem at once, not the first one.</b> A validator that throws on
/// the first fault turns fixing a fresh environment into as many deploy cycles
/// as there are mistakes. They are collected and reported together.
///
/// <b>Development is checked too, and told rather than refused.</b> A developer
/// whose CORS list is empty has the same broken app as production; the
/// difference is that they can fix it in ten seconds and should not have to
/// diagnose it first. So Development warns where production throws — except
/// where the setting is a secret, which no environment may invent.
/// </summary>
public static class StartupConfiguration
{
    public static void ValidateOrThrow(WebApplicationBuilder builder)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        var development = builder.Environment.IsDevelopment();

        void Require(string problem)
        {
            if (development) warnings.Add(problem);
            else problems.Add(problem);
        }

        // ── Mongo ─────────────────────────────────────────────────────────
        var mongo = builder.Configuration.GetSection(MongoOptions.SectionName).Get<MongoOptions>()
            ?? new MongoOptions();

        if (string.IsNullOrWhiteSpace(mongo.ConnectionString))
            problems.Add("Mongo:ConnectionString is empty. Nothing can be read or written.");

        if (string.IsNullOrWhiteSpace(mongo.Database))
            problems.Add("Mongo:Database is empty.");

        /*
         * <b>A standalone node is the trap this product has already fallen
         * into.</b> Everything works on one — register, sign in, sit an exam —
         * until two writes have to be atomic, and then token deduction is
         * silently non-atomic under exactly the retry pressure mobile clients
         * generate by design. The failure never appears in development.
         *
         * A connection string that names neither a replica set nor a
         * `mongodb+srv` cluster is almost certainly a standalone. Warned rather
         * than refused, because a valid deployment can express it in ways this
         * cannot parse — and `InitialiseInfrastructureAsync` already refuses to
         * start against a node that genuinely cannot do transactions, which is
         * the check that decides. → ADR-0011, threat `T22`, `R15`
         */
        if (!mongo.ConnectionString.Contains("replicaSet", StringComparison.OrdinalIgnoreCase)
            && !mongo.ConnectionString.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase)
            && !mongo.ConnectionString.Contains("directConnection", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                "Mongo:ConnectionString names no replica set. Transactions are unavailable on a "
                + "standalone node, and token deduction must be atomic. → ADR-0011");
        }

        // ── JWT ───────────────────────────────────────────────────────────
        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwt.Issuer))
            problems.Add("Jwt:Issuer is empty. Every token this process issues would be rejected by it.");

        if (string.IsNullOrWhiteSpace(jwt.Audience))
            problems.Add("Jwt:Audience is empty.");

        if (jwt.AccessTokenMinutes <= 0)
        {
            problems.Add(
                $"Jwt:AccessTokenMinutes is {jwt.AccessTokenMinutes}. A token that expires on "
                + "issue signs everybody out immediately.");
        }

        /*
         * <b>An access token that outlives a section is the bug the refresh
         * timer was written for, arriving from the other direction.</b> Long
         * lifetimes are not merely lax: an access token cannot be revoked, so
         * this is exactly the window between a suspension taking effect and the
         * suspended account losing access.
         */
        if (jwt.AccessTokenMinutes > 60)
        {
            warnings.Add(
                $"Jwt:AccessTokenMinutes is {jwt.AccessTokenMinutes}. An access token cannot be "
                + "revoked, so this is how long a suspended account keeps working.");
        }

        // ── CORS ──────────────────────────────────────────────────────────
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];

        if (origins.Length == 0)
        {
            /*
             * <b>The failure this produces has no server-side trace at all.</b>
             * The browser refuses the response after the API has already
             * answered 200, so the log says the request succeeded and the user
             * says the app is broken. It presents as a client bug and is not.
             */
            Require(
                "Cors:Origins is empty. Every browser request from the web app and the CMS will "
                + "be refused by the browser after this API has already answered — which leaves "
                + "no trace on this side at all.");
        }

        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsed))
            {
                problems.Add($"Cors:Origins contains '{origin}', which is not an absolute URL.");
                continue;
            }

            if (parsed.AbsolutePath != "/")
            {
                problems.Add(
                    $"Cors:Origins contains '{origin}'. An origin is scheme, host and port — a "
                    + "path is never matched and its presence means this entry matches nothing.");
            }

            if (!development && parsed.Scheme != Uri.UriSchemeHttps)
            {
                problems.Add(
                    $"Cors:Origins contains '{origin}' over plain HTTP outside Development. "
                    + "A session token travelling to that origin is readable in transit.");
            }
        }

        // ── SSO ───────────────────────────────────────────────────────────
        var sso = builder.Configuration.GetSection(SsoOptions.SectionName).Get<SsoOptions>()
            ?? new SsoOptions();

        var googleConfigured =
            !string.IsNullOrWhiteSpace(sso.Google.ClientId)
            && !string.IsNullOrWhiteSpace(sso.Google.ClientSecret);

        if (googleConfigured)
        {
            /*
             * <b>The provider sends the browser back to the API, and the API
             * forwards it to the web app.</b> With no client base URL the
             * second hop goes nowhere: the learner completes a Google sign-in
             * and lands on a blank page. → ADR-0014
             */
            if (string.IsNullOrWhiteSpace(sso.ClientBaseUrl))
            {
                problems.Add(
                    "Sso:ClientBaseUrl is empty while Google is configured. A learner who "
                    + "completes a social sign-in would be redirected to nowhere. → ADR-0014");
            }

            if (string.IsNullOrWhiteSpace(sso.Google.RedirectUri))
            {
                problems.Add(
                    "Sso:Google:RedirectUri is empty while Google is configured. It is the "
                    + "API's own callback address and must match the one registered with "
                    + "Google exactly.");
            }

            /*
             * <b>F2.5 — both hops of the same redirect, not just the first.</b>
             * The empty-CORS bug this file exists to prevent has a sibling here:
             * Google delivers the authorization code to `RedirectUri`, and
             * this API then sends the browser on to `ClientBaseUrl` carrying
             * a session. Either leg over plain HTTP puts an authorization
             * code or a session token on the wire in the clear.
             */
            if (!development
                && sso.ClientBaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"Sso:ClientBaseUrl is plain HTTP ({sso.ClientBaseUrl}) while Google is "
                    + "configured. A learner completing a social sign-in is redirected there "
                    + "carrying a session, readable in transit.");
            }

            if (!development
                && sso.Google.RedirectUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"Sso:Google:RedirectUri is plain HTTP ({sso.Google.RedirectUri}) while "
                    + "Google is configured. Google delivers the authorization code to this "
                    + "address, readable in transit.");
            }
        }

        /*
         * <b>The stub provider issues a session for anybody who asks.</b> It
         * exists so the SSO flow can be exercised without credentials, and it
         * is an authentication bypass anywhere it is not a test.
         */
        if (sso.EnableStubProvider && !development)
        {
            problems.Add(
                "Sso:EnableStubProvider is on outside Development. It issues a session to any "
                + "caller and is an authentication bypass.");
        }

        // ── Object storage ────────────────────────────────────────────────
        var storage = builder.Configuration.GetSection(ObjectStorageOptions.SectionName)
            .Get<ObjectStorageOptions>() ?? new ObjectStorageOptions();

        if (!storage.IsConfigured && !development)
        {
            /*
             * <b>Without it a production process has no media at all.</b> The
             * fixtures reader is registered only in Development, so Listening
             * would play nothing, Writing's charts would not load, and every
             * dictation exercise would be silent — and each of those looks like
             * a broken player rather than a missing adapter.
             */
            problems.Add(
                "ObjectStorage is not configured (ServiceUrl, AccessKey, SecretKey). Outside "
                + "Development there is no other source of exam audio, exam images or dictation "
                + "audio, and a missing adapter presents as a broken player.");
        }

        if (storage.IsConfigured
            && !development
            && storage.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                $"ObjectStorage:ServiceUrl is plain HTTP ({storage.ServiceUrl}). Credentials and "
                + "learner audio travel over it.");
        }

        // ── Email ─────────────────────────────────────────────────────────
        var email = builder.Configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>()
            ?? new SmtpOptions();

        if (!email.IsConfigured && !development)
        {
            /*
             * <b>Without it, registration reports success and nobody ever
             * receives anything.</b> The only other sender writes the link to
             * the server log — which is right in Development and is an outright
             * lie in production: an account is created, the API says a
             * verification mail was sent, and the address can never be
             * verified. Password reset is worse: the learner is locked out and
             * the recovery path silently does nothing.
             */
            problems.Add(
                "Email is not configured (Host, FromAddress, ClientBaseUrl). Outside Development "
                + "the only other sender writes verification and password-reset links to the "
                + "server log, so a learner would never receive either.");
        }

        if (email.IsConfigured && email.Port == 25)
        {
            problems.Add(
                "Email:Port is 25. That is unauthenticated server-to-server relay with no "
                + "expectation of encryption — a password-reset link would travel in the clear. "
                + "Use 587 for STARTTLS or 465 for implicit TLS.");
        }

        if (email.IsConfigured
            && !development
            && email.ClientBaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                $"Email:ClientBaseUrl is plain HTTP ({email.ClientBaseUrl}). Every verification "
                + "and password-reset link this process sends would point at an address a "
                + "network observer can read and rewrite.");
        }

        // ── Shutdown timeout ──────────────────────────────────────────────
        /*
         * <b>F2.5 — neither bad value used to be caught here.</b>
         * `Api:ShutdownTimeoutSeconds` flows straight into
         * `TimeSpan.FromSeconds(...)` and then `HostOptions.ShutdownTimeout`
         * (→ `Program.cs`, F2.3), and the two ways it can be wrong fail
         * differently, both worse than a startup refusal. A negative value
         * throws `ArgumentOutOfRangeException` out of
         * `CancellationTokenSource.CancelAfter` — confirmed by reproducing it
         * — but only once shutdown actually begins, the one moment a
         * deployment can least afford a surprise. Zero does not throw at
         * all: it is a legal `TimeSpan`, so the host boots and runs
         * normally, and only gives an in-flight request or a claimed job no
         * grace whatsoever the day it matters — silently undoing F2.3 with
         * no error anywhere. Checked here, unconditionally — this is not a
         * convenience trade-off Development gets to skip the way an empty
         * CORS list is; a nonsensical timeout is exactly as wrong in both.
         */
        var apiShutdownTimeoutSeconds = builder.Configuration.GetValue("Api:ShutdownTimeoutSeconds", 30);
        if (apiShutdownTimeoutSeconds <= 0)
        {
            problems.Add(
                $"Api:ShutdownTimeoutSeconds is {apiShutdownTimeoutSeconds}. A negative value "
                + "crashes the host during shutdown instead of at startup; zero boots fine but "
                + "silently gives an in-flight request no grace at all.");
        }

        // ── Transport ─────────────────────────────────────────────────────
        if (!development
            && builder.Configuration.GetValue("Https:Require", true)
            && origins.Length > 0
            && origins.All(o => o.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add(
                "Every configured origin is plain HTTP outside Development. Set Https:Require "
                + "to false only if something in front of this process terminates TLS.");
        }

        foreach (var warning in warnings)
            Console.WriteLine($"[config] {warning}");

        if (problems.Count == 0) return;

        /*
         * <b>All of them, in one message.</b> Throwing on the first turns
         * bringing up a fresh environment into as many deploy cycles as there
         * are mistakes, and the person doing it has no way to know how many
         * are left.
         */
        throw new InvalidOperationException(
            $"The configuration this process was given cannot work ({problems.Count} problem"
            + $"{(problems.Count == 1 ? "" : "s")}):\n\n"
            + string.Join("\n", problems.Select(p => $"  · {p}"))
            + "\n\nRefusing to start. A misconfiguration that surfaces as a user problem is a "
            + "production incident; one that refuses to boot is a deployment failure, and that "
            + "is the cheaper of the two.");
    }
}
