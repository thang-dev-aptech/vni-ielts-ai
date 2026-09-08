using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

/// <summary>
/// Plan 03's structural gate, against the hostile fixtures
/// `docs/security/zip-ingestion-security.md` names by name — built in-memory
/// with <see cref="ZipArchive"/> rather than as committed binary files, since
/// a ZIP archive is a first-class BCL type and constructing one at test time
/// keeps every fixture's intent readable in the test itself.
/// </summary>
public sealed class PackageStructuralValidatorTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly ExamPackageReader ExamReader =
        ExamPackageReader.FromSchemaFile(Path.Combine(RepoRoot, "contracts/schemas/exam.schema.json"));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string ReadingDemoJson() =>
        File.ReadAllText(Path.Combine(
            RepoRoot,
            "backend/tests/Vni.Ielts.Infrastructure.Tests/Content/valid-exam.json"));

    private static string FullDemoJson() =>
        File.ReadAllText(Path.Combine(
            RepoRoot,
            "backend/tests/Vni.Ielts.Infrastructure.Tests/Content/Fixtures/synthetic-full-1.json"));

    // Tight limits so cap tests do not need gigabyte-scale content.
    private static readonly PackageValidationLimits TightLimits = new(
        MaxUncompressedBytes: 10_000,
        MaxEntryCount: 5,
        MaxCompressionRatio: 20,
        MaxManifestBytes: 10_000,
        MaxExamFileBytes: 10_000,
        MaxAssetBytes: 10_000);

    private static PackageStructuralValidator Validator(PackageValidationLimits? limits = null) =>
        new(ExamReader, limits);

    private static readonly UserId Uploader = UserId.New();

    private static byte[] BuildZip(Action<ZipArchive> configure)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            configure(archive);
        }

        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void AddEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(content, 0, content.Length);
    }

    private static byte[] MinimalDocx()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", "<Types />");
            AddEntry(archive, "_rels/.rels", "<Relationships />");
            AddEntry(archive, "word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Synthetic exam</w:t></w:r></w:p></w:body></w:document>");
        }
        return stream.ToArray();
    }

    private static string ManifestWith(string examsJsonArray, string assetsJsonArray = "[]") =>
        $$"""
        { "formatVersion": "1.0", "exams": {{examsJsonArray}}, "assets": {{assetsJsonArray}} }
        """;

    [Fact]
    public void Raw_docx_archive_is_classified_without_manifest_error()
    {
        var bytes = BuildZip(archive => AddEntry(archive, "sources/exam.docx", MinimalDocx()));
        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.True(outcome.IsValid);
        Assert.Equal(PackageSourceClassification.RawSource, outcome.Classification);
        var entry = Assert.Single(outcome.RawInventory!.Entries);
        Assert.Equal("sources/exam.docx", entry.Path);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", entry.MediaType);
        Assert.DoesNotContain(outcome.Findings, finding => finding.Code == "MANIFEST_MISSING");
    }

    [Fact]
    public void Malformed_manifest_never_falls_back_to_raw()
    {
        var bytes = BuildZip(archive =>
        {
            AddEntry(archive, "manifest.json", "not-json");
            AddEntry(archive, "exam.docx", MinimalDocx());
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, finding => finding.Code == "MANIFEST_INVALID");
    }

    [Fact]
    public void Raw_archive_without_docx_or_pdf_is_rejected_actionably()
    {
        var bytes = BuildZip(archive => AddEntry(archive, "notes.json", "{}"));
        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, finding => finding.Code == "RAW_SOURCE_DOCUMENT_MISSING");
    }

    // ── JSON path ────────────────────────────────────────────────────────

    [Fact]
    public void A_valid_json_package_is_accepted_with_one_entry()
    {
        var outcome = Validator().ValidateJson(new MemoryStream(Encoding.UTF8.GetBytes(ReadingDemoJson())), Uploader);

        Assert.True(outcome.IsValid);
        Assert.Single(outcome.Entries);
        Assert.Equal(ExamModule.Reading, outcome.Entries[0].Module);
    }

    /// <summary>
    /// This is where the "Đề của tôi" bug actually lived — not in
    /// <c>ExamPackageReader.Read</c>, which always threaded author identity
    /// through correctly, but here: <c>ValidateJson</c> accepted the parameter
    /// and never passed it on, so <c>Versions[0].AuthorId</c> was always null
    /// regardless of who uploaded. `GET /admin/exams`'s server-side
    /// <c>AuthorId == caller</c> filter then matched nothing for an
    /// <c>exam-author</c>, who only holds <c>exam.read.own</c>.
    /// </summary>
    [Fact]
    public void The_uploader_becomes_the_owner_of_the_imported_version()
    {
        var outcome = Validator().ValidateJson(new MemoryStream(Encoding.UTF8.GetBytes(ReadingDemoJson())), Uploader);

        Assert.True(outcome.IsValid);
        Assert.Single(outcome.Versions);
        Assert.Equal(Uploader, outcome.Versions[0].AuthorId);
    }

    [Fact]
    public void An_invalid_json_package_is_rejected_with_schema_findings()
    {
        var outcome = Validator().ValidateJson(new MemoryStream(Encoding.UTF8.GetBytes("""{"not":"an exam"}""")), Uploader);

        Assert.False(outcome.IsValid);
        Assert.NotEmpty(outcome.Findings);
        Assert.All(outcome.Findings, f => Assert.Equal("schema", f.Stage));
    }

    [Fact]
    public void Malformed_utf8_is_rejected_not_thrown()
    {
        byte[] invalidUtf8 = [0xFF, 0xFE, 0xFD];
        var outcome = Validator().ValidateJson(new MemoryStream(invalidUtf8), Uploader);

        // Lone bytes above 0x7F decode as the replacement character rather
        // than throwing, so this exercises "the text parses but is not a
        // valid exam" rather than a decoding failure — still must not throw.
        Assert.False(outcome.IsValid);
    }

    // ── ZIP: happy path ──────────────────────────────────────────────────

    /// <summary>
    /// Fixture file is present on INT (<c>fixtures/packages/aptis-demo.zip</c>),
    /// but its exam JSON no longer validates against the current
    /// <c>exam.schema.json</c> (slots / type enums / scoringProfile). Not a
    /// missing-fixture failure — package content needs a separate refresh.
    /// </summary>
    [Fact(Skip = "aptis-demo.zip SCHEMA_INVALID vs current INT exam.schema.json — fixture present, content stale")]
    public void Aptis_demo_upload_package_passes_the_shared_structural_gate()
    {
        var path = Path.Combine(RepoRoot, "fixtures/packages/aptis-demo.zip");
        using var stream = File.OpenRead(path);

        var outcome = Validator().ValidateZip(stream, Uploader);

        Assert.True(outcome.IsValid, string.Join(" | ", outcome.Findings.Select(f => $"{f.Code}: {f.Message}")));
        Assert.Equal(PackageSourceClassification.Canonical, outcome.Classification);
        Assert.Single(outcome.Entries);
        Assert.Equal("VNI Aptis Practice — Stakeholder Demo", outcome.Entries[0].Title);
    }

    [Fact]
    public void A_valid_single_exam_zip_is_accepted()
    {
        var bytes = BuildZip(zip =>
        {
            AddEntry(zip, "manifest.json", ManifestWith("""["exam.json"]"""));
            AddEntry(zip, "exam.json", ReadingDemoJson());
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.True(outcome.IsValid);
        Assert.Single(outcome.Entries);
    }

    [Fact]
    public void A_valid_multi_exam_zip_is_accepted_with_one_entry_per_exam()
    {
        var bytes = BuildZip(zip =>
        {
            AddEntry(zip, "manifest.json", ManifestWith("""["exams/reading.json","exams/full.json"]"""));
            AddEntry(zip, "exams/reading.json", ReadingDemoJson());
            AddEntry(zip, "exams/full.json", FullDemoJson());
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.True(outcome.IsValid);
        Assert.Equal(2, outcome.Entries.Count);
    }

    [Fact]
    public void A_declared_asset_with_a_matching_checksum_is_accepted()
    {
        var assetBytes = "not really audio, just bytes"u8.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(assetBytes)).ToLowerInvariant();

        var bytes = BuildZip(zip =>
        {
            AddEntry(zip, "manifest.json", ManifestWith(
                """["exam.json"]""",
                $$"""[{"path":"assets/a.m4a","sha256":"{{sha}}"}]"""));
            AddEntry(zip, "exam.json", ReadingDemoJson());
            AddEntry(zip, "assets/a.m4a", assetBytes);
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.True(outcome.IsValid);
    }

    // ── ZIP: magic bytes / malformed ────────────────────────────────────

    [Fact]
    public void A_file_that_is_not_a_zip_is_rejected_on_magic_bytes()
    {
        var outcome = Validator().ValidateZip(new MemoryStream("not a zip"u8.ToArray()), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "MAGIC_BYTES_MISMATCH");
    }

    [Fact]
    public void Truncated_zip_bytes_are_rejected_not_thrown()
    {
        var valid = BuildZip(zip => AddEntry(zip, "manifest.json", ManifestWith("""["exam.json"]""")));
        var truncated = valid[..(valid.Length / 2)];

        var outcome = Validator().ValidateZip(new MemoryStream(truncated), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "MALFORMED_ZIP");
    }

    // ── ZIP: Zip Slip (A1) ───────────────────────────────────────────────

    [Fact]
    public void An_entry_with_a_path_traversal_segment_is_rejected()
    {
        var bytes = BuildZip(zip =>
        {
            AddEntry(zip, "manifest.json", ManifestWith("""["../../evil.json"]"""));
            AddEntry(zip, "../../evil.json", ReadingDemoJson());
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "PATH_ESCAPE");
    }

    [Fact]
    public void An_absolute_path_entry_is_rejected()
    {
        var bytes = BuildZip(zip => AddEntry(zip, "/etc/passwd", "x"));

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "PATH_ESCAPE");
    }

    [Fact]
    public void A_reserved_windows_device_name_is_rejected()
    {
        var bytes = BuildZip(zip => AddEntry(zip, "CON.json", "x"));

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "FILENAME_INVALID");
    }

    // ── ZIP: caps (A2) ───────────────────────────────────────────────────

    [Fact]
    public void More_entries_than_the_cap_is_rejected()
    {
        var bytes = BuildZip(zip =>
        {
            for (var i = 0; i < TightLimits.MaxEntryCount + 1; i++)
                AddEntry(zip, $"file-{i}.txt", "x");
        });

        var outcome = Validator(TightLimits).ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "ENTRY_COUNT_EXCEEDED");
    }

    [Fact]
    public void Total_uncompressed_size_over_the_cap_is_rejected()
    {
        // Random-ish (non-repeating) content so it does not also trip the
        // ratio cap — isolates this one check.
        var random = new Random(42);
        var big = new byte[TightLimits.MaxUncompressedBytes + 1];
        random.NextBytes(big);

        var bytes = BuildZip(zip => AddEntry(zip, "big.bin", big));

        var outcome = Validator(TightLimits).ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "UNCOMPRESSED_SIZE_EXCEEDED");
    }

    [Fact]
    public void A_single_entrys_compression_ratio_over_the_cap_is_rejected()
    {
        // Highly compressible, well under the size cap, so only the ratio
        // check fires.
        var zeros = new byte[5_000];

        var bytes = BuildZip(zip => AddEntry(zip, "zeros.bin", zeros));

        var outcome = Validator(TightLimits).ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "COMPRESSION_RATIO_EXCEEDED");
    }

    // ── ZIP: symlink / special entries (A3) ─────────────────────────────

    [Fact]
    public void An_entry_flagged_as_a_unix_symlink_is_rejected()
    {
        var bytes = BuildZip(zip =>
        {
            var entry = zip.CreateEntry("link", CompressionLevel.NoCompression);
            using (var s = entry.Open()) s.Write("target"u8);
            // S_IFLNK (0xA000) in the top 16 bits — how a Unix zip tool marks
            // a symlink entry. The .NET writer never sets this itself, so a
            // hostile fixture has to be built by hand exactly like this.
            entry.ExternalAttributes = unchecked((int)0xA1FF0000);
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "SYMLINK_ENTRY");
    }

    // ── ZIP: nested archive ──────────────────────────────────────────────

    [Fact]
    public void A_zip_nested_inside_the_zip_is_rejected()
    {
        var innerZip = BuildZip(zip => AddEntry(zip, "x.txt", "x"));

        var bytes = BuildZip(zip =>
        {
            AddEntry(zip, "manifest.json", ManifestWith(
                """["exam.json"]""",
                """[{"path":"assets/inner.zip","sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]"""));
            AddEntry(zip, "exam.json", ReadingDemoJson());
            AddEntry(zip, "assets/inner.zip", innerZip);
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "NESTED_ARCHIVE");
    }

    [Fact]
    public void A_valid_docx_asset_is_not_misclassified_as_a_nested_archive()
    {
        var document = BuildZip(zip =>
        {
            AddEntry(zip, "[Content_Types].xml", "<Types />");
            AddEntry(zip, "_rels/.rels", "<Relationships />");
            AddEntry(zip, "word/document.xml", "<document />");
        });
        var checksum = Convert.ToHexString(SHA256.HashData(document)).ToLowerInvariant();

        var bytes = BuildZip(zip =>
        {
            AddEntry(zip, "manifest.json", ManifestWith(
                """["exam.json"]""",
                $$"""[{"path":"assets/source.docx","sha256":"{{checksum}}"}]"""));
            AddEntry(zip, "exam.json", ReadingDemoJson());
            AddEntry(zip, "assets/source.docx", document);
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.True(outcome.IsValid);
        Assert.DoesNotContain(outcome.Findings, f => f.Code == "NESTED_ARCHIVE");
    }

    [Fact]
    public void A_zip_renamed_to_docx_is_still_rejected()
    {
        var renamedZip = BuildZip(zip => AddEntry(zip, "x.txt", "x"));

        var bytes = BuildZip(zip =>
        {
            AddEntry(zip, "manifest.json", ManifestWith(
                """["exam.json"]""",
                """[{"path":"assets/fake.docx","sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]"""));
            AddEntry(zip, "exam.json", ReadingDemoJson());
            AddEntry(zip, "assets/fake.docx", renamedZip);
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "NESTED_ARCHIVE");
    }

    // ── ZIP: manifest / assets ───────────────────────────────────────────

    [Fact]
    public void An_archive_without_a_manifest_or_raw_source_document_is_rejected()
    {
        var bytes = BuildZip(zip => AddEntry(zip, "exam.json", ReadingDemoJson()));

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "RAW_SOURCE_DOCUMENT_MISSING");
    }

    [Fact]
    public void An_unsupported_major_format_version_is_rejected()
    {
        var bytes = BuildZip(zip =>
        {
            AddEntry(zip, "manifest.json", """{ "formatVersion": "2.0", "exams": ["exam.json"], "assets": [] }""");
            AddEntry(zip, "exam.json", ReadingDemoJson());
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "UNSUPPORTED_FORMAT_VERSION");
    }

    [Fact]
    public void An_exam_file_declared_but_missing_from_the_archive_is_rejected()
    {
        var bytes = BuildZip(zip => AddEntry(zip, "manifest.json", ManifestWith("""["exam.json"]""")));

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "EXAM_FILE_NOT_FOUND");
    }

    [Fact]
    public void An_asset_present_but_undeclared_is_rejected_as_smuggled()
    {
        var bytes = BuildZip(zip =>
        {
            AddEntry(zip, "manifest.json", ManifestWith("""["exam.json"]"""));
            AddEntry(zip, "exam.json", ReadingDemoJson());
            AddEntry(zip, "assets/smuggled.bin", "surprise");
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "ASSET_UNDECLARED");
    }

    [Fact]
    public void An_asset_declared_but_missing_from_the_archive_is_rejected()
    {
        var bytes = BuildZip(zip =>
        {
            AddEntry(zip, "manifest.json", ManifestWith(
                """["exam.json"]""",
                """[{"path":"assets/a.m4a","sha256":"abc"}]"""));
            AddEntry(zip, "exam.json", ReadingDemoJson());
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "ASSET_NOT_FOUND");
    }

    [Fact]
    public void An_asset_whose_content_does_not_match_its_declared_checksum_is_rejected()
    {
        var bytes = BuildZip(zip =>
        {
            AddEntry(zip, "manifest.json", ManifestWith(
                """["exam.json"]""",
                """[{"path":"assets/a.m4a","sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]"""));
            AddEntry(zip, "exam.json", ReadingDemoJson());
            AddEntry(zip, "assets/a.m4a", "the actual bytes"u8.ToArray());
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "CHECKSUM_MISMATCH");
    }

    [Fact]
    public void A_duplicate_exam_path_in_the_manifest_is_rejected()
    {
        var bytes = BuildZip(zip =>
        {
            AddEntry(zip, "manifest.json", ManifestWith("""["exam.json","exam.json"]"""));
            AddEntry(zip, "exam.json", ReadingDemoJson());
        });

        var outcome = Validator().ValidateZip(new MemoryStream(bytes), Uploader);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Findings, f => f.Code == "DUPLICATE_EXAM_PATH");
    }
}
