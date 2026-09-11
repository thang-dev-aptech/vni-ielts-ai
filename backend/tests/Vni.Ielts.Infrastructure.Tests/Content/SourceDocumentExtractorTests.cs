using Vni.Ielts.Application.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

public sealed class SourceDocumentExtractorTests
{
    [Fact]
    public void Normalizes_pdf_pages_with_package_and_entry_provenance()
    {
        var dispatcher = new SourceDocumentExtractor(new StubDocx(), new StubPdf(), DocumentExtractionLimits.Conservative);
        using var content = new MemoryStream([1]);

        var result = dispatcher.Extract("package-1", "Đề/Paper.pdf", "application/pdf", "abc", content, default);

        Assert.Equal(DocumentExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal("package-1", result.PackageId);
        Assert.Equal("Đề/Paper.pdf", result.EntryPath);
        Assert.Equal("abc", result.Sha256);
        Assert.Equal([1, 2], result.Chunks.Select(chunk => chunk.PageNumber));
        Assert.Equal(["page-1", "page-2"], result.Chunks.Select(chunk => chunk.Id));
    }

    [Fact]
    public void Audio_and_images_are_inventory_only()
    {
        var docx = new StubDocx();
        var pdf = new StubPdf();
        var dispatcher = new SourceDocumentExtractor(docx, pdf, DocumentExtractionLimits.Conservative);
        using var content = new MemoryStream([1]);

        var result = dispatcher.Extract("package-1", "audio/test.mp3", "audio/mpeg", "abc", content, default);

        Assert.Equal(DocumentExtractionOutcome.Unsupported, result.Outcome);
        Assert.Empty(result.Chunks);
        Assert.Equal(0, docx.Calls);
        Assert.Equal(0, pdf.Calls);
    }

    private sealed class StubDocx : IDocxTextExtractor
    {
        public int Calls { get; private set; }
        public DocxExtractionResult Extract(Stream content, string fileName, CancellationToken ct)
        {
            Calls++;
            return new DocxExtractionResult(DocumentExtractionOutcome.Extracted, fileName,
                [new ExtractedTextChunk("chunk-1", 1, "text", null, "paragraph")]);
        }
    }

    private sealed class StubPdf : IPdfTextExtractor
    {
        public int Calls { get; private set; }
        public PdfExtractionResult Extract(Stream pdfContent, string fileName, CancellationToken ct)
        {
            Calls++;
            return new PdfExtractionResult(PdfExtractionOutcome.Extracted, fileName,
                [new PdfExtractedPage(1, "first"), new PdfExtractedPage(2, "second")]);
        }
    }
}
