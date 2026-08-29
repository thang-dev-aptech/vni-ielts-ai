using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vni.Ielts.Application.Content;
using Vni.Ielts.Domain.Content;
using Vni.Ielts.Infrastructure.Persistence.Content;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Wiring for the content rights registry.
///
/// <b>A separate extension rather than a block inside
/// <c>AddInfrastructure</c>, and only because of a file boundary.</b> It
/// belongs beside every other port binding; folding it in is a requested
/// follow-up. Splitting DI across two entry points is how a service ends up
/// registered in the API and missing in the Worker.
/// </summary>
public static class ContentRightsRegistration
{
    /// <summary>
    /// Where the source material would be, if this deployment had any.
    ///
    /// <b>Almost never does.</b> <c>/exam/</c> and <c>/Đề IELTS/</c> are
    /// gitignored and are not in the container image, so the honest default is
    /// the working directory and the honest result of a verification call
    /// there is "missing". Point <c>Content:RootPath</c> at a checkout that
    /// has the material to get a real answer.
    /// </summary>
    public const string RootPathKey = "Content:RootPath";

    public static IServiceCollection AddContentRights(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IContentRightsRegistry, MongoContentRightsRegistry>();

        var root = configuration[RootPathKey];
        var resolved = string.IsNullOrWhiteSpace(root)
            ? Directory.GetCurrentDirectory()
            : root;

        services.AddSingleton<IContentFileProbe>(_ => new FileSystemContentProbe(resolved));

        services.AddScoped<ContentPublishGuard>();
        services.AddScoped<VerifyContentSource>();

        return services;
    }

    /// <summary>
    /// Writes the seeded records for any source that has none yet.
    ///
    /// <b>Fills gaps; never overwrites.</b> A grant is an act by a named
    /// reviewer, and a deployment that rewrote existing records would silently
    /// revoke one the day somebody made it. → <see cref="IContentRightsRegistry.RegisterIfAbsentAsync"/>
    ///
    /// <b>Not required for the gate to be safe.</b> An unseeded registry
    /// refuses everything, because a source with no record has no rights. The
    /// seed exists so an operator can see what material this project holds,
    /// not so that publishing works.
    /// </summary>
    public static async Task SeedContentRightsAsync(
        this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<IContentRightsRegistry>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Vni.Ielts.ContentRights");

        var added = 0;

        foreach (var source in ContentRightsSeed.Sources)
        {
            if (source.AllowedEnvironments.Contains(ContentEnvironment.LearnerProduction))
            {
                // Belt and braces over the unit test. A publication right must
                // never enter the registry from a source file, whatever anybody
                // edits later.
                throw new InvalidOperationException(
                    $"The content rights seed grants learner-production to '{source.Id}'. "
                    + "M-53 is unanswered and a grant needs a named reviewer and a licence "
                    + "reference, neither of which a source file can supply.");
            }

            if (await registry.RegisterIfAbsentAsync(source, ct)) added++;
        }

        logger.LogInformation(
            "Content rights registry: {Added} new of {Total} known sources. None holds a "
            + "learner-production right (M-53 is open).",
            added, ContentRightsSeed.Sources.Count);
    }
}
