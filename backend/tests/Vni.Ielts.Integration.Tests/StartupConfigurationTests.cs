using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Vni.Ielts.Api.Common;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// F2.5 — `StartupConfiguration.ValidateOrThrow` had no dedicated tests at
/// all before this: every check in it was exercised only incidentally, by
/// whichever `WebApplicationFactory` test happened to supply a config close
/// enough to valid. These tests call the gate directly, against an in-memory
/// `WebApplicationBuilder` — no Mongo, no MinIO, nothing external — because
/// what is under test is "does this specific bad value get rejected", not
/// anything the process actually connects to.
/// </summary>
public sealed class StartupConfigurationTests
{
    [Fact]
    public void A_complete_production_configuration_is_accepted()
    {
        var builder = ProductionBuilder(ValidProductionConfig());

        var exception = Record.Exception(() => StartupConfiguration.ValidateOrThrow(builder));

        Assert.Null(exception);
    }

    [Fact]
    public void Production_rejects_an_http_Sso_ClientBaseUrl_when_Google_is_configured()
    {
        var config = ValidProductionConfig();
        config["Sso:ClientBaseUrl"] = "http://learn.example.com";
        var builder = ProductionBuilder(config);

        var ex = Assert.Throws<InvalidOperationException>(
            () => StartupConfiguration.ValidateOrThrow(builder));

        Assert.Contains("Sso:ClientBaseUrl", ex.Message);
    }

    [Fact]
    public void Production_rejects_an_http_Sso_Google_RedirectUri_when_Google_is_configured()
    {
        var config = ValidProductionConfig();
        config["Sso:Google:RedirectUri"] = "http://api.example.com/auth/sso/google/callback";
        var builder = ProductionBuilder(config);

        var ex = Assert.Throws<InvalidOperationException>(
            () => StartupConfiguration.ValidateOrThrow(builder));

        Assert.Contains("Sso:Google:RedirectUri", ex.Message);
    }

    [Fact]
    public void Development_tolerates_an_http_Sso_ClientBaseUrl()
    {
        var config = ValidProductionConfig();
        config["Sso:ClientBaseUrl"] = "http://localhost:5173";
        var builder = DevelopmentBuilder(config);

        var exception = Record.Exception(() => StartupConfiguration.ValidateOrThrow(builder));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_non_positive_shutdown_timeout_is_rejected_in_every_environment(int seconds)
    {
        var config = ValidProductionConfig();
        config["Api:ShutdownTimeoutSeconds"] = seconds.ToString();
        var builder = ProductionBuilder(config);

        var ex = Assert.Throws<InvalidOperationException>(
            () => StartupConfiguration.ValidateOrThrow(builder));

        Assert.Contains("Api:ShutdownTimeoutSeconds", ex.Message);

        var devConfig = ValidProductionConfig();
        devConfig["Api:ShutdownTimeoutSeconds"] = seconds.ToString();
        var devBuilder = DevelopmentBuilder(devConfig);

        var devEx = Assert.Throws<InvalidOperationException>(
            () => StartupConfiguration.ValidateOrThrow(devBuilder));

        Assert.Contains("Api:ShutdownTimeoutSeconds", devEx.Message);
    }

    [Fact]
    public void A_literal_wildcard_origin_is_rejected()
    {
        var config = ValidProductionConfig();
        config["Cors:Origins:0"] = "*";
        var builder = ProductionBuilder(config);

        var ex = Assert.Throws<InvalidOperationException>(
            () => StartupConfiguration.ValidateOrThrow(builder));

        Assert.Contains("Cors:Origins", ex.Message);
    }

    [Fact]
    public void Production_rejects_a_plain_http_Cors_origin()
    {
        var config = ValidProductionConfig();
        config["Cors:Origins:0"] = "http://learn.example.com";
        var builder = ProductionBuilder(config);

        Assert.Throws<InvalidOperationException>(() => StartupConfiguration.ValidateOrThrow(builder));
    }

    [Fact]
    public void Production_rejects_a_missing_ObjectStorage_configuration()
    {
        var config = ValidProductionConfig();
        config.Remove("ObjectStorage:ServiceUrl");
        config.Remove("ObjectStorage:AccessKey");
        config.Remove("ObjectStorage:SecretKey");
        var builder = ProductionBuilder(config);

        Assert.Throws<InvalidOperationException>(() => StartupConfiguration.ValidateOrThrow(builder));
    }

    [Fact]
    public void The_error_message_never_contains_a_configured_secret_value()
    {
        var config = ValidProductionConfig();
        config["Cors:Origins:0"] = "*";
        config["Api:ShutdownTimeoutSeconds"] = "0";
        const string signingKeySecret = "this-is-the-jwt-signing-key-nobody-should-see-in-logs";
        config["Jwt:SigningKey"] = signingKeySecret;
        const string objectStorageSecret = "this-is-the-object-storage-secret-key";
        config["ObjectStorage:SecretKey"] = objectStorageSecret;
        var builder = ProductionBuilder(config);

        var ex = Assert.Throws<InvalidOperationException>(
            () => StartupConfiguration.ValidateOrThrow(builder));

        Assert.DoesNotContain(signingKeySecret, ex.Message);
        Assert.DoesNotContain(objectStorageSecret, ex.Message);
    }

    // ── One bucket, one prefix per class (ADR-0016) ──────────────────────

    /// <summary>
    /// `[QUYẾT ĐỊNH]` chủ sản phẩm 04/09/2026: the three classes may share one
    /// bucket when each has its own folder. Before that day this configuration
    /// was refused outright.
    /// </summary>
    [Fact]
    public void Three_classes_in_one_bucket_under_three_prefixes_boot()
    {
        var config = ValidProductionConfig();
        config["ObjectStorage:ExamAssetsBucket"] = "vni-ielts-ai-dev";
        config["ObjectStorage:ExamAssetsPrefix"] = "examassets/";
        config["ObjectStorage:DictationBucket"] = "vni-ielts-ai-dev";
        config["ObjectStorage:DictationPrefix"] = "dictation/";
        config["ObjectStorage:SpeakingRecordingsBucket"] = "vni-ielts-ai-dev";
        config["ObjectStorage:SpeakingRecordingsPrefix"] = "speakingrecord/";
        config["ObjectStorage:SpeakingRecordingRetentionDays"] = "90";

        Assert.Null(Record.Exception(() => StartupConfiguration.ValidateOrThrow(ProductionBuilder(config))));
    }

    /// <summary>
    /// The thing a shared bucket must never do: put two classes in one key
    /// space. No prefix on either side is the default, and the default is
    /// exactly the configuration that used to be refused for a different reason.
    /// </summary>
    [Fact]
    public void Two_classes_in_one_bucket_with_no_prefix_are_refused()
    {
        var config = ValidProductionConfig();
        config["ObjectStorage:ExamAssetsBucket"] = "vni-ielts-ai-dev";
        config["ObjectStorage:SpeakingRecordingsBucket"] = "vni-ielts-ai-dev";
        config["ObjectStorage:SpeakingRecordingRetentionDays"] = "90";

        var refusal = Assert.Throws<InvalidOperationException>(
            () => StartupConfiguration.ValidateOrThrow(ProductionBuilder(config)));

        Assert.Contains("SpeakingRecordingsPrefix", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_prefixes_in_one_bucket_are_refused()
    {
        var config = ValidProductionConfig();
        config["ObjectStorage:ExamAssetsBucket"] = "vni-ielts-ai-dev";
        config["ObjectStorage:ExamAssetsPrefix"] = "media/";
        config["ObjectStorage:DictationBucket"] = "vni-ielts-ai-dev";
        config["ObjectStorage:DictationPrefix"] = "media/dictation/";

        var refusal = Assert.Throws<InvalidOperationException>(
            () => StartupConfiguration.ValidateOrThrow(ProductionBuilder(config)));

        Assert.Contains("do not keep their keys apart", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_prefix_with_a_dot_segment_is_refused_even_in_its_own_bucket()
    {
        var config = ValidProductionConfig();
        config["ObjectStorage:ExamAssetsPrefix"] = "../examassets/";

        var refusal = Assert.Throws<InvalidOperationException>(
            () => StartupConfiguration.ValidateOrThrow(ProductionBuilder(config)));

        Assert.Contains("ObjectStorage:ExamAssetsPrefix", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Separate buckets, no prefixes — the layout every deployment had before, still fine.</summary>
    [Fact]
    public void The_one_bucket_per_class_layout_still_boots_without_prefixes()
    {
        var config = ValidProductionConfig();
        config["ObjectStorage:SpeakingRecordingsBucket"] = "vni-speaking-recordings";
        config["ObjectStorage:SpeakingRecordingRetentionDays"] = "90";

        Assert.Null(Record.Exception(() => StartupConfiguration.ValidateOrThrow(ProductionBuilder(config))));
    }

    private static Dictionary<string, string?> ValidProductionConfig() => new()
    {
        ["Mongo:ConnectionString"] = "mongodb://localhost:27018/?replicaSet=rs0",
        ["Mongo:Database"] = "vni_ielts_startup_config_test",
        ["Jwt:Issuer"] = "vni-ielts-api",
        ["Jwt:Audience"] = "vni-ielts-clients",
        ["Jwt:AccessTokenMinutes"] = "15",
        ["Cors:Origins:0"] = "https://learn.example.com",
        ["Cors:Origins:1"] = "https://admin.example.com",
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

    private static WebApplicationBuilder ProductionBuilder(Dictionary<string, string?> config) =>
        BuilderWithEnvironment("Production", config);

    private static WebApplicationBuilder DevelopmentBuilder(Dictionary<string, string?> config) =>
        BuilderWithEnvironment("Development", config);

    private static WebApplicationBuilder BuilderWithEnvironment(
        string environment, Dictionary<string, string?> config)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(config);
        return builder;
    }
}
