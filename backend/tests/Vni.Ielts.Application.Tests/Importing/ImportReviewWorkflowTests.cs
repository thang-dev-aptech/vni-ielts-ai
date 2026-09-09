using Vni.Ielts.Application.Importing;
using PackageFinding = Vni.Ielts.Application.Importing.PackageFinding;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Importing;

public sealed class ImportReviewWorkflowTests
{
    private static readonly ImportReviewActor Editor = new("editor", true, false, false);
    private static readonly ImportReviewActor Reviewer = new("reviewer", false, true, false);
    private static readonly ImportReviewActor Publisher = new("publisher", false, false, true);

    [Fact]
    public async Task Manual_edit_revalidates_and_resets_review_at_expected_revision()
    {
        var store = new Store(Draft());
        var review = Review(store);

        var result = await review.EditAsync(store.Draft.Id, 0, "valid-edited", Editor, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Draft!.Revision);
        Assert.Equal(ExamImportWorkflow.Hash("valid-edited"), result.Draft.PackageHash);
        Assert.Equal(ImportApprovalState.ReviewRequired, result.Draft.ApprovalState);
        Assert.False(result.Draft.Checklist.IsComplete);
    }

    [Fact]
    public async Task Invalid_manual_edit_is_not_persisted()
    {
        var store = new Store(Draft());
        var review = Review(store);

        var result = await review.EditAsync(store.Draft.Id, 0, "invalid", Editor, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("PACKAGE_INVALID", result.ErrorCode);
        Assert.Equal(0, store.Draft.Revision);
    }

    [Fact]
    public async Task Approval_requires_every_check_and_resolved_warning()
    {
        var store = new Store(Draft(warning: true));
        var review = Review(store);

        var blockedWarning = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);
        Assert.Equal("IMPORT_WARNINGS_UNRESOLVED", blockedWarning.ErrorCode);

        var resolved = await review.ResolveWarningAsync(store.Draft.Id, 0, "w1", "checked against source", Reviewer, default);
        var blockedChecklist = await review.ApproveAsync(store.Draft.Id, resolved.Draft!.Revision, Reviewer, default);
        Assert.Equal("IMPORT_CHECKLIST_INCOMPLETE", blockedChecklist.ErrorCode);

        var checks = Enum.GetValues<ImportReviewCategory>().ToHashSet();
        var checkedDraft = await review.SetChecklistAsync(
            store.Draft.Id, resolved.Draft.Revision, checks, Reviewer, default);
        var approved = await review.ApproveAsync(
            store.Draft.Id, checkedDraft.Draft!.Revision, Reviewer, default);

        Assert.True(approved.IsSuccess);
        Assert.Equal("reviewer", approved.Draft!.ReviewedBy);
        Assert.Equal(ImportApprovalState.Approved, approved.Draft.ApprovalState);
        Assert.Equal("checked against source", approved.Draft.Warnings.Single(w => w.Id == "w1").OverrideReason);
    }

    /// <summary>
    /// `P-19`: "admin bỏ qua được nhưng bắt buộc ghi lý do và vào nhật ký".
    /// Enforced in the workflow itself, not only at the HTTP boundary — this
    /// is the red-when-removed target for that rule at the Application layer.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Resolving_a_warning_without_a_reason_is_refused(string? blankReason)
    {
        var store = new Store(Draft(warning: true));
        var review = Review(store);

        var result = await review.ResolveWarningAsync(store.Draft.Id, 0, "w1", blankReason!, Reviewer, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_WARNING_REASON_REQUIRED", result.ErrorCode);
        Assert.False(store.Draft.Warnings.Single().Resolved);
    }

    [Fact]
    public async Task Edit_permission_cannot_approve_and_review_permission_cannot_publish()
    {
        var store = new Store(Draft(complete: true));
        var review = Review(store);

        var approval = await review.ApproveAsync(store.Draft.Id, 0, Editor, default);

        Assert.Equal("IMPORT_REVIEW_FORBIDDEN", approval.ErrorCode);
        Assert.Equal("IMPORT_PUBLISH_FORBIDDEN", ImportReviewWorkflow.PublicationBlocker(store.Draft, Reviewer));
        Assert.Equal("IMPORT_NOT_APPROVED", ImportReviewWorkflow.PublicationBlocker(store.Draft, Publisher));
    }

    [Fact]
    public async Task Approval_succeeds_without_checklist_when_not_required()
    {
        var store = new Store(Draft() with { ChecklistRequired = false });
        var review = Review(store);

        var approved = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);

        Assert.True(approved.IsSuccess);
        Assert.Equal(ImportApprovalState.Approved, approved.Draft!.ApprovalState);
        Assert.False(approved.Draft.Checklist.IsComplete);
        Assert.False(approved.Draft.ChecklistRequired);
    }

    [Fact]
    public void Diff_keeps_source_and_parsed_package_side_by_side()
    {
        var diff = ImportReviewWorkflow.Diff(Draft());
        Assert.Equal("raw source", diff.SourceText);
        Assert.Equal("valid", diff.ParsedPackageJson);
        Assert.False(diff.TextIsIdentical);
    }

    [Fact]
    public async Task Approval_promotes_a_catalogue_draft_owned_by_the_uploader_not_the_reviewer()
    {
        var uploader = new UserId("uploader-1");
        var store = new Store(Draft(complete: true) with { CreatedBy = uploader });
        var catalogue = new MemoryCatalogue();
        var review = Review(store, catalogue);

        var approved = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);

        Assert.True(approved.IsSuccess);
        Assert.Equal(ImportApprovalState.Approved, approved.Draft!.ApprovalState);
        Assert.Equal("reviewer", approved.Draft.ReviewedBy);
        Assert.Equal(uploader, approved.Draft.Version.AuthorId);
        Assert.NotEqual(uploader.Value, approved.Draft.ReviewedBy);

        var promoted = Assert.Single(catalogue.Versions);
        Assert.Equal(approved.Draft.Version.Id, promoted.Id);
        Assert.Equal(ExamVersionStatus.Draft, promoted.Status);
        Assert.Equal(uploader, promoted.AuthorId);
        Assert.Equal("synthetic-validation", promoted.ContentSourceId?.Value);
        Assert.Equal("Paper", promoted.Title);
    }

    [Fact]
    public async Task Approval_retry_returns_the_same_version_without_a_second_catalogue_row()
    {
        var uploader = new UserId("uploader-1");
        var store = new Store(Draft(complete: true) with { CreatedBy = uploader });
        var catalogue = new MemoryCatalogue();
        var review = Review(store, catalogue);

        var first = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);
        Assert.True(first.IsSuccess);

        var second = await review.ApproveAsync(store.Draft.Id, first.Draft!.Revision, Reviewer, default);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Draft.Version.Id, second.Draft!.Version.Id);
        Assert.Single(catalogue.Versions);
    }

    [Fact]
    public async Task A_stale_revision_does_not_promote_a_catalogue_version()
    {
        var uploader = new UserId("uploader-1");
        var store = new Store(Draft(complete: true) with { CreatedBy = uploader });
        var catalogue = new MemoryCatalogue();
        var review = Review(store, catalogue);

        var bumped = await review.SetChecklistAsync(
            store.Draft.Id, 0, Enum.GetValues<ImportReviewCategory>().ToHashSet(), Reviewer, default);
        Assert.True(bumped.IsSuccess);

        var stale = await review.ApproveAsync(store.Draft.Id, expectedRevision: 0, Reviewer, default);

        Assert.Equal("IMPORT_REVISION_CONFLICT", stale.ErrorCode);
        Assert.Empty(catalogue.Versions);
        Assert.Equal(ImportApprovalState.ReviewRequired, store.Draft.ApprovalState);
    }

    [Fact]
    public async Task Approval_calls_the_committer_once_and_does_not_replace_approved_state_on_the_store()
    {
        var store = new Store(Draft(complete: true));
        var catalogue = new MemoryCatalogue();
        var committer = new SpyCommitter(new MemoryCommitter(store, catalogue));
        var review = new ImportReviewWorkflow(store, new Validator(), committer);

        var approved = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);

        Assert.True(approved.IsSuccess);
        Assert.Equal(1, committer.Calls);
        Assert.Equal(0, store.ApprovedReplaceCalls);
        Assert.Equal(ImportApprovalState.Approved, store.Draft.ApprovalState);
    }

    [Fact]
    public async Task Catalogue_commit_conflict_leaves_the_draft_unapproved()
    {
        var store = new Store(Draft(complete: true));
        var review = new ImportReviewWorkflow(store, new Validator(), new RefusingCommitter());

        var refused = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);

        Assert.Equal("IMPORT_REVISION_CONFLICT", refused.ErrorCode);
        Assert.Equal(ImportApprovalState.ReviewRequired, store.Draft.ApprovalState);
        Assert.Equal(0, store.ApprovedReplaceCalls);
        Assert.Null(store.Draft.ReviewedBy);
    }

    [Fact]
    public async Task Catalogue_commit_failure_leaves_the_draft_unapproved()
    {
        var store = new Store(Draft(complete: true));
        var review = new ImportReviewWorkflow(store, new Validator(), new ThrowingCommitter());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            review.ApproveAsync(store.Draft.Id, 0, Reviewer, default));

        Assert.Equal(ImportApprovalState.ReviewRequired, store.Draft.ApprovalState);
        Assert.Equal(0, store.ApprovedReplaceCalls);
    }

    private static ImportReviewWorkflow Review(Store store, MemoryCatalogue? catalogue = null) =>
        new(store, new Validator(), new MemoryCommitter(store, catalogue ?? new MemoryCatalogue()));

    private static ExamImportDraft Draft(bool warning = false, bool complete = false)
    {
        var definition = ExamDefinitionId.New();
        var paper = Validator.Paper(definition, 1);
        return new ExamImportDraft(
            Guid.NewGuid(), definition, 1, ExamImportRoute.AiParsedSource,
            new string('a', 64), ExamImportWorkflow.Hash("valid"), paper, null,
            ImportApprovalState.ReviewRequired, [], "raw source", "valid",
            complete
                ? new ImportReviewChecklist(Enum.GetValues<ImportReviewCategory>().ToHashSet())
                : ImportReviewChecklist.Empty,
            warning
                ? [new ImportReviewWarning("w1", ImportReviewCategory.Questions, "/q/1", "check", false)]
                : [],
            0, null);
    }

    private sealed class Store(ExamImportDraft draft) : IImportDraftStore
    {
        public ExamImportDraft Draft { get; set; } = draft;
        public int ApprovedReplaceCalls { get; private set; }
        public Task SaveAsync(ExamImportDraft value, CancellationToken ct) { Draft = value; return Task.CompletedTask; }
        public Task<ExamImportDraft?> FindAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ExamImportDraft?>(Draft.Id == id ? Draft : null);
        public Task<bool> ReplaceAsync(ExamImportDraft value, int expected, CancellationToken ct)
        {
            if (Draft.Id != value.Id || Draft.Revision != expected) return Task.FromResult(false);
            if (value.ApprovalState == ImportApprovalState.Approved) ApprovedReplaceCalls++;
            Draft = value;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<ExamImportDraft>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamImportDraft>>([Draft]);
    }

    private sealed class Validator : IExamPackageValidator
    {
        public PackageValidationResult Validate(string json, ExamDefinitionId id, int version) =>
            json == "invalid"
                ? new(false, null, [new PackageFinding("error", "INVALID", "/", "invalid")])
                : new(true, Paper(id, version), []);

        public static ExamVersion Paper(ExamDefinitionId id, int version) => ExamVersion.CreateDraft(
            id, version, "Paper", ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [new Section(ExamModule.Reading, 1, [])],
            contentSourceId: new ContentSourceId("synthetic-validation"));
    }

    private sealed class MemoryCatalogue
    {
        public List<ExamVersion> Versions { get; } = [];
    }

    private sealed class MemoryCommitter(Store store, MemoryCatalogue catalogue) : IImportApprovalCommitter
    {
        public Task<ImportApprovalCommitResult> CommitAsync(
            ExamImportDraft approvedDraft, int expectedRevision, ExamVersion catalogueDraft, CancellationToken ct)
        {
            if (store.Draft.ApprovalState == ImportApprovalState.Approved
                && store.Draft.Version.Id == catalogueDraft.Id)
            {
                return Task.FromResult(ImportApprovalCommitResult.AlreadyCommitted(store.Draft));
            }

            if (store.Draft.Id != approvedDraft.Id || store.Draft.Revision != expectedRevision
                || store.Draft.ApprovalState != ImportApprovalState.ReviewRequired)
            {
                return Task.FromResult(ImportApprovalCommitResult.RevisionConflict());
            }

            if (catalogue.Versions.Any(v => v.Id == catalogueDraft.Id
                && (v.AuthorId != catalogueDraft.AuthorId
                    || v.ContentSourceId != catalogueDraft.ContentSourceId)))
            {
                return Task.FromResult(ImportApprovalCommitResult.IdentityConflict());
            }

            catalogue.Versions.RemoveAll(v => v.Id == catalogueDraft.Id);
            catalogue.Versions.Add(catalogueDraft);
            store.Draft = approvedDraft;
            return Task.FromResult(ImportApprovalCommitResult.Committed(approvedDraft));
        }
    }

    private sealed class SpyCommitter(IImportApprovalCommitter inner) : IImportApprovalCommitter
    {
        public int Calls { get; private set; }

        public async Task<ImportApprovalCommitResult> CommitAsync(
            ExamImportDraft approvedDraft, int expectedRevision, ExamVersion catalogueDraft, CancellationToken ct)
        {
            Calls++;
            return await inner.CommitAsync(approvedDraft, expectedRevision, catalogueDraft, ct);
        }
    }

    private sealed class RefusingCommitter : IImportApprovalCommitter
    {
        public Task<ImportApprovalCommitResult> CommitAsync(
            ExamImportDraft approvedDraft, int expectedRevision, ExamVersion catalogueDraft, CancellationToken ct) =>
            Task.FromResult(ImportApprovalCommitResult.RevisionConflict());
    }

    private sealed class ThrowingCommitter : IImportApprovalCommitter
    {
        public Task<ImportApprovalCommitResult> CommitAsync(
            ExamImportDraft approvedDraft, int expectedRevision, ExamVersion catalogueDraft, CancellationToken ct) =>
            throw new InvalidOperationException("catalogue write failed");
    }
}
