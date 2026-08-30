using System.Text.Json.Nodes;
using Json.Schema;

namespace Vni.Ielts.Infrastructure.Ai.Writing;

/// <summary>
/// The writing evaluation JSON Schema — loaded once, validated on every response.
/// </summary>
public static class WritingEvaluationSchema
{
    public const string SchemaId = "https://vni.edu.vn/schemas/writing-evaluation.schema.json";

    private static readonly Lazy<JsonSchema> Lazy = new(Load);

    public static JsonSchema Instance => Lazy.Value;

    public static string LoadText()
    {
        var path = ResolveSchemaPath();
        return File.ReadAllText(path);
    }

    public static JsonNode LoadNode() => JsonNode.Parse(LoadText())
        ?? throw new InvalidOperationException("Writing evaluation schema did not parse.");

    private static JsonSchema Load()
    {
        var text = LoadText();
        return JsonSchema.FromText(text);
    }

    private static string ResolveSchemaPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "contracts", "schemas", "writing-evaluation.schema.json");

            if (File.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "writing-evaluation.schema.json was not found under contracts/schemas.");
    }
}
