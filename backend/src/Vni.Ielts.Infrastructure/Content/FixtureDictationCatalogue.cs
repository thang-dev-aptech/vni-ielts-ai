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
        : this(logger, Locate())
    {
    }

    /// <summary>
    /// The same catalogue, reading a directory named outright.
    ///
    /// <b>A seam, because the shelf being empty is the case worth testing and
    /// <see cref="Locate"/> cannot produce it.</b> It walks up from the running
    /// assembly until it finds <c>fixtures/dictation</c>, so inside this
    /// repository it always finds the real one — which means the empty shelf,
    /// the state a deployment can actually be in, was the one state no test
    /// could reach. Passing the directory in is what makes it reachable.
    /// </summary>
    public FixtureDictationCatalogue(ILogger<FixtureDictationCatalogue> logger, string? directory)
    {
        if (directory is null)
        {
            WarnTheShelfIsEmpty(logger, "no fixtures/dictation directory was found");
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

        if (_sets.Count == 0)
        {
            // The subtler half of the same silence. A directory that exists but
            // holds nothing readable took this branch and reported "Loaded 0"
            // at Information — which is the empty shelf again, one `if` further
            // along, and reads like a success.
            WarnTheShelfIsEmpty(logger, $"{directory} holds no readable dictation set");
            return;
        }

        logger.LogInformation("Loaded {Count} dictation set(s).", _sets.Count);
    }

    /// <summary>
    /// <b>Warning, not Information, and the reason is three weeks long.</b>
    /// `fixtures/dictation` was deleted on 2026-08-28 in a commit about
    /// something else. This class did exactly what it was told — found nothing,
    /// wrote one Information line, served an empty list — and `/dictation`, one
    /// of the four modules in the header, was blank until 2026-09-18 with no
    /// error anywhere. Information is the level you read once you already
    /// suspect a problem; an empty content store is how you find out you have
    /// one.
    ///
    /// <b>It stops at a log line and a startup announcement on purpose.</b>
    /// Readiness answers "can this instance serve a request", and an empty
    /// dictation shelf does not stop this API serving exams, sign-in or Writing
    /// marking. Failing readiness on it would let one missing content package
    /// take the whole product down — an outage caused by the monitoring choice
    /// rather than by the fault. → `StartupConfiguration.ContentInventoryWarnings`
    /// </summary>
    private static void WarnTheShelfIsEmpty(ILogger logger, string reason) =>
        logger.LogWarning(
            "Dictation catalogue is empty: {Reason}. /dictation will serve an empty list and "
            + "nothing downstream will say so.",
            reason);

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
