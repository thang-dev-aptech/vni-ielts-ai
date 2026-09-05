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
public sealed class ContentPublishGuard(IContentRightsRegistry registry, IClock clock)
{
    public async Task<ContentRightsDecision> MayPublishToLearnersAsync(
        ExamVersion version, CancellationToken ct)
    {
        var source = await registry.FindForExamAsync(version.Id, version.DefinitionId, ct);

        // The clock is the server's. Expiry decided from a client-supplied
        // time would be a client-supplied licence. → CLAUDE.md rule 1
        return ContentRightsPolicy.Evaluate(
            source, ContentEnvironment.LearnerProduction, clock.UtcNow);
    }
}
