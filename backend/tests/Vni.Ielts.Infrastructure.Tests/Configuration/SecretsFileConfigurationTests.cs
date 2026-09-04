using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Vni.Ielts.Infrastructure.Configuration;

namespace Vni.Ielts.Infrastructure.Tests.Configuration;

/// <summary>
/// <b>Written because the feature shipped without compiling.</b> The commit
/// that replaced <c>dotnet user-secrets</c> with these files called
/// <c>ConfigureAppConfiguration</c> on <see cref="IHostApplicationBuilder"/>,
/// which has no such method — so the whole solution failed to build and no
/// secret file was loaded anywhere.
///
/// A test that only asserted "the value comes back" would not have caught the
/// thing that actually matters once it does compile, which is <b>precedence</b>.
/// Every test here therefore pins a layer against the layer beside it:
/// the file beats <c>appsettings*.json</c>, the environment beats the file.
/// Get either backwards and the symptom is a rotated key that quietly does
/// nothing, or an operator editing a mounted file that is silently ignored.
/// </summary>
public sealed class SecretsFileConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"vni-secrets-{Guid.NewGuid():N}");

    public SecretsFileConfigurationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void The_develop_file_overrides_appsettings_in_Development()
    {
        WriteAppSettings("from-appsettings");
        Write(SecretsFileConfigurationExtensions.DevelopFileName, "from-develop-file");

        Assert.Equal("from-develop-file", Resolve(Environments.Development));
    }

    [Fact]
    public void The_production_file_overrides_appsettings_in_Production()
    {
        WriteAppSettings("from-appsettings");
        Write(SecretsFileConfigurationExtensions.ProductionFileName, "from-production-file");

        Assert.Equal("from-production-file", Resolve(Environments.Production));
    }

    /// <summary>
    /// The claim <c>secrets.README.md</c> makes in bold, and the one a key
    /// rotation depends on: a deployment supplies the new value through the
    /// environment, and a stale mounted file must not win.
    /// </summary>
    [Fact]
    public void An_environment_variable_overrides_the_production_file()
    {
        WriteAppSettings("from-appsettings");
        Write(SecretsFileConfigurationExtensions.ProductionFileName, "from-production-file");

        Environment.SetEnvironmentVariable(EnvironmentKey, "from-environment");

        try
        {
            Assert.Equal("from-environment", Resolve(Environments.Production));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvironmentKey, null);
        }
    }

    /// <summary>
    /// <b>The develop file outranks appsettings and nothing else.</b> A test
    /// host's <c>UseSetting(...)</c>, the command line and the environment are
    /// all registered by <c>CreateBuilder</c> before <c>AddVniSecretsFile</c>
    /// runs; appending the file put it above every one of them, and
    /// <c>SsoAppFactory</c>'s blanked Google client id lost to the real one in
    /// the developer's file — eleven <c>SessionsTests</c> red on 2026-09-04.
    /// </summary>
    [Fact]
    public void A_setting_registered_by_the_host_before_the_develop_file_still_wins()
    {
        WriteAppSettings("from-appsettings");
        Write(SecretsFileConfigurationExtensions.DevelopFileName, "from-develop-file");

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = _root,
            EnvironmentName = Environments.Development,
            ApplicationName = "Vni.Ielts.Tests",
            Args = [],
        });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [ConfigurationKey] = "from-host-setting" });

        builder.AddVniSecretsFile();

        Assert.Equal("from-host-setting", builder.Configuration[ConfigurationKey]);
    }

    /// <summary>
    /// Each file belongs to one environment. A develop file left on a server —
    /// the realistic accident, since it is the file that has real keys in it —
    /// must not be picked up by a Production process.
    /// </summary>
    [Fact]
    public void Neither_file_is_read_by_the_other_environment()
    {
        WriteAppSettings("from-appsettings");
        Write(SecretsFileConfigurationExtensions.DevelopFileName, "from-develop-file");

        Assert.Equal("from-appsettings", Resolve(Environments.Production));

        File.Delete(Path.Combine(_root, SecretsFileConfigurationExtensions.DevelopFileName));
        Write(SecretsFileConfigurationExtensions.ProductionFileName, "from-production-file");

        Assert.Equal("from-appsettings", Resolve(Environments.Development));
    }

    /// <summary>
    /// The Worker has no secrets file of its own; it reads the API project's.
    /// Without this it boots with no Mongo connection string and fails at the
    /// first job rather than at startup.
    /// </summary>
    [Fact]
    public void The_worker_reads_the_api_projects_file_from_its_own_content_root()
    {
        var worker = Path.Combine(_root, "Vni.Ielts.Worker");
        var api = Path.Combine(_root, "Vni.Ielts.Api");
        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(api);

        File.WriteAllText(
            Path.Combine(api, SecretsFileConfigurationExtensions.DevelopFileName),
            Json("from-api-folder"));

        Assert.Equal("from-api-folder", Resolve(Environments.Development, worker));
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private const string Section = "SecretsFileProbe";
    private const string Key = "Value";
    private const string ConfigurationKey = $"{Section}:{Key}";
    private const string EnvironmentKey = $"{Section}__{Key}";

    private string? Resolve(string environment, string? contentRoot = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = contentRoot ?? _root,
            EnvironmentName = environment,
            ApplicationName = "Vni.Ielts.Tests",
            Args = [],
        });

        builder.AddVniSecretsFile();

        return builder.Configuration[ConfigurationKey];
    }

    private void WriteAppSettings(string value) =>
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), Json(value));

    private void Write(string fileName, string value) =>
        File.WriteAllText(Path.Combine(_root, fileName), Json(value));

    private static string Json(string value) =>
        "{\"" + Section + "\":{\"" + Key + "\":\"" + value + "\"}}";
}
