using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Exams;

public sealed record ParsedCandidateActor(
    UserId UserId,
    string Email,
    IReadOnlyCollection<string> Permissions);

public sealed record GetParsedExamCandidateQuery(
    ParsedCandidateActor Actor,
    string PackageId,
    string CandidateId);

public sealed class GetParsedExamCandidate(IParsedExamCandidateRepository candidates)
{
    public async Task<Result<ParsedExamCandidate>> HandleAsync(
        GetParsedExamCandidateQuery query,
        CancellationToken ct)
    {
        if (!query.Actor.Permissions.Contains(PermissionKeys.PackageRead))
            return Denied(PermissionKeys.PackageRead);

        var candidate = await candidates.FindAsync(query.PackageId, query.CandidateId, ct);
        return candidate is null ? Missing() : candidate;
    }

    private static Error Missing() => Error.NotFound(
        ErrorCodes.ParsedCandidateNotFound,
        "Parsed exam candidate not found.");

    private static Error Denied(string permission) => Error.Forbidden(
        ErrorCodes.PermissionDenied,
        $"The '{permission}' permission is required.");
}

public sealed record ListParsedExamCandidatesQuery(
    ParsedCandidateActor Actor,
    string PackageId);

public sealed class ListParsedExamCandidates(IParsedExamCandidateRepository candidates)
{
    public async Task<Result<IReadOnlyList<ParsedExamCandidate>>> HandleAsync(
        ListParsedExamCandidatesQuery query,
        CancellationToken ct)
    {
        if (!query.Actor.Permissions.Contains(PermissionKeys.PackageRead))
            return Denied(PermissionKeys.PackageRead);

        var list = await candidates.ListByPackageAsync(query.PackageId, ct);
        return Result<IReadOnlyList<ParsedExamCandidate>>.Ok(list);
    }

    private static Error Denied(string permission) => Error.Forbidden(
        ErrorCodes.PermissionDenied,
        $"The '{permission}' permission is required.");
}

public sealed record CorrectParsedCandidateCommand(
    ParsedCandidateActor Actor,
    string PackageId,
    string CandidateId,
    int ExpectedVersion,
    string? Title = null,
    bool UpdateTitle = false,
    ParsedExamClassification? Classification = null,
    IReadOnlyList<ParsedModuleCandidate>? Modules = null);

public sealed record CorrectParsedCandidateClassificationCommand(
    ParsedCandidateActor Actor,
    string PackageId,
    string CandidateId,
    int ExpectedVersion,
    ParsedExamClassification Classification);

public sealed record CorrectParsedCandidateTitleCommand(
    ParsedCandidateActor Actor,
    string PackageId,
    string CandidateId,
    int ExpectedVersion,
    string? Title);

public sealed record ReplaceParsedCandidateModulesCommand(
    ParsedCandidateActor Actor,
    string PackageId,
    string CandidateId,
    int ExpectedVersion,
    IReadOnlyList<ParsedModuleCandidate> Modules);

public sealed record RejectParsedCandidateCommand(
    ParsedCandidateActor Actor,
    string PackageId,
    string CandidateId,
    int ExpectedVersion);

public sealed record ConfirmParsedCandidateCommand(
    ParsedCandidateActor Actor,
    string PackageId,
    string CandidateId,
    int ExpectedVersion);

public sealed class ReviewParsedExamCandidate(
    IParsedExamCandidateRepository candidates,
    IAuditLog audit,
    IClock clock)
{
    public Task<Result<ParsedExamCandidate>> CorrectAsync(
        CorrectParsedCandidateCommand command,
        CancellationToken ct) => MutateAsync(
            command.Actor,
            command.PackageId,
            command.CandidateId,
            command.ExpectedVersion,
            (candidate, now) => candidate.Correct(
                command.Actor.UserId,
                command.Title,
                command.UpdateTitle,
                command.Classification,
                command.Modules,
                now),
            AuditAction.ParsedCandidateCorrected,
            new Dictionary<string, string> { ["field"] = "composite" },
            ct);

    public Task<Result<ParsedExamCandidate>> CorrectClassificationAsync(
        CorrectParsedCandidateClassificationCommand command,
        CancellationToken ct) => MutateAsync(
            command.Actor,
            command.PackageId,
            command.CandidateId,
            command.ExpectedVersion,
            (candidate, now) => candidate.CorrectClassification(
                command.Actor.UserId, command.Classification, now),
            AuditAction.ParsedCandidateCorrected,
            new Dictionary<string, string> { ["field"] = CandidateCorrectionField.Classification.ToString() },
            ct);

    public Task<Result<ParsedExamCandidate>> CorrectTitleAsync(
        CorrectParsedCandidateTitleCommand command,
        CancellationToken ct) => MutateAsync(
            command.Actor,
            command.PackageId,
            command.CandidateId,
            command.ExpectedVersion,
            (candidate, now) => candidate.CorrectTitle(command.Actor.UserId, command.Title, now),
            AuditAction.ParsedCandidateCorrected,
            new Dictionary<string, string> { ["field"] = CandidateCorrectionField.Title.ToString() },
            ct);

    public Task<Result<ParsedExamCandidate>> ReplaceModulesAsync(
        ReplaceParsedCandidateModulesCommand command,
        CancellationToken ct) => MutateAsync(
            command.Actor,
            command.PackageId,
            command.CandidateId,
            command.ExpectedVersion,
            (candidate, now) => candidate.ReplaceModules(command.Actor.UserId, command.Modules, now),
            AuditAction.ParsedCandidateCorrected,
            new Dictionary<string, string> { ["field"] = CandidateCorrectionField.Module.ToString() },
            ct);

    public Task<Result<ParsedExamCandidate>> RejectAsync(
        RejectParsedCandidateCommand command,
        CancellationToken ct) => MutateAsync(
            command.Actor,
            command.PackageId,
            command.CandidateId,
            command.ExpectedVersion,
            (candidate, now) => candidate.Reject(command.Actor.UserId, now),
            AuditAction.ParsedCandidateRejected,
            null,
            ct);

    public Task<Result<ParsedExamCandidate>> ConfirmAsync(
        ConfirmParsedCandidateCommand command,
        CancellationToken ct) => MutateAsync(
            command.Actor,
            command.PackageId,
            command.CandidateId,
            command.ExpectedVersion,
            (candidate, now) => candidate.Confirm(command.Actor.UserId, now),
            AuditAction.ParsedCandidateConfirmed,
            null,
            ct,
            confirmationIsIdempotent: true);

    private async Task<Result<ParsedExamCandidate>> MutateAsync(
        ParsedCandidateActor actor,
        string packageId,
        string candidateId,
        int expectedVersion,
        Action<ParsedExamCandidate, DateTimeOffset> mutation,
        AuditAction action,
        IReadOnlyDictionary<string, string>? detail,
        CancellationToken ct,
        bool confirmationIsIdempotent = false)
    {
        if (!actor.Permissions.Contains(PermissionKeys.ExamReview))
            return Error.Forbidden(
                ErrorCodes.PermissionDenied,
                $"The '{PermissionKeys.ExamReview}' permission is required.");

        var candidate = await candidates.FindAsync(packageId, candidateId, ct);
        if (candidate is null)
            return Error.NotFound(
                ErrorCodes.ParsedCandidateNotFound,
                "Parsed exam candidate not found.");

        if (confirmationIsIdempotent && candidate.Status == ParsedCandidateStatus.Confirmed)
            return candidate;

        if (candidate.Version != expectedVersion)
            return Error.Conflict(
                ErrorCodes.ParsedCandidateVersionConflict,
                "The parsed candidate changed after it was loaded. Reload it and try again.");

        var now = clock.UtcNow;
        try
        {
            mutation(candidate, now);
        }
        catch (InvalidOperationException exception)
        {
            return Error.Conflict(ErrorCodes.ParsedCandidateInvalidState, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Error.Validation(ErrorCodes.ValidationFailed, exception.Message);
        }

        try
        {
            await candidates.SaveAsync(candidate, expectedVersion, ct);
        }
        catch (ParsedCandidateConcurrencyException)
        {
            return Error.Conflict(
                ErrorCodes.ParsedCandidateVersionConflict,
                "The parsed candidate changed after it was loaded. Reload it and try again.");
        }

        await audit.AppendAsync(AuditEntry.Record(
            actor.UserId,
            actor.Email,
            action,
            "parsed-exam-candidate",
            candidate.Id,
            candidate.Title ?? candidate.Id,
            now,
            detail), ct);

        return candidate;
    }
}
