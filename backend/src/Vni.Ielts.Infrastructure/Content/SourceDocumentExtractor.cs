using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Content;

public sealed class SourceDocumentExtractor(
    IDocxTextExtractor docx,
    IPdfTextExtractor pdf,
    DocumentExtractionLimits limits)
{
    public ExtractedSourceDocument Extract(
        string packageId,
        string entryPath,
        string mediaType,
        string sha256,
        Stream content,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentNullException.ThrowIfNull(content);

        if (mediaType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
        {
            var result = docx.Extract(content, entryPath, ct);
            return new ExtractedSourceDocument(packageId, entryPath, mediaType, sha256,
                result.Outcome, result.Chunks);
        }

        if (mediaType == "application/pdf")
        {
            var result = pdf.Extract(content, entryPath, ct);
            var outcome = result.Outcome switch
            {
                PdfExtractionOutcome.Extracted => DocumentExtractionOutcome.Extracted,
                PdfExtractionOutcome.EmptyOrImageOnly => DocumentExtractionOutcome.Empty,
                PdfExtractionOutcome.Encrypted => DocumentExtractionOutcome.Encrypted,
                _ => DocumentExtractionOutcome.Malformed,
            };
            var chunks = result.Pages
                .OrderBy(page => page.PageNumber)
                .Select((page, index) => new ExtractedTextChunk(
                    $"page-{page.PageNumber}", index + 1, page.Text, page.PageNumber, null))
                .ToArray();
            if (chunks.Length > limits.MaxChunks || chunks.Sum(chunk => chunk.Text.Length) > limits.MaxTextCharacters)
                return new ExtractedSourceDocument(packageId, entryPath, mediaType, sha256,
                    DocumentExtractionOutcome.LimitExceeded, []);
            return new ExtractedSourceDocument(packageId, entryPath, mediaType, sha256, outcome, chunks);
        }

        return new ExtractedSourceDocument(packageId, entryPath, mediaType, sha256,
            DocumentExtractionOutcome.Unsupported, []);
    }
}
