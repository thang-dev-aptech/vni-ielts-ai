using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Assessment;

/// <summary>
/// The rubrics in force, supplied by configuration.
///
/// <b>The criterion set is settled; its wording is not.</b> The product owner
/// confirmed on 2026-08-21 that Writing and Speaking are marked the IELTS way —
/// four criteria each, recorded as <c>A-13b</c> — so the criterion keys come
/// from <see cref="CriterionKeys"/> and are not a configuration value anyone
/// should be editing. What remains open is <c>H-8a</c>: the official band
/// descriptors carry a joint copyright (British Council · IDP · Cambridge) with
/// no stated third-party reuse terms, and whether a given deployment may embed
/// them is a legal question that can be answered differently in different
/// places and at different times.
///
/// So <see cref="RubricOptions.DescriptorSource"/> has <b>no default</b>. An
/// unset value means no rubric exists, which means Writing and Speaking report
/// <c>AwaitingRubric</c> rather than being marked against descriptors nobody
/// chose. → `G-11`
/// </summary>
public sealed class AssessmentOptions
{
    public const string SectionName = "Assessment";

    public RubricOptions Writing { get; set; } = new();
    public RubricOptions Speaking { get; set; } = new();
}

/// <summary>One module's rubric, or — when either field is unset — no rubric at all.</summary>
public sealed class RubricOptions
{
    /// <summary>
    /// Stamped on every evaluation produced under this rubric.
    ///
    /// <b>It is what makes a band from last month explicable this month.</b>
    /// Change the descriptors and you change the rubric; if the version does
    /// not change with it, two evaluations produced under different rules
    /// become indistinguishable and the calibration set (`H-8c`) is measuring
    /// a moving target.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Where the band descriptors came from — the answer to `H-8a`, recorded
    /// per version so it stays possible to tell which evaluations were produced
    /// under which answer. No default: see the note on <see cref="AssessmentOptions"/>.
    /// </summary>
    public string? DescriptorSource { get; set; }
}

/// <summary>
/// Builds the two rubrics from configuration, once.
///
/// <b>Null is the answer when nothing is configured, and the caller is built to
/// receive it.</b> An install with no rubric is a working install — Reading and
/// Listening are marked from the answer key and never reach a rubric (`A-11`) —
/// so throwing at startup would turn a fresh clone into a broken one to enforce
/// a policy that only applies to two of four skills.
/// </summary>
public sealed class ConfiguredRubricSource : IRubricSource
{
    private readonly Dictionary<ExamModule, Rubric> _rubrics = [];

    public ConfiguredRubricSource(IOptions<AssessmentOptions> options)
    {
        Add(ExamModule.Writing, options.Value.Writing, CriterionKeys.Writing);
        Add(ExamModule.Speaking, options.Value.Speaking, CriterionKeys.Speaking);
    }

    public Rubric? For(ExamModule module) => _rubrics.GetValueOrDefault(module);

    private void Add(ExamModule module, RubricOptions options, IReadOnlyList<string> criteria)
    {
        if (string.IsNullOrWhiteSpace(options.Version)) return;
        if (string.IsNullOrWhiteSpace(options.DescriptorSource)) return;

        _rubrics[module] = Rubric.Create(
            options.Version, module, criteria, options.DescriptorSource);
    }
}
