using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vni.Ielts.Application.Dictation;
using Vni.Ielts.Domain.Dictation;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Dictation sets read from <c>fixtures/dictation</c> once, at construction.
///
/// <b>Read-only, in memory, and registered as a singleton.</b> There is no
/// authoring surface for dictation yet, so there is nothing to persist and no
/// write path to design — putting it in Mongo now would be building a
/// repository for content that only ever arrives from a file. When the CMS can
/// author a set, the port stays and this implementation is replaced.
///
/// <b>The sentence text lives only here and in the comparer.</b> It is never
/// part of any view a client receives before an attempt. → threat `T7`
/// </summary>
public sealed class FixtureDictationCatalogue : IDictationCatalogue
{
    private readonly Dictionary<string, DictationSet> _sets = [];

    public FixtureDictationCatalogue(ILogger<FixtureDictationCatalogue> logger)
    {
        if (Locate() is not { } directory)
        {
            logger.LogInformation("No fixtures/dictation directory found; dictation is empty.");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f))
        {
            try
            {
                var raw = JsonSerializer.Deserialize<SetFile>(
                    File.ReadAllText(file),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (raw is null || raw.Id is null) continue;

                _sets[raw.Id] = new DictationSet(
                    raw.Id,
                    raw.Title ?? raw.Id,
                    raw.Description ?? string.Empty,
                    [.. (raw.Sentences ?? []).Select(s =>
                        new DictationSentence(s.Order, s.Audio ?? string.Empty, s.Text ?? string.Empty))]);
            }
            catch (JsonException e)
            {
                // Loudly. A silently empty list sends someone hunting through
                // the UI for a bug that is a comma in a fixture.
                logger.LogError(e, "Dictation fixture {File} could not be read.", Path.GetFileName(file));
            }
        }

        logger.LogInformation("Loaded {Count} dictation set(s).", _sets.Count);
    }

    public IReadOnlyList<DictationSet> List() => [.. _sets.Values.OrderBy(s => s.Title)];

    public DictationSet? Find(string id) => _sets.GetValueOrDefault(id);

    private static string? Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "fixtures", "dictation");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        return null;
    }

    private sealed record SetFile(
        string? Id, string? Title, string? Description, List<SentenceFile>? Sentences);

    private sealed record SentenceFile(int Order, string? Audio, string? Text);
}

/// <summary>
/// Dictation audio, from the same fixtures directory.
///
/// Separate from <see cref="FixtureAssetStore"/> rather than sharing it: the
/// two resolve into different roots, and one store that takes a root as an
/// argument is one call site away from being pointed at the wrong one.
/// </summary>
public sealed class FixtureDictationAssetStore : IDictationAssetStore
{
    private readonly string? _root;

    public FixtureDictationAssetStore()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "fixtures", "dictation", "assets");
            if (Directory.Exists(candidate))
            {
                _root = Path.GetFullPath(candidate);
                return;
            }

            directory = directory.Parent;
        }
    }

    public Task<DictationAsset?> OpenAsync(string reference, CancellationToken ct) =>
        Task.FromResult(Open(reference));

    private DictationAsset? Open(string reference)
    {
        if (_root is null) return null;
        if (!reference.StartsWith("assets/", StringComparison.Ordinal)) return null;

        var segments = reference["assets/".Length..].Split('/');
        if (segments.Any(s => s is "" or "." or "..")) return null;

        var resolved = Path.GetFullPath(Path.Combine(_root, Path.Combine(segments)));

        // The check that holds: a resolved path outside the root is refused
        // however it got that way.
        if (!resolved.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return null;

        if (!File.Exists(resolved)) return null;

        var file = new FileInfo(resolved);

        return new DictationAsset(
            File.OpenRead(resolved),
            Path.GetExtension(resolved).ToLowerInvariant() switch
            {
                ".m4a" or ".mp4" => "audio/mp4",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" or ".opus" => "audio/ogg",
                // Never guessed from the reference. An unknown extension is
                // served as bytes, not as whatever a browser sniffs it into.
                _ => "application/octet-stream",
            },
            file.Length,
            $"\"{file.Length:x}-{file.LastWriteTimeUtc.Ticks:x}\"");
    }
}
