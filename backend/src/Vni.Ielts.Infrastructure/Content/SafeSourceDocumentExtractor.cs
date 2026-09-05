using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Bounded DOCX/PDF/TXT extraction. The caller supplies only a path relative to a configured
/// sandbox; archive entries are read in-memory and never expanded onto the filesystem.
///
/// <b>PDF text comes from <c>pdftotext</c> when it is on PATH.</b> The previous path scanned
/// raw PDF bytes for <c>(… ) Tj</c> operators and silently returned empty or garbled text for
/// every Flate-compressed Cambridge book — Cam 17 has ~300 kB of readable text via
/// <c>pdftotext</c> and almost none via the regex. The regex remains as a fallback so the
/// page-cap unit test, which builds a minimal uncompressed PDF, keeps working without a
/// Poppler install.
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
                ".pdf" => ExtractPdf(info.Name, path, bytes, sourceHash, limits, token),
                ".txt" => ExtractPlainText(info.Name, bytes, sourceHash),
                _ => Rejected("SOURCE_TYPE_UNSUPPORTED", "/source", "Only PDF, DOCX and TXT sources are accepted."),
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

    private static SourceExtractionResult ExtractPlainText(string name, byte[] bytes, string sourceHash)
    {
        var text = Encoding.UTF8.GetString(bytes).Trim('\uFEFF');
        return Accepted(name, "text/plain", text, sourceHash, []);
    }

    private static SourceExtractionResult ExtractPdf(
        string name, string absolutePath, byte[] bytes, string sourceHash, SourceExtractionLimits limits, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            return Rejected("PDF_SIGNATURE_INVALID", "/source", "PDF signature is missing.");

        if (TryPdftotext(absolutePath, out var text, out var pages))
        {
            if (pages > limits.MaxPages)
                return Rejected("SOURCE_PAGE_LIMIT", "/source/pages", "PDF exceeds the configured page limit.");
            return Accepted(name, "application/pdf", text, sourceHash, []);
        }

        // Fallback: uncompressed literal strings only. Enough for the page-cap
        // unit fixture; useless on a real Cambridge book (Flate streams).
        var raw = Encoding.Latin1.GetString(bytes);
        pages = PdfPageRegex().Matches(raw).Count;
        if (pages > limits.MaxPages)
            return Rejected("SOURCE_PAGE_LIMIT", "/source/pages", "PDF exceeds the configured page limit.");
        text = string.Join("\n", PdfTextRegex().Matches(raw).Select(m => UnescapePdf(m.Groups[1].Value)));
        return Accepted(name, "application/pdf", text, sourceHash, []);
    }

    /// <summary>
    /// Shells out to Poppler's <c>pdftotext</c>. Returns false when the binary
    /// is missing or the process fails — the caller then falls back to the
    /// regex path so CI machines without Poppler still exercise the page cap.
    /// </summary>
    private static bool TryPdftotext(string absolutePath, out string text, out int pages)
    {
        text = "";
        pages = 0;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pdftotext",
                ArgumentList = { "-layout", absolutePath, "-" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            });
            if (process is null) return false;

            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(60_000);
            if (process.ExitCode != 0) return false;

            text = stdout;
            // Form-feed is how pdftotext marks page breaks under -layout.
            pages = Math.Max(1, stdout.Count(c => c == '\f') + (stdout.Length > 0 ? 1 : 0));
            // Empty extract on a real file usually means a scanned PDF — still
            // a successful run; the caller sees zero characters and can OCR.
            if (stdout.Length == 0) pages = CountPagesByPdfInfo(absolutePath) ?? pages;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static int? CountPagesByPdfInfo(string absolutePath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pdfinfo",
                ArgumentList = { absolutePath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return null;
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15_000);
            if (process.ExitCode != 0) return null;
            var match = Regex.Match(stdout, @"^Pages:\s*(\d+)\s*$", RegexOptions.Multiline);
            return match.Success ? int.Parse(match.Groups[1].Value) : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
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
