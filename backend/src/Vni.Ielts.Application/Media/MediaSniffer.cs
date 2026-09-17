namespace Vni.Ielts.Application.Media;

/// <summary>
/// What a media upload actually is, read from its own first bytes.
///
/// <para>
/// <b>Magic bytes, never the declared content type, never the file name.</b>
/// An <c>.mp3</c> that is really a PDF is a PDF, and the browser that uploaded
/// it does not get to decide what the server stores. The same signature table
/// lives in the CMS (<c>apps/admin/src/lib/media.ts</c>) so the operator sees
/// the rejection before spending the upload — but the server re-derives
/// everything from scratch, which that file's own comment requires.
/// </para>
///
/// <para>
/// <b><c>WEBP</c> is examined before <c>RIFF</c>, and the order is load
/// bearing.</b> A WebP image opens with <c>RIFF....WEBP</c>, so a table that
/// tests <c>RIFF</c> first answers <c>audio/wav</c> for every image and the
/// server would store pictures as audio.
/// </para>
///
/// <para>
/// The client table got this wrong until 2026-09-18 and was corrected to
/// match. This comment used to record that divergence as deliberate and
/// harmless; it was neither — the CMS offered an <c>&lt;audio&gt;</c> player
/// for a picture. Both tables now agree, and both have a test that pins the
/// order by feeding a real WebP header and a real WAVE header to the same
/// call. → <c>apps/admin/src/lib/media.ts</c>
/// </para>
/// </summary>
public static class MediaSniffer
{
    public const long MaxAudioBytes = 50 * 1024 * 1024; // 50 MB — the CMS table's own ceiling
    public const long MaxImageBytes = 5 * 1024 * 1024;
    public const long MaxFileBytes = 20 * 1024 * 1024;

    private readonly record struct Signature(int At, byte[] Bytes, MediaKind Kind, string ContentType);

    private static readonly Signature[] Signatures =
    [
        // WebP before RIFF: a WebP container opens with RIFF....WEBP, and the
        // first match wins. See the class comment.
        new(8, [0x57, 0x45, 0x42, 0x50], MediaKind.Image, "image/webp"),

        new(0, [0x49, 0x44, 0x33], MediaKind.Audio, "audio/mpeg"),          // ID3
        new(0, [0xff, 0xfb], MediaKind.Audio, "audio/mpeg"),                // MPEG frame sync
        new(0, [0xff, 0xf3], MediaKind.Audio, "audio/mpeg"),
        new(4, [0x66, 0x74, 0x79, 0x70], MediaKind.Audio, "audio/mp4"),     // ftyp
        new(0, [0x52, 0x49, 0x46, 0x46], MediaKind.Audio, "audio/wav"),     // RIFF
        new(0, [0x4f, 0x67, 0x67, 0x53], MediaKind.Audio, "audio/ogg"),     // OggS
        new(0, [0x89, 0x50, 0x4e, 0x47], MediaKind.Image, "image/png"),
        new(0, [0xff, 0xd8, 0xff], MediaKind.Image, "image/jpeg"),
        new(0, [0x25, 0x50, 0x44, 0x46], MediaKind.File, "application/pdf"),
    ];

    public sealed record Sniffed(MediaKind Kind, string ContentType);

    /// <summary>The first bytes of a stream, whatever it is.</summary>
    public const int ProbeLength = 16;

    /// <summary>
    /// What the probe bytes are, or null when nothing recognises them — and
    /// null means refuse. There is no "close enough".
    /// </summary>
    public static Sniffed? Sniff(ReadOnlySpan<byte> head)
    {
        foreach (var signature in Signatures)
        {
            if (head.Length < signature.At + signature.Bytes.Length) continue;
            var matched = true;
            for (var i = 0; i < signature.Bytes.Length; i++)
            {
                if (head[signature.At + i] != signature.Bytes[i])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                return new Sniffed(signature.Kind, signature.ContentType);
        }

        return null;
    }

    /// <summary>Upload ceiling for what was sniffed. Audio carries the largest.</summary>
    public static long MaxBytesFor(MediaKind kind) => kind switch
    {
        MediaKind.Audio => MaxAudioBytes,
        MediaKind.Image => MaxImageBytes,
        MediaKind.File => MaxFileBytes,
        _ => 0,
    };
}
