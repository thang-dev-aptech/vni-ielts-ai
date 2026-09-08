using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using SessionMode = Vni.Ielts.Domain.Sessions.SessionMode;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// `S1a` / `P-11` — a Reading or Listening band reaches the results screen
/// with a <c>BandVerified</c> flag, true only when the exam version's own
/// <c>ScoringProfile.Provenance</c> says <c>equated</c>. Every other status,
/// and its absence, must read as not-equated — `H-4` is open, and the
/// conservative reading is the only one this screen is allowed to show.
/// </summary>
public sealed class BandVerificationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Owner = UserId.New();
    private static readonly ExamSessionId Sitting = ExamSessionId.New();

    [Fact]
    public void Absent_provenance_is_not_verified()
    {
        var version = ReadingVersion(provenance: null);

        var results = SubmittedReading(version).ToResults(version, [Score(2)], []);

        var section = Assert.Single(results.Sections);
        Assert.NotNull(section.Band);
        Assert.False(section.BandVerified);
    }

    [Theory]
    [InlineData(BandTableProvenanceStatus.Synthetic)]
    [InlineData(BandTableProvenanceStatus.Provisional)]
    public void Anything_short_of_equated_is_not_verified(BandTableProvenanceStatus status)
    {
        var version = ReadingVersion(new BandTableProvenance(status, Source: "VNI academic team"));

        var results = SubmittedReading(version).ToResults(version, [Score(2)], []);

        var section = Assert.Single(results.Sections);
        Assert.NotNull(section.Band);
        Assert.False(section.BandVerified);
    }

    [Fact]
    public void Equated_is_the_only_status_that_verifies_the_band()
    {
        var version = ReadingVersion(new BandTableProvenance(
            BandTableProvenanceStatus.Equated,
            Source: "VNI academic team, equated against Cambridge 19 Test 1"));

        var results = SubmittedReading(version).ToResults(version, [Score(2)], []);

        var section = Assert.Single(results.Sections);
        Assert.NotNull(section.Band);
        Assert.True(section.BandVerified);
    }

    [Fact]
    public void Band_verified_gates_display_and_is_not_a_second_source_of_the_number()
    {
        // Same raw score, same band value, either way — only the permission
        // to show it should move. If this test ever needs two different
        // Band values to tell the cases apart, BandVerified has stopped
        // being a gate and become a second scorer.
        var synthetic = ReadingVersion(new BandTableProvenance(BandTableProvenanceStatus.Synthetic));
        var equated = ReadingVersion(new BandTableProvenance(
            BandTableProvenanceStatus.Equated, Source: "VNI academic team"));

        var withoutProof = SubmittedReading(synthetic).ToResults(synthetic, [Score(2)], []);
        var withProof = SubmittedReading(equated).ToResults(equated, [Score(2)], []);

        Assert.Equal(withoutProof.Sections[0].Band, withProof.Sections[0].Band);
        Assert.NotEqual(withoutProof.Sections[0].BandVerified, withProof.Sections[0].BandVerified);
    }

    // ── Fixtures ──────────────────────────────────────────────────────────

    private static SectionScore Score(int raw) =>
        new(ExamModule.Reading, raw, 2, BandScore.Create(6.0m), []);

    private static ExamSession SubmittedReading(ExamVersion version) =>
        ExamSession.Rehydrate(
            Sitting, Owner, version.Id, SessionMode.Single, SessionStatus.Submitted,
            T0, T0.AddHours(1),
            [SectionAttempt.Rehydrate(ExamModule.Reading, T0, null, T0)],
            SessionTiming.OpenEnded);

    private static ExamVersion ReadingVersion(BandTableProvenance? provenance)
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>
            {
                [ExamModule.Reading] =
                [
                    new BandBoundary(0, BandScore.Create(4.0m)),
                    new BandBoundary(1, BandScore.Create(5.5m)),
                    new BandBoundary(2, BandScore.Create(6.0m)),
                ],
            },
            AnswerMatchingRules.Default,
            Provenance: provenance);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Band verification", ExamVariant.Academic, scoring, timing,
            [
                new Section(ExamModule.Reading, 1,
                [
                    new SectionPart(1, "passage", "P", "body", null, null, null, null, null, null, null,
                    [
                        new Question("r-1", 1, QuestionType.ShortAnswer, "q", [], null,
                            new AnswerKey([new AcceptedAnswer("paper", null, null)], null)),
                        new Question("r-2", 2, QuestionType.ShortAnswer, "q", [], null,
                            new AnswerKey([new AcceptedAnswer("wood", null, null)], null)),
                    ]),
                ]),
            ]);
        version.Publish(T0.AddDays(-1));
        return version;
    }
}
