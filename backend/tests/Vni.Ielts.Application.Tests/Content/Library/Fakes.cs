using Vni.Ielts.Application.Content.Library;
using Vni.Ielts.Domain.Content.Library;

namespace Vni.Ielts.Application.Tests.Content.Library;

/// <summary>
/// In-memory stand-in for the Mongo store. Enforces the one invariant a
/// handler leans on without re-deriving it: a second row cannot hold a slug
/// another row already holds. Everything else is a dictionary.
/// </summary>
internal sealed class FakeArticleStore : IArticleStore
{
    private readonly Dictionary<string, Article> _byId = [];

    public Task<IReadOnlyList<Article>> ListPublishedAsync(ArticleCategory? category, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Article>>(
        [
            .. _byId.Values
                .Where(a => a.Status == LibraryContentStatus.Published)
                .Where(a => category is null || a.Details.Category == category),
        ]);

    public Task<IReadOnlyList<Article>> ListAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Article>>([.. _byId.Values]);

    public Task<Article?> FindAsync(ArticleId id, CancellationToken ct) =>
        Task.FromResult(_byId.GetValueOrDefault(id.Value));

    public Task<Article?> FindBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(_byId.Values.FirstOrDefault(a => a.Slug == slug));

    public Task UpsertAsync(Article article, CancellationToken ct)
    {
        var holder = _byId.Values.FirstOrDefault(a => a.Slug == article.Slug && a.Id != article.Id);
        if (holder is not null) throw new SlugTakenException(article.Slug);

        _byId[article.Id.Value] = article;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(ArticleId id, CancellationToken ct)
    {
        _byId.Remove(id.Value);
        return Task.CompletedTask;
    }
}

internal sealed class FakeLibraryDocumentStore : ILibraryDocumentStore
{
    private readonly Dictionary<string, LibraryDocument> _byId = [];

    public Task<IReadOnlyList<LibraryDocument>> ListPublishedAsync(
        LibraryDocumentFilter filter, CancellationToken ct)
    {
        var matches = _byId.Values.Where(d => d.Status == LibraryContentStatus.Published);
        if (filter.Skill is { } skill) matches = matches.Where(d => d.Details.Skill == skill);
        if (filter.Type is { } type) matches = matches.Where(d => d.Details.Type == type);
        if (filter.TargetBand is { } band) matches = matches.Where(d => d.Details.TargetBand == band);

        return Task.FromResult<IReadOnlyList<LibraryDocument>>([.. matches]);
    }

    public Task<IReadOnlyList<LibraryDocument>> ListAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryDocument>>([.. _byId.Values]);

    public Task<LibraryDocument?> FindAsync(LibraryDocumentId id, CancellationToken ct) =>
        Task.FromResult(_byId.GetValueOrDefault(id.Value));

    public Task UpsertAsync(LibraryDocument document, CancellationToken ct)
    {
        var holder = _byId.Values.FirstOrDefault(d => d.Details.Slug == document.Details.Slug && d.Id != document.Id);
        if (holder is not null) throw new SlugTakenException(document.Details.Slug);

        _byId[document.Id.Value] = document;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(LibraryDocumentId id, CancellationToken ct)
    {
        _byId.Remove(id.Value);
        return Task.CompletedTask;
    }
}
