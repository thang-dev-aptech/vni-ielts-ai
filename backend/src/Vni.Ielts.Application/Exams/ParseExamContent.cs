using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Exams;

/// <summary>
/// Parses one source document after the package security gates have passed and
/// stores only reviewable, untrusted output. Draft creation is a separate,
/// explicit staff-confirmation use case.
/// </summary>
public sealed class ParseExamContent(
    IExamContentParser parser,
    IParsedExamCandidateRepository candidates)
{
    public async Task<ParsedExamCandidate> HandleAsync(
        IReadOnlyList<ExtractedSourceDocument> documents,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0) throw new ArgumentException("At least one extracted document is required.", nameof(documents));
        var packageId = documents[0].PackageId;
        if (documents.Any(document => !string.Equals(document.PackageId, packageId, StringComparison.Ordinal)))
            throw new InvalidDataException("All extracted documents must belong to the same package.");

        var candidate = await parser.ParseAsync(documents, ct);
        if (!string.Equals(candidate.PackageId, packageId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Parsed candidate package id does not match the extracted documents' package id.");
        }

        await candidates.SaveAsync(candidate, candidate.Version, ct);
        return candidate;
    }
}
