using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vni.Ielts.Infrastructure.Assessment;

/// <summary>
/// A versioned rubric artifact loaded from JSON — descriptors, hash, provenance.
///
/// <b>Not hard-coded in an adapter.</b> Band descriptors carry copyright questions
/// (`H-8a`); the artifact records which version and source were in force, and
/// the hash makes silent drift visible.
/// </summary>
public sealed record WritingRubricArtifact(
    string Version,
    string DescriptorSource,
    DateOnly EffectiveDate,
    string ContentHash,
    string PromptVersion,
    IReadOnlyList<string> Criteria,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Descriptors);

/// <summary>Loads and validates rubric artifacts from disk.</summary>
public static class WritingRubricLoader
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Finds the repository <c>fixtures/assessment</c> directory by walking up
    /// from the executing assembly, mirroring <see cref="Content.FixtureAssetStore"/>.
    /// </summary>
    public static string? FindFixturesRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "fixtures", "assessment");
            if (Directory.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    public static WritingRubricArtifact Load(string? path, string? expectedHash)
    {
        var resolved = ResolvePath(path);
        var bytes = File.ReadAllBytes(resolved);
        VerifyHash(bytes, expectedHash);

        var dto = JsonSerializer.Deserialize<RubricArtifactDto>(bytes, Json)
            ?? throw new InvalidOperationException($"Rubric artifact '{resolved}' did not parse.");

        dto.Validate();

        var descriptors = dto.Descriptors.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<string, string>)kv.Value,
            StringComparer.Ordinal);

        return new WritingRubricArtifact(
            dto.Version,
            dto.DescriptorSource,
            DateOnly.Parse(dto.EffectiveDate),
            dto.ContentHash,
            dto.PromptVersion,
            dto.Criteria,
            descriptors);
    }

    private static string ResolvePath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full))
                throw new FileNotFoundException($"Rubric artifact not found at '{full}'.");

            return full;
        }

        var root = FindFixturesRoot()
            ?? throw new InvalidOperationException(
                "No rubric artifact path configured and fixtures/assessment was not found.");

        var fallback = Path.Combine(root, "writing-rubric-v1.json");
        if (!File.Exists(fallback))
            throw new FileNotFoundException($"Default rubric artifact not found at '{fallback}'.");

        return fallback;
    }

    private static void VerifyHash(byte[] bytes, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash)) return;

        var computed = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        if (!string.Equals(computed, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Rubric artifact hash mismatch. The file on disk differs from "
                + "Assessment:Writing:RubricContentHash — descriptors may have changed "
                + "without a version bump.");
        }
    }

    /// <summary>Formats descriptors for inclusion in a model prompt.</summary>
    public static string FormatDescriptorsForPrompt(WritingRubricArtifact artifact)
    {
        var sb = new StringBuilder();

        foreach (var criterion in artifact.Criteria)
        {
            sb.AppendLine($"## {criterion}");

            if (!artifact.Descriptors.TryGetValue(criterion, out var bands)) continue;

            foreach (var (band, text) in bands.OrderByDescending(b => decimal.Parse(b.Key)))
                sb.AppendLine($"- Band {band}: {text}");

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private sealed class RubricArtifactDto
    {
        public required string Version { get; init; }
        public required string DescriptorSource { get; init; }
        public required string EffectiveDate { get; init; }
        public required string ContentHash { get; init; }
        public required string PromptVersion { get; init; }
        public required List<string> Criteria { get; init; }

        [JsonPropertyName("descriptors")]
        public required Dictionary<string, Dictionary<string, string>> Descriptors { get; init; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Version))
                throw new InvalidOperationException("Rubric artifact must name a version.");

            if (Criteria.Count == 0)
                throw new InvalidOperationException("Rubric artifact must list criteria.");

            if (string.IsNullOrWhiteSpace(DescriptorSource))
                throw new InvalidOperationException("Rubric artifact must record descriptorSource.");
        }
    }
}
