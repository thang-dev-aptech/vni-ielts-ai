namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Copies an immutable upload from quarantine storage to a local temp file
/// before validation, and cleans it up afterward.
///
/// <b>Why this exists at all.</b> <see cref="PackageStructuralValidator.ValidateZip"/>
/// needs a seekable stream — <see cref="System.IO.Compression.ZipArchive"/>
/// reads its central directory from the end of the file — and a GridFS
/// download stream is forward-only (confirmed directly: a
/// <c>GridFSForwardOnlyDownloadStream</c> throws <c>NotSupportedException</c>
/// on <c>Seek</c>). Buffering the whole upload into a <c>byte[]</c> instead
/// was the original approach; it worked until a real 1.3 GB package proved it
/// does not scale — that much RAM per package being processed is a real
/// stability risk, not a hypothetical one. A local temp file gives
/// <c>ZipArchive</c> the seek it needs while keeping memory use bounded to
/// one copy buffer, the same way the upload endpoint already handles a large
/// multipart body via <c>FileBufferingReadStream</c>.
/// </summary>
public static class PackageContentStaging
{
    public static async Task<string> StageToTempFileAsync(Stream source, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vni-package-{Guid.NewGuid():n}.tmp");
        await using (var file = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
        {
            await source.CopyToAsync(file, ct);
        }

        return path;
    }

    /// <summary>Opens the staged file read-only and deletes it once the caller is done with it — including on failure.</summary>
    public static async Task<T> WithStagedContentAsync<T>(
        Stream source, Func<FileStream, Task<T>> action, CancellationToken ct)
    {
        var path = await StageToTempFileAsync(source, ct);
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
            return await action(stream);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
