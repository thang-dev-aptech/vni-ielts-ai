using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Content;

public sealed class OoxmlDocxTextExtractor(DocumentExtractionLimits limits) : IDocxTextExtractor
{
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public DocxExtractionResult Extract(Stream content, string fileName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        try
        {
            using var buffer = CopyBounded(content, limits.MaxDocumentBytes, ct);
            if (buffer is null)
                return DocxExtractionResult.Failed(DocumentExtractionOutcome.LimitExceeded, fileName);

            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.GetEntry("word/vbaProject.bin") is not null || HasExternalRelationship(archive))
                return DocxExtractionResult.Failed(DocumentExtractionOutcome.Unsupported, fileName);

            var documentEntry = archive.GetEntry("word/document.xml");
            if (documentEntry is null)
                return DocxExtractionResult.Failed(DocumentExtractionOutcome.Malformed, fileName);

            using var documentStream = documentEntry.Open();
            var document = XDocument.Load(documentStream, LoadOptions.None);
            var body = document.Root?.Element(Word + "body");
            if (body is null)
                return DocxExtractionResult.Failed(DocumentExtractionOutcome.Malformed, fileName);

            var chunks = new List<ExtractedTextChunk>();
            var characters = 0;
            foreach (var block in body.Elements())
            {
                ct.ThrowIfCancellationRequested();
                if (block.Name != Word + "p" && block.Name != Word + "tbl") continue;

                var text = TextOf(block);
                if (string.IsNullOrWhiteSpace(text)) continue;
                text = text.Trim();
                characters += text.Length;
                if (chunks.Count >= limits.MaxChunks || characters > limits.MaxTextCharacters)
                    return DocxExtractionResult.Failed(DocumentExtractionOutcome.LimitExceeded, fileName);

                var order = chunks.Count + 1;
                chunks.Add(new ExtractedTextChunk($"chunk-{order}", order, text, null,
                    block.Name == Word + "tbl" ? "table" : "paragraph"));
            }

            return chunks.Count == 0
                ? DocxExtractionResult.Failed(DocumentExtractionOutcome.Empty, fileName)
                : new DocxExtractionResult(DocumentExtractionOutcome.Extracted, fileName, chunks);
        }
        catch (InvalidDataException)
        {
            return DocxExtractionResult.Failed(DocumentExtractionOutcome.Malformed, fileName);
        }
        catch (XmlException)
        {
            return DocxExtractionResult.Failed(DocumentExtractionOutcome.Malformed, fileName);
        }
    }

    private static MemoryStream? CopyBounded(Stream source, long limit, CancellationToken ct)
    {
        var destination = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total += read;
            if (total > limit)
            {
                destination.Dispose();
                return null;
            }
            destination.Write(buffer, 0, read);
        }
        destination.Position = 0;
        return destination;
    }

    private static bool HasExternalRelationship(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = entry.Open();
            var document = XDocument.Load(stream, LoadOptions.None);
            if (document.Descendants(Relationships + "Relationship")
                .Any(relationship => string.Equals((string?)relationship.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private static string TextOf(XElement block)
    {
        var pieces = block.Descendants()
            .Where(element => element.Name == Word + "t" || element.Name == Word + "tab" || element.Name == Word + "br")
            .Select(element => element.Name == Word + "t" ? element.Value : " ");
        return string.Concat(pieces);
    }
}
