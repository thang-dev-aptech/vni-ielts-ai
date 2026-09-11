using Vni.Ielts.Application.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

/// <summary>
/// Fixtures under fixtures/pdf/ are hand-built minimal PDFs (no external PDF
/// library needed to generate them), covering the failure modes Plan 02 calls
/// out by name: valid text, image-only/scanned, malformed bytes, and encrypted.
/// </summary>
public sealed class PdfPigTextExtractorTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly PdfPigTextExtractor Extractor = new();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static Stream OpenFixture(string name) =>
        File.OpenRead(Path.Combine(RepoRoot, "fixtures/pdf", name));

    [Fact]
    public void Valid_pdf_extracts_page_text_with_page_provenance()
    {
        using var stream = OpenFixture("valid-two-page.pdf");

        var result = Extractor.Extract(stream, "valid-two-page.pdf", CancellationToken.None);

        Assert.Equal(PdfExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(1, result.Pages[0].PageNumber);
        Assert.Contains("capital of Vietnam", result.Pages[0].Text);
        Assert.Equal(2, result.Pages[1].PageNumber);
        Assert.Contains("synonym for happy", result.Pages[1].Text);
    }

    [Fact]
    public void Image_only_pdf_is_reported_as_empty_not_extracted()
    {
        using var stream = OpenFixture("image-only.pdf");

        var result = Extractor.Extract(stream, "image-only.pdf", CancellationToken.None);

        Assert.Equal(PdfExtractionOutcome.EmptyOrImageOnly, result.Outcome);
        Assert.Empty(result.Pages);
    }

    [Fact]
    public void Malformed_pdf_does_not_throw()
    {
        using var stream = OpenFixture("malformed.pdf");

        var result = Extractor.Extract(stream, "malformed.pdf", CancellationToken.None);

        Assert.Equal(PdfExtractionOutcome.Malformed, result.Outcome);
        Assert.Empty(result.Pages);
    }

    [Fact]
    public void Encrypted_pdf_is_reported_as_encrypted_not_thrown()
    {
        using var stream = OpenFixture("encrypted.pdf");

        var result = Extractor.Extract(stream, "encrypted.pdf", CancellationToken.None);

        Assert.Equal(PdfExtractionOutcome.Encrypted, result.Outcome);
        Assert.Empty(result.Pages);
    }

    [Fact]
    public void Normalizer_maps_pages_to_provenance_and_ordered_page_texts()
    {
        using var stream = OpenFixture("valid-two-page.pdf");
        var result = Extractor.Extract(stream, "valid-two-page.pdf", CancellationToken.None);

        var provenance = PdfSourceNormalizer.ToProvenance(result);
        var pageTexts = PdfSourceNormalizer.ToPageTexts(result);

        Assert.Equal(2, provenance.Count);
        Assert.Equal("valid-two-page.pdf", provenance[0].FileName);
        Assert.Equal(1, provenance[0].Page);
        Assert.Null(provenance[0].Section);
        Assert.Equal(2, pageTexts.Count);
        Assert.Contains("capital of Vietnam", pageTexts[0]);
        Assert.Contains("synonym for happy", pageTexts[1]);
    }
}
