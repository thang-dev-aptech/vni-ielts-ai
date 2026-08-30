namespace Vni.Ielts.Domain.Exams;

/// <summary>
/// The order a Full Test advances through its modules.
///
/// <b>One resolver, one canonical fallback.</b> Two ordered constants used to
/// disagree — <see cref="ExamVersion.FullTestOrder"/> and the old
/// <c>SittingBand.FourSkills</c> — and the disagreement was harmless only
/// while nobody read an order from data. Package-driven sequence makes that
/// untrue, so every caller asks here. → `E-12`, `docs/domain/versioned-policy-profiles.md`
/// </summary>
public static class SequenceProfile
{
    /// <summary>
    /// The settled Full Test order when a package declares nothing.
    /// Reading → Listening → Writing → Speaking (`E-12`).
    /// </summary>
    public static readonly IReadOnlyList<ExamModule> CanonicalOrder =
        [ExamModule.Reading, ExamModule.Listening, ExamModule.Writing, ExamModule.Speaking];

    /// <summary>
    /// The four IELTS modules, for set membership only — never for sitting order.
    /// </summary>
    public static readonly IReadOnlySet<ExamModule> FourSkills = new HashSet<ExamModule>(
        CanonicalOrder);

    /// <summary>
    /// Whether every module in <see cref="FourSkills"/> is present on the version.
    /// </summary>
    public static bool IsFullMock(IReadOnlySet<ExamModule> present) =>
        FourSkills.IsSubsetOf(present);

    /// <summary>
    /// Resolves the sitting order for a version's modules.
    ///
    /// When <paramref name="declared"/> is absent, filters
    /// <see cref="CanonicalOrder"/> to modules that are actually present.
    /// When present, returns the declared order filtered to present modules
    /// (validation at import ensures declared matches present exactly).
    /// </summary>
    public static IReadOnlyList<ExamModule> Resolve(
        IReadOnlyList<ExamModule>? declared,
        IReadOnlySet<ExamModule> present)
    {
        if (declared is { Count: > 0 })
            return [.. declared.Where(present.Contains)];

        return [.. CanonicalOrder.Where(present.Contains)];
    }

    /// <summary>Lower-case wire names for API views.</summary>
    public static IReadOnlyList<string> ToWire(IReadOnlyList<ExamModule> sequence) =>
        [.. sequence.Select(m => m.ToString().ToLowerInvariant())];
}
