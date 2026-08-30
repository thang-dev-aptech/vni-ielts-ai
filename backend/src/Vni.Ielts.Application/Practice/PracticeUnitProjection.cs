using System.Security.Cryptography;
using System.Text;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Practice;

public enum PracticeRunKind { Practice, Mock }
public enum PracticeScope { Part, Skill, FullTest }
public enum PracticeScoreCapability { Raw, EstimatedBand, Band }

/// <summary>
/// A catalogue projection over immutable content. It names an exam version and selection only;
/// passages, media, questions and keys remain owned by that version.
/// </summary>
public sealed record PracticeUnit(
    string Id,
    ExamVersionId ExamVersionId,
    string Title,
    ExamVariant Variant,
    PracticeRunKind RunKind,
    PracticeScope Scope,
    ExamModule? Module,
    IReadOnlyList<string> PartIds,
    int SlotCount,
    int DurationSeconds,
    bool Available,
    PracticeScoreCapability ScoreCapability);

public static class PracticeUnitProjection
{
    public static IReadOnlyList<PracticeUnit> From(ExamVersion version)
    {
        var units = new List<PracticeUnit>();
        foreach (var section in version.Sections.OrderBy(section => section.Order))
        {
            var partIds = section.Parts.OrderBy(part => part.Order)
                .Select(part => PartId(section.Module, part.Order)).ToArray();
            foreach (var part in section.Parts.OrderBy(part => part.Order))
            {
                units.Add(Create(
                    version, PracticeRunKind.Practice, PracticeScope.Part, section.Module,
                    [PartId(section.Module, part.Order)],
                    part.Questions.Where(q => q.Type.IsAutoScored()).Sum(q => q.Marks),
                    part.Timing?.DurationSeconds
                        ?? DivideDuration(version, section.Module, section.Parts.Count),
                    PracticeScoreCapability.Raw));
            }

            units.Add(Create(
                version, PracticeRunKind.Practice, PracticeScope.Skill, section.Module,
                partIds, section.AutoScoredMarks,
                (int)version.Timing.DurationFor(section.Module).TotalSeconds,
                section.Module is ExamModule.Reading or ExamModule.Listening
                    ? PracticeScoreCapability.Band
                    : PracticeScoreCapability.EstimatedBand));
        }

        var present = version.Sections.Select(section => section.Module).ToHashSet();
        if (SequenceProfile.IsFullMock(present))
        {
            units.Add(Create(
                version, PracticeRunKind.Mock, PracticeScope.FullTest, null,
                [.. version.ModuleSequence
                    .SelectMany(module => version.Section(module)!.Parts.OrderBy(p => p.Order)
                        .Select(p => PartId(module, p.Order)))],
                version.Sections.Sum(s => s.AutoScoredMarks),
                version.ModuleSequence.Sum(m => (int)version.Timing.DurationFor(m).TotalSeconds),
                PracticeScoreCapability.Band));
        }

        return units;
    }

    private static PracticeUnit Create(
        ExamVersion version, PracticeRunKind runKind, PracticeScope scope, ExamModule? module,
        IReadOnlyList<string> partIds, int slots, int duration, PracticeScoreCapability capability)
    {
        var selection = $"{runKind}:{scope}:{module?.ToString() ?? "all"}:{string.Join(',', partIds)}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{version.Id.Value}:{selection}")))[..20];
        return new PracticeUnit(
            $"pu-{hash}", version.Id, version.Title, version.Variant, runKind, scope, module,
            partIds, slots, duration, version.IsSittable, capability);
    }

    private static string PartId(ExamModule module, int order) =>
        $"{module.ToString().ToLowerInvariant()}-part-{order}";

    private static int DivideDuration(ExamVersion version, ExamModule module, int parts) =>
        parts == 0 ? 0 : (int)version.Timing.DurationFor(module).TotalSeconds / parts;
}
