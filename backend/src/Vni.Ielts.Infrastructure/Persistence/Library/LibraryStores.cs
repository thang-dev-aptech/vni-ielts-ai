using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Content.Library;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content.Library;

namespace Vni.Ielts.Infrastructure.Persistence.Library;

/*
 * Persistence shapes for the two libraries — P-22.
 *
 * Enums are stored as their NAMES, never ordinals (PersistenceRepresentationTests
 * explains the corruption an ordinal invites). `relatedExamIds` is written as an
 * absent field while null: the reserved column costs nothing until the day a
 * document is linked to a paper, and reading an absent field back as null is
 * exactly the round-trip the tests pin.
 */

internal sealed class LibraryDocumentDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("slug")]
    public string Slug { get; set; } = string.Empty;

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("skill")]
    public string Skill { get; set; } = string.Empty;

    [BsonElement("category")]
    public string Category { get; set; } = string.Empty;

    [BsonElement("type")]
    public string Type { get; set; } = string.Empty;

    [BsonElement("format")]
    public string Format { get; set; } = string.Empty;

    [BsonElement("targetBand")]
    [BsonIgnoreIfNull]
    public string? TargetBand { get; set; }

    [BsonElement("topic")]
    [BsonIgnoreIfNull]
    public string? Topic { get; set; }

    [BsonElement("pageCount")]
    [BsonIgnoreIfNull]
    public int? PageCount { get; set; }

    [BsonElement("size")]
    public string Size { get; set; } = string.Empty;

    [BsonElement("fileUrl")]
    [BsonIgnoreIfNull]
    public string? FileUrl { get; set; }

    [BsonElement("isFeatured")]
    public bool IsFeatured { get; set; }

    [BsonElement("isNew")]
    public bool IsNew { get; set; }

    [BsonElement("isUpdated")]
    public bool IsUpdated { get; set; }

    [BsonElement("isPopular")]
    public bool IsPopular { get; set; }

    [BsonElement("access")]
    public string Access { get; set; } = string.Empty;

    [BsonElement("relatedExamIds")]
    [BsonIgnoreIfNull]
    public List<string>? RelatedExamIds { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("publishedAt")]
    [BsonIgnoreIfNull]
    public DateTime? PublishedAt { get; set; }

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;
}

internal sealed class ArticleDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    /// <summary>Unique index <c>ux_articles_slug</c> — the guarantee behind <see cref="SlugTakenException"/>.</summary>
    [BsonElement("slug")]
    public string Slug { get; set; } = string.Empty;

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("excerpt")]
    public string Excerpt { get; set; } = string.Empty;

    [BsonElement("category")]
    public string Category { get; set; } = string.Empty;

    [BsonElement("readMinutes")]
    public int ReadMinutes { get; set; }

    [BsonElement("author")]
    public string Author { get; set; } = string.Empty;

    [BsonElement("body")]
    public List<string> Body { get; set; } = [];

    [BsonElement("relatedExamIds")]
    [BsonIgnoreIfNull]
    public List<string>? RelatedExamIds { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("publishedAt")]
    [BsonIgnoreIfNull]
    public DateTime? PublishedAt { get; set; }

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;
}

internal static class LibraryMappers
{
    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is null ? null : Utc(value.Value);

    public static LibraryDocumentDocument ToDocument(LibraryDocument d) => new()
    {
        Id = d.Id.Value,
        Slug = d.Details.Slug,
        Title = d.Details.Title,
        Description = d.Details.Description,
        Skill = d.Details.Skill.ToString(),
        Category = d.Details.Category,
        Type = d.Details.Type.ToString(),
        Format = d.Details.Format.ToString(),
        TargetBand = d.Details.TargetBand,
        Topic = d.Details.Topic,
        PageCount = d.Details.PageCount,
        Size = d.Details.Size,
        FileUrl = d.Details.FileUrl,
        IsFeatured = d.Details.IsFeatured,
        IsNew = d.Details.IsNew,
        IsUpdated = d.Details.IsUpdated,
        IsPopular = d.Details.IsPopular,
        Access = d.Details.Access.ToString(),
        RelatedExamIds = d.RelatedExamIds is null ? null : [.. d.RelatedExamIds],
        Status = d.Status.ToString(),
        CreatedAt = d.CreatedAt.UtcDateTime,
        UpdatedAt = d.UpdatedAt.UtcDateTime,
        PublishedAt = d.PublishedAt?.UtcDateTime,
        CreatedBy = d.CreatedBy.Value,
    };

    public static LibraryDocument ToDomain(LibraryDocumentDocument doc) =>
        LibraryDocument.Rehydrate(
            new LibraryDocumentId(doc.Id),
            new LibraryDocumentDetails(
                doc.Slug, doc.Title, doc.Description,
                Enum.Parse<DocumentSkill>(doc.Skill), doc.Category,
                Enum.Parse<DocumentType>(doc.Type), Enum.Parse<DocumentFormat>(doc.Format),
                doc.TargetBand, doc.Topic, doc.PageCount, doc.Size, doc.FileUrl,
                doc.IsFeatured, doc.IsNew, doc.IsUpdated, doc.IsPopular,
                Enum.Parse<DocumentAccess>(doc.Access)),
            doc.RelatedExamIds,
            Enum.Parse<LibraryContentStatus>(doc.Status),
            Utc(doc.CreatedAt), Utc(doc.UpdatedAt), Utc(doc.PublishedAt),
            new UserId(doc.CreatedBy));

    public static ArticleDocument ToDocument(Article a) => new()
    {
        Id = a.Id.Value,
        Slug = a.Details.Slug,
        Title = a.Details.Title,
        Excerpt = a.Details.Excerpt,
        Category = a.Details.Category.ToString(),
        ReadMinutes = a.Details.ReadMinutes,
        Author = a.Details.Author,
        Body = [.. a.Details.Body],
        RelatedExamIds = a.RelatedExamIds is null ? null : [.. a.RelatedExamIds],
        Status = a.Status.ToString(),
        CreatedAt = a.CreatedAt.UtcDateTime,
        UpdatedAt = a.UpdatedAt.UtcDateTime,
        PublishedAt = a.PublishedAt?.UtcDateTime,
        CreatedBy = a.CreatedBy.Value,
    };

    public static Article ToDomain(ArticleDocument doc) =>
        Article.Rehydrate(
            new ArticleId(doc.Id),
            new ArticleDetails(
                doc.Slug, doc.Title, doc.Excerpt, Enum.Parse<ArticleCategory>(doc.Category),
                doc.ReadMinutes, doc.Author, doc.Body),
            doc.RelatedExamIds,
            Enum.Parse<LibraryContentStatus>(doc.Status),
            Utc(doc.CreatedAt), Utc(doc.UpdatedAt), Utc(doc.PublishedAt),
            new UserId(doc.CreatedBy));
}

internal sealed class MongoLibraryDocumentStore(MongoContext ctx) : ILibraryDocumentStore
{
    private static readonly string Published = LibraryContentStatus.Published.ToString();

    /// <summary>
    /// <b>The status filter is applied here, in the query.</b> A learner-side
    /// listing that fetched everything and filtered in memory would work until
    /// the day someone reused the method — the store simply has no path that
    /// returns a draft to this caller.
    /// </summary>
    public async Task<IReadOnlyList<LibraryDocument>> ListPublishedAsync(
        LibraryDocumentFilter filter, CancellationToken ct)
    {
        var f = Builders<LibraryDocumentDocument>.Filter;
        var query = f.Eq(d => d.Status, Published);

        if (filter.Skill is { } skill) query &= f.Eq(d => d.Skill, skill.ToString());
        if (filter.Type is { } type) query &= f.Eq(d => d.Type, type.ToString());
        if (filter.TargetBand is { } band) query &= f.Eq(d => d.TargetBand, band);

        var docs = await ctx.LibraryDocuments.Find(query)
            .SortByDescending(d => d.PublishedAt)
            .ToListAsync(ct);

        // Free-text search stays in memory: the library is small, and a text
        // index would need diacritic-aware collation decided before it is
        // worth having. A hundred documents filtered here is not a cost.
        IEnumerable<LibraryDocumentDocument> result = docs;
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var q = filter.Query.Trim();
            result = docs.Where(d =>
                d.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || d.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (d.Topic?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return [.. result.Select(LibraryMappers.ToDomain)];
    }

    public async Task<IReadOnlyList<LibraryDocument>> ListAllAsync(CancellationToken ct) =>
        [.. (await ctx.LibraryDocuments.Find(FilterDefinition<LibraryDocumentDocument>.Empty)
                .SortByDescending(d => d.UpdatedAt)
                .ToListAsync(ct))
            .Select(LibraryMappers.ToDomain)];

    public async Task<LibraryDocument?> FindAsync(LibraryDocumentId id, CancellationToken ct)
    {
        var doc = await ctx.LibraryDocuments.Find(d => d.Id == id.Value).FirstOrDefaultAsync(ct);
        return doc is null ? null : LibraryMappers.ToDomain(doc);
    }

    public Task UpsertAsync(LibraryDocument document, CancellationToken ct) =>
        ctx.LibraryDocuments.ReplaceOneAsync(
            d => d.Id == document.Id.Value,
            LibraryMappers.ToDocument(document),
            new ReplaceOptions { IsUpsert = true },
            ct);

    public Task DeleteAsync(LibraryDocumentId id, CancellationToken ct) =>
        ctx.LibraryDocuments.DeleteOneAsync(d => d.Id == id.Value, ct);
}

internal sealed class MongoArticleStore(MongoContext ctx) : IArticleStore
{
    private static readonly string Published = LibraryContentStatus.Published.ToString();

    public async Task<IReadOnlyList<Article>> ListPublishedAsync(ArticleCategory? category, CancellationToken ct)
    {
        var f = Builders<ArticleDocument>.Filter;
        var query = f.Eq(a => a.Status, Published);
        if (category is { } c) query &= f.Eq(a => a.Category, c.ToString());

        return [.. (await ctx.Articles.Find(query).SortByDescending(a => a.PublishedAt).ToListAsync(ct))
            .Select(LibraryMappers.ToDomain)];
    }

    public async Task<IReadOnlyList<Article>> ListAllAsync(CancellationToken ct) =>
        [.. (await ctx.Articles.Find(FilterDefinition<ArticleDocument>.Empty)
                .SortByDescending(a => a.UpdatedAt)
                .ToListAsync(ct))
            .Select(LibraryMappers.ToDomain)];

    public async Task<Article?> FindAsync(ArticleId id, CancellationToken ct)
    {
        var doc = await ctx.Articles.Find(a => a.Id == id.Value).FirstOrDefaultAsync(ct);
        return doc is null ? null : LibraryMappers.ToDomain(doc);
    }

    public async Task<Article?> FindBySlugAsync(string slug, CancellationToken ct)
    {
        var doc = await ctx.Articles.Find(a => a.Slug == slug).FirstOrDefaultAsync(ct);
        return doc is null ? null : LibraryMappers.ToDomain(doc);
    }

    public async Task UpsertAsync(Article article, CancellationToken ct)
    {
        try
        {
            await ctx.Articles.ReplaceOneAsync(
                a => a.Id == article.Id.Value,
                LibraryMappers.ToDocument(article),
                new ReplaceOptions { IsUpsert = true },
                ct);
        }
        catch (MongoWriteException e)
            when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The unique slug index refused the write. Translated here so the
            // handler above never sees a driver type. → ADR-0004
            throw new SlugTakenException(article.Slug);
        }
    }

    public Task DeleteAsync(ArticleId id, CancellationToken ct) =>
        ctx.Articles.DeleteOneAsync(a => a.Id == id.Value, ct);
}
