namespace Vni.Ielts.Application.Exams;

public sealed record CanonicalValidationFinding(
    string Severity,
    string Code,
    string Path,
    string Message);

public sealed class CandidateCanonicalValidationException(
    IReadOnlyList<CanonicalValidationFinding> findings)
    : InvalidOperationException("The canonical exam definition failed schema or invariant validation.")
{
    public IReadOnlyList<CanonicalValidationFinding> Findings { get; } = findings;
}
