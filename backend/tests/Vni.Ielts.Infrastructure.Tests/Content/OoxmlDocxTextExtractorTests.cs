using System.IO.Compression;
using System.Text;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

public sealed class OoxmlDocxTextExtractorTests
{
    private readonly OoxmlDocxTextExtractor _extractor = new(new DocumentExtractionLimits(1024 * 1024, 100, 10_000));

    [Fact]
    public void Preserves_paragraph_and_table_order_and_unicode()
    {
        using var docx = Docx("""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
              <w:p><w:r><w:t>Đề đọc</w:t></w:r></w:p>
              <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Cell A</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Cell B</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
              <w:p><w:r><w:t>Question 1</w:t></w:r></w:p>
            </w:body></w:document>
            """);

        var result = _extractor.Extract(docx, "Đề thi.docx", default);

        Assert.Equal(DocumentExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal(["Đề đọc", "Cell ACell B", "Question 1"], result.Chunks.Select(chunk => chunk.Text));
        Assert.Equal(["paragraph", "table", "paragraph"], result.Chunks.Select(chunk => chunk.Section));
        Assert.Equal([1, 2, 3], result.Chunks.Select(chunk => chunk.Order));
    }

    [Fact]
    public void Empty_document_is_a_value()
    {
        using var docx = Docx("""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p /></w:body></w:document>
            """);

        Assert.Equal(DocumentExtractionOutcome.Empty, _extractor.Extract(docx, "empty.docx", default).Outcome);
    }

    [Fact]
    public void Malformed_container_is_a_value()
    {
        using var content = new MemoryStream("not a zip"u8.ToArray());

        Assert.Equal(DocumentExtractionOutcome.Malformed, _extractor.Extract(content, "broken.docx", default).Outcome);
    }

    [Fact]
    public void Text_over_the_configured_limit_is_a_value()
    {
        var extractor = new OoxmlDocxTextExtractor(new DocumentExtractionLimits(1024 * 1024, 100, 3));
        using var docx = Docx("""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>four</w:t></w:r></w:p></w:body></w:document>
            """);

        Assert.Equal(DocumentExtractionOutcome.LimitExceeded, extractor.Extract(docx, "limited.docx", default).Outcome);
    }

    [Fact]
    public void External_relationship_is_unsupported_and_not_resolved()
    {
        using var docx = Docx("""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Local</w:t></w:r></w:p></w:body></w:document>
            """, """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="x" Target="https://example.invalid/data" TargetMode="External" /></Relationships>
            """);

        Assert.Equal(DocumentExtractionOutcome.Unsupported, _extractor.Extract(docx, "external.docx", default).Outcome);
    }

    private static MemoryStream Docx(string documentXml, string relationships = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\" />")
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", "<Types />");
            Add(archive, "_rels/.rels", relationships);
            Add(archive, "word/document.xml", documentXml);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
