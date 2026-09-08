using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

/// <summary>
/// `S1a` / `P-11` — the scoring profile carries no opinion about its own
/// table's provenance unless something said one.
///
/// <b>Absent is a first-class, common state, not an oversight.</b> `H-4` is
/// open, so every <see cref="ScoringProfile"/> built before this field
/// existed — and every package that still omits <c>bandTableProvenance</c> —
/// must read as not-equated rather than as silently trustworthy. A default
/// of <c>Synthetic</c> or <c>Provisional</c> would have been just as wrong as
/// a default of <c>Equated</c>: either one answers a question nobody decided.
/// </summary>
public sealed class BandTableProvenanceTests
{
    private static ScoringProfile Profile(BandTableProvenance? provenance = null) => new(
        new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(),
        AnswerMatchingRules.Default,
        Provenance: provenance);

    [Fact]
    public void A_scoring_profile_with_no_provenance_argument_carries_none()
    {
        var profile = Profile();

        Assert.Null(profile.Provenance);
    }

    [Theory]
    [InlineData(BandTableProvenanceStatus.Synthetic)]
    [InlineData(BandTableProvenanceStatus.Provisional)]
    [InlineData(BandTableProvenanceStatus.Equated)]
    public void Every_declared_status_round_trips_through_the_record(BandTableProvenanceStatus status)
    {
        var provenance = new BandTableProvenance(status, Source: "VNI academic team, 2026-09");
        var profile = Profile(provenance);

        Assert.Equal(status, profile.Provenance!.Status);
        Assert.Equal("VNI academic team, 2026-09", profile.Provenance.Source);
    }

    [Fact]
    public void Source_and_note_are_optional_on_a_non_equated_status()
    {
        // The package schema only requires `source` when `status` is
        // `equated` — a synthetic or provisional table is free to carry
        // neither, and the domain record must not invent a requirement the
        // schema does not have.
        var provenance = new BandTableProvenance(BandTableProvenanceStatus.Synthetic);

        Assert.Null(provenance.Source);
        Assert.Null(provenance.Note);
    }
}
