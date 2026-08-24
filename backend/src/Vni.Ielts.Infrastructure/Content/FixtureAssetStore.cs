using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Serves exam media from the development fixtures directory.
///
/// <b>A stopgap, and named like one.</b> Real packages arrive as uploaded ZIPs
/// and their media belongs in object storage — MinIO is already in the local
/// stack for it. This exists so Listening has audio to play while that path is
/// unbuilt, and it is registered only in Development.
///
/// <b>The path is treated as hostile even though it comes from our own
/// fixture.</b> An asset reference reaches this method having travelled
/// through an authored package, a database and an HTTP route, and "it is our
/// own file" stops being true the first time a real ZIP is imported. Three
/// checks, in order: the reference must match the schema's own pattern, it
/// must contain no traversal segment, and the resolved absolute path must
/// still sit inside the assets directory. The third is the one that actually
/// holds — the first two are cheap ways to fail early with a clear reason.
/// → `zip-ingestion-security.md`, threat `T9`
/// </summary>
public sealed class FixtureAssetStore : IExamAssetStore
{
    private readonly string? _root;

    public FixtureAssetStore()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "fixtures", "exams", "assets");
            if (Directory.Exists(candidate))
            {
                _root = Path.GetFullPath(candidate);
                return;
            }

            directory = directory.Parent;
        }
    }

    public ExamAsset? Open(string reference)
    {
        if (_root is null) return null;

        // `assets/listening-part-1.m4a` — the schema's own shape.
        if (!reference.StartsWith("assets/", StringComparison.Ordinal)) return null;

        var relative = reference["assets/".Length..];
        if (relative.Length == 0) return null;

        var segments = relative.Split('/', StringSplitOptions.None);
        if (segments.Any(s => s is "" or "." or "..")) return null;

        var resolved = Path.GetFullPath(Path.Combine(_root, Path.Combine(segments)));

        // The check that holds even if the two above are wrong.
        if (!resolved.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return null;

        if (!File.Exists(resolved)) return null;

        return new ExamAsset(
            File.OpenRead(resolved),
            Path.GetExtension(resolved).ToLowerInvariant() switch
            {
                ".m4a" or ".mp4" => "audio/mp4",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" or ".opus" => "audio/ogg",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                // Never guessed from the reference. An unknown extension is
                // served as bytes, not as whatever a browser feels like
                // sniffing it into.
                _ => "application/octet-stream",
            });
    }
}
