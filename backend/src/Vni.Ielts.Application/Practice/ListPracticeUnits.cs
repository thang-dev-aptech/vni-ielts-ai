using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Practice;

public sealed record ListPracticeUnitsQuery(
    ExamModule? Module = null, PracticeScope? Scope = null, ExamVariant? Variant = null);

public sealed record PracticeUnitView(
    string Id,
    string ExamVersionId,
    string Title,
    string Variant,
    string RunKind,
    string Scope,
    string? Module,
    IReadOnlyList<string> PartIds,
    int SlotCount,
    int DurationSeconds,
    bool Available,
    string ScoreCapability);

public sealed record PracticeUnitCatalogueView(IReadOnlyList<PracticeUnitView> Units);

public sealed class ListPracticeUnits(IExamCatalogue catalogue)
{
    public async Task<PracticeUnitCatalogueView> HandleAsync(
        ListPracticeUnitsQuery query, CancellationToken ct)
    {
        var versions = await catalogue.ListSittableAsync(ct);
        var units = versions.SelectMany(PracticeUnitProjection.From)
            .Where(unit => query.Module is null || unit.Module == query.Module)
            .Where(unit => query.Scope is null || unit.Scope == query.Scope)
            .Where(unit => query.Variant is null || unit.Variant == query.Variant)
            .OrderBy(unit => unit.Title, StringComparer.Ordinal)
            .ThenBy(unit => unit.Module)
            .ThenBy(unit => unit.Scope)
            .ThenBy(unit => unit.PartIds.FirstOrDefault(), StringComparer.Ordinal)
            .Select(ToView)
            .ToArray();
        return new PracticeUnitCatalogueView(units);
    }

    private static PracticeUnitView ToView(PracticeUnit unit) => new(
        unit.Id,
        unit.ExamVersionId.Value,
        unit.Title,
        unit.Variant.ToString().ToLowerInvariant(),
        unit.RunKind.ToString().ToLowerInvariant(),
        Wire(unit.Scope),
        unit.Module?.ToString().ToLowerInvariant(),
        unit.PartIds,
        unit.SlotCount,
        unit.DurationSeconds,
        unit.Available,
        Wire(unit.ScoreCapability));

    private static string Wire(PracticeScope scope) => scope switch
    {
        PracticeScope.Part => "part",
        PracticeScope.Skill => "skill",
        PracticeScope.FullTest => "full-test",
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };

    private static string Wire(PracticeScoreCapability capability) => capability switch
    {
        PracticeScoreCapability.Raw => "raw",
        PracticeScoreCapability.EstimatedBand => "estimated-band",
        PracticeScoreCapability.Band => "band",
        _ => throw new ArgumentOutOfRangeException(nameof(capability)),
    };
}
