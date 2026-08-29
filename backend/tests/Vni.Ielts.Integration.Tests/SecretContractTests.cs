using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Vni.Ielts.Api.Common;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// FS0.4 — the phase-gate requirement, which is worded as a proof and not as a
/// review: <i>the startup log and config dump are proven not to leak a
/// secret</i>.
///
/// ── What makes this test worth having ─────────────────────────────────────
///
/// <b>The obvious version of this test is worthless, and it is worth being
/// explicit about why.</b> Asserting "the startup output does not contain my
/// fake key" passes trivially on a process that prints nothing about
/// configuration at all — and it keeps passing while somebody adds a debug dump
/// six months later, because the fake key in the test was never wired to
/// anything the dump would read.
///
/// So every assertion here comes in a pair. First: <b>this setting is named in
/// the output</b> — the path really does render `Ai:OpenAi:ApiKey`, so the
/// value had every opportunity to appear beside it. Second: <b>and its value is
/// not</b>. The first half is what gives the second half meaning, and it is the
/// half that fails if a future change quietly stops describing a setting.
///
/// <b>The negative proof was run, not assumed.</b> Redaction was removed from
/// `StartupConfiguration.Describe` and this test was watched going red before
/// it was accepted as green — recorded in
/// `_workspace/workflow/agents/security-secret-contract.md`.
///
/// <b>No Mongo, no MinIO, no network.</b> What is under test is what the
/// process says about its configuration, which it says before it connects to
/// anything. `RedactionTests` covers the other direction — a secret sent
/// <i>into</i> the API not coming back out — and needs a database for it.
/// </summary>
public sealed class SecretContractTests
{
    /*
     * Deliberately unmistakable, and deliberately not shaped like any real
     * credential: `scripts/check-docs.mjs` fails the build on
     * credential-shaped strings in tracked files, and a fixture that trips the
     * repository's own secret scanner is a fixture that teaches people to
     * silence the scanner.
     *
     * Each value is distinct so that a failure names the setting that leaked
     * rather than "a secret leaked".
     */
    private const string JwtSigningKey = "FAKE-NOT-A-REAL-KEY-jwt-signing-000000000000";
    private const string GoogleClientSecret = "FAKE-NOT-A-REAL-KEY-google-client-secret-01";
    private const string SmtpPassword = "FAKE-NOT-A-REAL-KEY-smtp-password-02";
    private const string StorageSecretKey = "FAKE-NOT-A-REAL-KEY-object-storage-03";
    private const string OpenAiApiKey = "FAKE-NOT-A-REAL-KEY-openai-04";
    private const string GeminiApiKey = "FAKE-NOT-A-REAL-KEY-gemini-05";
    private const string MongoPassword = "FAKE-NOT-A-REAL-KEY-mongo-password-06";

    /// <summary>The setting each secret is supplied through, for the paired assertion.</summary>
    private static readonly (string Setting, string Secret)[] SecretSettings =
    [
        ("Jwt:SigningKey", JwtSigningKey),
        ("Sso:Google:ClientSecret", GoogleClientSecret),
        ("Email:Password", SmtpPassword),
        ("ObjectStorage:SecretKey", StorageSecretKey),
        ("Ai:OpenAi:ApiKey", OpenAiApiKey),
        ("Ai:Gemini:ApiKey", GeminiApiKey),
        ("Mongo:ConnectionString", MongoPassword),
    ];

    [Fact]
    public void The_config_dump_names_every_secret_setting_and_prints_none_of_their_values()
    {
        var builder = ProductionBuilder(ConfigWithEverySecret());

        var dump = string.Join("\n", StartupConfiguration.Describe(builder));

        foreach (var (setting, secret) in SecretSettings)
        {
            // Half one — the dump really does render this setting, so its value
            // had every opportunity to be printed beside it.
            Assert.True(
                dump.Contains(setting, StringComparison.Ordinal),
                $"The config dump does not mention {setting} at all. That makes the leak "
                    + "assertion below meaningless: a value cannot leak from a line that is not "
                    + "written. Either describe the setting or delete it from this test.");

            // Half two — and it did not.
            Assert.False(
                dump.Contains(secret, StringComparison.Ordinal),
                $"The config dump prints the value of {setting}. It reaches stdout on every "
                    + "boot and, wherever logs are shipped, a collector. → SecretRedaction");
        }
    }

    /// <summary>
    /// <b>The same proof against what is actually written to the console</b>,
    /// because `Describe` being clean says nothing about the caller that prints
    /// it — nor about the warnings printed beside it, which interpolate
    /// configured values of their own.
    /// </summary>
    [Fact]
    public void The_startup_log_prints_no_secret_and_no_credential_bearing_url()
    {
        var config = ConfigWithEverySecret();

        // A plain-HTTP storage endpoint, so the warning that interpolates
        // `ObjectStorage:ServiceUrl` is definitely emitted — this is the exact
        // line that carried userinfo verbatim before FS0.4, and a test that
        // does not trigger it proves nothing about it.
        config["ObjectStorage:ServiceUrl"] =
            $"http://storage-id:{StorageSecretKey}@storage.example.com";

        var builder = ProductionBuilder(config);

        var written = new StringWriter();
        var previous = Console.Out;

        try
        {
            Console.SetOut(written);
            StartupConfiguration.ValidateOrThrow(builder);
        }
        catch (InvalidOperationException refusal)
        {
            // A credential inside the endpoint is itself a refusal, so this
            // configuration does not boot — and the refusal message is a sink
            // in its own right. Both are checked.
            Assert.DoesNotContain(StorageSecretKey, refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(previous);
        }

        var log = written.ToString();

        Assert.True(
            log.Contains("ObjectStorage:ServiceUrl", StringComparison.Ordinal),
            "The startup log never mentions ObjectStorage:ServiceUrl, so the assertion below "
                + "would pass on a process that says nothing at all.");

        foreach (var (setting, secret) in SecretSettings)
        {
            Assert.False(
                log.Contains(secret, StringComparison.Ordinal),
                $"The startup log contains the value of {setting}.");
        }
    }

    /// <summary>
    /// <b>The refusal messages are a sink too, and the noisiest one.</b> An
    /// exception thrown at boot is printed by the host, captured by the
    /// orchestrator, and pasted into a chat window by whoever is on call.
    /// </summary>
    [Fact]
    public void The_refusal_message_contains_no_secret()
    {
        var config = ConfigWithEverySecret();
        config["Cors:Origins:0"] = "*";                        // force a refusal
        config["Ai:OpenAi:BaseUrl"] = $"https://user:{OpenAiApiKey}@reseller.example/v1";

        var builder = ProductionBuilder(config);

        var written = new StringWriter();
        var previous = Console.Out;
        InvalidOperationException refusal;

        try
        {
            Console.SetOut(written);
            refusal = Assert.Throws<InvalidOperationException>(
                () => StartupConfiguration.ValidateOrThrow(builder));
        }
        finally
        {
            Console.SetOut(previous);
        }

        Assert.Contains("Ai:OpenAi:BaseUrl", refusal.Message, StringComparison.Ordinal);

        foreach (var (setting, secret) in SecretSettings)
        {
            Assert.False(
                refusal.Message.Contains(secret, StringComparison.Ordinal),
                $"The startup refusal message contains the value of {setting}.");
            Assert.False(
                written.ToString().Contains(secret, StringComparison.Ordinal),
                $"The startup log written before the refusal contains the value of {setting}.");
        }
    }

    // ── Refusing to boot, rather than booting degraded ────────────────────

    /// <summary>
    /// <b>A key with no model is the shape a half-finished AI setup takes.</b>
    /// There is no default to fall back on, and inventing one would decide
    /// which model marks a learner's work. → `G-11`
    /// </summary>
    [Fact]
    public void An_api_key_with_no_model_refuses_to_boot()
    {
        var config = ValidProductionConfig();
        config["Ai:OpenAi:ApiKey"] = OpenAiApiKey;

        var refusal = Assert.Throws<InvalidOperationException>(
            () => Validate(ProductionBuilder(config)));

        Assert.Contains("Ai:OpenAi:Model", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_ai_base_url_refuses_to_boot()
    {
        var config = ValidProductionConfig();
        config["Ai:OpenAi:ApiKey"] = OpenAiApiKey;
        config["Ai:OpenAi:Model"] = "gpt-5.5";
        config["Ai:OpenAi:BaseUrl"] = "reseller.example/v1";   // no scheme

        var refusal = Assert.Throws<InvalidOperationException>(
            () => Validate(ProductionBuilder(config)));

        Assert.Contains("Ai:OpenAi:BaseUrl", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The startup half of the synthetic-data guard: the combination is refused
    /// at boot as well as at the call, because the call happens in a background
    /// job on the day a learner submits.
    /// </summary>
    [Fact]
    public void Lifting_the_synthetic_restriction_on_a_reseller_refuses_to_boot()
    {
        var config = ValidProductionConfig();
        config["Ai:OpenAi:ApiKey"] = OpenAiApiKey;
        config["Ai:OpenAi:Model"] = "gpt-5.5";
        config["Ai:OpenAi:BaseUrl"] = "https://reseller.example/v1";
        config["Ai:OpenAi:SyntheticDataOnly"] = "false";

        var refusal = Assert.Throws<InvalidOperationException>(
            () => Validate(ProductionBuilder(config)));

        Assert.Contains("SyntheticDataOnly", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The vendor's own endpoint is not a reseller, so the same lift is
    /// accepted there.</b> Without this the previous test would also pass on a
    /// gate that simply refused everything.
    /// </summary>
    [Fact]
    public void Lifting_it_on_the_vendor_endpoint_is_accepted()
    {
        var config = ValidProductionConfig();
        config["Ai:OpenAi:ApiKey"] = OpenAiApiKey;
        config["Ai:OpenAi:Model"] = "gpt-5.5";
        config["Ai:OpenAi:BaseUrl"] = "https://api.openai.com/v1";
        config["Ai:OpenAi:SyntheticDataOnly"] = "false";

        Assert.Null(Record.Exception(() => Validate(ProductionBuilder(config))));
    }

    [Fact]
    public void An_object_storage_endpoint_carrying_credentials_refuses_to_boot()
    {
        var config = ValidProductionConfig();
        config["ObjectStorage:ServiceUrl"] =
            $"https://storage-id:{StorageSecretKey}@storage.example.com";

        var refusal = Assert.Throws<InvalidOperationException>(
            () => Validate(ProductionBuilder(config)));

        Assert.Contains("ObjectStorage:ServiceUrl", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(StorageSecretKey, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// R2 as a configuration profile: the one requirement that differs from
    /// AWS, caught at boot instead of as an SDK signature error at the first
    /// upload of a learner's recording. → plan decision 4
    /// </summary>
    [Fact]
    public void An_r2_endpoint_with_the_wrong_region_refuses_to_boot()
    {
        var config = ValidProductionConfig();
        config["ObjectStorage:ServiceUrl"] = "https://acct.r2.cloudflarestorage.com";
        config["ObjectStorage:Region"] = "us-east-1";

        var refusal = Assert.Throws<InvalidOperationException>(
            () => Validate(ProductionBuilder(config)));

        Assert.Contains("ObjectStorage:Region", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_r2_endpoint_with_region_auto_is_accepted()
    {
        var config = ValidProductionConfig();
        config["ObjectStorage:ServiceUrl"] = "https://acct.r2.cloudflarestorage.com";
        config["ObjectStorage:Region"] = "auto";

        Assert.Null(Record.Exception(() => Validate(ProductionBuilder(config))));
    }

    /// <summary>
    /// <b>Learner voice must not land in a versioned bucket.</b> A version
    /// history of a recording outlives the deletion PDPL requires to be final.
    /// </summary>
    [Fact]
    public void Speaking_recordings_sharing_the_exam_assets_bucket_refuses_to_boot()
    {
        var config = ValidProductionConfig();
        config["ObjectStorage:SpeakingRecordingsBucket"] = "vni-exam-assets";

        var refusal = Assert.Throws<InvalidOperationException>(
            () => Validate(ProductionBuilder(config)));

        Assert.Contains("SpeakingRecordingsBucket", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The seams stay seams.</b> An unset Speaking bucket and an unset
    /// retention period do not stop the process — nothing writes a recording
    /// yet — and they are never filled in with a guess. `vni-audio-90d` names a
    /// retention in its own name; choosing it here would answer a question
    /// nobody has asked. → `G-11`
    /// </summary>
    [Fact]
    public void The_speaking_retention_seams_are_unset_and_do_not_invent_a_value()
    {
        var builder = ProductionBuilder(ValidProductionConfig());

        Assert.Null(Record.Exception(() => Validate(builder)));

        var dump = string.Join("\n", StartupConfiguration.Describe(builder));

        Assert.Contains(
            "ObjectStorage:SpeakingRecordingsBucket = not set", dump, StringComparison.Ordinal);
        Assert.Contains(
            "ObjectStorage:SpeakingRecordingRetentionDays = not set",
            dump,
            StringComparison.Ordinal);
        Assert.DoesNotContain("vni-audio-90d", dump.Split('\n')
            .First(l => l.StartsWith(
                "ObjectStorage:SpeakingRecordingsBucket", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_zero_retention_period_is_refused_rather_than_treated_as_delete_immediately()
    {
        var config = ValidProductionConfig();
        config["ObjectStorage:SpeakingRecordingsBucket"] = "vni-speaking";
        config["ObjectStorage:SpeakingRecordingRetentionDays"] = "0";

        var refusal = Assert.Throws<InvalidOperationException>(
            () => Validate(ProductionBuilder(config)));

        Assert.Contains(
            "SpeakingRecordingRetentionDays", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The cross-border switch reaches the code now.</b> It was named in
    /// `CLAUDE.md` and declared nowhere, so nothing could read it and no test
    /// could set it. Default false, and the permission is announced when it is
    /// granted — because its consequence is a filing obligation rather than an
    /// error. → `B-2`
    /// </summary>
    [Fact]
    public void Cross_border_transfer_is_off_by_default_and_announced_when_it_is_on()
    {
        var offBuilder = ProductionBuilder(ValidProductionConfig());
        Assert.Contains(
            "Ai:AllowCrossBorderTransfer = False",
            string.Join("\n", StartupConfiguration.Describe(offBuilder)),
            StringComparison.Ordinal);

        var config = ValidProductionConfig();
        config["Ai:AllowCrossBorderTransfer"] = "true";

        var written = new StringWriter();
        var previous = Console.Out;

        try
        {
            Console.SetOut(written);
            StartupConfiguration.ValidateOrThrow(ProductionBuilder(config));
        }
        finally
        {
            Console.SetOut(previous);
        }

        Assert.Contains("CTIA", written.ToString(), StringComparison.Ordinal);
    }

    // ── Fixtures ──────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the gate with console output captured, so a test asserting on the
    /// refusal is not also printing the whole config dump into the test log.
    /// </summary>
    private static void Validate(WebApplicationBuilder builder)
    {
        var previous = Console.Out;

        try
        {
            Console.SetOut(new StringWriter());
            StartupConfiguration.ValidateOrThrow(builder);
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    private static Dictionary<string, string?> ConfigWithEverySecret()
    {
        var config = ValidProductionConfig();

        config["Mongo:ConnectionString"] =
            $"mongodb://vni:{MongoPassword}@db.internal:27017/?replicaSet=rs0";
        config["Jwt:SigningKey"] = JwtSigningKey;
        config["Sso:Google:ClientSecret"] = GoogleClientSecret;
        config["Email:Username"] = "smtp-user";
        config["Email:Password"] = SmtpPassword;
        config["ObjectStorage:SecretKey"] = StorageSecretKey;
        config["Ai:OpenAi:ApiKey"] = OpenAiApiKey;
        config["Ai:OpenAi:Model"] = "gpt-5.5";
        config["Ai:Gemini:ApiKey"] = GeminiApiKey;
        config["Ai:Gemini:Model"] = "gemini-3-pro";

        return config;
    }

    private static Dictionary<string, string?> ValidProductionConfig() => new()
    {
        ["Mongo:ConnectionString"] = "mongodb://localhost:27018/?replicaSet=rs0",
        ["Mongo:Database"] = "vni_ielts_secret_contract_test",
        ["Jwt:Issuer"] = "vni-ielts-api",
        ["Jwt:Audience"] = "vni-ielts-clients",
        ["Jwt:AccessTokenMinutes"] = "15",
        ["Cors:Origins:0"] = "https://learn.example.com",
        ["Sso:Google:ClientId"] = "google-client-id",
        ["Sso:Google:ClientSecret"] = "google-client-secret",
        ["Sso:ClientBaseUrl"] = "https://learn.example.com",
        ["Sso:Google:RedirectUri"] = "https://api.example.com/api/v1/auth/sso/google/callback",
        ["ObjectStorage:ServiceUrl"] = "https://storage.example.com",
        ["ObjectStorage:AccessKey"] = "object-storage-access-key",
        ["ObjectStorage:SecretKey"] = "object-storage-secret-key",
        ["Email:Host"] = "smtp.example.com",
        ["Email:Port"] = "587",
        ["Email:FromAddress"] = "no-reply@example.com",
        ["Email:ClientBaseUrl"] = "https://learn.example.com",
        ["Api:ShutdownTimeoutSeconds"] = "30",
    };

    private static WebApplicationBuilder ProductionBuilder(Dictionary<string, string?> config)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(config);
        return builder;
    }
}
