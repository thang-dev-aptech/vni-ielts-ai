using Vni.Ielts.Application.Content;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Content;

/// <summary>
/// Publishing material whose rights nobody has proven.
///
/// <b>Owner decision 2026-09-18: the papers in use go out.</b> `M-53` asked
/// who may publish what; the owner answered that these particular papers are
/// already being used and accepted the exposure. That is an accepted risk, not
/// a resolved question, and the difference has to survive in the code — a
/// codebase that simply deleted the gate would, six months on, read as though
/// the rights had been cleared.
///
/// So the rule is untouched and the acceptance is a configured seam that is
/// <b>off unless an environment turns it on</b>, names itself in the decision
/// it returns, and leaves the refusal in place everywhere it is not set. That
/// is `G-11`'s shape: the number a business owner would want to change lives
/// where they can change it, and the code still knows the difference between
/// "allowed" and "allowed anyway".
///
/// The day VNI holds a real licence, the proof goes in the registry, the
/// ordinary path permits it, and this seam goes back to false having changed
/// nothing else.
/// </summary>
public sealed class PublicationOverrideTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private sealed class EmptyRegistry : IContentRightsRegistry
    {
        public Task<ContentSource?> FindAsync(ContentSourceId id, CancellationToken ct) =>
            Task.FromResult<ContentSource?>(null);

        public Task<ContentSource?> FindForExamAsync(
            ExamVersionId versionId, ExamDefinitionId definitionId, CancellationToken ct) =>
            Task.FromResult<ContentSource?>(null);

        public Task<IReadOnlyList<ContentSource>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentSource>>([]);

        public Task<bool> RegisterIfAbsentAsync(ContentSource source, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class FixedClock(DateTimeOffset at) : IClock
    {
        public DateTimeOffset UtcNow => at;
    }

    private static ExamVersion Version()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
            AnswerMatchingRules.Default, null, null);
        var timing = new TimingProfile(new Dictionary<ExamModule, int>(), null, []);

        return ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Borrowed paper", ExamVariant.Academic, scoring, timing,
            [
                new Section(ExamModule.Reading, 1,
                [
                    new SectionPart(
                        1, "passage", "Passage 1", "Body.", null, null, null, null, null, null, null,
                        [new Question("r-1", 1, QuestionType.Completion, "Q", [], null, null)]),
                ]),
            ]);
    }

    /// <summary>
    /// <b>Default off.</b> An environment nobody configured must behave exactly
    /// as it did before this seam existed, or the seam is a silent change of
    /// policy rather than a recorded one.
    /// </summary>
    [Fact]
    public async Task Unconfigured_it_still_refuses_material_with_no_recorded_right()
    {
        var guard = new ContentPublishGuard(
            new EmptyRegistry(), new FixedClock(Now), new ContentRightsOptions());

        var decision = await guard.MayPublishToLearnersAsync(Version(), default);

        Assert.False(decision.Allowed);
        Assert.Equal(ContentRightsDenial.NoRegistryEntry, decision.Denial);
    }

    [Fact]
    public async Task With_the_owners_acceptance_it_permits_the_same_material()
    {
        var guard = new ContentPublishGuard(
            new EmptyRegistry(), new FixedClock(Now),
            new ContentRightsOptions { AllowPublicationWithoutProvenRights = true });

        var decision = await guard.MayPublishToLearnersAsync(Version(), default);

        Assert.True(decision.Allowed);
    }

    /// <summary>
    /// <b>"Allowed anyway" is not "allowed", and the decision says which.</b>
    /// The audit row and the operator's screen both read this explanation. If
    /// it claimed a right existed, the record of a launch made on accepted risk
    /// would be indistinguishable from one made on a licence — and the person
    /// who has to answer for it later would be reading a lie.
    /// </summary>
    [Fact]
    public async Task The_permission_says_it_is_an_override_and_names_the_refusal_it_replaced()
    {
        var guard = new ContentPublishGuard(
            new EmptyRegistry(), new FixedClock(Now),
            new ContentRightsOptions { AllowPublicationWithoutProvenRights = true });

        var decision = await guard.MayPublishToLearnersAsync(Version(), default);

        Assert.True(decision.OverriddenByConfiguration);
        Assert.Contains("NoRegistryEntry", decision.Explanation, StringComparison.Ordinal);
        Assert.Contains("M-53", decision.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// A permission the registry really granted is not an override, so the
    /// audit trail can still tell the two apart once VNI holds a licence.
    /// </summary>
    [Fact]
    public void A_genuine_permission_is_not_marked_as_an_override()
    {
        var permitted = ContentRightsDecision.Permit(
            new ContentSourceId("vni-own-1"), ContentEnvironment.LearnerProduction);

        Assert.True(permitted.Allowed);
        Assert.False(permitted.OverriddenByConfiguration);
    }
}
