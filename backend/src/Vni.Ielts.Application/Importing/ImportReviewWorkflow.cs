using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Importing;

public sealed record ImportReviewActor(
    string ActorId, bool CanEdit, bool CanReview, bool CanPublish);

public sealed record ImportReviewDiff(
    string SourceText, string ParsedPackageJson, string SourceHash, string PackageHash,
    bool TextIsIdentical);

public sealed record ImportReviewResult(
    bool IsSuccess, ExamImportDraft? Draft, string? ErrorCode,
    IReadOnlyList<PackageFinding> Findings)
{
    public static ImportReviewResult Success(ExamImportDraft draft) => new(true, draft, null, []);
    public static ImportReviewResult Refused(string code) => new(false, null, code, []);
    public static ImportReviewResult Invalid(IReadOnlyList<PackageFinding> findings) =>
        new(false, null, "PACKAGE_INVALID", findings);
}

/// <summary>Server-side review state; approval never implies publication authority.</summary>
public sealed class ImportReviewWorkflow(
    IImportDraftStore drafts,
    IExamPackageValidator validator,
    CanonicalExplanationWorkflow? canonicalExplanations = null)
{
    public static ImportReviewDiff Diff(ExamImportDraft draft) => new(
        draft.SourceText, draft.PackageJson, draft.SourceHash, draft.PackageHash,
        string.Equals(draft.SourceText, draft.PackageJson, StringComparison.Ordinal));

    public async Task<ImportReviewResult> EditAsync(
        Guid draftId, int expectedRevision, string packageJson, ImportReviewActor actor,
        CancellationToken ct)
    {
        if (!actor.CanEdit) return ImportReviewResult.Refused("IMPORT_EDIT_FORBIDDEN");
        var draft = await drafts.FindAsync(draftId, ct);
        if (draft is null) return ImportReviewResult.Refused("IMPORT_DRAFT_NOT_FOUND");
        if (draft.Revision != expectedRevision) return ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");
        var validation = validator.Validate(packageJson, draft.DefinitionId, draft.VersionNumber);
        if (!validation.IsValid || validation.Version is null)
            return ImportReviewResult.Invalid(validation.Findings);

        var edited = draft with
        {
            PackageJson = packageJson,
            PackageHash = ExamImportWorkflow.Hash(packageJson),
            Version = validation.Version,
            ApprovalState = ImportApprovalState.ReviewRequired,
            Checklist = ImportReviewChecklist.Empty,
            ReviewedBy = null,
            Revision = draft.Revision + 1,
        };
        return await drafts.ReplaceAsync(edited, expectedRevision, ct)
            ? ImportReviewResult.Success(edited)
            : ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");
    }

    public async Task<ImportReviewResult> ResolveWarningAsync(
        Guid draftId, int expectedRevision, string warningId, ImportReviewActor actor,
        CancellationToken ct)
    {
        if (!actor.CanReview) return ImportReviewResult.Refused("IMPORT_REVIEW_FORBIDDEN");
        var draft = await drafts.FindAsync(draftId, ct);
        if (draft is null) return ImportReviewResult.Refused("IMPORT_DRAFT_NOT_FOUND");
        if (draft.Revision != expectedRevision) return ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");
        if (!draft.Warnings.Any(w => w.Id == warningId))
            return ImportReviewResult.Refused("IMPORT_WARNING_NOT_FOUND");
        var updated = draft with
        {
            Warnings = draft.Warnings.Select(w => w.Id == warningId ? w with { Resolved = true } : w).ToArray(),
            Revision = draft.Revision + 1,
        };
        return await drafts.ReplaceAsync(updated, expectedRevision, ct)
            ? ImportReviewResult.Success(updated)
            : ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");
    }

    public async Task<ImportReviewResult> SetChecklistAsync(
        Guid draftId, int expectedRevision, IReadOnlySet<ImportReviewCategory> confirmed,
        ImportReviewActor actor, CancellationToken ct)
    {
        if (!actor.CanReview) return ImportReviewResult.Refused("IMPORT_REVIEW_FORBIDDEN");
        var draft = await drafts.FindAsync(draftId, ct);
        if (draft is null) return ImportReviewResult.Refused("IMPORT_DRAFT_NOT_FOUND");
        if (draft.Revision != expectedRevision) return ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");
        var updated = draft with
        {
            Checklist = new ImportReviewChecklist(new HashSet<ImportReviewCategory>(confirmed)),
            Revision = draft.Revision + 1,
        };
        return await drafts.ReplaceAsync(updated, expectedRevision, ct)
            ? ImportReviewResult.Success(updated)
            : ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");
    }

    public async Task<ImportReviewResult> ApproveAsync(
        Guid draftId, int expectedRevision, ImportReviewActor actor, CancellationToken ct)
    {
        if (!actor.CanReview) return ImportReviewResult.Refused("IMPORT_REVIEW_FORBIDDEN");
        var draft = await drafts.FindAsync(draftId, ct);
        if (draft is null) return ImportReviewResult.Refused("IMPORT_DRAFT_NOT_FOUND");
        if (draft.Revision != expectedRevision) return ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");
        if (draft.Warnings.Any(w => !w.Resolved))
            return ImportReviewResult.Refused("IMPORT_WARNINGS_UNRESOLVED");
        if (!draft.Checklist.IsComplete)
            return ImportReviewResult.Refused("IMPORT_CHECKLIST_INCOMPLETE");
        var approved = draft with
        {
            ApprovalState = ImportApprovalState.Approved,
            ReviewedBy = actor.ActorId,
            Revision = draft.Revision + 1,
        };
        return await drafts.ReplaceAsync(approved, expectedRevision, ct)
            ? ImportReviewResult.Success(approved)
            : ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");
    }

    public async Task<ImportReviewResult> EnrichCanonicalExplanationsAsync(
        Guid draftId, int expectedRevision, ImportReviewActor actor, CancellationToken ct)
    {
        if (!actor.CanEdit && !actor.CanReview)
            return ImportReviewResult.Refused("IMPORT_REVIEW_FORBIDDEN");
        if (canonicalExplanations is null)
            return ImportReviewResult.Refused("EXPLANATION_GENERATOR_UNAVAILABLE");

        var draft = await drafts.FindAsync(draftId, ct);
        if (draft is null) return ImportReviewResult.Refused("IMPORT_DRAFT_NOT_FOUND");
        if (draft.Revision != expectedRevision) return ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");

        var enriched = await canonicalExplanations.EnrichDraftAsync(draft, ct);
        if (enriched.Generated == 0 && enriched.Cached == 0 && enriched.Refused == 0)
            return ImportReviewResult.Success(draft);

        var validation = validator.Validate(
            enriched.Draft.PackageJson, enriched.Draft.DefinitionId, enriched.Draft.VersionNumber);
        if (!validation.IsValid || validation.Version is null)
            return ImportReviewResult.Invalid(validation.Findings);

        var updated = enriched.Draft with { Version = validation.Version };
        return await drafts.ReplaceAsync(updated, expectedRevision, ct)
            ? ImportReviewResult.Success(updated)
            : ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");
    }

    public static string? PublicationBlocker(ExamImportDraft draft, ImportReviewActor actor)
    {
        if (!actor.CanPublish) return "IMPORT_PUBLISH_FORBIDDEN";
        if (draft.ApprovalState != ImportApprovalState.Approved) return "IMPORT_NOT_APPROVED";
        return null;
    }
}
