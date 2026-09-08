using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

/// <summary>
/// `P-20` — author submits, a different person reviews, admin publishes —
/// tested at the entity level, where the one rule that must never be
/// bypassable lives: a reviewer may not approve a version they authored.
///
/// <b>That rule is the primary target of this file.</b> A disabled button in
/// the CMS proves nothing about a caller who skips the CMS entirely; only a
/// test that calls <see cref="ExamVersion.Approve"/> directly, the same way
/// any future handler or script would, proves the server itself refuses.
/// </summary>
public sealed class ExamVersionReviewLifecycleTests
{
    private static ExamVersion Draft(UserId? authorId = null) => ExamVersion.CreateDraft(
        ExamDefinitionId.New(), 1, "Test paper", ExamVariant.Academic,
        new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default),
        new TimingProfile(new Dictionary<ExamModule, int> { [ExamModule.Reading] = 60 }, null, []),
        [new Section(ExamModule.Reading, 1, [])],
        authorId: authorId);

    // ── SubmitForReview ──────────────────────────────────────────────────

    [Fact]
    public void Submitting_a_draft_moves_it_to_in_review()
    {
        var version = Draft();
        version.SubmitForReview();
        Assert.Equal(ExamVersionStatus.InReview, version.Status);
    }

    [Fact]
    public void Submitting_a_version_that_is_not_a_draft_is_refused()
    {
        var version = Draft();
        version.SubmitForReview();

        Assert.Throws<InvalidOperationException>(version.SubmitForReview);
    }

    // ── Approve: the reviewer ≠ author rule ─────────────────────────────

    [Fact]
    public void A_different_reviewer_may_approve_a_version_in_review()
    {
        var author = UserId.New();
        var reviewer = UserId.New();

        var version = Draft(author);
        version.SubmitForReview();

        version.Approve(reviewer);

        Assert.Equal(ExamVersionStatus.Approved, version.Status);
    }

    /// <summary>
    /// The primary red-when-removed target for this slice. Verified by
    /// temporarily removing the `author == reviewerId` guard in
    /// <c>ExamContent.cs</c>, confirming this test fails, and restoring it —
    /// see the S7 report for the before/after run.
    /// </summary>
    [Fact]
    public void The_author_may_not_approve_their_own_version()
    {
        var author = UserId.New();

        var version = Draft(author);
        version.SubmitForReview();

        var ex = Assert.Throws<ReviewerIsAuthorException>(() => version.Approve(author));
        Assert.Contains("duyệt", ex.Message, StringComparison.OrdinalIgnoreCase);

        // And the refusal did not half-apply — the version is still sitting
        // in review, not silently approved or corrupted into some other state.
        Assert.Equal(ExamVersionStatus.InReview, version.Status);
    }

    [Fact]
    public void An_unknown_author_cannot_trigger_the_reviewer_equals_author_refusal()
    {
        // Documented gap, not an oversight: a version imported through
        // ExamPackageReader today carries no author at all, and Approve must
        // still be usable on it — refusing every review until the import
        // pipeline threads an actor id through would stop content shipping
        // for a reason no operator on this screen could fix.
        var version = Draft(authorId: null);
        version.SubmitForReview();

        version.Approve(UserId.New());

        Assert.Equal(ExamVersionStatus.Approved, version.Status);
    }

    [Fact]
    public void Approving_a_draft_that_is_not_in_review_is_refused()
    {
        var version = Draft();
        Assert.Throws<InvalidOperationException>(() => version.Approve(UserId.New()));
    }

    // ── ReturnToDraft ─────────────────────────────────────────────────────

    [Fact]
    public void Returning_a_version_in_review_sends_it_back_to_draft()
    {
        var version = Draft();
        version.SubmitForReview();

        version.ReturnToDraft("Thiếu transcript phần Listening.");

        Assert.Equal(ExamVersionStatus.Draft, version.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Returning_a_version_without_a_reason_is_refused(string? reason)
    {
        var version = Draft();
        version.SubmitForReview();

        Assert.Throws<ArgumentException>(() => version.ReturnToDraft(reason!));
        // And refusing the reason did not change the status underneath it.
        Assert.Equal(ExamVersionStatus.InReview, version.Status);
    }

    [Fact]
    public void Returning_a_draft_that_is_not_in_review_is_refused()
    {
        var version = Draft();
        Assert.Throws<InvalidOperationException>(() => version.ReturnToDraft("lý do"));
    }
}
