using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Exams;

/// <summary>
/// Turns extracted PDF pages into the same provenance shape <c>ParsedExamCandidate</c>
/// already carries (file name + page number), so the AI parser (Plan 03) can attach
/// per-page/per-section origin to whatever it proposes instead of losing where a
/// question came from. This is normalization only — it never guesses exam structure
/// or category; extracted text stays plain text until a parser reads it.
/// </summary>
public static class PdfSourceNormalizer
{
    public static IReadOnlyList<ParsedSourceProvenance> ToProvenance(PdfExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return [.. result.Pages.Select(page =>
            new ParsedSourceProvenance(result.FileName, page.PageNumber, Section: null, Reference: null))];
    }

    /// <summary>
    /// One string per page, in page order, for a parser that wants page-bounded
    /// chunks rather than a single blob — keeps the page boundary a parser could use
    /// for provenance without re-deriving it from raw text.
    /// </summary>
    public static IReadOnlyList<string> ToPageTexts(PdfExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return [.. result.Pages.OrderBy(page => page.PageNumber).Select(page => page.Text)];
    }
}
