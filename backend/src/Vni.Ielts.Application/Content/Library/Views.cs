using Vni.Ielts.Domain.Content.Library;

namespace Vni.Ielts.Application.Content.Library;

/// <summary>
/// The wire spelling of every closed set the libraries carry, in one place.
///
/// The learner app already speaks these values — <c>skill: 'reading'</c>,
/// <c>format: 'PDF'</c>, <c>category: 'huong-dan'</c> — and the CLR member
/// name is not the contract. Parsing is case-insensitive and returns null on
/// a miss so a handler can report every bad field at once rather than throw
/// on the first.
/// </summary>
public static class LibraryWire
{
    public static string Status(LibraryContentStatus s) => s switch
    {
        LibraryContentStatus.Draft => "draft",
        LibraryContentStatus.PendingReview => "pending",
        LibraryContentStatus.Published => "published",
        LibraryContentStatus.Unpublished => "unpublished",
        _ => s.ToString().ToLowerInvariant(),
    };

    public static string Skill(DocumentSkill s) => s.ToString().ToLowerInvariant();

    public static DocumentSkill? ParseSkill(string? s) => ParseEnum<DocumentSkill>(s);

    public static string Type(DocumentType t) => t.ToString().ToLowerInvariant();

    public static DocumentType? ParseType(string? s) => ParseEnum<DocumentType>(s);

    public static string Format(DocumentFormat f) => f.ToString().ToUpperInvariant();

    public static DocumentFormat? ParseFormat(string? s) => ParseEnum<DocumentFormat>(s);

    public static string Access(DocumentAccess a) => a.ToString().ToLowerInvariant();

    public static DocumentAccess? ParseAccess(string? s) => ParseEnum<DocumentAccess>(s);

    public static string Category(ArticleCategory c) => c switch
    {
        ArticleCategory.HuongDan => "huong-dan",
        ArticleCategory.BaiViet => "bai-viet",
        ArticleCategory.TuyenDung => "tuyen-dung",
        _ => c.ToString().ToLowerInvariant(),
    };

    public static ArticleCategory? ParseCategory(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "huong-dan" => ArticleCategory.HuongDan,
        "bai-viet" => ArticleCategory.BaiViet,
        "tuyen-dung" => ArticleCategory.TuyenDung,
        _ => null,
    };

    private static T? ParseEnum<T>(string? s) where T : struct, Enum =>
        !string.IsNullOrWhiteSpace(s) && Enum.TryParse<T>(s.Trim(), ignoreCase: true, out var v)
        && Enum.IsDefined(v)
            ? v
            : null;
}

/// <summary>
/// A document as both the learner app and the CMS receive it. One shape for
/// both surfaces: the learner-only fields (<c>isFeatured</c>…) cost the CMS
/// nothing, and the lifecycle fields (<c>status</c>, <c>createdBy</c>) tell a
/// learner nothing they can act on.
/// </summary>
public sealed record LibraryDocumentView(
    string Id,
    string Slug,
    string Title,
    string Description,
    string Skill,
    string Category,
    string Type,
    string Format,
    string? TargetBand,
    string? Topic,
    int? PageCount,
    string Size,
    string? FileUrl,
    bool IsFeatured,
    bool IsNew,
    bool IsUpdated,
    bool IsPopular,
    string Access,
    IReadOnlyList<string>? RelatedExamIds,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    string CreatedBy)
{
    public static LibraryDocumentView Of(LibraryDocument d) => new(
        d.Id.Value, d.Details.Slug, d.Details.Title, d.Details.Description,
        LibraryWire.Skill(d.Details.Skill), d.Details.Category, LibraryWire.Type(d.Details.Type),
        LibraryWire.Format(d.Details.Format), d.Details.TargetBand, d.Details.Topic, d.Details.PageCount,
        d.Details.Size, d.Details.FileUrl, d.Details.IsFeatured, d.Details.IsNew, d.Details.IsUpdated,
        d.Details.IsPopular, LibraryWire.Access(d.Details.Access),
        d.RelatedExamIds, LibraryWire.Status(d.Status),
        d.CreatedAt, d.UpdatedAt, d.PublishedAt, d.CreatedBy.Value);
}

/// <summary>An article in a listing — everything but the body.</summary>
public sealed record ArticleSummaryView(
    string Id,
    string Slug,
    string Title,
    string Excerpt,
    string Category,
    int ReadMinutes,
    string Author,
    IReadOnlyList<string>? RelatedExamIds,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    string CreatedBy)
{
    public static ArticleSummaryView Of(Article a) => new(
        a.Id.Value, a.Slug, a.Details.Title, a.Details.Excerpt, LibraryWire.Category(a.Details.Category),
        a.Details.ReadMinutes, a.Details.Author, a.RelatedExamIds, LibraryWire.Status(a.Status),
        a.CreatedAt, a.UpdatedAt, a.PublishedAt, a.CreatedBy.Value);
}

/// <summary>The whole post. Same fields as the summary plus <c>body</c>.</summary>
public sealed record ArticleView(
    string Id,
    string Slug,
    string Title,
    string Excerpt,
    string Category,
    int ReadMinutes,
    string Author,
    IReadOnlyList<string> Body,
    IReadOnlyList<string>? RelatedExamIds,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    string CreatedBy)
{
    public static ArticleView Of(Article a) => new(
        a.Id.Value, a.Slug, a.Details.Title, a.Details.Excerpt, LibraryWire.Category(a.Details.Category),
        a.Details.ReadMinutes, a.Details.Author, a.Details.Body, a.RelatedExamIds, LibraryWire.Status(a.Status),
        a.CreatedAt, a.UpdatedAt, a.PublishedAt, a.CreatedBy.Value);
}
