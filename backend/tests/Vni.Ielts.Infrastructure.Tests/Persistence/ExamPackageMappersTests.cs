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
        Assert.Equal("ERR_CUSTOM", roundTrip.FailureCode);
        Assert.Equal("Safe failure detail", roundTrip.FailureDetail);
    }
}
