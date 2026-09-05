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

    /// <summary>
    /// Turns the configured artifact path into a file on disk.
    ///
    /// ── Why a relative path gets more than one chance ─────────────────────
    ///
    /// <b>It used to get exactly one: <c>Path.GetFullPath(path)</c>, which
    /// resolves against the current working directory.</b> That made
    /// <c>"fixtures/assessment/writing-rubric-v1.json"</c> — the value shipped
    /// in <c>secrets.example.json</c>, so the value everybody copies — work
    /// only when the process happened to be started from the repository root,
    /// and throw everywhere else: from a test binary, from
    /// <c>dotnet run --project</c>, from a container.
    ///
    /// The failure is loud but badly placed. It comes out of
    /// <see cref="WritingSectionEvaluator"/>'s constructor, which DI resolves
    /// the first time a job is marked, so a worker that started cleanly dies on
    /// the first learner essay with a stack trace about a missing file — long
    /// after the configuration that caused it was accepted.
    ///
    /// So a relative path is now tried against the working directory, then the
    /// binary's own folder, then the repository's <c>fixtures/</c> root. An
    /// absolute path is still taken literally: someone who wrote one meant it.
    ///
    /// <b>What this does not fix, and it matters:</b> <c>backend/Dockerfile</c>
    /// builds from <c>backend/</c> and copies only <c>src/</c>, so
    /// <c>fixtures/</c> is not in the image at all. None of these three
    /// candidates exists in a container, and a production deployment that
    /// configures <c>Assessment</c> will fail here. The rubric has to ship with
    /// the image or be mounted beside the secrets before that configuration is
    /// used in production.
    /// </summary>
    private static string ResolvePath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (Path.IsPathRooted(path))
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Rubric artifact not found at '{path}'.");

                return path;
            }

            foreach (var candidate in RelativeCandidates(path))
            {
                if (File.Exists(candidate)) return candidate;
            }

            throw new FileNotFoundException(
                $"Rubric artifact '{path}' was not found relative to the working directory "
                + $"({Directory.GetCurrentDirectory()}), the application directory "
                + $"({AppContext.BaseDirectory}), or any fixtures root above either. Configure "
                + "an absolute Assessment:WritingMarking:RubricArtifactPath, or leave it unset to "
                + "use the bundled rubric.");
        }

        var root = FindFixturesRoot()
            ?? throw new InvalidOperationException(
                "No rubric artifact path configured and fixtures/assessment was not found.");

        var fallback = Path.Combine(root, "writing-rubric-v1.json");
        if (!File.Exists(fallback))
            throw new FileNotFoundException($"Default rubric artifact not found at '{fallback}'.");

        return fallback;
    }

    /// <summary>
    /// Where a relative artifact path might live, most explicit first.
    ///
    /// The repository candidate is derived from <see cref="FindFixturesRoot"/>,
    /// which already knows how to walk up to <c>fixtures/assessment</c> — so
    /// <c>"fixtures/assessment/writing-rubric-v1.json"</c> and
    /// <c>"assessment/writing-rubric-v1.json"</c> both land, and neither
    /// depends on where the process was launched from.
    /// </summary>
    private static IEnumerable<string> RelativeCandidates(string path)
    {
        yield return Path.GetFullPath(path);
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

        if (FindFixturesRoot() is not { } fixturesRoot) yield break;

        // fixtures/assessment → fixtures → the directory holding it.
        var fixtures = Directory.GetParent(fixturesRoot)?.FullName;
        if (fixtures is null) yield break;

        yield return Path.GetFullPath(Path.Combine(fixtures, path));

        if (Directory.GetParent(fixtures)?.FullName is { } repositoryRoot)
            yield return Path.GetFullPath(Path.Combine(repositoryRoot, path));
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
