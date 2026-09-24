using System.Text.Json.Nodes;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Importing;

public sealed record ImportReviewActor(
    string ActorId, bool CanEdit, bool CanReview, bool CanPublish);

/// <summary>One hotspot placement, as an admin submits it — a fraction of the group's image content box.</summary>
public sealed record GroupPositionInput(string Key, double X, double Y);

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
    CanonicalExplanationWorkflow? canonicalExplanations = null,
    IImportAssetPromoter? assetPromoter = null)
{
    private readonly IImportAssetPromoter promoter = assetPromoter ?? NoOpImportAssetPromoter.Instance;

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

    /// <summary>
    /// Persists hotspot positions for one shared group. Follows
    /// <see cref="EditAsync"/>'s shape — mutate <see cref="ExamImportDraft.PackageJson"/>
    /// text, re-validate the whole package, keep <c>Version</c> and
    /// <c>PackageJson</c> in lockstep — rather than
    /// <see cref="ResolveWarningAsync"/>'s "patch one in-memory field" shape,
    /// because a group's fields (positions included) are repeated once per
    /// question occurrence in the package text, the same as everywhere else
    /// a group's data lives. Placing or moving a pin is content, so it resets
    /// <c>ApprovalState</c>/<c>Checklist</c> exactly like any other edit.
    /// </summary>
    public async Task<ImportReviewResult> SetGroupPositionsAsync(
        Guid draftId, int expectedRevision, string groupId,
        IReadOnlyList<GroupPositionInput> positions, ImportReviewActor actor, CancellationToken ct)
    {
        if (!actor.CanEdit) return ImportReviewResult.Refused("IMPORT_EDIT_FORBIDDEN");
        var draft = await drafts.FindAsync(draftId, ct);
        if (draft is null) return ImportReviewResult.Refused("IMPORT_DRAFT_NOT_FOUND");
        if (draft.Revision != expectedRevision) return ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");

        var root = JsonNode.Parse(draft.PackageJson)!.AsObject();
        var occurrences = new List<JsonObject>();
        HashSet<string>? bankKeys = null;

        foreach (var sectionNode in root["sections"]!.AsArray())
        foreach (var partNode in sectionNode!["parts"]!.AsArray())
        foreach (var questionNode in (partNode!["questions"]?.AsArray() ?? []))
        {
            if (questionNode!["group"] is not JsonObject group
                || group["id"]?.GetValue<string>() != groupId)
            {
                continue;
            }

            occurrences.Add(group);
            // Every occurrence carries the same bank (CheckGroups already
            // enforces this); the first one found is representative.
            bankKeys ??= [.. (questionNode["options"]?.AsArray() ?? [])
                .Select(o => o!["key"]!.GetValue<string>())];
        }

        if (occurrences.Count == 0) return ImportReviewResult.Refused("IMPORT_GROUP_NOT_FOUND");
        if (positions.Any(p => !bankKeys!.Contains(p.Key)))
            return ImportReviewResult.Refused("IMPORT_POSITION_UNKNOWN_KEY");

        foreach (var group in occurrences)
        {
            group["positions"] = new JsonArray(
                [.. positions.Select(p => (JsonNode)new JsonObject
                {
                    ["key"] = p.Key,
                    ["x"] = p.X,
                    ["y"] = p.Y,
                })]);
        }

        var packageJson = root.ToJsonString();
        var validation = validator.Validate(packageJson, draft.DefinitionId, draft.VersionNumber);
        if (!validation.IsValid || validation.Version is null)
            return ImportReviewResult.Invalid(validation.Findings);

        var updated = draft with
        {
            PackageJson = packageJson,
            PackageHash = ExamImportWorkflow.Hash(packageJson),
            Version = validation.Version,
            ApprovalState = ImportApprovalState.ReviewRequired,
            Checklist = ImportReviewChecklist.Empty,
            ReviewedBy = null,
            Revision = draft.Revision + 1,
        };
        return await drafts.ReplaceAsync(updated, expectedRevision, ct)
            ? ImportReviewResult.Success(updated)
            : ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");
    }

    /// <summary>
    /// Resolves a warning — either because the underlying condition was
    /// genuinely fixed, or because a reviewer is overriding it.
    /// <c>reason</c> is mandatory either way: `P-19`'s
    /// "bắt buộc ghi lý do" is enforced here, in the workflow, rather than
    /// only at the HTTP boundary — an endpoint is not the only caller this
    /// type will ever have, and a rule that lives only in a controller is a
    /// rule the next caller can forget.
    /// </summary>
    public async Task<ImportReviewResult> ResolveWarningAsync(
        Guid draftId, int expectedRevision, string warningId, string reason, ImportReviewActor actor,
        CancellationToken ct)
    {
        if (!actor.CanReview) return ImportReviewResult.Refused("IMPORT_REVIEW_FORBIDDEN");
        if (string.IsNullOrWhiteSpace(reason)) return ImportReviewResult.Refused("IMPORT_WARNING_REASON_REQUIRED");
        var draft = await drafts.FindAsync(draftId, ct);
        if (draft is null) return ImportReviewResult.Refused("IMPORT_DRAFT_NOT_FOUND");
        if (draft.Revision != expectedRevision) return ImportReviewResult.Refused("IMPORT_REVISION_CONFLICT");
        if (!draft.Warnings.Any(w => w.Id == warningId))
            return ImportReviewResult.Refused("IMPORT_WARNING_NOT_FOUND");
        var updated = draft with
        {
            Warnings = draft.Warnings
                .Select(w => w.Id == warningId ? w with { Resolved = true, OverrideReason = reason } : w)
                .ToArray(),
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
        /*
         * P-20's rule, mirrored from ExamVersion.Approve(): a reviewer may not
         * sign off content they authored. This workflow never drives
         * draft.Version's own status machine (import review runs on
         * ImportApprovalState, a separate field), so the check is repeated
         * here rather than reached by calling ExamVersion.Approve() itself.
         * A no-op when AuthorId is null, for the same reason the domain
         * method is: batch/legacy imports that captured no actor must stay
         * approvable, since there is no author to conflict with. → ADR-0017
         */
        if (draft.Version.AuthorId is { } author && author == new UserId(actor.ActorId))
            return ImportReviewResult.Refused(ErrorCodes.ReviewerIsAuthor);
        /*
         * A blocking finding has no override, and that is the difference from a
         * warning. `P-19`'s warnings are judgements a reviewer may make with a
         * recorded reason. An error here is two documents in the same package
         * contradicting each other — a key answer the passage does not contain, an
         * answer over the paper's own word limit — and no amount of authority makes
         * those consistent. The fix is a corrected file. → IP-03
         */
        if (draft.Findings.Any(f => f.Severity == "error"))
            return ImportReviewResult.Refused("IMPORT_FINDINGS_BLOCKING");
        if (draft.Warnings.Any(w => !w.Resolved))
            return ImportReviewResult.Refused("IMPORT_WARNINGS_UNRESOLVED");
        if (!draft.Checklist.IsComplete)
            return ImportReviewResult.Refused("IMPORT_CHECKLIST_INCOMPLETE");

        /*
         * Promote before flipping ApprovalState. A failed copy must not leave
         * an Approved draft whose learner-facing media is still only in private
         * staging — and a successful copy that then loses the revision race is
         * far rarer than approving first and discovering the copy failed.
         */
        try
        {
            await PromoteDraftAssetsAsync(draft, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return ImportReviewResult.Refused("IMPORT_ASSET_PROMOTE_FAILED");
        }

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

    /// <summary>
    /// Every <c>AudioKey</c>, <c>ImageKey</c> and group <c>Image</c> on the
    /// draft's validated Version, promoted from
    /// <c>imports/{draftId}/{reference}</c> to the plain <c>assets/…</c>
    /// reference <c>IExamAssetStore</c> opens.
    /// </summary>
    private async Task PromoteDraftAssetsAsync(ExamImportDraft draft, CancellationToken ct)
    {
        foreach (var reference in AssetReferences(draft.Version))
        {
            ct.ThrowIfCancellationRequested();
            var staged = StagedKeyFor(draft.Id, reference);
            await promoter.PromoteAsync(staged, reference, ct);
        }
    }

    /// <summary>Same key shape the admin draft-asset preview composes.</summary>
    public static string StagedKeyFor(Guid draftId, string publicReference)
    {
        var normalized = publicReference.Replace('\\', '/').Trim('/');
        return $"imports/{draftId:D}/{normalized}";
    }

    private static IEnumerable<string> AssetReferences(ExamVersion version) =>
        version.Sections
            .SelectMany(s => s.Parts)
            .SelectMany(p => new[] { p.AudioKey, p.ImageKey }
                .Concat(p.Questions.Select(q => q.Group?.Image)))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal);

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
