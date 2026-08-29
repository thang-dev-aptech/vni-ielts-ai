using Vni.Ielts.Domain.Content;

namespace Vni.Ielts.Application.Content;

/// <summary>
/// Checks a source's recorded files against what is on disk now.
///
/// <b>Why a rights registry cares about hashes.</b> A permission is granted
/// for a particular body of material. If the bytes change afterwards — a file
/// replaced, a folder re-exported, a PDF swapped — the grant no longer covers
/// what is there, and nothing else in the system would notice. So the record
/// carries the hash, and a change is detectable rather than assumed away.
///
/// Most files have no recorded hash yet, and that answer is
/// <see cref="ContentFileState.NotHashed"/> rather than a quiet pass.
/// </summary>
public sealed class VerifyContentSource(IContentRightsRegistry registry, IContentFileProbe probe)
{
    /// <returns>
    /// <c>null</c> when no such source is registered — <b>not</b> an empty
    /// report, which would read as "checked, nothing wrong" when in fact
    /// nothing was checked.
    /// </returns>
    public async Task<ContentIntegrityReport?> RunAsync(
        ContentSourceId id, CancellationToken ct)
    {
        var source = await registry.FindAsync(id, ct);
        if (source is null) return null;

        var observed = new Dictionary<string, ContentFileObservation>(StringComparer.Ordinal);

        foreach (var file in source.Files)
        {
            if (observed.ContainsKey(file.RelativePath)) continue;
            observed[file.RelativePath] = await probe.ObserveAsync(file.RelativePath, ct);
        }

        return ContentIntegrity.Compare(source, observed);
    }
}
