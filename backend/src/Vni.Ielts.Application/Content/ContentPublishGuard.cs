using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Content;

/// <summary>
/// Answers one question: may this exam version be put in front of learners?
///
/// <b>Import and publish are separate authorities, and this is the second
/// one.</b> Material may be imported, converted, sat by a developer and shown
/// to a reviewer with no publication right whatever — <c>exam/Exam1</c> is
/// exactly that case, and its own README says "do not ship it to a learner".
/// What this refuses is the last step.
///
/// <b>It refuses by default.</b> An exam version the registry knows nothing
/// about is not a gap to be filled in later; it is a paper nobody has checked
/// the rights on, and the safe answer to that is no. → <c>M-53</c>, `G-11`
/// </summary>
/// <summary>
/// Whether this deployment publishes material whose rights nobody has proven.
///
/// <para>
/// <b>Owner decision 2026-09-18, and an accepted risk rather than a resolved
/// question.</b> `M-53` asked who may publish what. The owner answered that
/// the papers already in use go out, and took the exposure. The rule is
/// therefore untouched — a codebase that deleted the gate would, six months
/// on, read as though the rights had been cleared — and the acceptance lives
/// here, where a business owner can change it.
/// </para>
///
/// <para>
/// <b>False unless an environment sets it.</b> A deployment nobody configured
/// behaves exactly as it did before this existed, which is what makes the seam
/// a recorded decision rather than a silent change of policy. → `G-11`
/// </para>
///
/// <para>
/// The day VNI holds a real licence, the proof goes into the registry, the
/// ordinary path permits it, and this goes back to false having changed
/// nothing else.
/// </para>
/// </summary>
public sealed class ContentRightsOptions
{
    public const string SectionName = "ContentRights";

    public bool AllowPublicationWithoutProvenRights { get; set; }
}

public sealed class ContentPublishGuard(
    IContentRightsRegistry registry, IClock clock, ContentRightsOptions options)
{
    public async Task<ContentRightsDecision> MayPublishToLearnersAsync(
        ExamVersion version, CancellationToken ct)
    {
        var source = await registry.FindForExamAsync(version.Id, version.DefinitionId, ct);

        // The clock is the server's. Expiry decided from a client-supplied
        // time would be a client-supplied licence. → CLAUDE.md rule 1
        var decision = ContentRightsPolicy.Evaluate(
            source, ContentEnvironment.LearnerProduction, clock.UtcNow);

        if (decision.Allowed || !options.AllowPublicationWithoutProvenRights) return decision;

        /*
         * <b>The refusal is computed first and then overridden, not skipped.</b>
         * Which rule refused, and why, is the part worth keeping: it is what
         * tells a later reader whether the exposure taken was "no registry
         * entry" or "the licence expired", and those are not the same risk.
         */
        return decision.AllowAnyway(
            "ContentRights:AllowPublicationWithoutProvenRights is set, recording the product "
            + "owner's decision of 2026-09-18 that the papers already in use are published and "
            + "the exposure accepted. This is an accepted risk under M-53, not a licence.");
    }
}
