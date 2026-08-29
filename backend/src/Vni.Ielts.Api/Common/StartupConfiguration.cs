using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Configuration;
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
            /*
             * <b>FS0.4 — the URL is redacted on the way into this message.</b>
             * It used to be interpolated whole, and an S3-compatible service
             * URL is allowed to carry userinfo (`https://key:secret@host`),
             * which several clients accept. So the warning written by the code
             * whose job is to make misconfiguration safe was itself the place
             * a credential reached the startup log. → `SecretRedaction`
             */
            warnings.Add(
                $"ObjectStorage:ServiceUrl is plain HTTP ({SecretRedaction.Url(storage.ServiceUrl)}). "
                + "Credentials and learner audio travel over it.");
        }

        Uri? storageUrl = null;

        if (storage.IsConfigured)
        {
            if (Uri.TryCreate(storage.ServiceUrl, UriKind.Absolute, out var parsedStorageUrl))
            {
                storageUrl = parsedStorageUrl;
            }
            else
            {
                problems.Add(
                    "ObjectStorage:ServiceUrl is not an absolute URL. The SDK's failure for this "
                    + "is a signature or DNS error at the first object read, which reads as a "
                    + "broken player rather than a typo in a setting.");
            }
        }

        /*
         * <b>A credential in the endpoint is a credential in every log line
         * that names the endpoint.</b> `https://key:secret@host` is accepted by
         * enough S3 clients that somebody will try it, and it defeats every
         * redaction rule that treats a URL as a non-secret. There are two
         * settings for exactly these two values; refusing is what makes them
         * the only place they can be.
         */
        if (storageUrl is not null && !string.IsNullOrEmpty(storageUrl.UserInfo))
        {
            problems.Add(
                "ObjectStorage:ServiceUrl carries credentials in the URL itself. Put them in "
                + "ObjectStorage:AccessKey and ObjectStorage:SecretKey — a credential inside an "
                + "endpoint ends up in every log line, span and error message that names the "
                + "endpoint.");
        }

        /*
         * <b>R2 is a configuration profile, not a second adapter.</b> The one
         * thing it needs that AWS does not is the region `auto`, and the wrong
         * value is the one you get by leaving the default alone — so it fails
         * at the first upload with an SDK signature error that names nothing
         * useful. → plan decision 4, https://developers.cloudflare.com/r2/api/s3/api/
         */
        if (storage.IsConfigured
            && storage.IsCloudflareR2
            && !string.Equals(storage.Region, "auto", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                $"ObjectStorage:ServiceUrl is a Cloudflare R2 endpoint but ObjectStorage:Region is "
                + $"'{storage.Region}'. R2 signs against the region 'auto' and rejects anything "
                + "else with a signature error that names neither setting.");
        }

        if (storage.IsConfigured
            && storage.IsCloudflareR2
            && storage.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                "ObjectStorage:ServiceUrl is a Cloudflare R2 endpoint over plain HTTP. R2 is "
                + "reachable over TLS only, so this cannot work — and it would carry a learner's "
                + "voice if it did.");
        }

        /*
         * <b>Learner voice may not share a bucket with authored content, and
         * this is the check that keeps the retention layout honest.</b> The
         * exam-asset and package buckets are versioned on purpose, because an
         * operator overwriting a good package is how authored content is lost.
         * A version history is the opposite of what a recording needs: under
         * PDPL a deleted recording has to actually be gone, and a version
         * outlives the deletion it was supposed to honour.
         * → `infra/docker/compose.yaml`, `privacy-vietnam-pdpl.md`
         */
        if (!string.IsNullOrWhiteSpace(storage.SpeakingRecordingsBucket)
            && (storage.SpeakingRecordingsBucket == storage.ExamAssetsBucket))
        {
            problems.Add(
                "ObjectStorage:SpeakingRecordingsBucket is the same bucket as "
                + "ExamAssetsBucket. That bucket is versioned, because authored content is "
                + "recovered by rolling back — and a version history of a learner's voice "
                + "survives the deletion that PDPL requires to be final.");
        }

        /*
         * <b>Two seams, warned rather than refused, and the distinction is
         * deliberate.</b> Nothing writes a Speaking recording yet (FS5), so
         * refusing to boot on an unset bucket would be refusing on a setting
         * nothing uses. What must not happen is a *default* — `vni-audio-90d`
         * names a ninety-day retention in the bucket name, and choosing it here
         * would decide in a property initialiser how long a minor's voice is
         * kept. → `G-11`
         *
         * FS5 turns the first of these into a problem when the upload path
         * lands, because at that point an unset bucket is a recording with
         * nowhere to go.
         */
        if (storage.IsConfigured
            && !development
            && string.IsNullOrWhiteSpace(storage.SpeakingRecordingsBucket))
        {
            warnings.Add(
                "ObjectStorage:SpeakingRecordingsBucket is unset, so no Speaking recording can "
                + "be stored. It has no default because the bucket names its own retention class, "
                + "and how long a learner's voice is kept is an unanswered business decision. "
                + "→ G-11, B-2");
        }

        if (!string.IsNullOrWhiteSpace(storage.SpeakingRecordingsBucket)
            && storage.SpeakingRecordingRetentionDays is null)
        {
            warnings.Add(
                "ObjectStorage:SpeakingRecordingsBucket is set but "
                + "ObjectStorage:SpeakingRecordingRetentionDays is not. Nothing here enforces it — "
                + "the bucket's own lifecycle rule does — but with no value there is nothing to "
                + "check that rule against. → PDPL storage limitation");
        }

        if (storage.SpeakingRecordingRetentionDays is <= 0)
        {
            problems.Add(
                $"ObjectStorage:SpeakingRecordingRetentionDays is "
                + $"{storage.SpeakingRecordingRetentionDays}. Zero or negative is not a retention "
                + "policy; leave it unset if the decision has not been made.");
        }

        // ── AI providers ──────────────────────────────────────────────────
        ValidateAi(builder, development, problems, warnings);

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

        /*
         * <b>The summary is printed before the verdict, including when the
         * verdict is a refusal.</b> Somebody staring at a boot failure wants to
         * know what the process actually read, and the alternative to giving
         * them a redacted answer is that they add an unredacted one. Making the
         * safe view the convenient view is the only durable form of this rule.
         */
        foreach (var line in Describe(builder))
            Console.WriteLine($"[config] {line}");

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

    /// <summary>
    /// Checks the two AI providers, and the two switches that decide what may
    /// be sent through them.
    ///
    /// <b>FS0.4. Before it, the only AI check anywhere was the Claude
    /// exclusion, thrown from `AddInfrastructure`.</b> A key with no model, a
    /// base URL that is not a URL, a key travelling to a plain-HTTP endpoint,
    /// and a cross-border switch that existed only in `CLAUDE.md` all reached
    /// production unremarked — and every one of them fails first at the moment
    /// a learner submits an essay.
    /// </summary>
    private static void ValidateAi(
        WebApplicationBuilder builder,
        bool development,
        List<string> problems,
        List<string> warnings)
    {
        var ai = builder.Configuration.GetSection(AiOptions.SectionName).Get<AiOptions>()
            ?? new AiOptions();

        foreach (var (section, provider) in new[] { ("OpenAi", ai.OpenAi), ("Gemini", ai.Gemini) })
        {
            /*
             * <b>The exclusion is checked here as well as in
             * `AddInfrastructure`, and the duplication is deliberate.</b> This
             * file's rule is that every problem is reported at once; a check
             * that throws from somewhere else means the operator fixes the
             * model name, redeploys, and only then learns about the other four
             * mistakes. The throw in `AddInfrastructure` stays, because a
             * process that reached DI registration without passing through
             * this gate must still not call Claude.
             */
            if (AiProviderPolicy.Rejects(section, provider) is { } excluded)
                problems.Add(excluded);

            if (!provider.IsConfigured)
            {
                /*
                 * <b>Unconfigured is a supported state, so this is silent.</b>
                 * Reading and Listening are marked from the answer key and
                 * never reach a model (`A-11`), so an install with no provider
                 * is an install that works. The one thing worth saying is when
                 * a permission has been granted to nothing.
                 */
                if (!provider.SyntheticDataOnly)
                {
                    warnings.Add(
                        $"Ai:{section}:SyntheticDataOnly is false but Ai:{section}:ApiKey is not "
                        + "set. Permission has been granted to a provider that cannot be called, "
                        + "which will silently become permission the day a key is added.");
                }

                continue;
            }

            /*
             * <b>A key with no model is the shape a half-finished setup takes,
             * and there is no default to fall back on.</b> `G-11`: a default
             * model would send a learner's essay to whichever model the author
             * of the adapter thought of, and the band it produced could not be
             * reproduced or calibrated afterwards. So the process refuses
             * instead of choosing.
             */
            if (string.IsNullOrWhiteSpace(provider.Model))
            {
                problems.Add(
                    $"Ai:{section}:ApiKey is set but Ai:{section}:Model is not, and Model has no "
                    + "default. A default would decide which model marks a learner's work, and "
                    + "that band would be unreproducible. → G-11");
            }

            if (!string.IsNullOrWhiteSpace(provider.BaseUrl))
            {
                if (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var baseUrl))
                {
                    // The value is never echoed: the commonest way this setting
                    // is wrong is that a key was pasted into it.
                    problems.Add(
                        $"Ai:{section}:BaseUrl is not an absolute URL. It is left unset for the "
                        + "vendor's own endpoint; it is not a hostname and not a key.");
                }
                else
                {
                    if (!string.IsNullOrEmpty(baseUrl.UserInfo))
                    {
                        problems.Add(
                            $"Ai:{section}:BaseUrl carries credentials in the URL itself. The key "
                            + $"belongs in Ai:{section}:ApiKey — a credential inside an endpoint "
                            + "reaches every log line and span that names the endpoint.");
                    }

                    if (!development && baseUrl.Scheme != Uri.UriSchemeHttps)
                    {
                        problems.Add(
                            $"Ai:{section}:BaseUrl is not HTTPS. The API key is sent on every "
                            + "request to it, and so is whatever the request carries.");
                    }
                }
            }

            /*
             * <b>The one combination that is refused rather than left to the
             * runtime guard.</b> A third-party endpoint plus `SyntheticDataOnly
             * = false` is somebody asserting that real learner work may go
             * through a company VNI has no contract with. `AiEgress` refuses
             * that call anyway — but it refuses it at the moment a learner
             * submits, in a background job, and the operator who set it finds
             * out from a support ticket. Boot is the cheaper place to learn.
             */
            if (!provider.SyntheticDataOnly
                && AiProviderPolicy.IsThirdPartyEndpoint(section, provider.BaseUrl))
            {
                problems.Add(
                    $"Ai:{section}:SyntheticDataOnly is false while Ai:{section}:BaseUrl points "
                    + "at neither the vendor's own endpoint nor any contracted processor "
                    + "(AiProviderPolicy.ContractedProcessorHosts is empty). That combination "
                    + "asserts a learner's essay may pass through a company with no "
                    + "data-processing agreement. → CLAUDE.md rule 6");
            }
        }

        /*
         * <b>Permission to cross the border, granted to nothing.</b> Not a
         * fault — a deployment may legitimately be prepared ahead of its
         * provider — but it is worth saying out loud, because the switch is the
         * one whose consequence is a filing obligation rather than an error.
         */
        if (ai.AllowCrossBorderTransfer && !ai.OpenAi.IsConfigured && !ai.Gemini.IsConfigured)
        {
            warnings.Add(
                "Ai:AllowCrossBorderTransfer is true but no provider is configured. Nothing "
                + "crosses any border today; the permission takes effect silently the moment a "
                + "key is added. → B-2");
        }

        if (ai.AllowCrossBorderTransfer)
        {
            warnings.Add(
                "Ai:AllowCrossBorderTransfer is true. Personal data reaching a provider outside "
                + "Vietnam is a cross-border transfer under the PDPL and requires a CTIA filing "
                + "within 60 days of the first transfer. → B-2, "
                + "docs/security/privacy-vietnam-pdpl.md");
        }
    }

    /// <summary>
    /// What this process read, in a form that is safe to print — the config
    /// dump, owned rather than improvised.
    ///
    /// ── Why this exists at all ────────────────────────────────────────────
    ///
    /// <b>Because the alternative gets written anyway, in a hurry, by whoever
    /// is debugging at the time.</b> "Which settings did it actually pick up"
    /// is the first question of every misconfiguration, and a process that
    /// cannot answer it invites `Console.WriteLine(configuration.AsEnumerable())`
    /// — which prints the signing key, the SMTP password and every API key,
    /// into a log that ships to a collector. Providing the answer in redacted
    /// form is what makes the unredacted version unnecessary.
    ///
    /// ── The rule every line obeys ─────────────────────────────────────────
    ///
    /// <b>Names are printed; secret values never are.</b> A secret-bearing
    /// setting appears as presence and length — enough to see that a key
    /// arrived and that it was not truncated or given a trailing newline, which
    /// are the two failures this is read for, and not enough to be the key.
    /// URLs lose their userinfo and their query string. → `SecretRedaction`
    ///
    /// <b>It is public because it is tested directly.</b> A leak test that only
    /// scrapes console output can pass because nothing was printed at all;
    /// asserting on this list lets the test insist that each secret-bearing
    /// setting really is named here, which is what makes "and its value is
    /// absent" mean something.
    /// </summary>
    public static IReadOnlyList<string> Describe(WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;

        var mongo = configuration.GetSection(MongoOptions.SectionName).Get<MongoOptions>()
            ?? new MongoOptions();
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions();
        var sso = configuration.GetSection(SsoOptions.SectionName).Get<SsoOptions>()
            ?? new SsoOptions();
        var storage = configuration.GetSection(ObjectStorageOptions.SectionName)
            .Get<ObjectStorageOptions>() ?? new ObjectStorageOptions();
        var email = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>()
            ?? new SmtpOptions();
        var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        var origins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];

        var lines = new List<string>
        {
            $"environment = {builder.Environment.EnvironmentName}",

            // A connection string is the classic carrier: the password lives
            // inside it, so it goes through the URL redactor rather than being
            // described as a secret or printed as a value.
            $"Mongo:ConnectionString = {SecretRedaction.Url(mongo.ConnectionString)}",
            $"Mongo:Database = {mongo.Database}",

            $"Jwt:Issuer = {jwt.Issuer}",
            $"Jwt:Audience = {jwt.Audience}",
            $"Jwt:SigningKey = {SecretRedaction.Describe(jwt.SigningKey)}",
            $"Jwt:AccessTokenMinutes = {jwt.AccessTokenMinutes}",
            $"Jwt:RefreshTokenDays = {jwt.RefreshTokenDays}",

            $"Cors:Origins = {(origins.Length == 0 ? "none" : string.Join(", ", origins))}",

            $"Sso:EnableStubProvider = {sso.EnableStubProvider}",
            $"Sso:ClientBaseUrl = {SecretRedaction.Url(sso.ClientBaseUrl)}",
            $"Sso:Google:ClientId = {SecretRedaction.Identifier(sso.Google.ClientId)}",
            $"Sso:Google:ClientSecret = {SecretRedaction.Describe(sso.Google.ClientSecret)}",
            $"Sso:Google:RedirectUri = {SecretRedaction.Url(sso.Google.RedirectUri)}",

            $"ObjectStorage:ServiceUrl = {SecretRedaction.Url(storage.ServiceUrl)}"
                + (storage.IsCloudflareR2 ? "  (Cloudflare R2)" : string.Empty),
            $"ObjectStorage:AccessKey = {SecretRedaction.Identifier(storage.AccessKey)}",
            $"ObjectStorage:SecretKey = {SecretRedaction.Describe(storage.SecretKey)}",
            $"ObjectStorage:Region = {storage.Region}",
            $"ObjectStorage:ForcePathStyle = {storage.ForcePathStyle}",
            $"ObjectStorage:ExamAssetsBucket = {storage.ExamAssetsBucket}",
            $"ObjectStorage:DictationBucket = {storage.DictationBucket}",
            "ObjectStorage:SpeakingRecordingsBucket = "
                + (string.IsNullOrWhiteSpace(storage.SpeakingRecordingsBucket)
                    ? "not set"
                    : storage.SpeakingRecordingsBucket),
            "ObjectStorage:SpeakingRecordingRetentionDays = "
                + (storage.SpeakingRecordingRetentionDays?.ToString()
                    ?? "not set — unanswered business decision, G-11"),

            $"Email:Host = {email.Host}",
            $"Email:Port = {email.Port}",
            $"Email:Username = {SecretRedaction.Identifier(email.Username)}",
            $"Email:Password = {SecretRedaction.Describe(email.Password)}",
            $"Email:FromAddress = {email.FromAddress}",
            $"Email:ClientBaseUrl = {SecretRedaction.Url(email.ClientBaseUrl)}",

            $"Ai:AllowCrossBorderTransfer = {ai.AllowCrossBorderTransfer}",
        };

        foreach (var (section, provider) in new[] { ("OpenAi", ai.OpenAi), ("Gemini", ai.Gemini) })
        {
            lines.Add($"Ai:{section}:BaseUrl = {SecretRedaction.Url(provider.BaseUrl)}"
                + (AiProviderPolicy.IsThirdPartyEndpoint(section, provider.BaseUrl)
                    ? "  (third-party processor)"
                    : string.Empty));
            lines.Add($"Ai:{section}:ApiKey = {SecretRedaction.Describe(provider.ApiKey)}");
            lines.Add($"Ai:{section}:Model = {provider.Model ?? "not set"}");
            lines.Add($"Ai:{section}:SyntheticDataOnly = {provider.SyntheticDataOnly}");
        }

        return lines;
    }
}
