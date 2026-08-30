using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Vni.Ielts.Infrastructure.Configuration;

/// <summary>
/// Loads environment-specific secret files next to the API project.
///
/// <b>Three files, three jobs — see <c>secrets.README.md</c>.</b>
/// <list type="bullet">
/// <item><c>secrets.example.json</c> — committed template, no real keys</item>
/// <item><c>secrets.develop.json</c> — gitignored; loaded in Development (overrides appsettings)</item>
/// <item><c>secrets.production.json</c> — gitignored; optional mount in Production (env vars win)</item>
/// </list>
/// </summary>
public static class SecretsFileConfigurationExtensions
{
    public const string DevelopFileName = "secrets.develop.json";
    public const string ProductionFileName = "secrets.production.json";

    /// <summary>
    /// Development: append <see cref="DevelopFileName"/> so local secrets override appsettings.
    /// Production: register <see cref="ProductionFileName"/> ahead of environment variables.
    /// </summary>
    public static IHostApplicationBuilder AddVniSecretsFile(this IHostApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            AddJsonIfPresent(builder.Configuration, builder.Environment.ContentRootPath, DevelopFileName, reloadOnChange: true);
            return builder;
        }

        if (builder.Environment.IsProduction())
        {
            builder.ConfigureAppConfiguration((context, config) =>
                AddJsonIfPresent(config, context.HostingEnvironment.ContentRootPath, ProductionFileName, reloadOnChange: false));
        }

        return builder;
    }

    private static void AddJsonIfPresent(
        IConfigurationBuilder configuration,
        string contentRootPath,
        string fileName,
        bool reloadOnChange)
    {
        foreach (var path in ResolveCandidatePaths(contentRootPath, fileName))
        {
            if (!File.Exists(path)) continue;

            configuration.AddJsonFile(path, optional: true, reloadOnChange: reloadOnChange);
            return;
        }
    }

    internal static IEnumerable<string> ResolveCandidatePaths(string contentRootPath, string fileName)
    {
        yield return Path.Combine(contentRootPath, fileName);

        // Worker shares the API project's secrets file when run from its own output folder.
        var sharedApi = Path.GetFullPath(Path.Combine(contentRootPath, "..", "Vni.Ielts.Api", fileName));
        yield return sharedApi;
    }
}
