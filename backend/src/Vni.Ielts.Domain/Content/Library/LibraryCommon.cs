using System.Text.RegularExpressions;

namespace Vni.Ielts.Domain.Content.Library;

/// <summary>
/// Identifiers for the two libraries. Strings, never <c>ObjectId</c> — the
/// same reasoning as every other id in <c>Domain.Common.Ids</c>.
/// </summary>
public readonly record struct LibraryDocumentId(string Value)
{
    public override string ToString() => Value;
    public static LibraryDocumentId New() => new(Guid.NewGuid().ToString("n"));
}

public readonly record struct ArticleId(string Value)
{
    public override string ToString() => Value;
    public static ArticleId New() => new(Guid.NewGuid().ToString("n"));
}

/// <summary>
/// The four-state lifecycle that Articles, Documents and (later) Dictation
/// share — <c>Đ8</c> in <c>cms-content-operations.md</c> § 3.2.
///
/// <pre>
/// Draft ──► PendingReview ──► Published ◄──► Unpublished
///   ▲              │
///   └── return ────┘
/// </pre>
///
/// <b>No separate "approved" state, on purpose.</b> An exam needs a subject
/// expert's signature before an admin ships it; a blog post does not, so the
/// person holding <c>*.publish</c> is the reviewer. Adding the state here
/// would make the CMS teach two lifecycles for content nobody distinguishes.
///
/// <b>Publish is allowed straight from Draft.</b> The pending state exists so
/// an editor without publish rights has somewhere to put finished work; an
/// admin who writes and ships their own post should not have to submit it to
/// themselves first.
/// </summary>
public enum LibraryContentStatus
{
    Draft,
    PendingReview,
    Published,
    Unpublished,
}

/// <summary>
/// The address part of a URL.
///
/// Lowercase ASCII letters, digits and single hyphens between them —
/// <c>huong-dan-viet-task-2</c>. No leading or trailing hyphen, no doubled
/// hyphen, no diacritics: the slug is what a learner sees in the address bar
/// and pastes into a chat, and anything that needs percent-encoding gets
/// mangled on the way. Uniqueness is not a property of the string; the store
/// enforces it with a unique index.
/// </summary>
public static partial class Slug
{
    public const int MaxLength = 120;

    public static bool IsValid(string? candidate) =>
        candidate is not null
        && candidate.Length is > 0 and <= MaxLength
        && Pattern().IsMatch(candidate);

    public static string Require(string? candidate, string paramName)
    {
        if (!IsValid(candidate))
            throw new ArgumentException(
                "A slug is lowercase letters, digits and single hyphens, up to 120 characters.",
                paramName);
        return candidate!;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Pattern();
}

/// <summary>
/// The lifecycle rules, written once so both libraries move the same way.
/// Each returns the next state or throws — a wrong transition is a caller
/// defect, and the handlers check before asking.
/// </summary>
public static class LibraryLifecycle
{
    public static bool CanSubmit(LibraryContentStatus s) => s == LibraryContentStatus.Draft;

    public static bool CanReturn(LibraryContentStatus s) => s == LibraryContentStatus.PendingReview;

    public static bool CanPublish(LibraryContentStatus s) => s != LibraryContentStatus.Published;

    public static bool CanUnpublish(LibraryContentStatus s) => s == LibraryContentStatus.Published;

    /// <summary>
    /// Learner-visible content is not deleted; it is unpublished first. A row
    /// that vanishes from under a learner with no audit line is the failure
    /// this prevents.
    /// </summary>
    public static bool CanDelete(LibraryContentStatus s) => s != LibraryContentStatus.Published;

    internal static void Guard(bool allowed, string verb, LibraryContentStatus current)
    {
        if (!allowed)
            throw new InvalidOperationException($"Cannot {verb} content that is {current}.");
    }
}
