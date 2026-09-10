using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

public sealed class ExamPackageT1ContractTests
{
    private static readonly UserId Uploader = UserId.New();
    private static readonly DateTimeOffset T0 = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(5);

    private static ExamPackage CreatePackage() => ExamPackage.Create(
        "pkg-contract-1", ExamPackageSourceKind.Zip, Uploader,
        sha256: "hash123", fileName: "package.zip", uploadRef: "gridfs-ref", T0);

    private static readonly PackageFinding SomeFinding =
        new("validation", "INVALID_LAYOUT", "/root", "No root files found.");

    [Fact]
    public void MarkNeedsReview_from_validating_sets_import_draft_id_and_advances_version()
    {
        var package = CreatePackage();
        package.MarkValidating(T0);
        var initialVersion = package.Version;

        package.MarkNeedsReview("draft-abc-123", T1, [SomeFinding]);

        Assert.Equal(PackageImportStatus.NeedsReview, package.Status);
        Assert.Equal("draft-abc-123", package.ImportDraftId);
        Assert.Single(package.Findings);
        Assert.Equal(T1, package.UpdatedAt);
        Assert.Equal(initialVersion + 1, package.Version);
    }

    [Fact]
    public void MarkNeedsReview_from_uploaded_throws()
    {
        var package = CreatePackage();

        Assert.Throws<InvalidOperationException>(() =>
            package.MarkNeedsReview("draft-abc-123", T1, [SomeFinding]));
    }

    [Fact]
    public void Reject_is_allowed_from_uploaded_state()
    {
        var package = CreatePackage();
        var initialVersion = package.Version;

        package.Reject([SomeFinding], T1);

        Assert.Equal(PackageImportStatus.Rejected, package.Status);
        Assert.Single(package.Findings);
        Assert.Equal(T1, package.UpdatedAt);
        Assert.Equal(initialVersion + 1, package.Version);
    }

    [Fact]
    public void MarkFailed_sanitizes_and_records_failure_code_and_detail()
    {
        var package = CreatePackage();
        package.MarkValidating(T0);
        var initialVersion = package.Version;

        package.MarkFailed("INTERNAL_ERROR", "An unexpected error occurred during processing.", T1);

        Assert.Equal(PackageImportStatus.Failed, package.Status);
        Assert.Equal("INTERNAL_ERROR", package.FailureCode);
        Assert.Equal("An unexpected error occurred during processing.", package.FailureDetail);
        Assert.Equal(T1, package.UpdatedAt);
        Assert.Equal(initialVersion + 1, package.Version);
    }

    [Fact]
    public void MarkFailed_from_terminal_states_throws()
    {
        var package = CreatePackage();
        package.MarkValidating(T0);
        package.Reject([SomeFinding], T0);

        Assert.Throws<InvalidOperationException>(() =>
            package.MarkFailed("ERROR", "Detail", T1));
    }

    [Fact]
    public void MarkImported_preserves_import_draft_id()
    {
        var package = CreatePackage();
        package.MarkValidating(T0);
        package.MarkNeedsReview("draft-789", T0);

        package.MarkImported(["exam-version-1"], T1);

        Assert.Equal(PackageImportStatus.Imported, package.Status);
        Assert.Equal("draft-789", package.ImportDraftId);
        Assert.Contains("exam-version-1", package.CreatedVersionIds);
    }

    [Fact]
    public void Rehydrate_preserves_import_draft_id_failure_code_and_failure_detail()
    {
        var rehydrated = ExamPackage.Rehydrate(
            id: "pkg-1",
            sourceKind: ExamPackageSourceKind.Zip,
            uploadedBy: Uploader,
            sha256: "hash123",
            fileName: "package.zip",
            uploadRef: "ref-1",
            status: PackageImportStatus.Failed,
            findings: [SomeFinding],
            entries: [],
            createdVersionIds: [],
            version: 3,
            uploadPurged: false,
            createdAt: T0,
            updatedAt: T1,
            importDraftId: "draft-xyz",
            failureCode: "PROCESSING_FAILED",
            failureDetail: "Safe message");

        Assert.Equal("draft-xyz", rehydrated.ImportDraftId);
        Assert.Equal("PROCESSING_FAILED", rehydrated.FailureCode);
        Assert.Equal("Safe message", rehydrated.FailureDetail);
    }
}
