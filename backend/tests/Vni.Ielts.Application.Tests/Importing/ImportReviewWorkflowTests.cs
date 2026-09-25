using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
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
        var review = new ImportReviewWorkflow(store, new Validator());

        var result = await review.ResolveWarningAsync(store.Draft.Id, 0, "w1", blankReason!, Reviewer, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_WARNING_REASON_REQUIRED", result.ErrorCode);
        Assert.False(store.Draft.Warnings.Single().Resolved);
    }

    /// <summary>
    /// `P-20` through the import path: `ImportReviewWorkflow.ApproveAsync`
    /// never drives `draft.Version`'s own status machine (import review runs
    /// entirely on `ImportApprovalState`), so it cannot rely on
    /// `ExamVersion.Approve` to enforce reviewer != author for an imported
    /// draft — it has to check `draft.Version.AuthorId` itself. This is the
    /// red-when-removed target for that check.
    /// </summary>
    [Fact]
    public async Task A_reviewer_who_uploaded_the_package_cannot_approve_their_own_draft()
    {
        var uploader = new UserId(Reviewer.ActorId);
        var store = new Store(Draft(complete: true, authorId: uploader));
        var review = new ImportReviewWorkflow(store, new Validator());

        var result = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ReviewerIsAuthor, result.ErrorCode);
        Assert.Equal(ImportApprovalState.ReviewRequired, store.Draft.ApprovalState);
    }

    [Fact]
    public async Task A_different_reviewer_may_approve_an_authored_draft()
    {
        var uploader = UserId.New();
        var store = new Store(Draft(complete: true, authorId: uploader));
        var review = new ImportReviewWorkflow(store, new Validator());

        var result = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ImportApprovalState.Approved, result.Draft!.ApprovalState);
    }

    /// <summary>
    /// The gap this task closes: approving an import draft used to only flip
    /// <c>ImportApprovalState</c> — the draft's already-validated
    /// <c>ExamVersion</c> never reached the catalogue, so it never appeared
    /// on the CMS "Đề thi" screen (which reads <c>IExamCatalogue.ListAllAsync</c>,
    /// a different collection than the import-draft store). Red when the
    /// <c>catalogue.UpsertAsync</c> call is removed from <c>ApproveAsync</c>.
    /// </summary>
    [Fact]
    public async Task Approving_a_draft_writes_its_version_to_the_exam_catalogue()
    {
        var store = new Store(Draft(complete: true));
        var catalogue = new RecordingCatalogue();
        var review = new ImportReviewWorkflow(store, new Validator(), catalogue: catalogue);

        var result = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);

        Assert.True(result.IsSuccess);
        var written = Assert.Single(catalogue.Upserted);
        Assert.Equal(result.Draft!.Version.Id, written.Id);
        Assert.Equal(ExamVersionStatus.Draft, written.Status);
    }

    /// <summary>
    /// A catalogue write failure must not leave the draft looking approved —
    /// same "fail together" reasoning already applied to asset promotion.
    /// </summary>
    [Fact]
    public async Task A_catalogue_write_failure_refuses_approval_and_leaves_the_draft_unapproved()
    {
        var store = new Store(Draft(complete: true));
        var review = new ImportReviewWorkflow(store, new Validator(), catalogue: new ThrowingCatalogue());

        var result = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_CATALOGUE_WRITE_FAILED", result.ErrorCode);
        Assert.Equal(ImportApprovalState.ReviewRequired, store.Draft.ApprovalState);
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
    public async Task A_draft_with_a_blocking_finding_cannot_be_approved()
    {
        var store = new Store(Draft(complete: true, findings:
            [new PackageFinding("error", PaperKeyConsistency.WordLimitCode, "/q/1", "over the limit")]));
        var review = new ImportReviewWorkflow(store, new Validator());

        var result = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_FINDINGS_BLOCKING", result.ErrorCode);
    }

    /// <summary>
    /// Warnings stay clearable — that is `P-19`, and the transcript case depends
    /// on it. Only errors are absolute.
    ///
    /// A generic, made-up code on purpose: this pins <c>ApproveAsync</c>'s own
    /// gate ("a `PackageFinding` only blocks at `Severity == "error"`"), not
    /// any particular check. <see cref="PassageAnchorCheck"/>'s own order
    /// result never reaches <c>draft.Findings</c> at all any more — see
    /// <c>ExamPackageImportPipelineTests.A_draft_carrying_an_out_of_order_result_cannot_be_approved</c>
    /// for that.
    /// </summary>
    [Fact]
    public async Task A_warning_severity_finding_does_not_block_approval()
    {
        var store = new Store(Draft(complete: true, findings:
            [new PackageFinding("warning", "SOME_NON_BLOCKING_FINDING", "/q/2", "worth a look")]));
        var review = new ImportReviewWorkflow(store, new Validator());

        var result = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// There is no override. A reviewer with every permission still cannot clear
    /// a contradiction between a paper and its key — the fix is a corrected file,
    /// not a recorded reason.
    ///
    /// <c>PASSAGE_DOES_NOT_MATCH_QUESTIONS</c>, on purpose: it is one of the
    /// codes that is still filed as an error. <c>KEY_ANSWER_NOT_IN_PASSAGE</c>
    /// stood here until 2026-09-10 and no longer belongs — it is now a
    /// blocking but clearable warning, so using it as the example would have
    /// described a shape the code no longer produces.
    /// </summary>
    [Fact]
    public async Task Resolving_every_warning_does_not_clear_a_blocking_finding()
    {
        var store = new Store(Draft(complete: true, findings:
            [new PackageFinding("error", PassageAnchorCheck.PassageMismatchCode, "/q/1", "absent")]));
        var review = new ImportReviewWorkflow(store, new Validator());

        var result = await review.ApproveAsync(store.Draft.Id, 0, Reviewer, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_FINDINGS_BLOCKING", result.ErrorCode);
    }

    /// <summary>
    /// The whole point of the "mutate every occurrence" contract: a group
    /// repeated across two questions must come out byte-identical in both
    /// places, proving this isn't a "first occurrence only" patch.
    /// </summary>
    [Fact]
    public async Task Setting_positions_writes_them_into_every_occurrence_and_resets_review()
    {
        var store = new Store(PositionDraft());
        var review = new ImportReviewWorkflow(store, new Validator());

        var result = await review.SetGroupPositionsAsync(
            store.Draft.Id, 0, "g-1",
            [new GroupPositionInput("A", 0.25, 0.4), new GroupPositionInput("B", 0.75, 0.6)],
            Editor, default);

        Assert.True(result.IsSuccess);
        var draft = result.Draft!;
        Assert.Equal(1, draft.Revision);
        Assert.Equal(ImportApprovalState.ReviewRequired, draft.ApprovalState);
        Assert.False(draft.Checklist.IsComplete);
        Assert.Null(draft.ReviewedBy);
        Assert.Equal(ExamImportWorkflow.Hash(draft.PackageJson), draft.PackageHash);

        var root = System.Text.Json.Nodes.JsonNode.Parse(draft.PackageJson)!.AsObject();
        var groups = root["sections"]![0]!["parts"]![0]!["questions"]!.AsArray()
            .Select(q => q!["group"]!["positions"]!.ToJsonString())
            .ToArray();
        Assert.Equal(2, groups.Length);
        Assert.Equal(groups[0], groups[1]);
        Assert.Contains("\"key\":\"A\"", groups[0]);
    }

    [Fact]
    public async Task Setting_positions_at_the_wrong_revision_is_a_conflict()
    {
        var store = new Store(PositionDraft());
        var review = new ImportReviewWorkflow(store, new Validator());

        var result = await review.SetGroupPositionsAsync(
            store.Draft.Id, 5, "g-1", [new GroupPositionInput("A", 0.1, 0.1)], Editor, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_REVISION_CONFLICT", result.ErrorCode);
    }

    [Fact]
    public async Task Setting_positions_without_edit_permission_is_forbidden()
    {
        var store = new Store(PositionDraft());
        var review = new ImportReviewWorkflow(store, new Validator());

        var result = await review.SetGroupPositionsAsync(
            store.Draft.Id, 0, "g-1", [new GroupPositionInput("A", 0.1, 0.1)], Reviewer, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_EDIT_FORBIDDEN", result.ErrorCode);
        Assert.Equal(0, store.Draft.Revision);
    }

    [Fact]
    public async Task A_key_outside_the_group_s_own_option_bank_is_refused()
    {
        var store = new Store(PositionDraft());
        var review = new ImportReviewWorkflow(store, new Validator());

        var result = await review.SetGroupPositionsAsync(
            store.Draft.Id, 0, "g-1", [new GroupPositionInput("Z", 0.1, 0.1)], Editor, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_POSITION_UNKNOWN_KEY", result.ErrorCode);
        Assert.Equal(0, store.Draft.Revision);
    }

    [Fact]
    public async Task An_unknown_group_id_is_refused()
    {
        var store = new Store(PositionDraft());
        var review = new ImportReviewWorkflow(store, new Validator());

        var result = await review.SetGroupPositionsAsync(
            store.Draft.Id, 0, "no-such-group", [new GroupPositionInput("A", 0.1, 0.1)], Editor, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("IMPORT_GROUP_NOT_FOUND", result.ErrorCode);
    }

    /// <summary>
    /// A package JSON shaped like a real labelling group repeated across two
    /// questions — the fake <see cref="Validator"/> only special-cases the
    /// literal string "invalid", so any other well-formed JSON here is
    /// accepted, letting these tests exercise
    /// <see cref="ImportReviewWorkflow.SetGroupPositionsAsync"/> against text
    /// <see cref="System.Text.Json.Nodes.JsonNode.Parse(string, System.Text.Json.Nodes.JsonNodeOptions?, System.Text.Json.JsonDocumentOptions)"/>
    /// can actually parse, unlike the bare-word "valid" the other fixture uses.
    /// </summary>
    private static ExamImportDraft PositionDraft()
    {
        const string packageJson = """
            {
              "sections": [
                {
                  "module": "reading",
                  "order": 1,
                  "parts": [
                    {
                      "order": 1,
                      "questions": [
                        {
                          "id": "q-1",
                          "order": 1,
                          "type": "labelling",
                          "options": [{ "key": "A", "text": "Kitchen" }, { "key": "B", "text": "Garden" }],
                          "group": { "id": "g-1", "image": "assets/map.jpg" },
                          "answerKey": { "accepted": ["A"] }
                        },
                        {
                          "id": "q-2",
                          "order": 2,
                          "type": "labelling",
                          "options": [{ "key": "A", "text": "Kitchen" }, { "key": "B", "text": "Garden" }],
                          "group": { "id": "g-1", "image": "assets/map.jpg" },
                          "answerKey": { "accepted": ["B"] }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        var definition = ExamDefinitionId.New();
        var paper = Validator.Paper(definition, 1);
        return new ExamImportDraft(
            Guid.NewGuid(), definition, 1, ExamImportRoute.StructuredPackage,
            new string('a', 64), ExamImportWorkflow.Hash(packageJson), paper, null,
            ImportApprovalState.Approved, [], "raw source", packageJson,
            new ImportReviewChecklist(Enum.GetValues<ImportReviewCategory>().ToHashSet()),
            [], 0, "some-reviewer");
    }

    [Fact]
    public void Diff_keeps_source_and_parsed_package_side_by_side()
    {
        var diff = ImportReviewWorkflow.Diff(Draft());
        Assert.Equal("raw source", diff.SourceText);
        Assert.Equal("valid", diff.ParsedPackageJson);
        Assert.False(diff.TextIsIdentical);
    }

    private static ExamImportDraft Draft(
        bool warning = false, bool complete = false, IReadOnlyList<PackageFinding>? findings = null,
        UserId? authorId = null)
    {
        var definition = ExamDefinitionId.New();
        var paper = Validator.Paper(definition, 1, authorId);
        return new ExamImportDraft(
            Guid.NewGuid(), definition, 1, ExamImportRoute.AiParsedSource,
            new string('a', 64), ExamImportWorkflow.Hash("valid"), paper, null,
            ImportApprovalState.ReviewRequired, findings ?? [], "raw source", "valid",
            complete
                ? new ImportReviewChecklist(Enum.GetValues<ImportReviewCategory>().ToHashSet())
                : ImportReviewChecklist.Empty,
            warning
                ? [new ImportReviewWarning("w1", ImportReviewCategory.Questions, "/q/1", "check", false)]
                : [],
            0, null);
    }

    private sealed class RecordingCatalogue : IExamCatalogue
    {
        public List<ExamVersion> Upserted { get; } = [];

        public Task UpsertAsync(ExamVersion version, CancellationToken ct)
        {
            Upserted.Add(version);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([]);
        public Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>(Upserted);
        public Task<(IReadOnlyList<ExamVersion> Versions, long Total)> ListPagedAsync(
            string? search, ExamVersionStatus? status, int skip, int take, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<ExamVersion>, long)>(([.. Upserted.Skip(skip).Take(take)], Upserted.Count));
        public Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct) =>
            Task.FromResult(Upserted.FirstOrDefault(v => v.Id == id));
        public Task<IReadOnlyDictionary<ExamVersionId, ExamVersion>> FindManyAsync(
            IReadOnlyCollection<ExamVersionId> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<ExamVersionId, ExamVersion>>(
                Upserted.Where(v => ids.Contains(v.Id)).ToDictionary(v => v.Id));
        public Task SetStatusAsync(ExamVersionId id, ExamVersionStatus status, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingCatalogue : IExamCatalogue
    {
        public Task UpsertAsync(ExamVersion version, CancellationToken ct) =>
            throw new InvalidOperationException("catalogue unreachable");

        public Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([]);
        public Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ExamVersion>>([]);
        public Task<(IReadOnlyList<ExamVersion> Versions, long Total)> ListPagedAsync(
            string? search, ExamVersionStatus? status, int skip, int take, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<ExamVersion>, long)>(([], 0));
        public Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct) =>
            Task.FromResult<ExamVersion?>(null);
        public Task<IReadOnlyDictionary<ExamVersionId, ExamVersion>> FindManyAsync(
            IReadOnlyCollection<ExamVersionId> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<ExamVersionId, ExamVersion>>(
                new Dictionary<ExamVersionId, ExamVersion>());
        public Task SetStatusAsync(ExamVersionId id, ExamVersionStatus status, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class Store(ExamImportDraft draft) : IImportDraftStore
    {
        public ExamImportDraft Draft { get; private set; } = draft;
        public Task SaveAsync(ExamImportDraft value, CancellationToken ct) { Draft = value; return Task.CompletedTask; }
        public Task<ExamImportDraft?> FindAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ExamImportDraft?>(Draft.Id == id ? Draft : null);
        public Task<ExamImportDraft?> FindBySourceAsync(
            ExamDefinitionId definitionId, int versionNumber, ExamImportRoute route,
            string sourceHash, string parsePromptVersion, CancellationToken ct) =>
            Task.FromResult<ExamImportDraft?>(
                Draft.DefinitionId == definitionId && Draft.VersionNumber == versionNumber
                && Draft.Route == route && Draft.SourceHash == sourceHash
                && Draft.Parser?.PromptVersion == parsePromptVersion ? Draft : null);
        public Task<bool> ReplaceAsync(ExamImportDraft value, int expected, CancellationToken ct)
        {
            if (Draft.Id != value.Id || Draft.Revision != expected) return Task.FromResult(false);
            Draft = value;
            return Task.FromResult(true);
        }
    }

    private sealed class Validator : IExamPackageValidator
    {
        public PackageValidationResult Validate(string json, ExamDefinitionId id, int version, UserId? authorId = null) =>
            json == "invalid"
                ? new(false, null, [new PackageFinding("error", "INVALID", "/", "invalid")])
                : new(true, Paper(id, version, authorId), []);

        public static ExamVersion Paper(ExamDefinitionId id, int version, UserId? authorId = null) => ExamVersion.CreateDraft(
            id, version, "Paper", ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [new Section(ExamModule.Reading, 1, [])],
            authorId: authorId);
    }
}
