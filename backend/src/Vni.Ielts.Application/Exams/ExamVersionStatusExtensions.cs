using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Exams;

/// <summary>
/// CMS wire spelling for <see cref="ExamVersionStatus"/>.
///
/// Matches main's existing convention: <c>ToString().ToLowerInvariant()</c>
/// → <c>inreview</c>, not the feature-branch kebab-case <c>in-review</c>.
/// Main has no distinct <c>Returned</c> status — return-to-draft lands on
/// <see cref="ExamVersionStatus.Draft"/>.
/// </summary>
public static class ExamVersionStatusExtensions
{
    public static string ToWire(this ExamVersionStatus status) => status switch
    {
        ExamVersionStatus.Draft => "draft",
        ExamVersionStatus.InReview => "inreview",
        ExamVersionStatus.Approved => "approved",
        ExamVersionStatus.Published => "published",
        ExamVersionStatus.Unpublished => "unpublished",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped exam version status."),
    };
}
