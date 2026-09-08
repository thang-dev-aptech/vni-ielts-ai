namespace Vni.Ielts.Application.Exams;

/// <summary>
/// Why a PDF produced no usable text, when it did not. Distinguishing these lets a
/// caller route "needs OCR" and "this file is broken" differently instead of both
/// becoming an empty candidate.
/// </summary>
public enum PdfExtractionOutcome
{
    Extracted,
    EmptyOrImageOnly,
    Encrypted,
    Malformed,
}

/// <summary>One page's plain text, kept separate so page-level provenance survives.</summary>
public sealed record PdfExtractedPage(int PageNumber, string Text);

public sealed record PdfExtractionResult(
    PdfExtractionOutcome Outcome,
    string FileName,
    IReadOnlyList<PdfExtractedPage> Pages)
{
    public static PdfExtractionResult Failed(PdfExtractionOutcome outcome, string fileName)
    {
        if (outcome == PdfExtractionOutcome.Extracted)
        {
            throw new ArgumentException(
                "Extracted is not a failure outcome; use the primary constructor with pages.",
                nameof(outcome));
        }

        return new PdfExtractionResult(outcome, fileName, []);
    }
}

/// <summary>
/// Reads raw PDF bytes into per-page text after the package security gates have
/// passed. Never infers exam structure or category — that is the AI parser's job
/// (Plan 03), working from this port's output rather than raw bytes, so the LLM
/// adapter never has to embed its own PDF-parsing logic.
/// </summary>
public interface IPdfTextExtractor
{
    PdfExtractionResult Extract(Stream pdfContent, string fileName, CancellationToken ct);
}
