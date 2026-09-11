namespace Vni.Ielts.Application.Exams;

public enum DocumentExtractionOutcome
{
    Extracted,
    Empty,
    Unsupported,
    Encrypted,
    Malformed,
    LimitExceeded,
}

public sealed record ExtractedTextChunk(
    string Id,
    int Order,
    string Text,
    int? PageNumber,
    string? Section);

public sealed record ExtractedSourceDocument(
    string PackageId,
    string EntryPath,
    string MediaType,
    string Sha256,
    DocumentExtractionOutcome Outcome,
    IReadOnlyList<ExtractedTextChunk> Chunks)
{
    public bool HasText => Outcome == DocumentExtractionOutcome.Extracted && Chunks.Count > 0;
}

public sealed record DocxExtractionResult(
    DocumentExtractionOutcome Outcome,
    string FileName,
    IReadOnlyList<ExtractedTextChunk> Chunks)
{
    public static DocxExtractionResult Failed(DocumentExtractionOutcome outcome, string fileName) =>
        outcome == DocumentExtractionOutcome.Extracted
            ? throw new ArgumentException("Extracted is not a failure outcome.", nameof(outcome))
            : new(outcome, fileName, []);
}

public interface IDocxTextExtractor
{
    DocxExtractionResult Extract(Stream content, string fileName, CancellationToken ct);
}

public sealed record DocumentExtractionLimits(long MaxDocumentBytes, int MaxChunks, int MaxTextCharacters)
{
    public static DocumentExtractionLimits Conservative { get; } = new(50 * 1024 * 1024, 20_000, 5_000_000);
}
