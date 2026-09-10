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
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Descriptors,
    bool IsV2 = false,
    WritingTaskTypeArtifact? Task1 = null,
    WritingTaskTypeArtifact? Task2 = null);

public sealed record WritingTaskTypeArtifact(
    IReadOnlyList<string> Criteria,
    IReadOnlyDictionary<string, WritingDescriptorSet> Descriptors);

/// <summary>
/// Shared bands, or a variant split (Task 1 Task Achievement only).
/// </summary>
public sealed record WritingDescriptorSet(
    IReadOnlyDictionary<string, string> Shared,
    IReadOnlyDictionary<string, string>? Academic = null,
    IReadOnlyDictionary<string, string>? GeneralTraining = null);


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

        if (dto.TaskTypes is not null)
            return dto.ToV2();

        var descriptors = (dto.Descriptors ?? [])
            .ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<string, string>)kv.Value,
                StringComparer.Ordinal);

        return new WritingRubricArtifact(
            dto.Version,
            dto.DescriptorSource,
            DateOnly.Parse(dto.EffectiveDate),
            dto.ContentHash,
            dto.PromptVersion,
            dto.Criteria ?? [],
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
    public static string FormatDescriptorsForPrompt(WritingRubricArtifact artifact) =>
        FormatDescriptorsForPrompt(artifact, taskNumber: 2, generalTraining: false);

    public static IReadOnlyList<string> CriteriaFor(WritingRubricArtifact artifact, int? taskNumber)
    {
        if (artifact.IsV2)
        {
            if (taskNumber == 1 && artifact.Task1 is { } t1) return t1.Criteria;
            if (artifact.Task2 is { } t2) return t2.Criteria;
        }

        return artifact.Criteria;
    }

    public static string FormatDescriptorsForPrompt(
        WritingRubricArtifact artifact, int? taskNumber, bool generalTraining)
    {
        if (artifact.IsV2)
        {
            var task = taskNumber == 1 ? artifact.Task1 : artifact.Task2;
            if (task is null) return string.Empty;

            var sb = new StringBuilder();
            foreach (var criterion in task.Criteria)
            {
                if (!task.Descriptors.TryGetValue(criterion, out var set)) continue;

                sb.AppendLine($"## {criterion}");
                var bands = SelectBands(set, taskNumber == 1 && criterion == "taskAchievement", generalTraining);
                foreach (var (band, text) in bands.OrderByDescending(b => decimal.Parse(b.Key)))
                    sb.AppendLine($"- Band {band}: {text}");

                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        var flat = new StringBuilder();
        foreach (var criterion in artifact.Criteria)
        {
            flat.AppendLine($"## {criterion}");

            if (!artifact.Descriptors.TryGetValue(criterion, out var bands)) continue;

            foreach (var (band, text) in bands.OrderByDescending(b => decimal.Parse(b.Key)))
                flat.AppendLine($"- Band {band}: {text}");

            flat.AppendLine();
        }

        return flat.ToString().TrimEnd();
    }

    private static IReadOnlyDictionary<string, string> SelectBands(
        WritingDescriptorSet set, bool variantSplit, bool generalTraining)
    {
        if (!variantSplit) return set.Shared;

        return generalTraining
            ? set.GeneralTraining ?? set.Shared
            : set.Academic ?? set.Shared;
    }

    private sealed class RubricArtifactDto
    {
        public required string Version { get; init; }
        public required string DescriptorSource { get; init; }
        public required string EffectiveDate { get; init; }
        public required string ContentHash { get; init; }
        public required string PromptVersion { get; init; }
        public List<string>? Criteria { get; init; }

        [JsonPropertyName("descriptors")]
        public Dictionary<string, Dictionary<string, string>>? Descriptors { get; init; }

        [JsonPropertyName("taskTypes")]
        public TaskTypesDto? TaskTypes { get; init; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Version))
                throw new InvalidOperationException("Rubric artifact must name a version.");

            if (string.IsNullOrWhiteSpace(DescriptorSource))
                throw new InvalidOperationException("Rubric artifact must record descriptorSource.");

            if (ClaimsOfficialScale(DescriptorSource))
            {
                throw new InvalidOperationException(
                    "Rubric artifact descriptorSource must not claim to be the official IELTS scale. "
                    + "VNI authors its own descriptors (W-2).");
            }

            if (TaskTypes is not null)
            {
                if (TaskTypes.Task1 is null || TaskTypes.Task2 is null)
                    throw new InvalidOperationException("A v2 rubric must declare both task1 and task2.");

                return;
            }

            if (Criteria is null || Criteria.Count == 0)
                throw new InvalidOperationException("Rubric artifact must list criteria.");
        }

        public WritingRubricArtifact ToV2()
        {
            var task1 = TaskTypes!.Task1!.ToArtifact();
            var task2 = TaskTypes.Task2!.ToArtifact();

            return new WritingRubricArtifact(
                Version,
                DescriptorSource,
                DateOnly.Parse(EffectiveDate),
                ContentHash,
                PromptVersion,
                task2.Criteria,
                Flatten(task2),
                IsV2: true,
                Task1: task1,
                Task2: task2);
        }

        private static bool ClaimsOfficialScale(string source)
        {
            var text = source.Trim();
            if (text.Contains("vni-authored", StringComparison.OrdinalIgnoreCase)) return false;

            return text.Contains("official", StringComparison.OrdinalIgnoreCase)
                && text.Contains("ielts", StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Flatten(
            WritingTaskTypeArtifact task) =>
            task.Descriptors.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Shared,
                StringComparer.Ordinal);
    }

    private sealed class TaskTypesDto
    {
        public TaskTypeDto? Task1 { get; init; }
        public TaskTypeDto? Task2 { get; init; }
    }

    private sealed class TaskTypeDto
    {
        public required List<string> Criteria { get; init; }
        public required Dictionary<string, JsonElement> Descriptors { get; init; }

        public WritingTaskTypeArtifact ToArtifact()
        {
            var sets = new Dictionary<string, WritingDescriptorSet>(StringComparer.Ordinal);

            foreach (var (key, node) in Descriptors)
                sets[key] = ParseDescriptorSet(node);

            return new WritingTaskTypeArtifact(Criteria, sets);
        }

        private static WritingDescriptorSet ParseDescriptorSet(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object)
                return new WritingDescriptorSet(new Dictionary<string, string>());

            if (node.TryGetProperty("academic", out _) || node.TryGetProperty("generalTraining", out _))
            {
                return new WritingDescriptorSet(
                    Shared: ReadBandMap(node.TryGetProperty("academic", out var ac) ? ac : default)
                            ?? new Dictionary<string, string>(),
                    Academic: ReadBandMap(node.TryGetProperty("academic", out var a) ? a : default),
                    GeneralTraining: ReadBandMap(
                        node.TryGetProperty("generalTraining", out var gt) ? gt : default));
            }

            return new WritingDescriptorSet(ReadBandMap(node) ?? new Dictionary<string, string>());
        }

        private static Dictionary<string, string>? ReadBandMap(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object) return null;

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in node.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    map[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }

            return map;
        }
    }
}
