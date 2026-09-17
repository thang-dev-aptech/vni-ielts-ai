using System.Security.Cryptography;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Application.Media;

/// <summary>
/// Store one new file in the CMS media library.
///
/// <para>
/// <b>The order of the checks is the design.</b> Empty → recognisable → within
/// the kind's ceiling, each refused before a byte is stored. A file that fails
/// any of them must leave no asset record and no blob behind: the library is
/// what authors browse when they build papers, and a half-uploaded row is
/// worse than none.
/// </para>
///
/// <para>
/// <b>Why a temp file.</b> The checksum has to cover the whole upload and the
/// blob store needs the same bytes a second time, but a 50 MB recording does
/// not belong in memory. The stream is spooled to one temporary file — the
/// same move the import uploader makes — hashed on the way in, and reopened
/// for the put. The temp file is deleted no matter how the upload ends.
/// </para>
///
/// <para>
/// <b>The audit row is written by the endpoint, not here</b> — the same split
/// the library endpoints use: the handler knows what was stored, the endpoint
/// knows who was holding the token when it was stored.
/// </para>
/// </summary>
public sealed class UploadMediaAsset(
    IMediaAssetStore store,
    IMediaBlobStore blobs,
    IClock clock)
{
    public sealed record Command(
        Stream Content,
        string FileName,
        long? DurationMs,
        string UploadedByName);

    public async Task<Result<MediaAsset>> HandleAsync(Command command, CancellationToken ct)
    {
        if (command.Content is null || !command.Content.CanRead)
            return Error.Validation(ErrorCodes.MediaUnrecognisedFormat, "The upload carried no readable content.");
        if (string.IsNullOrWhiteSpace(command.FileName))
            return Error.Validation(ErrorCodes.MediaUnrecognisedFormat, "A file name is required.");
        if (command.DurationMs is < 0 or > 86_400_000)
            return Error.Validation(ErrorCodes.MediaInvalidDuration, "The measured duration is out of range.");

        if (!blobs.IsConfigured)
            return Error.Conflict(ErrorCodes.MediaUploadUnavailable,
                "This deployment has no media storage configured.");

        var spool = Path.Combine(Path.GetTempPath(), "vni-media", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.GetDirectoryName(spool)!);
        try
        {
            long bytes;
            string checksum;
            MediaSniffer.Sniffed? sniffed;

            await using (var buffered = File.Create(spool))
            {
                var probe = new byte[MediaSniffer.ProbeLength];
                var probeRead = await command.Content.ReadAtLeastAsync(
                    probe, MediaSniffer.ProbeLength, throwOnEndOfStream: false, ct);

                if (probeRead == 0)
                    return Error.Validation(ErrorCodes.MediaEmptyFile, "The uploaded file is empty.");

                sniffed = MediaSniffer.Sniff(probe.AsSpan(0, probeRead));
                if (sniffed is null)
                    return Error.Validation(ErrorCodes.MediaUnrecognisedFormat,
                        "The file's own bytes do not match any accepted format.");

                await buffered.WriteAsync(probe.AsMemory(0, probeRead), ct);

                var kind = sniffed.Kind;
                var ceiling = MediaSniffer.MaxBytesFor(kind);
                var counted = await CopyCountingAsync(command.Content, buffered, ceiling - probeRead, ct);
                if (counted > ceiling - probeRead)
                    return Error.Validation(ErrorCodes.MediaTooLarge,
                        $"A {kind.ToString().ToLowerInvariant()} upload may be at most "
                        + $"{ceiling / (1024 * 1024)} MB.");

                bytes = probeRead + counted;
                buffered.Position = 0;
                checksum = await ChecksumAsync(buffered, ct);
            }

            var mediaId = Guid.NewGuid().ToString("n");
            var objectKey = MediaObjectKey.For(mediaId);
            await using var uploadStream = File.OpenRead(spool);
            await blobs.PutAsync(objectKey, uploadStream, sniffed.ContentType, checksum, ct);

            var asset = new MediaAsset(
                mediaId, sniffed.Kind, command.FileName.Trim(), sniffed.ContentType,
                bytes, command.DurationMs, checksum, command.UploadedByName.Trim(), clock.UtcNow,
                Retired: false);

            await store.InsertAsync(asset, ct);
            return asset;
        }
        finally
        {
            File.Delete(spool); // best effort — a missed file is a temp-file leak, not a state problem
        }
    }

    /// <summary>
    /// Copies at most <paramref name="ceiling"/> bytes, so the ceiling is
    /// enforced by arithmetic and not by trusting a declared length. One byte
    /// past it stops the copy — the upload is refused, and the
    /// partially-copied temp file is about to be deleted anyway.
    /// </summary>
    private static async Task<long> CopyCountingAsync(
        Stream source, Stream destination, long ceiling, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (total + read > ceiling)
                return total + read; // over — caller refuses on the comparison
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
        }

        return total;
    }

    private static async Task<string> ChecksumAsync(Stream content, CancellationToken ct)
    {
        var hash = await SHA256.HashDataAsync(content, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
