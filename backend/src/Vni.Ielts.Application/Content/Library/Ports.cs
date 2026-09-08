using Vni.Ielts.Domain.Content.Library;

namespace Vni.Ielts.Application.Content.Library;

/// <summary>Optional learner-side filters. Null means "any".</summary>
public sealed record LibraryDocumentFilter(
    DocumentSkill? Skill = null,
    DocumentType? Type = null,
    string? TargetBand = null,
    string? Query = null);

/// <summary>
/// The document library, persisted.
///
/// <b>Two listing methods, not one with a flag.</b> The learner one filters to
/// published rows inside the store, so a draft cannot reach a learner by a
/// handler forgetting to filter — the store simply has no way to hand it one.
/// The admin one sees everything, because an unreviewed draft is what an
/// editor came to look at.
/// </summary>
public interface ILibraryDocumentStore
{
    Task<IReadOnlyList<LibraryDocument>> ListPublishedAsync(LibraryDocumentFilter filter, CancellationToken ct);
    Task<IReadOnlyList<LibraryDocument>> ListAllAsync(CancellationToken ct);
    Task<LibraryDocument?> FindAsync(LibraryDocumentId id, CancellationToken ct);
    Task UpsertAsync(LibraryDocument document, CancellationToken ct);
    Task DeleteAsync(LibraryDocumentId id, CancellationToken ct);
}

/// <summary>
/// Articles. Same two-listing shape as documents; plus lookup by slug, which
/// is the address a learner actually holds.
/// </summary>
public interface IArticleStore
{
    Task<IReadOnlyList<Article>> ListPublishedAsync(ArticleCategory? category, CancellationToken ct);
    Task<IReadOnlyList<Article>> ListAllAsync(CancellationToken ct);
    Task<Article?> FindAsync(ArticleId id, CancellationToken ct);

    /// <summary>Any status. The learner handler decides what it may show.</summary>
    Task<Article?> FindBySlugAsync(string slug, CancellationToken ct);

    /// <exception cref="SlugTakenException">
    /// Another article holds the slug. The handler checks first for a clean
    /// message; the store's unique index is what holds under a race, and this
    /// is how it says so.
    /// </exception>
    Task UpsertAsync(Article article, CancellationToken ct);

    Task DeleteAsync(ArticleId id, CancellationToken ct);
}

public sealed class SlugTakenException(string slug)
    : Exception($"The slug '{slug}' is already taken.")
{
    public string Slug { get; } = slug;
}

/// <summary>
/// Error codes for the two libraries. Kept beside the feature rather than
/// appended to <c>ErrorCodes</c> so a slice touching only its own files does
/// not contend for the shared registry; the same stability rule applies —
/// clients branch on these, so renaming one is a breaking change.
/// </summary>
public static class LibraryErrorCodes
{
    public const string SlugTaken = "SLUG_TAKEN";

    /// <summary>The verb does not apply to the current status — publishing a published post, deleting a live one.</summary>
    public const string ContentStatusConflict = "CONTENT_STATUS_CONFLICT";
}
