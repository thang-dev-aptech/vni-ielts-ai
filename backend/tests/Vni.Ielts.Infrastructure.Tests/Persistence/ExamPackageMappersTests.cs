using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests.Persistence;

public sealed class ExamPackageMappersTests
{
    private static readonly UserId Uploader = UserId.New();
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Linkage_and_failure_fields_survive_document_round_trip()
    {
        var package = ExamPackage.Rehydrate(
            id: "pkg-mapped-1",
            sourceKind: ExamPackageSourceKind.Zip,
            uploadedBy: Uploader,
            sha256: "sha256-abc",
            fileName: "package.zip",
            uploadRef: "ref-123",
            status: PackageImportStatus.NeedsReview,
            findings: [new PackageFinding("validation", "INVALID_LAYOUT", "/", "Message")],
            entries: [],
            createdVersionIds: ["ver-1"],
            version: 2,
            uploadPurged: false,
            createdAt: Now,
            updatedAt: Now.AddMinutes(5),
            importDraftId: "draft-id-456",
            failureCode: "ERR_CUSTOM",
            failureDetail: "Safe failure detail");

        var doc = package.ToDocument();
        var roundTrip = doc.ToDomain();

        Assert.Equal("draft-id-456", roundTrip.ImportDraftId);
        Assert.Equal(new[] { "draft-id-456" }, roundTrip.ImportDraftIds);
        Assert.Equal("ERR_CUSTOM", roundTrip.FailureCode);
        Assert.Equal("Safe failure detail", roundTrip.FailureDetail);
    }

    [Fact]
    public void Legacy_document_with_only_import_draft_id_string_reads_as_one_element_list()
    {
        var doc = new ExamPackageDocument
        {
            Id = "pkg-legacy-1",
            SourceKind = nameof(ExamPackageSourceKind.Zip),
            UploadedBy = Uploader.Value,
            Sha256 = "sha",
            FileName = "package.zip",
            UploadRef = "ref",
            Status = nameof(PackageImportStatus.NeedsReview),
            Version = 1,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
            ImportDraftId = "legacy-draft-only",
            ImportDraftIds = null,
        };

        var domain = doc.ToDomain();

        Assert.Equal("legacy-draft-only", domain.ImportDraftId);
        Assert.Equal(new[] { "legacy-draft-only" }, domain.ImportDraftIds);
    }

    [Fact]
    public void Document_with_import_draft_ids_list_round_trips_all_ids()
    {
        var package = ExamPackage.Rehydrate(
            id: "pkg-multi-1",
            sourceKind: ExamPackageSourceKind.Zip,
            uploadedBy: Uploader,
            sha256: "sha",
            fileName: "package.zip",
            uploadRef: "ref",
            status: PackageImportStatus.NeedsReview,
            findings: [],
            entries: [],
            createdVersionIds: [],
            version: 1,
            uploadPurged: false,
            createdAt: Now,
            updatedAt: Now,
            importDraftIds: ["draft-a", "draft-b", "draft-c"]);

        var roundTrip = package.ToDocument().ToDomain();

        Assert.Equal("draft-a", roundTrip.ImportDraftId);
        Assert.Equal(new[] { "draft-a", "draft-b", "draft-c" }, roundTrip.ImportDraftIds);
        Assert.Equal(new[] { "draft-a", "draft-b", "draft-c" }, roundTrip.ToDocument().ImportDraftIds);
    }
}
