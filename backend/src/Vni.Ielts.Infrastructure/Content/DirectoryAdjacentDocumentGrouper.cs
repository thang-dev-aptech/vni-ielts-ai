using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Creates non-final source groups from canonical archive directory adjacency.
/// It deliberately does not inspect directory or filename text for IELTS labels.
/// </summary>
public sealed class DirectoryAdjacentDocumentGrouper
{
    public SourceDocumentGroupingProposal Propose(IReadOnlyList<ExtractedSourceDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
            throw new ArgumentException("At least one extracted document is required.", nameof(documents));

        var packageId = documents[0].PackageId;
        if (documents.Any(document => !string.Equals(document.PackageId, packageId, StringComparison.Ordinal)))
            throw new InvalidDataException("All extracted documents must belong to the same package.");

        var groups = documents
            .OrderBy(document => document.EntryPath, StringComparer.Ordinal)
            .GroupBy(document => DirectoryOf(document.EntryPath), StringComparer.Ordinal)
            .OrderBy(group => group.Key is null ? 0 : 1)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select((group, index) => new SourceDocumentGroup(
                $"directory-{index + 1}",
                group.Key,
                [.. group.Select(document => document.EntryPath)]))
            .ToArray();

        return SourceDocumentGroupingProposal.Create(packageId, groups);
    }

    private static string? DirectoryOf(string entryPath)
    {
        var slash = entryPath.LastIndexOf('/');
        return slash < 0 ? null : entryPath[..slash];
    }
}
