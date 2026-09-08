using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

/// <summary>
/// The upload-pipeline lifecycle for deterministic content (JSON/ZIP) — the
/// Phase 2 counterpart to `ExamVersionLifecycleTests`. Every transition is
/// tested both for what it does and for the state it refuses to run from.
/// → `docs/development/phase-2-content-import-plan.md` Plan 01
/// </summary>
public sealed class ExamPackageTests
{
    private static readonly UserId Uploader = UserId.New();
    private static readonly DateTimeOffset T0 = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(5);

    private static ExamPackage Uploaded() => ExamPackage.Create(
        "package-1", ExamPackageSourceKind.Json, Uploader,
        sha256: "abc123", fileName: "reading-demo.json", uploadRef: "upload-ref-1", T0);

    private static readonly PackageFinding SomeFinding =
        new("structural", "ASSET_UNDECLARED", "/sections/0", "An asset is referenced but not declared.");

    private static readonly ExamPackageEntry SomeEntry =
        new("definition-1", "Reading Demo", ExamModule.Reading, 10);

    [Fact]
    public void Create_starts_uploaded_with_no_findings_or_entries()
    {
        var package = Uploaded();

        Assert.Equal(PackageImportStatus.Uploaded, package.Status);
        Assert.Empty(package.Findings);
        Assert.Empty(package.Entries);
        Assert.Equal(T0, package.CreatedAt);
        Assert.Equal(T0, package.UpdatedAt);
    }

    // ── MarkScanning ─────────────────────────────────────────────────────

    [Fact]
    public void Marking_scanning_from_uploaded_moves_it_to_scanning()
    {
        var package = Uploaded();
        package.MarkScanning(T1);

        Assert.Equal(PackageImportStatus.Scanning, package.Status);
        Assert.Equal(T1, package.UpdatedAt);
    }

    [Fact]
    public void Marking_scanning_from_validating_throws()
    {
        var package = Uploaded();
        package.MarkValidating(T1);

        Assert.Throws<InvalidOperationException>(() => package.MarkScanning(T1));
    }

    // ── MarkValidating ───────────────────────────────────────────────────

    [Fact]
    public void Marking_validating_from_uploaded_skips_scanning()
    {
        var package = Uploaded();
        package.MarkValidating(T1);

        Assert.Equal(PackageImportStatus.Validating, package.Status);
    }

    [Fact]
    public void Marking_validating_from_scanning_moves_it_to_validating()
    {
        var package = Uploaded();
        package.MarkScanning(T0);
        package.MarkValidating(T1);

        Assert.Equal(PackageImportStatus.Validating, package.Status);
    }

    [Fact]
    public void Marking_validating_from_rejected_throws()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.Reject([SomeFinding], T0);

        Assert.Throws<InvalidOperationException>(() => package.MarkValidating(T1));
    }

    // ── Reject ───────────────────────────────────────────────────────────

    [Fact]
    public void Rejecting_from_validating_records_findings()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.Reject([SomeFinding], T1);

        Assert.Equal(PackageImportStatus.Rejected, package.Status);
        Assert.Single(package.Findings);
        Assert.Equal(SomeFinding, package.Findings[0]);
    }

    [Fact]
    public void Rejecting_from_scanning_records_findings()
    {
        var package = Uploaded();
        package.MarkScanning(T0);
        package.Reject([SomeFinding], T1);

        Assert.Equal(PackageImportStatus.Rejected, package.Status);
    }

    [Fact]
    public void Rejecting_from_uploaded_throws()
    {
        var package = Uploaded();

        Assert.Throws<InvalidOperationException>(() => package.Reject([SomeFinding], T1));
    }

    [Fact]
    public void Rejecting_with_no_findings_throws()
    {
        var package = Uploaded();
        package.MarkValidating(T0);

        Assert.Throws<ArgumentException>(() => package.Reject([], T1));
    }

    [Fact]
    public void Raw_package_moves_from_validating_through_parsing_to_needs_review()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.MarkParsing(T1);
        package.MarkNeedsReview(T1.AddMinutes(1));

        Assert.Equal(PackageImportStatus.NeedsReview, package.Status);
        Assert.Equal(3, package.Version);
    }

    [Fact]
    public void Needs_review_requires_parsing()
    {
        var package = Uploaded();
        package.MarkValidating(T0);

        Assert.Throws<InvalidOperationException>(() => package.MarkNeedsReview(T1));
    }

    // ── MarkReadyToImport ────────────────────────────────────────────────

    [Fact]
    public void Marking_ready_to_import_from_validating_records_entries()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.MarkReadyToImport([SomeEntry], T1);

        Assert.Equal(PackageImportStatus.ReadyToImport, package.Status);
        Assert.Single(package.Entries);
    }

    [Fact]
    public void Marking_ready_to_import_from_uploaded_throws()
    {
        var package = Uploaded();

        Assert.Throws<InvalidOperationException>(() => package.MarkReadyToImport([SomeEntry], T1));
    }

    [Fact]
    public void Marking_ready_to_import_with_no_entries_throws()
    {
        var package = Uploaded();
        package.MarkValidating(T0);

        Assert.Throws<ArgumentException>(() => package.MarkReadyToImport([], T1));
    }

    // ── MarkImported ─────────────────────────────────────────────────────

    [Fact]
    public void A_single_source_package_is_imported_directly_from_validating()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.MarkImported(["definition-1"], T1);

        Assert.Equal(PackageImportStatus.Imported, package.Status);
    }

    [Fact]
    public void A_confirmed_multi_exam_package_is_imported_from_ready_to_import()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.MarkReadyToImport([SomeEntry], T0);
        package.MarkImported(["definition-1"], T1);

        Assert.Equal(PackageImportStatus.Imported, package.Status);
    }

    [Fact]
    public void Marking_imported_from_uploaded_throws()
    {
        var package = Uploaded();

        Assert.Throws<InvalidOperationException>(() => package.MarkImported(["definition-1"], T1));
    }

    [Fact]
    public void Marking_imported_twice_throws()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.MarkImported(["definition-1"], T0);

        Assert.Throws<InvalidOperationException>(() => package.MarkImported(["definition-1"], T1));
    }

    // ── MarkFailed ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(PackageImportStatus.Uploaded)]
    [InlineData(PackageImportStatus.Scanning)]
    [InlineData(PackageImportStatus.Validating)]
    [InlineData(PackageImportStatus.ReadyToImport)]
    public void Marking_failed_is_allowed_from_any_non_terminal_state(PackageImportStatus from)
    {
        var package = Uploaded();
        if (from is PackageImportStatus.Scanning or PackageImportStatus.Validating or PackageImportStatus.ReadyToImport)
            package.MarkScanning(T0);
        if (from is PackageImportStatus.Validating or PackageImportStatus.ReadyToImport)
            package.MarkValidating(T0);
        if (from is PackageImportStatus.ReadyToImport)
            package.MarkReadyToImport([SomeEntry], T0);

        package.MarkFailed(T1);

        Assert.Equal(PackageImportStatus.Failed, package.Status);
    }

    [Fact]
    public void Marking_failed_from_imported_throws()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.MarkImported(["definition-1"], T0);

        Assert.Throws<InvalidOperationException>(() => package.MarkFailed(T1));
    }

    [Fact]
    public void Marking_failed_from_rejected_throws()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.Reject([SomeFinding], T0);

        Assert.Throws<InvalidOperationException>(() => package.MarkFailed(T1));
    }

    [Fact]
    public void Marking_failed_twice_throws()
    {
        var package = Uploaded();
        package.MarkFailed(T0);

        Assert.Throws<InvalidOperationException>(() => package.MarkFailed(T1));
    }

    // ── MarkUploadPurged ─────────────────────────────────────────────────

    [Theory]
    [InlineData(PackageImportStatus.Imported)]
    [InlineData(PackageImportStatus.Rejected)]
    [InlineData(PackageImportStatus.Failed)]
    public void Purging_the_upload_is_allowed_from_any_terminal_state(PackageImportStatus terminal)
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        switch (terminal)
        {
            case PackageImportStatus.Imported:
                package.MarkImported(["definition-1"], T0);
                break;
            case PackageImportStatus.Rejected:
                package.Reject([SomeFinding], T0);
                break;
            case PackageImportStatus.Failed:
                package.MarkFailed(T0);
                break;
        }

        package.MarkUploadPurged(T1);

        Assert.True(package.UploadPurged);
        Assert.Equal(T1, package.UpdatedAt);
    }

    [Fact]
    public void Purging_the_upload_from_a_non_terminal_state_throws()
    {
        var package = Uploaded();
        package.MarkValidating(T0);

        Assert.Throws<InvalidOperationException>(() => package.MarkUploadPurged(T1));
    }

    [Fact]
    public void Purging_the_upload_twice_throws()
    {
        var package = Uploaded();
        package.MarkValidating(T0);
        package.MarkImported(["definition-1"], T0);
        package.MarkUploadPurged(T1);

        Assert.Throws<InvalidOperationException>(() => package.MarkUploadPurged(T1));
    }
}
