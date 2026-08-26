using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests.Exams;

/// <summary>
/// The lifecycle fields added for the six-state workflow round-trip through
/// Mongo exactly like every other field on <see cref="ExamVersion"/>.
///
/// <b>Why this matters more here than elsewhere in the mapper.</b> Every
/// <c>DateTimeOffset</c> on the domain type crosses the Mongo boundary through
/// an explicit UTC conversion (see <c>ExamMappers.Utc</c>/<c>Offset</c>) — a
/// new timestamp field that skips this gets silently read back as
/// <c>DateTimeKind.Unspecified</c>, which compares wrong against
/// <c>DateTimeOffset.UtcNow</c> everywhere except a UTC machine.
/// </summary>
public sealed class ExamMapperTests
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

    [Fact]
    public void A_freshly_created_draft_round_trips_its_creator()
    {
        var version = Draft();

        var restored = version.ToDocument().ToDomain();

        Assert.Equal(version.CreatedBy, restored.CreatedBy);
        Assert.Equal(ExamVersionStatus.Draft, restored.Status);
        Assert.Null(restored.SubmittedBy);
        Assert.Null(restored.ReviewedBy);
        Assert.Empty(restored.ReviewNotes);
    }

    [Fact]
    public void Submission_and_review_stamps_round_trip_with_their_UTC_offset()
    {
        var version = Draft();
        version.SubmitForReview(Author, T0);
        version.Approve(Reviewer, T0.AddHours(2));

        var restored = version.ToDocument().ToDomain();

        Assert.Equal(ExamVersionStatus.Approved, restored.Status);
        Assert.Equal(Author, restored.SubmittedBy);
        Assert.Equal(T0, restored.SubmittedAt);
        Assert.Equal(Reviewer, restored.ReviewedBy);
        Assert.Equal(T0.AddHours(2), restored.ReviewedAt);
        Assert.Equal(TimeSpan.Zero, restored.SubmittedAt!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, restored.ReviewedAt!.Value.Offset);
    }

    [Fact]
    public void A_review_note_with_no_anchor_round_trips_as_null()
    {
        var version = Draft();
        version.SubmitForReview(Author, T0);
        version.Return(Reviewer, "Câu 7 thiếu đáp án.", T0.AddHours(1));

        var restored = version.ToDocument().ToDomain();

        var note = Assert.Single(restored.ReviewNotes);
        Assert.Equal(Reviewer, note.AuthorId);
        Assert.Equal("Câu 7 thiếu đáp án.", note.Body);
        Assert.Null(note.Anchor);
        Assert.Equal(T0.AddHours(1), note.At);
    }

    /// <summary>
    /// The domain never sets an anchor itself today — <c>Approve</c>/<c>Return</c>
    /// both pass <c>null</c> — so this goes through <c>Rehydrate</c> directly
    /// to prove the mapper preserves a non-null anchor whenever one exists,
    /// ahead of the CMS authoring screen that will eventually set one.
    /// </summary>
    [Fact]
    public void A_review_note_with_an_anchor_round_trips_the_anchor()
    {
        var version = ExamVersion.Rehydrate(
            ExamVersionId.New(), ExamDefinitionId.New(), 1, "Test", ExamVariant.Academic,
            ExamVersionStatus.Returned, Author, Author, T0, Reviewer, T0, null,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
                AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int> { [ExamModule.Reading] = 60 }, null, []),
            [new Section(ExamModule.Reading, 1, [])],
            [new ReviewNote("note-1", Reviewer, "Xem lại Câu 12", "Câu 12", T0)]);

        var restored = version.ToDocument().ToDomain();

        var note = Assert.Single(restored.ReviewNotes);
        Assert.Equal("Xem lại Câu 12", note.Body);
        Assert.Equal("Câu 12", note.Anchor);
    }
}
