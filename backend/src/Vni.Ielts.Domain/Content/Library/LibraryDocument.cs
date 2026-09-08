using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Content.Library;

public enum DocumentSkill { Reading, Listening, Writing, Speaking, Vocabulary, Grammar, General }

public enum DocumentType { Pdf, Worksheet, Guide, Practice }

/// <summary>Display label for the file — PDF, DOCX, MP3.</summary>
public enum DocumentFormat { Pdf, Docx, Mp3 }

/// <summary>
/// Free to anyone signed in, or part of VNI's paid material.
///
/// <b>There is no price on this record.</b> <c>B-4</c> and <c>B-5b</c> are
/// open; a premium document routes to the hotline, never to a checkout. → <c>G-11</c>
/// </summary>
public enum DocumentAccess { Free, Premium }

/// <summary>
/// The editable half of a document — everything an editor types. Separated
/// from the entity so create and update take one shape and validate once.
/// </summary>
/// <param name="TargetBand">One of <see cref="LibraryDocument.Bands"/>, or null.</param>
/// <param name="Size">A display string — "2.4 MB". The file itself is not stored yet.</param>
/// <param name="FileUrl">Absent until a file exists. The page renders that as "Sắp có".</param>
public sealed record LibraryDocumentDetails(
    string Slug,
    string Title,
    string Description,
    DocumentSkill Skill,
    string Category,
    DocumentType Type,
    DocumentFormat Format,
    string? TargetBand,
    string? Topic,
    int? PageCount,
    string Size,
    string? FileUrl,
    bool IsFeatured,
    bool IsNew,
    bool IsUpdated,
    bool IsPopular,
    DocumentAccess Access);

/// <summary>
/// One entry in the document library — <c>P-22</c>.
///
/// <b>Independent of the exam catalogue.</b> <see cref="RelatedExamIds"/> is
/// reserved and stays <c>null</c>; linking a document to the papers it
/// supports is a later feature, and reserving the field now means that
/// feature is a write, not a migration.
/// </summary>
public sealed class LibraryDocument
{
    /// <summary>The band labels the catalogue filters on. A closed set, and a display one.</summary>
    public static readonly IReadOnlyList<string> Bands = ["5.0", "5.5", "6.0", "6.5", "7.0+"];

    private LibraryDocument(
        LibraryDocumentId id, LibraryDocumentDetails details, IReadOnlyList<string>? relatedExamIds,
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

    public LibraryDocumentId Id { get; }
    public LibraryDocumentDetails Details { get; private set; }

    /// <summary>Reserved. Always <c>null</c> in this slice — see the class remarks.</summary>
    public IReadOnlyList<string>? RelatedExamIds { get; }

    public LibraryContentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public UserId CreatedBy { get; }

    public static LibraryDocument Create(LibraryDocumentDetails details, UserId createdBy, DateTimeOffset now) =>
        new(LibraryDocumentId.New(), Validate(details), relatedExamIds: null,
            LibraryContentStatus.Draft, now, now, publishedAt: null, createdBy);

    public static LibraryDocument Rehydrate(
        LibraryDocumentId id, LibraryDocumentDetails details, IReadOnlyList<string>? relatedExamIds,
        LibraryContentStatus status, DateTimeOffset createdAt, DateTimeOffset updatedAt,
        DateTimeOffset? publishedAt, UserId createdBy) =>
        new(id, details, relatedExamIds, status, createdAt, updatedAt, publishedAt, createdBy);

    /// <summary>
    /// Replaces the editable fields. Allowed in any state: a published
    /// document that gains a file URL should not have to be unpublished to
    /// get it. The status does not move.
    /// </summary>
    public void Update(LibraryDocumentDetails details, DateTimeOffset now)
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

    private static LibraryDocumentDetails Validate(LibraryDocumentDetails d)
    {
        ArgumentNullException.ThrowIfNull(d);
        Slug.Require(d.Slug, nameof(d.Slug));
        if (string.IsNullOrWhiteSpace(d.Title))
            throw new ArgumentException("A title is required.", nameof(d.Title));
        if (d.TargetBand is not null && !Bands.Contains(d.TargetBand))
            throw new ArgumentException($"Target band must be one of {string.Join(", ", Bands)}.", nameof(d.TargetBand));
        if (d.PageCount is < 1)
            throw new ArgumentException("Page count must be positive.", nameof(d.PageCount));
        return d with { Title = d.Title.Trim() };
    }
}
