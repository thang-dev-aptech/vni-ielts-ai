using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
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
///
/// ── Why the production file is spliced into the source list ───────────────
///
/// <b>The first version of this called <c>ConfigureAppConfiguration</c>, which
/// does not exist on <see cref="IHostApplicationBuilder"/>.</b> The whole
/// solution stopped compiling, so nothing loaded any secret file at all — the
/// failure was total rather than subtle, which is the only good thing about it.
///
/// The replacement has to satisfy two claims the README makes, and appending
/// to <c>builder.Configuration</c> satisfies neither in Production:
/// <list type="number">
/// <item><b>Environment variables always win.</b> They are how a real
/// deployment supplies a rotated key, and a mounted file that overrode them
/// would make rotation silently ineffective.</item>
/// <item><b>The file beats <c>appsettings*.json</c>.</b> A mounted secret that
/// lost to a committed default would be a file an operator fills in and then
/// watches do nothing.</item>
/// </list>
/// So the JSON source is moved to sit immediately below the last environment
/// variable source: above every file source, below the environment.
/// </summary>
public static class SecretsFileConfigurationExtensions
{
    public const string DevelopFileName = "secrets.develop.json";
    public const string ProductionFileName = "secrets.production.json";

    /// <summary>
    /// Development: splice <see cref="DevelopFileName"/> in above <c>appsettings*.json</c> and below
    /// the environment, so local secrets override committed defaults and nothing else.
    /// Production: the same for <see cref="ProductionFileName"/>.
    /// </summary>
    /// <summary>
    /// Environment variable that, when set to <c>off</c>, stops both files from
    /// being read. <b>For test hosts, which run as Development and would
    /// otherwise inherit whatever the developer's <c>secrets.develop.json</c>
    /// says.</b> Three suites learned this on 2026-09-04 alone: a real Google
    /// client id disabled the SSO stub, a real Writing rubric made a journey
    /// test call the provider, and an R2 recordings bucket sent a test's
    /// Speaking upload to Cloudflare. Pinning each key in each factory is the
    /// wrong layer; a test host says once that it wants no developer file.
    /// → <c>Vni.Ielts.Integration.Tests.TestHostIsolation</c>
    /// </summary>
    public const string SkipVariable = "VNI_SECRETS_FILE";

    public static bool IsSkipped =>
        string.Equals(Environment.GetEnvironmentVariable(SkipVariable), "off", StringComparison.OrdinalIgnoreCase);

    public static IHostApplicationBuilder AddVniSecretsFile(this IHostApplicationBuilder builder)
    {
        if (IsSkipped) return builder;

        if (builder.Environment.IsDevelopment())
        {
            /*
             * <b>Spliced, not appended — the same as Production, for a reason
             * that only showed up in tests.</b> Appending put the file above
             * everything `CreateBuilder` had registered, including the
             * in-memory settings a test host adds with `UseSetting(...)`. So
             * `SsoAppFactory`'s `Sso:Google:ClientId = ""` — the line that
             * keeps the suite off the real Google — lost to the real client id
             * in the developer's secrets file, the stub provider never
             * engaged, and eleven `SessionsTests` failed with a null redirect
             * on any machine with a filled `secrets.develop.json`
             * (2026-09-04). The README's claim is "overrides appsettings",
             * and that is all the file should outrank.
             */
            AddJsonIfPresent(
                builder.Configuration,
                builder.Environment.ContentRootPath,
                DevelopFileName,
                reloadOnChange: true,
                belowEnvironmentVariables: true);

            return builder;
        }

        if (builder.Environment.IsProduction())
        {
            AddJsonIfPresent(
                builder.Configuration,
                builder.Environment.ContentRootPath,
                ProductionFileName,
                reloadOnChange: false,
                belowEnvironmentVariables: true);
        }

        return builder;
    }

    private static void AddJsonIfPresent(
        IConfigurationBuilder configuration,
        string contentRootPath,
        string fileName,
        bool reloadOnChange,
        bool belowEnvironmentVariables)
    {
        foreach (var path in ResolveCandidatePaths(contentRootPath, fileName))
        {
            if (!File.Exists(path)) continue;

            configuration.AddJsonFile(path, optional: true, reloadOnChange: reloadOnChange);

            if (belowEnvironmentVariables) MoveLastSourceBelowEnvironmentVariables(configuration);

            return;
        }
    }

    /// <summary>
    /// Moves the source just added so that the environment still overrides it.
    ///
    /// <b>"Before the first environment source" is the answer that looks right
    /// and is wrong.</b> A host builder registers environment variables twice:
    /// once early for host configuration (the <c>DOTNET_</c> and
    /// <c>ASPNETCORE_</c> prefixes, which decide the environment name and
    /// content root) and once late for application configuration. The early
    /// one sits <i>above</i> <c>appsettings.json</c>, so splicing in beside it
    /// would put the mounted secrets file below the committed defaults —
    /// exactly the trap this method exists to avoid.
    ///
    /// So the target is the <i>last</i> environment source. Failing that — a
    /// configuration built without one — the file goes directly above the last
    /// file source, which still beats <c>appsettings*.json</c>.
    /// </summary>
    private static void MoveLastSourceBelowEnvironmentVariables(IConfigurationBuilder configuration)
    {
        var sources = configuration.Sources;
        var addedIndex = sources.Count - 1;

        var insertAt = LastIndexOf<EnvironmentVariablesConfigurationSource>(sources, addedIndex);

        if (insertAt < 0) insertAt = LastIndexOf<FileConfigurationSource>(sources, addedIndex) + 1;
        if (insertAt >= addedIndex) return;

        var added = sources[addedIndex];
        sources.RemoveAt(addedIndex);
        sources.Insert(insertAt, added);
    }

    /// <summary>Last index of <typeparamref name="T"/> below <paramref name="exclusiveUpperBound"/>, or -1.</summary>
    private static int LastIndexOf<T>(IList<IConfigurationSource> sources, int exclusiveUpperBound)
    {
        for (var i = exclusiveUpperBound - 1; i >= 0; i--)
        {
            if (sources[i] is T) return i;
        }

        return -1;
    }

    internal static IEnumerable<string> ResolveCandidatePaths(string contentRootPath, string fileName)
    {
        yield return Path.Combine(contentRootPath, fileName);

        // Worker shares the API project's secrets file when run from its own output folder.
        var sharedApi = Path.GetFullPath(Path.Combine(contentRootPath, "..", "Vni.Ielts.Api", fileName));
        yield return sharedApi;
    }
}
