using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// PdfPig is the only place in this codebase that knows a PDF library exists — the
/// port it implements (<see cref="IPdfTextExtractor"/>) is the only thing Application
/// ever sees, per the vendor-abstraction rule that already governs AI providers.
///
/// PdfPig throws on both "expected bad input" (encrypted) and "genuinely broken
/// file" (malformed). Both are routed to a result value rather than propagated,
/// because a hostile or damaged upload is exactly the kind of input a package
/// pipeline must handle, not crash on.
/// </summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public PdfExtractionResult Extract(Stream pdfContent, string fileName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pdfContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        using var buffered = new MemoryStream();
        pdfContent.CopyTo(buffered);
        buffered.Position = 0;

        PdfDocument document;
        try
        {
            document = PdfDocument.Open(buffered);
        }
        catch (PdfDocumentEncryptedException)
        {
            return PdfExtractionResult.Failed(PdfExtractionOutcome.Encrypted, fileName);
        }
        catch (Exception)
        {
            return PdfExtractionResult.Failed(PdfExtractionOutcome.Malformed, fileName);
        }

        using (document)
        {
            var pages = new List<PdfExtractedPage>(document.NumberOfPages);
            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                pages.Add(new PdfExtractedPage(page.Number, page.Text ?? string.Empty));
            }

            var hasText = pages.Any(page => !string.IsNullOrWhiteSpace(page.Text));
            return hasText
                ? new PdfExtractionResult(PdfExtractionOutcome.Extracted, fileName, pages)
                : PdfExtractionResult.Failed(PdfExtractionOutcome.EmptyOrImageOnly, fileName);
        }
    }
}
