using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Plan 01 tracer only. It deliberately proposes no module, title, score,
/// timing, or answer data; later plans replace it with configured adapters.
/// </summary>
public sealed class NeedsReviewExamContentParser : IExamContentParser
{
    public Task<ParsedExamCandidate> ParseAsync(
        IReadOnlyList<ExtractedSourceDocument> documents, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (documents.Count == 0) throw new ArgumentException("At least one extracted document is required.", nameof(documents));
        var candidateId = documents[0].Sha256;
        return Task.FromResult(ParsedExamCandidate.Create(
            candidateId,
            documents[0].PackageId,
            null,
            ParsedExamClassification.NeedsReview,
            null,
            [],
            [.. documents.SelectMany(document => document.Chunks.Count == 0
                ? [new ParsedSourceProvenance(document.EntryPath, null, null, document.Sha256)]
                : document.Chunks.Select(chunk => new ParsedSourceProvenance(
                    document.EntryPath, chunk.PageNumber, chunk.Section, document.Sha256)))]));
    }
}
