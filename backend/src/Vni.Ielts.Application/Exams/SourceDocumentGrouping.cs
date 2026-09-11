namespace Vni.Ielts.Application.Exams;

/// <summary>
/// A review-only suggestion for how source documents might be worked on together.
/// Directory adjacency supplies context only; it never classifies an IELTS module.
/// </summary>
public sealed record SourceDocumentGroup(
    string Id,
    string? DirectoryPath,
    IReadOnlyList<string> EntryPaths);

public sealed record SourceDocumentGroupingProposal(
    string PackageId,
    IReadOnlyList<SourceDocumentGroup> Groups,
    bool NeedsReview,
    int Version)
{
    public static SourceDocumentGroupingProposal Create(
        string packageId,
        IReadOnlyList<SourceDocumentGroup> groups)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("A package id is required.", nameof(packageId));
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count == 0)
            throw new ArgumentException("At least one source document group is required.", nameof(groups));
        if (groups.Any(group => string.IsNullOrWhiteSpace(group.Id)))
            throw new ArgumentException("Every source document group needs an id.", nameof(groups));
        if (groups.Select(group => group.Id).Distinct(StringComparer.Ordinal).Count() != groups.Count)
            throw new ArgumentException("Source document group ids must be unique.", nameof(groups));

        var paths = groups.SelectMany(group => group.EntryPaths).ToArray();
        if (groups.Any(group => group.EntryPaths.Count == 0)
            || paths.Any(string.IsNullOrWhiteSpace)
            || paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
            throw new ArgumentException("Every source document must appear once in a non-empty group.", nameof(groups));

        return new SourceDocumentGroupingProposal(packageId, groups, NeedsReview: true, Version: 0);
    }
}

public sealed class SourceDocumentGroupingConcurrencyException : InvalidOperationException
{
    public SourceDocumentGroupingConcurrencyException(
        string packageId,
        int expectedVersion,
        Exception? innerException = null)
        : base(
            $"Source document grouping for package '{packageId}' is no longer at version {expectedVersion}.",
            innerException)
    {
    }
}

/// <summary>
/// Stores a staff-editable, non-final grouping proposal. Replacements must use
/// compare-and-swap so one reviewer cannot silently overwrite another review.
/// </summary>
public interface ISourceDocumentGroupingProposalRepository
{
    Task<SourceDocumentGroupingProposal?> FindAsync(string packageId, CancellationToken ct);
    Task SaveAsync(SourceDocumentGroupingProposal proposal, int expectedVersion, CancellationToken ct);
}
