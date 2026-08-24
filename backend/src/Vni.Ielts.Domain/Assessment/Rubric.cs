using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Assessment;

/// <summary>
/// The criteria a skill is marked against, and how they combine into a band.
///
/// <b>Data, not code, and versioned.</b> A rubric is the thing an evaluation
/// is reproducible against: `docs/ai/output-contracts.md` records a
/// <c>rubricVersion</c> on every evaluation precisely so a band produced last
/// month can be explained after the wording changes. Hard-coding the criterion
/// set would make that version field a decoration.
///
/// <b>The criterion set for Writing and Speaking is the official IELTS one</b>
/// — four criteria, confirmed by the product owner on 2026-08-21: mark the way
/// IELTS marks, with a stated basis, rather than inventing a scheme. What that
/// statement did <i>not</i> settle is the wording of the descriptors and their
/// licensing, which is why <see cref="DescriptorSource"/> is recorded per
/// version rather than assumed.
/// </summary>
public sealed record Rubric
{
    private Rubric(
        string version,
        ExamModule module,
        IReadOnlyList<string> criteria,
        string descriptorSource)
    {
        Version = version;
        Module = module;
        Criteria = criteria;
        DescriptorSource = descriptorSource;
    }

    /// <summary>Recorded on every evaluation produced under it.</summary>
    public string Version { get; }

    public ExamModule Module { get; }

    /// <summary>
    /// The criterion keys, in reporting order. Order is part of the rubric:
    /// a results screen that lists them differently between two evaluations
    /// invites the reader to think something changed.
    /// </summary>
    public IReadOnlyList<string> Criteria { get; }

    /// <summary>
    /// Where the band descriptors this version used came from.
    ///
    /// <b>Not decoration.</b> The official descriptors are published by IELTS
    /// but carry a joint copyright (British Council · IDP · Cambridge) and no
    /// stated third-party reuse terms. Whether a given deployment may embed
    /// them verbatim is a legal question with more than one possible answer,
    /// and the answer can change. Recording the provenance per version is what
    /// makes it possible to tell, later, which evaluations were produced under
    /// which answer.
    /// </summary>
    public string DescriptorSource { get; }

    public static Rubric Create(
        string version, ExamModule module, IReadOnlyList<string> criteria, string descriptorSource)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("A rubric must be versioned.", nameof(version));

        if (criteria.Count == 0)
            throw new ArgumentException(
                "A rubric with no criteria cannot produce a defensible band.", nameof(criteria));

        if (criteria.Distinct(StringComparer.Ordinal).Count() != criteria.Count)
            throw new ArgumentException("Criterion keys must be distinct.", nameof(criteria));

        if (string.IsNullOrWhiteSpace(descriptorSource))
            throw new ArgumentException(
                "A rubric must record where its descriptors came from.", nameof(descriptorSource));

        return new Rubric(version, module, [.. criteria], descriptorSource);
    }
}

/// <summary>
/// The criterion keys used by the seeded rubrics.
///
/// These mirror the official IELTS criterion names. They are constants so the
/// seeder and the tests stop typoing them — <b>not</b> an enum, because the
/// criterion set travels with a <see cref="Rubric"/> and a rubric is data.
/// </summary>
public static class CriterionKeys
{
    // Writing. Task Response (Task 2) and Task Achievement (Task 1) are the
    // same criterion slot under two official names; the key is neutral so one
    // rubric covers both tasks and the label is a presentation concern.
    public const string TaskResponse = "taskResponse";
    public const string CoherenceAndCohesion = "coherenceAndCohesion";
    public const string LexicalResource = "lexicalResource";
    public const string GrammaticalRangeAndAccuracy = "grammaticalRangeAndAccuracy";

    // Speaking. Shares two keys with Writing by design — the same criterion is
    // assessed, and a results screen comparing them across skills is a feature
    // rather than a coincidence.
    public const string FluencyAndCoherence = "fluencyAndCoherence";
    public const string Pronunciation = "pronunciation";

    public static readonly IReadOnlyList<string> Writing =
    [
        TaskResponse, CoherenceAndCohesion, LexicalResource, GrammaticalRangeAndAccuracy,
    ];

    public static readonly IReadOnlyList<string> Speaking =
    [
        FluencyAndCoherence, LexicalResource, GrammaticalRangeAndAccuracy, Pronunciation,
    ];
}
