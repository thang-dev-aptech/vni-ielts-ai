using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Content.Library;

/// <summary>
/// What kind of post this is — not which skill it is about. The set the
/// learner app filters on since 22/08: guides, general posts, recruitment.
/// Wire spelling is kebab-case Vietnamese (<c>huong-dan</c>, <c>bai-viet</c>,
/// <c>tuyen-dung</c>); the mapping lives in Application.
/// </summary>
public enum ArticleCategory { HuongDan, BaiViet, TuyenDung }

/// <param name="ReadMinutes">Rounded minutes. An estimate, and labelled as one.</param>
/// <param name="Body">Paragraphs. Plain strings until the CMS decides what a body is made of.</param>
public sealed record ArticleDetails(
    string Slug,
    string Title,
    string Excerpt,
    ArticleCategory Category,
    int ReadMinutes,
    string Author,
    IReadOnlyList<string> Body);

/// <summary>
/// One post — <c>P-22</c>.
///
/// <b>Addressed by slug, never by id.</b> <c>/articles/&lt;slug&gt;</c> is the
/// URL a learner shares; the id exists so the slug can be corrected without
/// the record losing its history. The slug is unique across the collection
/// regardless of status — a draft that takes a slug reserves it, otherwise
/// publishing the draft would break whichever live post held it.
///
/// <see cref="RelatedExamIds"/> is reserved and <c>null</c>, as on
/// <see cref="LibraryDocument"/>.
/// </summary>
public sealed class Article
{
    private Article(
        ArticleId id, ArticleDetails details, IReadOnlyList<string>? relatedExamIds,
        LibraryContentStatus status, DateTimeOffset createdAt, DateTimeOffset updatedAt,
        DateTimeOffset? publishedAt, UserId createdBy)
    {
        Id = id;
        Details = details;
        RelatedExamIds = relatedExamIds;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        PublishedAt = publishedAt;
        CreatedBy = createdBy;
    }

    public ArticleId Id { get; }
    public ArticleDetails Details { get; private set; }
    public string Slug => Details.Slug;

    /// <summary>Reserved. Always <c>null</c> in this slice.</summary>
    public IReadOnlyList<string>? RelatedExamIds { get; }

    public LibraryContentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public UserId CreatedBy { get; }

    public static Article Create(ArticleDetails details, UserId createdBy, DateTimeOffset now) =>
        new(ArticleId.New(), Validate(details), relatedExamIds: null,
            LibraryContentStatus.Draft, now, now, publishedAt: null, createdBy);

    public static Article Rehydrate(
        ArticleId id, ArticleDetails details, IReadOnlyList<string>? relatedExamIds,
        LibraryContentStatus status, DateTimeOffset createdAt, DateTimeOffset updatedAt,
        DateTimeOffset? publishedAt, UserId createdBy) =>
        new(id, details, relatedExamIds, status, createdAt, updatedAt, publishedAt, createdBy);

    public void Update(ArticleDetails details, DateTimeOffset now)
    {
        Details = Validate(details);
        UpdatedAt = now;
    }

    public void Submit(DateTimeOffset now) =>
        Move(LibraryLifecycle.CanSubmit(Status), "submit", LibraryContentStatus.PendingReview, now);

    public void ReturnToDraft(DateTimeOffset now) =>
        Move(LibraryLifecycle.CanReturn(Status), "return", LibraryContentStatus.Draft, now);

    public void Publish(DateTimeOffset now)
    {
        Move(LibraryLifecycle.CanPublish(Status), "publish", LibraryContentStatus.Published, now);
        PublishedAt = now;
    }

    public void Unpublish(DateTimeOffset now) =>
        Move(LibraryLifecycle.CanUnpublish(Status), "unpublish", LibraryContentStatus.Unpublished, now);

    private void Move(bool allowed, string verb, LibraryContentStatus next, DateTimeOffset now)
    {
        LibraryLifecycle.Guard(allowed, verb, Status);
        Status = next;
        UpdatedAt = now;
    }

    private static ArticleDetails Validate(ArticleDetails d)
    {
        ArgumentNullException.ThrowIfNull(d);
        Library.Slug.Require(d.Slug, nameof(d.Slug));
        if (string.IsNullOrWhiteSpace(d.Title))
            throw new ArgumentException("A title is required.", nameof(d.Title));
        if (d.ReadMinutes < 1)
            throw new ArgumentException("Read time is at least one minute.", nameof(d.ReadMinutes));
        ArgumentNullException.ThrowIfNull(d.Body, nameof(d.Body));
        return d with { Title = d.Title.Trim(), Body = [.. d.Body] };
    }
}
