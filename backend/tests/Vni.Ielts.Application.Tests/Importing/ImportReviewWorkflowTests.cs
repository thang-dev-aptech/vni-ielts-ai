using Vni.Ielts.Application.Importing;
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
        var review = new ImportReviewWorkflow(store, new Validator());

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
        var review = new ImportReviewWorkflow(store, new Validator());

        var result = await review.EditAsync(store.Draft.Id, 0, "invalid", Editor, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("PACKAGE_INVALID", result.ErrorCode);
        Assert.Equal(0, store.Draft.Revision);
    }

    [Fact]
    public async Task Approval_requires_every_check_and_resolved_warning()
    {
        var store = new Store(Draft(warning: true));
        var review = new ImportReviewWorkflow(store, new Validator());

        var blockedWarning = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);
        Assert.Equal("IMPORT_WARNINGS_UNRESOLVED", blockedWarning.ErrorCode);

        var resolved = await review.ResolveWarningAsync(store.Draft.Id, 0, "w1", Reviewer, default);
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
    }

    [Fact]
    public async Task Edit_permission_cannot_approve_and_review_permission_cannot_publish()
    {
        var store = new Store(Draft(complete: true));
        var review = new ImportReviewWorkflow(store, new Validator());

        var approval = await review.ApproveAsync(store.Draft.Id, 0, Editor, default);

        Assert.Equal("IMPORT_REVIEW_FORBIDDEN", approval.ErrorCode);
        Assert.Equal("IMPORT_PUBLISH_FORBIDDEN", ImportReviewWorkflow.PublicationBlocker(store.Draft, Reviewer));
        Assert.Equal("IMPORT_NOT_APPROVED", ImportReviewWorkflow.PublicationBlocker(store.Draft, Publisher));
    }

    [Fact]
    public void Diff_keeps_source_and_parsed_package_side_by_side()
    {
        var diff = ImportReviewWorkflow.Diff(Draft());
        Assert.Equal("raw source", diff.SourceText);
        Assert.Equal("valid", diff.ParsedPackageJson);
        Assert.False(diff.TextIsIdentical);
    }

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
        public ExamImportDraft Draft { get; private set; } = draft;
        public Task SaveAsync(ExamImportDraft value, CancellationToken ct) { Draft = value; return Task.CompletedTask; }
        public Task<ExamImportDraft?> FindAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ExamImportDraft?>(Draft.Id == id ? Draft : null);
        public Task<bool> ReplaceAsync(ExamImportDraft value, int expected, CancellationToken ct)
        {
            if (Draft.Id != value.Id || Draft.Revision != expected) return Task.FromResult(false);
            Draft = value;
            return Task.FromResult(true);
        }
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
            [new Section(ExamModule.Reading, 1, [])]);
    }
}
