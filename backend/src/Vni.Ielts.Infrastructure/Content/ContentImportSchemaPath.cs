using Microsoft.Extensions.Configuration;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Where <c>exam.schema.json</c> is loaded from.
///
/// <para>
/// Configured path, if set, must exist — a missing file is a deployment
/// fault, not a reason to search elsewhere. The published copy next to the
/// assembly is preferred after that. Walking parents is a last resort for
/// <c>dotnet test</c> on a developer machine. A path that does not exist is
/// never returned.
/// </para>
/// </summary>
public static class ContentImportSchemaPath
{
    public const string RelativeFile = "contracts/schemas/exam.schema.json";

    public static string Resolve(
        IConfiguration configuration,
        string? baseDirectory = null,
        string? currentDirectory = null)
    {
        var configured = configuration["ContentImport:SchemaPath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!File.Exists(configured))
            {
                throw new FileNotFoundException(
                    "ContentImport:SchemaPath was set but the file does not exist.",
                    configured);
            }

            return configured;
        }

        var baseDir = baseDirectory ?? AppContext.BaseDirectory;
        var cwd = currentDirectory ?? Directory.GetCurrentDirectory();

        var published = Path.Combine(baseDir, "contracts", "schemas", "exam.schema.json");
        if (File.Exists(published)) return published;

        foreach (var startDir in new[] { baseDir, cwd })
        {
            if (string.IsNullOrWhiteSpace(startDir)) continue;

            var dir = new DirectoryInfo(startDir);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "contracts", "schemas", "exam.schema.json");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }

        throw new FileNotFoundException(
            "exam.schema.json was not found under contracts/schemas.");
    }
}
