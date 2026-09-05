using System.IO.Compression;
using System.Text;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

public sealed class SafeSourceDocumentExtractorTests : IDisposable
{
    private readonly string sandbox = Path.Combine(Path.GetTempPath(), $"vni-import-{Guid.NewGuid():n}");
    private readonly FakeAssets assets = new();

    public SafeSourceDocumentExtractorTests() => Directory.CreateDirectory(sandbox);

    [Fact]
    public async Task Docx_text_is_hashed_and_probed_media_is_uploaded_private()
    {
        var path = Path.Combine(sandbox, "paper.docx");
        await WriteDocxAsync(path, media: Png());
        var extractor = new SafeSourceDocumentExtractor(assets);

        var result = await extractor.ExtractAsync(
            sandbox, "paper.docx", SourceExtractionLimits.Default, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Question một", result.Source!.Text);
        Assert.Equal(64, result.Source.SourceSha256.Length);
        Assert.Equal(ExamImportWorkflow.Hash(result.Source.Text), result.Source.TextSha256);
        var asset = Assert.Single(result.Assets);
        Assert.Equal("image/png", asset.ContentType);
        Assert.StartsWith("private://imports/", asset.Reference);
        Assert.Single(assets.Uploaded);
    }

    [Fact]
    public async Task Traversal_is_refused_before_any_file_is_opened()
    {
        var extractor = new SafeSourceDocumentExtractor(assets);

        var result = await extractor.ExtractAsync(
            sandbox, "../outside.pdf", SourceExtractionLimits.Default, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("SOURCE_PATH_OUTSIDE_SANDBOX", Assert.Single(result.Findings).Code);
        Assert.Empty(assets.Uploaded);
    }

    [Fact]
    public async Task Pdf_page_cap_is_enforced_deterministically()
    {
        await File.WriteAllTextAsync(
            Path.Combine(sandbox, "large.pdf"),
            "%PDF-1.7\n1 0 obj <</Type /Page>> endobj\n2 0 obj <</Type /Page>> endobj");
        var extractor = new SafeSourceDocumentExtractor(assets);
        var limits = SourceExtractionLimits.Default with { MaxPages = 1 };

        var result = await extractor.ExtractAsync(sandbox, "large.pdf", limits, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("SOURCE_PAGE_LIMIT", Assert.Single(result.Findings).Code);
    }

    [Fact]
    public async Task Unknown_embedded_media_is_refused_and_never_uploaded()
    {
        await WriteDocxAsync(Path.Combine(sandbox, "bad.docx"), media: "not-an-image"u8.ToArray());
        var extractor = new SafeSourceDocumentExtractor(assets);

        var result = await extractor.ExtractAsync(
            sandbox, "bad.docx", SourceExtractionLimits.Default, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("EMBEDDED_MEDIA_INVALID", Assert.Single(result.Findings).Code);
        Assert.Empty(assets.Uploaded);
    }

    private static async Task WriteDocxAsync(string path, byte[] media)
    {
        await using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
        var document = archive.CreateEntry("word/document.xml");
        await using (var writer = new StreamWriter(document.Open(), Encoding.UTF8))
        {
            await writer.WriteAsync("""
                <?xml version="1.0" encoding="UTF-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p><w:r><w:t>Question một</w:t></w:r></w:p></w:body>
                </w:document>
                """);
        }
        var image = archive.CreateEntry("word/media/image1.png");
        await using var output = image.Open();
        await output.WriteAsync(media);
    }

    private static byte[] Png() =>
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00];

    public void Dispose()
    {
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    private sealed class FakeAssets : IPrivateImportAssetStore
    {
        public List<(string Key, string ContentType, string Hash)> Uploaded { get; } = [];

        public Task<string> PutPrivateAsync(
            string key, Stream content, string contentType, string sha256, CancellationToken ct)
        {
            Uploaded.Add((key, contentType, sha256));
            return Task.FromResult($"private://{key}");
        }
    }
}
