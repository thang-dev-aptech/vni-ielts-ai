namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Magic-byte media probe shared by DOCX embedded-media extraction and ZIP
/// exam-asset staging. Type comes from bytes, never from an extension.
/// </summary>
internal static class MediaContentProbe
{
    public static string? Probe(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return "image/png";
        if (bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return "image/jpeg";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return "image/gif";
        if (bytes.StartsWith("ID3"u8) || (bytes.Length > 1 && bytes[0] == 0xff && (bytes[1] & 0xe0) == 0xe0))
            return "audio/mpeg";
        return null;
    }
}
