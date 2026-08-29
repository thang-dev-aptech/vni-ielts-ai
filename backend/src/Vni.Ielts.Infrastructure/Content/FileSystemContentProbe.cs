using System.Security.Cryptography;
using Vni.Ielts.Application.Content;
using Vni.Ielts.Domain.Content;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Reads a recorded file and reports what is actually there.
///
/// <b>"Not there" is the normal answer.</b> <c>/exam/</c> and
/// <c>/Đề IELTS/</c> are gitignored, so CI, a container image and a clean
/// checkout all have none of the material. That is reported as
/// <see cref="ContentFileObservation.Exists"/> <c>= false</c> and never as an
/// exception, because an absent source is a fact about the deployment rather
/// than a fault.
///
/// <b>Registry paths are data, so they are confined.</b> A record whose path
/// walked out of the content root would turn a verification call into an
/// arbitrary file reader; the domain refuses to build such a
/// <see cref="ContentFileRef"/>, and this refuses to follow one that arrived
/// some other way.
/// </summary>
public sealed class FileSystemContentProbe(string contentRoot) : IContentFileProbe
{
    private readonly string _root = Path.GetFullPath(contentRoot);

    public async Task<ContentFileObservation> ObserveAsync(
        string relativePath, CancellationToken ct)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));

        if (!full.StartsWith(
                _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            && !string.Equals(full, _root, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{relativePath}' resolves outside the content root.", nameof(relativePath));
        }

        if (!File.Exists(full)) return new ContentFileObservation(false, null);

        try
        {
            // Streamed. Some of this material is a 1.3 GB archive and several
            // audio files are tens of megabytes; reading one into memory to
            // decide whether it changed is how a verification call takes a
            // process down.
            await using var stream = new FileStream(
                full, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);

            var hash = await SHA256.HashDataAsync(stream, ct);

            return new ContentFileObservation(true, Convert.ToHexStringLower(hash));
        }
        catch (IOException)
        {
            // Present but unreadable — locked, or on a volume that went away.
            // Not "unchanged": nothing was verified.
            return new ContentFileObservation(false, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ContentFileObservation(false, null);
        }
    }
}
