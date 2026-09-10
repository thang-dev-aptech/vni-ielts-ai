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
    public WritingMarkingOptions WritingMarking { get; set; } = new();
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

    /// <summary>
    /// Optional path to a JSON rubric artifact. When set with
    /// <see cref="AssessmentOptions.WritingMarking"/>, version and
    /// descriptorSource may be taken from the artifact at startup.
    /// </summary>
    public string? ArtifactPath { get; set; }

    /// <summary>
    /// The deployment-wide Task 1 : Task 2 ratio — <c>Assessment:Writing:TaskWeights</c>.
    ///
    /// <b>Read for Writing only.</b> Speaking is one marking and has nothing
    /// to combine, so the same key under <c>Assessment:Speaking</c> is
    /// ignored. An exam version that carries its own ratio wins over this
    /// one; when this is unset and the version carries none, Writing has no
    /// combined band. The value in `appsettings.json` is `P-12` (owner
    /// decision 06/09/2026): 1 : 2. → `G-11`
    /// </summary>
    public WritingTaskWeightOptions? TaskWeights { get; set; }

    /// <summary>
    /// Learner-facing feedback language for Writing. Default <c>vi</c>:
    /// Vietnamese explanation, English criterion acronyms, English evidence.
    /// </summary>
    public string FeedbackLanguage { get; set; } = "vi";

    /// <summary>
    /// <c>whole</c> (v2 default) or <c>half-step</c> (v1). Whole-band
    /// criterion scores are an inference from descriptors existing only at
    /// whole bands, so this is a seam. → `G-11`, `W-Q2` sibling
    /// </summary>
    public string CriterionGranularity { get; set; } = "whole";
}

/// <summary>
/// Both halves of the ratio, or neither.
///
/// A half-stated ratio is refused, in the same way the package format refuses
/// <c>criterionWeights.writing</c> with one side missing — it used to fail at
/// marking time in front of a learner rather than at startup in front of the
/// person who can fix it.
/// </summary>
public sealed class WritingTaskWeightOptions
{
    public decimal? Task1 { get; set; }
    public decimal? Task2 { get; set; }

    /// <summary>Nothing configured: the null implementation, not an error.</summary>
    public bool IsUnset => Task1 is null && Task2 is null;

    /// <summary>
    /// What is wrong with this pair, or null when it is usable or unset.
    /// One validation for the startup gate and the DI factory, so they cannot
    /// disagree about what "positive" means.
    /// </summary>
    public string? Problem()
    {
        if (IsUnset) return null;

        if (Task1 is null || Task2 is null)
            return "Assessment:Writing:TaskWeights declares one half of the ratio. Declare both "
                + "Task1 and Task2, or neither.";

        if (Task1 <= 0m || Task2 <= 0m)
            return $"Assessment:Writing:TaskWeights is {Task1}:{Task2}. Both halves must be "
                + "strictly positive; a zero or negative weight cannot combine two bands.";

        return null;
    }

    /// <summary>
    /// The policy Application runs on: the configured pair, or the
    /// unconfigured policy when nothing is set. Throws on a pair
    /// <see cref="Problem"/> rejects — the startup gate reports it first with
    /// every other fault; this is the backstop for a host that skipped the gate.
    /// </summary>
    public static WritingTaskWeightPolicy ToPolicy(WritingTaskWeightOptions? options)
    {
        if (options is null || options.IsUnset) return WritingTaskWeightPolicy.Unconfigured;

        if (options.Problem() is { } problem) throw new InvalidOperationException(problem);

        return new WritingTaskWeightPolicy(new WritingTaskWeights(options.Task1!.Value, options.Task2!.Value));
    }

    public override string ToString() =>
        IsUnset
            ? "not set"
            : $"{Task1?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}"
                + $":{Task2?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}";
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
    private Rubric? _writingTask1;
    private Rubric? _writingTask2;

    public ConfiguredRubricSource(IOptions<AssessmentOptions> options)
    {
        var assessment = options.Value;
        var artifact = TryLoadArtifact(assessment);
        ApplyArtifactMetadata(assessment, artifact);

        var writingCriteria = artifact is { IsV2: true }
            ? WritingRubricLoader.CriteriaFor(artifact, 2)
            : CriterionKeys.Writing;

        Add(ExamModule.Writing, assessment.Writing, writingCriteria);
        Add(ExamModule.Speaking, assessment.Speaking, CriterionKeys.Speaking);

        if (artifact is { IsV2: true } && _rubrics.TryGetValue(ExamModule.Writing, out var writing))
        {
            _writingTask1 = Rubric.Create(
                writing.Version, ExamModule.Writing,
                WritingRubricLoader.CriteriaFor(artifact, 1), writing.DescriptorSource);
            _writingTask2 = Rubric.Create(
                writing.Version, ExamModule.Writing,
                WritingRubricLoader.CriteriaFor(artifact, 2), writing.DescriptorSource);
        }
    }

    public Rubric? For(ExamModule module) => _rubrics.GetValueOrDefault(module);

    public Rubric? For(ExamModule module, int? taskNumber)
    {
        if (module == ExamModule.Writing && taskNumber == 1 && _writingTask1 is not null)
            return _writingTask1;
        if (module == ExamModule.Writing && taskNumber == 2 && _writingTask2 is not null)
            return _writingTask2;

        return For(module);
    }

    private static WritingRubricArtifact? TryLoadArtifact(AssessmentOptions assessment)
    {
        if (!assessment.WritingMarking.Enabled) return null;

        try
        {
            return WritingRubricLoader.Load(
                assessment.WritingMarking.RubricArtifactPath ?? assessment.Writing.ArtifactPath,
                assessment.WritingMarking.RubricContentHash);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// When a rubric artifact is configured, stamp version and provenance from it
    /// so configuration and prompt content stay aligned.
    /// </summary>
    private static void ApplyArtifactMetadata(AssessmentOptions assessment, WritingRubricArtifact? artifact)
    {
        if (artifact is null) return;

        if (string.IsNullOrWhiteSpace(assessment.Writing.Version))
            assessment.Writing.Version = artifact.Version;

        if (string.IsNullOrWhiteSpace(assessment.Writing.DescriptorSource))
            assessment.Writing.DescriptorSource = artifact.DescriptorSource;

        if (string.IsNullOrWhiteSpace(assessment.WritingMarking.PromptVersion))
            assessment.WritingMarking.PromptVersion = artifact.PromptVersion;
    }

    private void Add(ExamModule module, RubricOptions options, IReadOnlyList<string> criteria)
    {
        if (string.IsNullOrWhiteSpace(options.Version)) return;
        if (string.IsNullOrWhiteSpace(options.DescriptorSource)) return;

        _rubrics[module] = Rubric.Create(
            options.Version, module, criteria, options.DescriptorSource);
    }
}
