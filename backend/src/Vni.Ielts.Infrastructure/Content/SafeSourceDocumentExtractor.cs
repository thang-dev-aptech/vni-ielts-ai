using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Bounded DOCX/PDF extraction. The caller supplies only a path relative to a configured
/// sandbox; archive entries are read in-memory and never expanded onto the filesystem.
/// </summary>
public sealed partial class SafeSourceDocumentExtractor(IPrivateImportAssetStore assets)
    : ISourceDocumentExtractor
{
    public async Task<SourceExtractionResult> ExtractAsync(
        string sandboxRoot,
        string relativePath,
        SourceExtractionLimits limits,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limits.Timeout);
        var token = timeout.Token;

        try
        {
            var root = Path.GetFullPath(sandboxRoot);
            var path = Path.GetFullPath(Path.Combine(root, relativePath));
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return Rejected("SOURCE_PATH_OUTSIDE_SANDBOX", "/source/path", "Source path leaves the import sandbox.");

            var info = new FileInfo(path);
            if (!info.Exists)
                return Rejected("SOURCE_NOT_FOUND", "/source/path", "Source file was not found.");
            if (info.Length > limits.MaxSourceBytes)
                return Rejected("SOURCE_SIZE_LIMIT", "/source", "Source exceeds the configured byte limit.");

            var bytes = await File.ReadAllBytesAsync(path, token);
            var sourceHash = Hex(SHA256.HashData(bytes));
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".docx" => await ExtractDocxAsync(info.Name, bytes, sourceHash, limits, token),
                ".pdf" => ExtractPdf(info.Name, bytes, sourceHash, limits, token),
                _ => Rejected("SOURCE_TYPE_UNSUPPORTED", "/source", "Only PDF and DOCX sources are accepted."),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Rejected("SOURCE_EXTRACTION_TIMEOUT", "/source", "Source extraction exceeded its time limit.");
        }
        catch (InvalidDataException e)
        {
            return Rejected("SOURCE_CONTAINER_INVALID", "/source", e.Message);
        }
    }

    private async Task<SourceExtractionResult> ExtractDocxAsync(
        string name, byte[] bytes, string sourceHash, SourceExtractionLimits limits, CancellationToken ct)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var document = archive.GetEntry("word/document.xml");
        if (document is null)
            return Rejected("DOCX_DOCUMENT_MISSING", "/source", "DOCX has no word/document.xml entry.");

        var media = archive.Entries.Where(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal)).ToArray();
        if (media.Length > limits.MaxEmbeddedMedia)
            return Rejected("EMBEDDED_MEDIA_COUNT_LIMIT", "/source/media", "DOCX exceeds the embedded-media count limit.");
        if (media.Any(e => e.Length > limits.MaxEmbeddedMediaBytes))
            return Rejected("EMBEDDED_MEDIA_SIZE_LIMIT", "/source/media", "DOCX contains an embedded asset over the byte limit.");

        var uploaded = new List<PrivateImportAsset>();
        foreach (var entry in media)
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = entry.Open();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, ct);
            var payload = copy.ToArray();
            var probe = Probe(payload);
            if (probe is null)
                return Rejected("EMBEDDED_MEDIA_INVALID", "/source/media", $"Embedded media '{entry.Name}' has an unknown or mismatched signature.");
            var hash = Hex(SHA256.HashData(payload));
            var key = $"imports/{sourceHash}/{hash}-{SafeName(entry.Name)}";
            await using var upload = new MemoryStream(payload, writable: false);
            var reference = await assets.PutPrivateAsync(key, upload, probe, hash, ct);
            uploaded.Add(new PrivateImportAsset(reference, probe, payload.Length, hash));
        }

        var text = await ReadDocumentTextAsync(document, ct);
        return Accepted(name, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", text, sourceHash, uploaded);
    }

    private static SourceExtractionResult ExtractPdf(
        string name, byte[] bytes, string sourceHash, SourceExtractionLimits limits, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            return Rejected("PDF_SIGNATURE_INVALID", "/source", "PDF signature is missing.");
        var raw = Encoding.Latin1.GetString(bytes);
        var pages = PdfPageRegex().Matches(raw).Count;
        if (pages > limits.MaxPages)
            return Rejected("SOURCE_PAGE_LIMIT", "/source/pages", "PDF exceeds the configured page limit.");
        var text = string.Join("\n", PdfTextRegex().Matches(raw).Select(m => UnescapePdf(m.Groups[1].Value)));
        return Accepted(name, "application/pdf", text, sourceHash, []);
    }

    private static async Task<string> ReadDocumentTextAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        await using var stream = entry.Open();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, Async = true };
        using var reader = XmlReader.Create(stream, settings);
        var text = new StringBuilder();
        while (await reader.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "p" or "br" && text.Length > 0)
                text.AppendLine();
            if (reader.NodeType == XmlNodeType.Text) text.Append(reader.Value);
        }
        return text.ToString().Trim();
    }

    private static SourceExtractionResult Accepted(
        string name, string mediaType, string text, string sourceHash, IReadOnlyList<PrivateImportAsset> uploaded) =>
        new(true, new ExtractedImportSource(
            name, mediaType, text, sourceHash, ExamImportWorkflow.Hash(text),
            ImportDataClassification.Restricted), uploaded, []);

    private static SourceExtractionResult Rejected(string code, string path, string message) =>
        new(false, null, [], [new PackageFinding("error", code, path, message)]);

    private static string? Probe(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return "image/png";
        if (bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return "image/jpeg";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return "image/gif";
        if (bytes.StartsWith("ID3"u8) || (bytes.Length > 1 && bytes[0] == 0xff && (bytes[1] & 0xe0) == 0xe0)) return "audio/mpeg";
        return null;
    }

    private static string SafeName(string name) =>
        new(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());

    private static string Hex(byte[] value) => Convert.ToHexStringLower(value);
    private static string UnescapePdf(string value) => value.Replace("\\(", "(").Replace("\\)", ")").Replace("\\\\", "\\");

    [GeneratedRegex(@"/Type\s*/Page(?!s)\b", RegexOptions.CultureInvariant)]
    private static partial Regex PdfPageRegex();

    [GeneratedRegex(@"\((?<text>(?:\\.|[^\\)])*)\)\s*Tj", RegexOptions.CultureInvariant)]
    private static partial Regex PdfTextRegex();
}
