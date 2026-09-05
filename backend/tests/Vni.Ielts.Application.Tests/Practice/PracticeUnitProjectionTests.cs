using Vni.Ielts.Application.Practice;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Practice;

public sealed class PracticeUnitProjectionTests
{
    [Fact]
    public async Task Catalogue_filters_skill_scope_and_variant_and_returns_contract_fields()
    {
        var version = Version();
        var handler = new ListPracticeUnits(new Catalogue(version));

        var result = await handler.HandleAsync(
            new ListPracticeUnitsQuery(ExamModule.Reading, PracticeScope.Part, ExamVariant.Academic),
            default);

        Assert.Equal(3, result.Units.Count);
        Assert.All(result.Units, unit =>
        {
            Assert.Equal("reading", unit.Module);
            Assert.Equal("part", unit.Scope);
            Assert.Equal("raw", unit.ScoreCapability);
            Assert.True(unit.SlotCount > 0);
            Assert.True(unit.DurationSeconds > 0);
            Assert.True(unit.Available);
        });
    }

    [Fact]
    public async Task Catalogue_never_projects_a_draft()
    {
        var published = Version();
        var draft = Version(publish: false);
        var result = await new ListPracticeUnits(new Catalogue(published, draft))
            .HandleAsync(new(), default);

        Assert.DoesNotContain(result.Units, unit => unit.ExamVersionId == draft.Id.Value);
    }

    [Fact]
    public void Four_skill_version_projects_exact_part_skill_and_mock_units()
    {
        var version = Version();

        var units = PracticeUnitProjection.From(version);

        var reading = units.Where(u => u.Module == ExamModule.Reading).ToArray();
        Assert.Equal(3, reading.Count(u => u.Scope == PracticeScope.Part));
        Assert.Single(reading, u => u.Scope == PracticeScope.Skill);
        Assert.Equal(3, reading.SelectMany(u => u.PartIds).Distinct().Count());

        var listening = units.Where(u => u.Module == ExamModule.Listening).ToArray();
        Assert.Equal(4, listening.Count(u => u.Scope == PracticeScope.Part));
        Assert.Single(listening, u => u.Scope == PracticeScope.Skill);

        var mock = Assert.Single(units, u => u.Scope == PracticeScope.FullTest);
        Assert.Equal(PracticeRunKind.Mock, mock.RunKind);
        Assert.Null(mock.Module);
        Assert.Equal(version.Id, mock.ExamVersionId);
        Assert.True(mock.Available);
    }

    [Fact]
    public void Projection_references_version_and_selection_without_copying_content()
    {
        var unit = PracticeUnitProjection.From(Version()).First(
            u => u.Module == ExamModule.Reading && u.Scope == PracticeScope.Part);

        Assert.StartsWith("reading-part-", Assert.Single(unit.PartIds));
        Assert.DoesNotContain("passage secret", unit.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correct", unit.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void New_version_mints_new_units_without_changing_old_projection()
    {
        var oldVersion = Version(version: 1);
        var before = PracticeUnitProjection.From(oldVersion).Select(u => u.Id).ToArray();
        var newVersion = Version(version: 2);

        var after = PracticeUnitProjection.From(oldVersion).Select(u => u.Id).ToArray();
        var newer = PracticeUnitProjection.From(newVersion).Select(u => u.Id).ToArray();

        Assert.Equal(before, after);
        Assert.Empty(before.Intersect(newer));
    }

    private static ExamVersion Version(int version = 1, bool publish = true)
    {
        var sections = new[]
        {
            Section(ExamModule.Reading, 1, 3), Section(ExamModule.Listening, 2, 4),
            Section(ExamModule.Writing, 3, 2), Section(ExamModule.Speaking, 4, 3),
        };
        var paper = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), version, "Paper", ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>
            {
                [ExamModule.Reading] = 3600, [ExamModule.Listening] = 2400,
                [ExamModule.Writing] = 3600, [ExamModule.Speaking] = 840,
            }, null, []), sections);
        if (publish) paper.Publish(DateTimeOffset.UtcNow);
        return paper;
    }

    private static Section Section(ExamModule module, int order, int parts) => new(
        module, order,
        [.. Enumerable.Range(1, parts).Select(part => new SectionPart(
            part, "part", module == ExamModule.Reading ? "passage secret" : null,
            null, null, null, null, null, part, null, null,
            module is ExamModule.Reading or ExamModule.Listening
                ? [Question(module, part)] : []))]);

    private static Question Question(ExamModule module, int part) => new(
        $"{module}-{part}", part, QuestionType.ShortAnswer, "prompt", [], null,
        new AnswerKey([new AcceptedAnswer("correct", null, null)], null), null, 1);

    private sealed class Catalogue(params ExamVersion[] versions) : IExamCatalogue
    {
        public Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([.. versions.Where(v => v.IsSittable)]);
        public Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>(versions);
        public Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct) =>
            Task.FromResult(versions.FirstOrDefault(v => v.Id == id));
        public Task UpsertAsync(ExamVersion version, CancellationToken ct) => Task.CompletedTask;
        public Task SetStatusAsync(ExamVersionId id, ExamVersionStatus status, CancellationToken ct) => Task.CompletedTask;
    }
}
