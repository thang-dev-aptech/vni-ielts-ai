using System.Text.Json.Nodes;
using Json.Schema;

namespace Vni.Ielts.Infrastructure.Ai.Extraction;

/// <summary>
/// The exam-extraction JSON Schema — loaded once, applied to every response.
///
/// <para>
/// <b>The file is found by walking up from the running assembly, not from the
/// working directory.</b> <c>Directory.GetCurrentDirectory()</c> is whatever
/// the process happened to be launched from: the repository root for
/// <c>dotnet run</c>, a <c>bin/Debug</c> folder for a test host, <c>/app</c> in
/// a container. The Worker's exam-schema registration takes that route and its
/// DI tests fail on it; this one does not repeat the mistake.
/// </para>
///
/// <para>
/// <b>Loaded lazily, so composition does not touch the disk.</b> Registering
/// the parser must not require the schema to exist — an install with no
/// provider configured never parses, and a startup that reads a file it will
/// not use turns a missing contract into a boot failure for a feature nobody
/// enabled.
/// </para>
/// </summary>
public static class ExamExtractionSchema
{
    public const string SchemaId = "https://vni.edu.vn/schemas/exam-extraction.schema.json";

    private const string RelativePath = "contracts/schemas/exam-extraction.schema.json";

    private static readonly Lazy<JsonSchema> Lazy = new(
        () => JsonSchema.FromText(LoadText()), LazyThreadSafetyMode.ExecutionAndPublication);

    public static JsonSchema Instance => Lazy.Value;

    public static string LoadText() => File.ReadAllText(ResolveSchemaPath());

    public static JsonNode LoadNode() => JsonNode.Parse(LoadText())
        ?? throw new InvalidOperationException("The exam extraction schema did not parse.");

    private static string ResolveSchemaPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, RelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "exam-extraction.schema.json was not found under contracts/schemas.");
    }
}
