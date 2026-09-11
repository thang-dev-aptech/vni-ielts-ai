using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests.Exams;

public sealed class ExamPackageMapperTests
{
    private static readonly UserId Uploader = UserId.New();
    private static readonly DateTimeOffset T0 = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(5);

    private static ExamPackage Uploaded() => ExamPackage.Create(
        "package-1", ExamPackageSourceKind.Zip, Uploader,
        sha256: "deadbeef", fileName: "reading.zip", uploadRef: "upload-ref-1", T0);

    [Fact]
    public void A_freshly_created_package_round_trips_with_no_findings_or_entries()
    {
        var package = Uploaded();

        var restored = package.ToDocument().ToDomain();

        Assert.Equal(package.Id, restored.Id);
        Assert.Equal(ExamPackageSourceKind.Zip, restored.SourceKind);
        Assert.Equal(Uploader, restored.UploadedBy);
        Assert.Equal("deadbeef", restored.Sha256);
        Assert.Equal("reading.zip", restored.FileName);
        Assert.Equal("upload-ref-1", restored.UploadRef);
        Assert.Equal(PackageImportStatus.Uploaded, restored.Status);
        Assert.Empty(restored.Findings);
        Assert.Empty(restored.Entries);
        Assert.Equal(T0, restored.CreatedAt);
        Assert.Equal(T0, restored.UpdatedAt);
    }

    [Fact]
    public void Rejection_findings_round_trip_including_a_null_pointer()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.Reject(
            [
                new PackageFinding("structural", "PATH_ESCAPE", "/entries/3", "Entry escapes the sandbox root."),
                new PackageFinding("structural", "SCHEMA_INVALID", null, "The manifest failed schema validation."),
            ],
            T1);

        var restored = package.ToDocument().ToDomain();

        Assert.Equal(PackageImportStatus.Rejected, restored.Status);
        Assert.Equal(2, restored.Findings.Count);
        Assert.Equal("/entries/3", restored.Findings[0].Pointer);
        Assert.Null(restored.Findings[1].Pointer);
        Assert.Equal(T1, restored.UpdatedAt);
    }

    [Fact]
    public void Extraction_review_findings_round_trip()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.MarkParsing(T0);
        package.MarkNeedsReview(
            T1,
            [new PackageFinding("extraction", "DOCUMENT_EMPTY", "đề/trống.docx", "The document contains no extractable text.")]);

        var restored = package.ToDocument().ToDomain();

        var finding = Assert.Single(restored.Findings);
        Assert.Equal(PackageImportStatus.NeedsReview, restored.Status);
        Assert.Equal("extraction", finding.Stage);
        Assert.Equal("DOCUMENT_EMPTY", finding.Code);
        Assert.Equal("đề/trống.docx", finding.Pointer);
    }

    [Fact]
    public void Ready_to_import_entries_round_trip()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.MarkReadyToImport(
            [
                new ExamPackageEntry("definition-1", "Reading Demo", ExamModule.Reading, 12),
                new ExamPackageEntry("definition-2", "Listening Demo", ExamModule.Listening, 8),
            ],
            T1);

        var restored = package.ToDocument().ToDomain();

        Assert.Equal(PackageImportStatus.ReadyToImport, restored.Status);
        Assert.Equal(2, restored.Entries.Count);
        Assert.Equal(ExamModule.Listening, restored.Entries[1].Module);
        Assert.Equal(8, restored.Entries[1].QuestionCount);
    }

    [Fact]
    public void Version_increments_and_round_trips_across_transitions()
    {
        var package = Uploaded();
        Assert.Equal(0, package.ToDocument().ToDomain().Version);

        package.MarkValidating(T0);
        Assert.Equal(1, package.ToDocument().ToDomain().Version);

        package.MarkReadyToImport([new ExamPackageEntry("definition-1", "Reading Demo", ExamModule.Reading, 12)], T1);
        Assert.Equal(2, package.ToDocument().ToDomain().Version);
    }

    [Fact]
    public void Created_version_ids_round_trip_after_import()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.MarkImported(["exam-version-1", "exam-version-2"], T1);

        var restored = package.ToDocument().ToDomain();

        Assert.Equal(PackageImportStatus.Imported, restored.Status);
        Assert.Equal(["exam-version-1", "exam-version-2"], restored.CreatedVersionIds);
    }

    [Fact]
    public void Upload_purged_flag_round_trips()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.MarkImported(["exam-version-1"], T0);
        package.MarkUploadPurged(T1);

        var restored = package.ToDocument().ToDomain();

        Assert.True(restored.UploadPurged);
        Assert.Equal(T1, restored.UpdatedAt);
    }
}
