using Vni.Ielts.Application.Media;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Exams;

/// <summary>
/// Every <c>assetRef</c> an exam version names, and whether the store can
/// open it.
///
/// <b>Resolution is server-side on purpose.</b> The CMS used to invent a
/// present/missing list from a browser mock. Approve and publish gates that
/// list, so a client-only answer would let a version with no audio through.
/// </summary>
public static class ExamAssetInventory
{
    public static IReadOnlyList<ExamAssetReference> Collect(ExamVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var collected = new List<ExamAssetReference>();

        foreach (var section in version.Sections.OrderBy(s => s.Order))
        {
            foreach (var part in section.Parts.OrderBy(p => p.Order))
            {
                var usedAt = part.Title is { Length: > 0 } title
                    ? $"{section.Module} · {title}"
                    : $"{section.Module} · Part {part.Order}";

                Add(collected, part.AudioKey, usedAt, "audio");
                Add(collected, part.ImageKey, usedAt, "image");

                foreach (var question in part.Questions.OrderBy(q => q.Order))
                {
                    if (question.Group?.Image is not { Length: > 0 } groupImage) continue;
                    Add(collected, groupImage, $"{usedAt} · câu {question.Order}", "image");
                }
            }
        }

        return collected;
    }

    public static async Task<IReadOnlyList<ExamAssetInfo>> ResolveAsync(
        ExamVersion version,
        IExamAssetStore assets,
        IMediaAssetRepository media,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(media);

        var references = Collect(version);
        var cache = new Dictionary<string, ResolvedBytes>(StringComparer.Ordinal);
        var resolved = new List<ExamAssetInfo>(references.Count);

        foreach (var reference in references)
        {
            if (!cache.TryGetValue(reference.Ref, out var bytes))
            {
                bytes = await OpenAsync(reference.Ref, assets, media, ct);
                cache[reference.Ref] = bytes;
            }

            resolved.Add(new ExamAssetInfo(
                reference.Ref,
                reference.UsedAt,
                reference.Kind,
                bytes.FileName,
                bytes.SizeBytes,
                bytes.Checksum,
                bytes.Resolved,
                bytes.MediaId));
        }

        return resolved;
    }

    private static void Add(
        List<ExamAssetReference> collected, string? assetRef, string usedAt, string kind)
    {
        if (string.IsNullOrWhiteSpace(assetRef)) return;
        collected.Add(new ExamAssetReference(assetRef, usedAt, kind));
    }

    private static async Task<ResolvedBytes> OpenAsync(
        string assetRef, IExamAssetStore assets, IMediaAssetRepository media, CancellationToken ct)
    {
        var mediaId = MediaIdOf(assetRef);
        var library = mediaId is null ? null : await media.FindAsync(mediaId, ct);
        var opened = await assets.OpenAsync(assetRef, ct);

        if (opened is not null)
        {
            await using (opened.Content)
            {
                return new ResolvedBytes(
                    FileName: library?.FileName ?? DisplayName(assetRef),
                    SizeBytes: library?.Bytes ?? opened.ContentLength ?? 0,
                    Checksum: library?.Checksum ?? opened.ETag,
                    Resolved: true,
                    MediaId: mediaId);
            }
        }

        return new ResolvedBytes(
            FileName: library?.FileName ?? DisplayName(assetRef),
            SizeBytes: library?.Bytes ?? 0,
            Checksum: library?.Checksum,
            Resolved: false,
            MediaId: mediaId);
    }

    private static string? MediaIdOf(string assetRef)
    {
        const string prefix = "media/";
        if (!assetRef.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var id = assetRef[prefix.Length..];
        return id.Length == 0 ? null : id;
    }

    /// <summary>
    /// Display only — never a storage key, and never an audio <c>src</c>.
    /// </summary>
    private static string DisplayName(string assetRef)
    {
        var slash = assetRef.LastIndexOf('/');
        return slash < 0 || slash == assetRef.Length - 1 ? assetRef : assetRef[(slash + 1)..];
    }

    private readonly record struct ResolvedBytes(
        string FileName, long SizeBytes, string? Checksum, bool Resolved, string? MediaId);
}

public sealed record ExamAssetReference(string Ref, string UsedAt, string Kind);

public sealed record ExamAssetInfo(
    string Ref,
    string UsedAt,
    string Kind,
    string FileName,
    long SizeBytes,
    string? Checksum,
    bool Resolved,
    string? MediaId);
