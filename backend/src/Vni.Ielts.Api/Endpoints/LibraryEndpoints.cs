using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Content.Library;
using Vni.Ielts.Domain.Content.Library;

namespace Vni.Ielts.Api.Endpoints;

/// <summary>
/// The learner-facing half of the two libraries — <c>P-22</c>.
///
/// <b>Anonymous, read-only, published rows only.</b> A document catalogue and
/// a blog are the two surfaces a prospective learner reads before they have an
/// account, so nothing here demands a token. Everything here goes through a
/// handler that cannot see a draft: the store filters on status in the query,
/// and a draft slug answers 404 rather than confirming it exists.
///
/// Rate-limited under <see cref="RateLimitPolicies.InSessionRead"/>, which
/// partitions anonymous callers by address; a scraper meets the bound, a
/// person paging a library never does.
/// </summary>
public static class LibraryEndpoints
{
    public static void MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/library")
            .WithTags("Library")
            .RequireRateLimiting(RateLimitPolicies.InSessionRead);

        group.MapGet("/documents", ListDocumentsEndpoint)
            .WithName("ListLibraryDocuments")
            .WithSummary("Published documents; filter by skill, type, band, or free text")
            .Produces<LibraryDocumentListView>();

        group.MapGet("/documents/{id}", GetDocumentEndpoint)
            .WithName("GetLibraryDocument")
            .WithSummary("One published document")
            .Produces<LibraryDocumentView>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/articles", ListArticlesEndpoint)
            .WithName("ListArticles")
            .WithSummary("Published articles, newest first; filter by category")
            .Produces<ArticleListView>();

        group.MapGet("/articles/{slug}", GetArticleEndpoint)
            .WithName("GetArticleBySlug")
            .WithSummary("One published article by its slug; a draft slug is 404")
            .Produces<ArticleView>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListDocumentsEndpoint(
        string? skill, string? type, string? band, string? q,
        ListLibraryDocuments handler, HttpContext http, CancellationToken ct)
    {
        // A misspelt filter is a 400, not "everything". Silently ignoring it
        // would make a typo look like an empty filter — a learner filtering
        // to "listenng" would be shown the Reading shelf and never know.
        var errors = new List<FieldError>();

        DocumentSkill? skillValue = null;
        if (skill is not null && (skillValue = LibraryWire.ParseSkill(skill)) is null)
            errors.Add(new("skill", "INVALID", "Unknown skill."));

        DocumentType? typeValue = null;
        if (type is not null && (typeValue = LibraryWire.ParseType(type)) is null)
            errors.Add(new("type", "INVALID", "Unknown document type."));

        if (band is not null && !LibraryDocument.Bands.Contains(band))
            errors.Add(new("band", "INVALID", $"One of {string.Join(", ", LibraryDocument.Bands)}."));

        if (errors.Count > 0) return ApiProblem.Validation(errors, http);

        var items = await handler.HandleAsync(
            new LibraryDocumentFilter(skillValue, typeValue, band, string.IsNullOrWhiteSpace(q) ? null : q), ct);

        return Results.Ok(new LibraryDocumentListView(items));
    }

    private static async Task<IResult> GetDocumentEndpoint(
        string id, GetLibraryDocument handler, HttpContext http, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new LibraryDocumentId(id), ct);
        return result.Match(Results.Ok, e => ApiProblem.From(e, http));
    }

    private static async Task<IResult> ListArticlesEndpoint(
        string? category, ListArticles handler, HttpContext http, CancellationToken ct)
    {
        ArticleCategory? categoryValue = null;
        if (category is not null && (categoryValue = LibraryWire.ParseCategory(category)) is null)
            return ApiProblem.Validation(
                [new("category", "INVALID", "Category is huong-dan, bai-viet or tuyen-dung.")], http);

        var items = await handler.HandleAsync(categoryValue, ct);
        return Results.Ok(new ArticleListView(items));
    }

    private static async Task<IResult> GetArticleEndpoint(
        string slug, GetArticleBySlug handler, HttpContext http, CancellationToken ct)
    {
        // Not a valid slug means not an article; same answer as a draft so the
        // two cannot be told apart from outside.
        if (!Slug.IsValid(slug))
            return ApiProblem.From(
                Application.Common.Error.NotFound(Application.Common.ErrorCodes.NotFound, "No such article."), http);

        var result = await handler.HandleAsync(slug, ct);
        return result.Match(Results.Ok, e => ApiProblem.From(e, http));
    }
}

public sealed record LibraryDocumentListView(IReadOnlyList<LibraryDocumentView> Items);

public sealed record ArticleListView(IReadOnlyList<ArticleSummaryView> Items);
