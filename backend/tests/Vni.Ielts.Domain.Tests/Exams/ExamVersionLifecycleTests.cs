using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

/// <summary>
/// The six-state content lifecycle. → `C-19`,
/// `docs/ux/cms-content-operations.md` §3.1
///
/// Every transition is tested both for what it does and for the state it
/// refuses to run from — a lifecycle method that only works on the happy path
/// is a lifecycle a wrong-order request can quietly corrupt.
/// </summary>
public sealed class ExamVersionLifecycleTests
{
    private static readonly UserId Author = UserId.New();
    private static readonly UserId Reviewer = UserId.New();
    private static readonly DateTimeOffset T0 = new(2026, 8, 26, 9, 0, 0, TimeSpan.Zero);

    private static ExamVersion Draft() => ExamVersion.CreateDraft(
        ExamDefinitionId.New(), 1, "Test", ExamVariant.Academic, Author,
        new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default),
        new TimingProfile(new Dictionary<ExamModule, int> { [ExamModule.Reading] = 60 }, null, []),
        [new Section(ExamModule.Reading, 1, [])]);

    // ── SubmitForReview ──────────────────────────────────────────────────

    [Fact]
    public void Submitting_a_draft_moves_it_to_in_review_and_stamps_who_and_when()
    {
        var version = Draft();
        version.SubmitForReview(Author, T0);

        Assert.Equal(ExamVersionStatus.InReview, version.Status);
        Assert.Equal(Author, version.SubmittedBy);
        Assert.Equal(T0, version.SubmittedAt);
    }

    [Fact]
    public void Submitting_anything_but_a_draft_throws()
    {
        var version = Draft();
        version.SubmitForReview(Author, T0);

        Assert.Throws<InvalidOperationException>(() => version.SubmitForReview(Author, T0));
    }

    // ── Withdraw ─────────────────────────────────────────────────────────

    [Fact]
    public void Withdrawing_a_submission_returns_it_to_draft_and_clears_the_submission_stamp()
    {
        var version = Draft();
        version.SubmitForReview(Author, T0);

        version.Withdraw();

        Assert.Equal(ExamVersionStatus.Draft, version.Status);
        Assert.Null(version.SubmittedBy);
        Assert.Null(version.SubmittedAt);
    }

    [Fact]
    public void Withdrawing_a_draft_that_was_never_submitted_throws() =>
        Assert.Throws<InvalidOperationException>(() => Draft().Withdraw());

    // ── Return ───────────────────────────────────────────────────────────

    [Fact]
    public void Returning_a_submission_moves_it_to_returned_and_records_the_note()
    {
        var version = Draft();
        version.SubmitForReview(Author, T0);

        version.Return(Reviewer, "Câu 7 thiếu đáp án.", T0.AddHours(1));

        Assert.Equal(ExamVersionStatus.Returned, version.Status);
        Assert.Equal(Reviewer, version.ReviewedBy);
        Assert.Equal(T0.AddHours(1), version.ReviewedAt);
        var note = Assert.Single(version.ReviewNotes);
        Assert.Equal(Reviewer, note.AuthorId);
        Assert.Equal("Câu 7 thiếu đáp án.", note.Body);
        Assert.Null(note.Anchor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Returning_without_a_real_note_throws(string blankNote)
    {
        var version = Draft();
        version.SubmitForReview(Author, T0);

        // An author who receives a return with no explanation has to guess
        // what to fix — a guess burns a whole review cycle on the wrong
        // change. → docs/ux/cms-content-operations.md P1-8
        var ex = Assert.Throws<InvalidOperationException>(() => version.Return(Reviewer, blankNote, T0));
        Assert.Contains("note", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Returning_a_draft_that_was_never_submitted_throws()
    {
        var version = Draft();
        Assert.Throws<InvalidOperationException>(() => version.Return(Reviewer, "why", T0));
    }

    // ── Approve ──────────────────────────────────────────────────────────

    [Fact]
    public void Approving_a_submission_moves_it_to_approved_without_a_note()
    {
        var version = Draft();
        version.SubmitForReview(Author, T0);

        version.Approve(Reviewer, T0.AddHours(1));

        Assert.Equal(ExamVersionStatus.Approved, version.Status);
        Assert.Equal(Reviewer, version.ReviewedBy);
        Assert.Equal(T0.AddHours(1), version.ReviewedAt);
        Assert.Empty(version.ReviewNotes);
    }

    [Fact]
    public void Approving_with_a_note_records_it()
    {
        var version = Draft();
        version.SubmitForReview(Author, T0);

        version.Approve(Reviewer, T0.AddHours(1), "Đẹp, chỉ cần sửa chính tả nhỏ.");

        var note = Assert.Single(version.ReviewNotes);
        Assert.Equal("Đẹp, chỉ cần sửa chính tả nhỏ.", note.Body);
    }

    [Fact]
    public void Approving_anything_but_an_in_review_submission_throws() =>
        Assert.Throws<InvalidOperationException>(() => Draft().Approve(Reviewer, T0));

    // ── Unapprove ────────────────────────────────────────────────────────

    [Fact]
    public void Unapproving_reopens_review_and_clears_the_review_stamp()
    {
        var version = Draft();
        version.SubmitForReview(Author, T0);
        version.Approve(Reviewer, T0.AddHours(1));

        version.Unapprove();

        Assert.Equal(ExamVersionStatus.InReview, version.Status);
        Assert.Null(version.ReviewedBy);
        Assert.Null(version.ReviewedAt);
    }

    [Fact]
    public void Unapproving_anything_but_approved_throws() =>
        Assert.Throws<InvalidOperationException>(() => Draft().Unapprove());

    // ── Resume ───────────────────────────────────────────────────────────

    [Fact]
    public void Resuming_a_returned_version_sends_it_back_to_draft()
    {
        var version = Draft();
        version.SubmitForReview(Author, T0);
        version.Return(Reviewer, "sửa Câu 3", T0.AddHours(1));

        version.Resume();

        Assert.Equal(ExamVersionStatus.Draft, version.Status);
    }

    [Fact]
    public void Resuming_anything_but_returned_throws() =>
        Assert.Throws<InvalidOperationException>(() => Draft().Resume());

    // ── Publish / Unpublish ──────────────────────────────────────────────

    /// <summary>
    /// The domain method is a backstop, not the gate. The real rule — a
    /// version reaches Published only from Approved or Unpublished — is
    /// enforced by the caller (`AdminEndpoints.PublishEndpoint`), the same
    /// division of labour this method has always had. Existing test fixtures
    /// across the suite construct a sittable version with `CreateDraft` then
    /// `Publish` directly, and that shortcut has to keep working.
    /// </summary>
    [Fact]
    public void Publishing_works_from_any_state_that_is_not_already_published()
    {
        var version = Draft();
        version.Publish(T0);

        Assert.Equal(ExamVersionStatus.Published, version.Status);
        Assert.Equal(T0, version.PublishedAt);
    }

    [Fact]
    public void Publishing_an_already_published_version_throws()
    {
        var version = Draft();
        version.Publish(T0);

        Assert.Throws<InvalidOperationException>(() => version.Publish(T0.AddDays(1)));
    }

    [Fact]
    public void Unpublishing_a_published_version_works()
    {
        var version = Draft();
        version.Publish(T0);

        version.Unpublish();

        Assert.Equal(ExamVersionStatus.Unpublished, version.Status);
    }

    [Fact]
    public void Unpublishing_anything_but_published_throws() =>
        Assert.Throws<InvalidOperationException>(() => Draft().Unpublish());

    [Fact]
    public void Republishing_an_unpublished_version_works()
    {
        var version = Draft();
        version.Publish(T0);
        version.Unpublish();

        version.Publish(T0.AddDays(1));

        Assert.Equal(ExamVersionStatus.Published, version.Status);
        Assert.Equal(T0.AddDays(1), version.PublishedAt);
    }

    // ── CreatedBy ────────────────────────────────────────────────────────

    [Fact]
    public void CreatedBy_never_changes_across_the_whole_lifecycle()
    {
        var version = Draft();
        Assert.Equal(Author, version.CreatedBy);

        version.SubmitForReview(Author, T0);
        version.Return(Reviewer, "note", T0);
        version.Resume();
        version.SubmitForReview(Author, T0);
        version.Approve(Reviewer, T0);
        version.Publish(T0);
        version.Unpublish();

        Assert.Equal(Author, version.CreatedBy);
    }
}
