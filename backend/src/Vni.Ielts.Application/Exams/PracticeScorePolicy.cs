using Vni.Ielts.Application.Practice;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Exams;

/// <summary>
/// Resolves how a sitting should be scored from its practice-unit selection.
/// </summary>
internal static class PracticeScorePolicy
{
    public static DeterministicScoringContext ScoringContext(ExamSession session, ExamVersion version)
    {
        if (session.PracticeUnitId is null)
            return DeterministicScoringContext.FullSection;

        var unit = PracticeUnitProjection.From(version)
            .SingleOrDefault(candidate => candidate.Id == session.PracticeUnitId);
        if (unit is null)
            return DeterministicScoringContext.FullSection;

        return new DeterministicScoringContext(
            QuestionsInParts(version, session.PartIds).Select(q => q.Id).ToHashSet(StringComparer.Ordinal),
            unit.ScoreCapability == PracticeScoreCapability.Band);
    }

    public static PracticeScoreCapability? ScoreCapability(ExamSession session, ExamVersion version)
    {
        if (session.PracticeUnitId is null) return null;

        return PracticeUnitProjection.From(version)
            .SingleOrDefault(candidate => candidate.Id == session.PracticeUnitId)
            ?.ScoreCapability;
    }

    public static string ScoreLabel(PracticeScoreCapability? capability, BandScore? band)
    {
        if (capability is null) return band is null ? "raw" : "band";

        return capability.Value switch
        {
            PracticeScoreCapability.Raw => "raw",
            PracticeScoreCapability.EstimatedBand => "estimated-band",
            PracticeScoreCapability.Band => "band",
            _ => band is null ? "raw" : "band",
        };
    }

    private static IEnumerable<Question> QuestionsInParts(
        ExamVersion version, IReadOnlyList<string> partIds)
    {
        foreach (var section in version.Sections)
        {
            foreach (var part in section.Parts)
            {
                var partId = $"{section.Module.ToString().ToLowerInvariant()}-part-{part.Order}";
                if (!partIds.Contains(partId)) continue;

                foreach (var question in part.Questions.Where(q => q.Type.IsAutoScored()))
                    yield return question;
            }
        }
    }
}
