using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Exams;

public interface IExamContentParser
{
    Task<ParsedExamCandidate> ParseAsync(
        IReadOnlyList<ExtractedSourceDocument> documents,
        CancellationToken ct);
}

public interface IParsedExamCandidateRepository
{
    Task<ParsedExamCandidate?> FindAsync(string packageId, string candidateId, CancellationToken ct);
    Task<IReadOnlyList<ParsedExamCandidate>> ListByPackageAsync(string packageId, CancellationToken ct);
    /// <summary>Persists with compare-and-swap semantics; stale versions must fail.</summary>
    Task SaveAsync(ParsedExamCandidate candidate, int expectedVersion, CancellationToken ct);
}

public sealed class ParsedCandidateConcurrencyException : InvalidOperationException
{
    public ParsedCandidateConcurrencyException(
        string packageId,
        string candidateId,
        int expectedVersion,
        Exception? innerException = null)
        : base(
            $"Parsed candidate '{candidateId}' in package '{packageId}' is no longer at version {expectedVersion}.",
            innerException)
    {
    }
}

public sealed record CandidateDraftCreation(
    ParsedExamCandidate Candidate,
    CandidateCompletionData Completion,
    UserId CreatedBy,
    string IdempotencyKey);

public interface IConfirmedCandidatePackageBuilder
{
    string BuildCanonicalJson(
        ParsedExamCandidate candidate,
        CandidateCompletionData completion,
        ExamDefinitionId definitionId,
        int versionNumber);
}

/// <summary>
/// Revalidates a confirmed candidate through the shared exam-package validator,
/// creates one Draft, and records the import atomically. Implementations must
/// return the existing Draft when the idempotency key has already succeeded.
/// </summary>
public interface IConfirmedCandidateDraftCreator
{
    Task<ExamVersion> CreateOnceAsync(CandidateDraftCreation request, CancellationToken ct);
}
