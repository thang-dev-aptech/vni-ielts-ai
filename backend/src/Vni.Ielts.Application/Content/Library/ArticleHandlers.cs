using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content.Library;

namespace Vni.Ielts.Application.Content.Library;

/// <summary>What the CMS sends for an article. See <see cref="LibraryDocumentInput"/> for why strings.</summary>
public sealed record ArticleInput(
    string? Slug,
    string? Title,
    string? Excerpt,
    string? Category,
    int? ReadMinutes,
    string? Author,
    IReadOnlyList<string>? Body)
{
    public (ArticleDetails? Details, IReadOnlyList<LibraryFieldError> Errors) Parse()
    {
        var errors = new List<LibraryFieldError>();

        if (string.IsNullOrWhiteSpace(Title))
            errors.Add(new("/title", "REQUIRED", "/title is required."));
        if (!Domain.Content.Library.Slug.IsValid(Slug))
            errors.Add(new("/slug", "SLUG_INVALID", "Lowercase letters, digits and single hyphens only."));

        var category = LibraryWire.ParseCategory(Category);
        if (category is null) errors.Add(new("/category", "INVALID", "Category is huong-dan, bai-viet or tuyen-dung."));

        if (ReadMinutes is null or < 1)
            errors.Add(new("/readMinutes", "INVALID", "Read time is at least one minute."));

        if (Body is null)
            errors.Add(new("/body", "REQUIRED", "/body is a list of paragraphs (may be empty)."));

        if (errors.Count > 0) return (null, errors);

        return (new ArticleDetails(
            Slug!, Title!.Trim(), Excerpt?.Trim() ?? string.Empty, category!.Value,
            ReadMinutes!.Value, Author?.Trim() ?? string.Empty, [.. Body!]), errors);
    }
}

// ── Learner side: published rows only ────────────────────────────────────

public sealed class ListArticles(IArticleStore store)
{
    public async Task<IReadOnlyList<ArticleSummaryView>> HandleAsync(
        ArticleCategory? category, CancellationToken ct) =>
        [.. (await store.ListPublishedAsync(category, ct)).Select(ArticleSummaryView.Of)];
}

public sealed class GetArticleBySlug(IArticleStore store)
{
    /// <summary>
    /// <b>A draft slug is a 404.</b> The slug is reserved the moment a draft
    /// takes it, so a learner who guesses one must learn nothing from the
    /// answer — the same rule as an unpublished exam.
    /// </summary>
    public async Task<Result<ArticleView>> HandleAsync(string slug, CancellationToken ct)
    {
        var article = await store.FindBySlugAsync(slug, ct);
        if (article is null || article.Status != LibraryContentStatus.Published)
            return Error.NotFound(ErrorCodes.NotFound, "No such article.");
        return ArticleView.Of(article);
    }
}

// ── CMS side: every status ───────────────────────────────────────────────

public sealed class ListAllArticles(IArticleStore store)
{
    public async Task<IReadOnlyList<ArticleSummaryView>> HandleAsync(CancellationToken ct) =>
        [.. (await store.ListAllAsync(ct)).Select(ArticleSummaryView.Of)];
}

public sealed class GetArticleForEditing(IArticleStore store)
{
    public async Task<Result<ArticleView>> HandleAsync(ArticleId id, CancellationToken ct)
    {
        var article = await store.FindAsync(id, ct);
        return article is null
            ? Error.NotFound(ErrorCodes.NotFound, "No such article.")
            : ArticleView.Of(article);
    }
}

public sealed class CreateArticle(IArticleStore store, IClock clock)
{
    /// <summary>
    /// <b>Two checks for one rule.</b> The lookup gives a clean 409 in the
    /// ordinary case; the unique index behind <see cref="SlugTakenException"/>
    /// is what holds when two editors save the same slug in the same second.
    /// Either alone is wrong: a lookup loses the race, an index alone gives a
    /// 500.
    /// </summary>
    public async Task<Result<ArticleView>> HandleAsync(
        ArticleDetails details, UserId actor, CancellationToken ct)
    {
        if (await store.FindBySlugAsync(details.Slug, ct) is not null)
            return SlugTaken(details.Slug);

        var article = Article.Create(details, actor, clock.UtcNow);
        try
        {
            await store.UpsertAsync(article, ct);
        }
        catch (SlugTakenException)
        {
            return SlugTaken(details.Slug);
        }

        return ArticleView.Of(article);
    }

    internal static Error SlugTaken(string slug) =>
        Error.Conflict(LibraryErrorCodes.SlugTaken, $"Slug '{slug}' đã được dùng cho một bài viết khác.");
}

public sealed class UpdateArticle(IArticleStore store, IClock clock)
{
    public async Task<Result<ArticleView>> HandleAsync(
        ArticleId id, ArticleDetails details, CancellationToken ct)
    {
        var article = await store.FindAsync(id, ct);
        if (article is null) return Error.NotFound(ErrorCodes.NotFound, "No such article.");

        var holder = await store.FindBySlugAsync(details.Slug, ct);
        if (holder is not null && holder.Id != id)
            return CreateArticle.SlugTaken(details.Slug);

        article.Update(details, clock.UtcNow);
        try
        {
            await store.UpsertAsync(article, ct);
        }
        catch (SlugTakenException)
        {
            return CreateArticle.SlugTaken(details.Slug);
        }

        return ArticleView.Of(article);
    }
}

public sealed class DeleteArticle(IArticleStore store)
{
    public async Task<Result<bool>> HandleAsync(ArticleId id, CancellationToken ct)
    {
        var article = await store.FindAsync(id, ct);
        if (article is null) return Error.NotFound(ErrorCodes.NotFound, "No such article.");

        if (!LibraryLifecycle.CanDelete(article.Status))
            return Error.Conflict(LibraryErrorCodes.ContentStatusConflict,
                "Gỡ bài viết trước khi xoá.");

        await store.DeleteAsync(id, ct);
        return true;
    }
}

public sealed class ChangeArticleStatus(IArticleStore store, IClock clock)
{
    public async Task<Result<ArticleView>> HandleAsync(
        ArticleId id, LibraryTransition transition, CancellationToken ct)
    {
        var article = await store.FindAsync(id, ct);
        if (article is null) return Error.NotFound(ErrorCodes.NotFound, "No such article.");

        var now = clock.UtcNow;
        var allowed = transition switch
        {
            LibraryTransition.Submit => LibraryLifecycle.CanSubmit(article.Status),
            LibraryTransition.Return => LibraryLifecycle.CanReturn(article.Status),
            LibraryTransition.Publish => LibraryLifecycle.CanPublish(article.Status),
            LibraryTransition.Unpublish => LibraryLifecycle.CanUnpublish(article.Status),
            _ => false,
        };

        if (!allowed)
            return Error.Conflict(LibraryErrorCodes.ContentStatusConflict,
                $"Cannot {transition.ToString().ToLowerInvariant()} an article that is {LibraryWire.Status(article.Status)}.");

        switch (transition)
        {
            case LibraryTransition.Submit: article.Submit(now); break;
            case LibraryTransition.Return: article.ReturnToDraft(now); break;
            case LibraryTransition.Publish: article.Publish(now); break;
            case LibraryTransition.Unpublish: article.Unpublish(now); break;
        }

        await store.UpsertAsync(article, ct);
        return ArticleView.Of(article);
    }
}
