namespace Vni.Ielts.Domain.Exams;

/// <summary>
/// An IELTS band score. A value type that cannot hold an invalid value.
///
/// Modelled as a value object rather than a bare <c>decimal</c> so an
/// out-of-scale band can never reach persistence: the only way to obtain one
/// is through a constructor that rejects everything outside the reportable
/// set. That matters most where AI output is concerned — a model returning
/// <c>47</c> must fail loudly, and a type that accepts any decimal turns that
/// into a silent write.
/// </summary>
public readonly record struct BandScore : IComparable<BandScore>
{
    /// <summary>
    /// Bands are reported in whole and half bands only. Not a range — 6.3 is
    /// not a band score, and neither is 7.77.
    /// </summary>
    public static readonly IReadOnlyList<decimal> Valid =
    [
        0m, 0.5m, 1m, 1.5m, 2m, 2.5m, 3m, 3.5m, 4m, 4.5m,
        5m, 5.5m, 6m, 6.5m, 7m, 7.5m, 8m, 8.5m, 9m,
    ];

    private BandScore(decimal value) => Value = value;

    public decimal Value { get; }

    public static bool IsValid(decimal value) => Valid.Contains(value);

    public static bool TryCreate(decimal value, out BandScore band)
    {
        band = default;
        if (!IsValid(value)) return false;
        band = new BandScore(value);
        return true;
    }

    /// <summary>
    /// Throws on an invalid value. <b>Never clamp instead.</b> An out-of-scale
    /// band means something went wrong — a broken prompt, a provider change,
    /// or a successful injection. Clamping 47 to 9 converts a visible fault
    /// into a plausible-looking wrong score, and nobody investigates a
    /// plausible score.
    /// </summary>
    public static BandScore Create(decimal value) =>
        TryCreate(value, out var band)
            ? band
            : throw new ArgumentOutOfRangeException(
                nameof(value), value,
                "Not a reportable IELTS band. Reject the value; never clamp it into range.");

    /// <summary>
    /// The overall band from the section bands.
    ///
    /// <b>This rule is asymmetric and a generic rounding helper gets it
    /// wrong.</b> The official rule: the mean of the section bands, rounded to
    /// the nearest half band — but a mean ending in <c>.25</c> rounds UP to the
    /// next half band, and one ending in <c>.75</c> rounds UP to the next whole
    /// band.
    ///
    /// Round-half-to-even on a 0.5 grid turns 6.75 into 6.5. The correct answer
    /// is 7.0. That single row is why this has its own function and its own
    /// table-driven test rather than a call to <c>Math.Round</c>.
    ///
    /// It is also the one scoring rule stable enough to live in code at all —
    /// the raw-to-band conversion tables are versioned configuration, because
    /// their boundaries are equated per test version.
    /// </summary>
    public static BandScore Overall(IReadOnlyCollection<BandScore> sectionBands)
    {
        if (sectionBands.Count == 0)
            throw new ArgumentException("An overall band needs at least one section band.", nameof(sectionBands));

        return RoundToHalfBand(sectionBands.Sum(b => b.Value) / sectionBands.Count);
    }

    /// <summary>
    /// A weighted mean of bands, rounded on the same asymmetric rule.
    ///
    /// <b>For Writing, whose two tasks do not count equally.</b> This function
    /// deliberately takes the weights rather than knowing them: the Task 1 :
    /// Task 2 ratio is not published by IELTS and is not defaulted anywhere in
    /// this codebase (`H-8b`). A caller that cannot supply weights has no
    /// business producing a combined band, which is why there is no overload
    /// that assumes any.
    ///
    /// The rounding is <see cref="Overall"/>'s, shared rather than copied — two
    /// implementations of an asymmetric rule is one implementation that will be
    /// wrong about 6.75.
    /// </summary>
    public static BandScore Weighted(IReadOnlyCollection<(BandScore Band, decimal Weight)> parts)
    {
        if (parts.Count == 0)
            throw new ArgumentException("A weighted band needs at least one part.", nameof(parts));

        var total = parts.Sum(p => p.Weight);

        if (total <= 0m)
            throw new ArgumentException(
                "Weights must sum to something positive. Refusing to divide by a weighting that "
                + "cancels itself out.", nameof(parts));

        return RoundToHalfBand(parts.Sum(p => p.Band.Value * p.Weight) / total);
    }

    /// <summary>
    /// The official rounding, in one place.
    ///
    /// Work on the half-band grid: the fractional part of <c>mean * 2</c> is
    /// 0.5 exactly when the mean ends in <c>.25</c> or <c>.75</c> — the two
    /// cases the official rule singles out, and both round up.
    /// </summary>
    private static BandScore RoundToHalfBand(decimal mean)
    {
        var onHalfGrid = mean * 2m;
        var floor = Math.Floor(onHalfGrid);
        var remainder = onHalfGrid - floor;

        var steps = remainder >= 0.5m ? floor + 1m : floor;

        return Create(Math.Clamp(steps / 2m, 0m, 9m));
    }

    public int CompareTo(BandScore other) => Value.CompareTo(other.Value);

    /// <summary>Always one decimal. "7.0", never "7".</summary>
    public override string ToString() =>
        Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    public static bool operator <(BandScore a, BandScore b) => a.Value < b.Value;
    public static bool operator >(BandScore a, BandScore b) => a.Value > b.Value;
    public static bool operator <=(BandScore a, BandScore b) => a.Value <= b.Value;
    public static bool operator >=(BandScore a, BandScore b) => a.Value >= b.Value;
}
