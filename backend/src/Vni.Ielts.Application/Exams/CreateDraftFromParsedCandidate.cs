using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Exams;

public sealed record CreateDraftFromParsedCandidateCommand(
    ParsedCandidateActor Actor,
    string PackageId,
    string CandidateId,
    CandidateCompletionData Completion);

/// <summary>
/// The sole Application entry from reviewed AI output into exam content. The
/// transactional port owns shared-validator execution and create-once storage;
/// this use case never publishes and never accepts an unresolved candidate.
/// </summary>
public sealed class CreateDraftFromParsedCandidate(
    IParsedExamCandidateRepository candidates,
    IConfirmedCandidateDraftCreator drafts)
{
    public async Task<Result<ExamVersion>> HandleAsync(
        CreateDraftFromParsedCandidateCommand command,
        CancellationToken ct)
    {
        if (!command.Actor.Permissions.Contains(PermissionKeys.ExamCreate))
        {
            return Error.Forbidden(
                ErrorCodes.PermissionDenied,
                $"The '{PermissionKeys.ExamCreate}' permission is required.");
        }

        if (command.Completion is null ||
            command.Completion.TimingProfile is null ||
            command.Completion.ScoringProfile is null)
        {
            return Error.Validation(
                ErrorCodes.CandidateCompletionMissing,
                "Canonical completion data (variant, timing, scoring) is required.");
        }

        var candidate = await candidates.FindAsync(command.PackageId, command.CandidateId, ct);
        if (candidate is null)
        {
            return Error.NotFound(
                ErrorCodes.ParsedCandidateNotFound,
                "Parsed exam candidate not found.");
        }

        if (candidate.Status != ParsedCandidateStatus.Confirmed ||
            candidate.Classification is ParsedExamClassification.Unclassified or ParsedExamClassification.NeedsReview)
        {
            return Error.Conflict(
                ErrorCodes.ParsedCandidateInvalidState,
                "Only a confirmed candidate with a resolved classification can create a Draft.");
        }

        var draft = await drafts.CreateOnceAsync(
            new CandidateDraftCreation(
                candidate,
                command.Completion,
                command.Actor.UserId,
                $"parsed-candidate:{candidate.PackageId}:{candidate.Id}"),
            ct);

        if (draft.Status != ExamVersionStatus.Draft)
            throw new InvalidDataException("Confirmed candidate import must create a Draft exam version.");

        return draft;
    }
}
