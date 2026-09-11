using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content;

public enum PackageSourceClassification { Canonical, RawSource }

public sealed record RawPackageInventoryEntry(
    string Path, string MediaType, long ByteLength, string Sha256);

public sealed record RawPackageInventory(IReadOnlyList<RawPackageInventoryEntry> Entries);

public sealed record PackageValidationOutcome(
    bool IsValid,
    IReadOnlyList<PackageFinding> Findings,
    IReadOnlyList<ExamPackageEntry> Entries,
    /// <summary>
    /// Same order and count as <see cref="Entries"/> — the actual, ready-to-
    /// persist <see cref="ExamVersion"/> objects. <c>Entries</c> is what
    /// <c>ReadyToImport</c> shows an operator before confirming; this is what
    /// actually becomes a <c>Draft</c>, and its <c>AuthorId</c> reflects
    /// whoever this validation run was told is the actor — the uploader for a
    /// single-source package, or the confirming principal when this runs
    /// again at confirm time.
    /// </summary>
    IReadOnlyList<ExamVersion> Versions,
    PackageSourceClassification Classification,
    RawPackageInventory? RawInventory)
{
    public static PackageValidationOutcome Accepted(
        IReadOnlyList<ExamPackageEntry> entries, IReadOnlyList<ExamVersion> versions) =>
        new(true, [], entries, versions, PackageSourceClassification.Canonical, null);

    public static PackageValidationOutcome Raw(RawPackageInventory inventory) =>
        new(true, [], [], [], PackageSourceClassification.RawSource, inventory);

    public static PackageValidationOutcome Rejected(IReadOnlyList<PackageFinding> findings) =>
        new(false, findings, [], [], PackageSourceClassification.Canonical, null);
}

/// <summary>
/// `[ASSUMPTION]` — zip-ingestion-security.md's starting caps, tuned later
/// against real packages. Overridable per instance (tests use tight limits so
/// a cap test does not need to write gigabyte-scale content) rather than
/// hardcoded, since Plan 03's own open-questions list names these exact
/// numbers as still undecided.
/// </summary>
public sealed record PackageValidationLimits(
    long MaxUncompressedBytes,
    int MaxEntryCount,
    int MaxCompressionRatio,
    long MaxManifestBytes,
    long MaxExamFileBytes,
    long MaxAssetBytes)
{
    /// <summary>
    /// Raised 2026-08-28 from the security doc's original starting numbers
    /// (200 MB / 1 GB / 100 MB per asset) after a real package — a 1.3 GB
    /// multi-skill ZIP with substantial audio — hit the original compressed
    /// cap. That is exactly the "tune later against real packages" the doc's
    /// own `[ASSUMPTION]` tag anticipated, not a business decision that
    /// needed an owner's sign-off.
    /// </summary>
    public static readonly PackageValidationLimits Default = new(
        MaxUncompressedBytes: 4L * 1024 * 1024 * 1024,
        MaxEntryCount: 5_000,
        MaxCompressionRatio: 100,
        MaxManifestBytes: 10L * 1024 * 1024,
        MaxExamFileBytes: 20L * 1024 * 1024,
        MaxAssetBytes: 300L * 1024 * 1024);
}

/// <summary>
/// The structural/security gate for an uploaded package — Plan 03 of
/// `phase-2-content-import-plan.md`, following the flowchart in
/// `docs/security/zip-ingestion-security.md` in order. Runs against a
/// <em>seekable</em> stream over the immutable bytes a caller already read
/// from <c>IPackageUploadStore</c> (`A8`) — a caller-supplied local
/// <c>FileStream</c>, in practice, since a GridFS download stream is
/// forward-only and <see cref="ZipArchive"/> has to seek to read the central
/// directory. Individual entries (the manifest, each exam file, each asset)
/// are still read fully into memory, but each is bounded by
/// <see cref="PackageValidationLimits"/> — it is the *outer* container that
/// must never be buffered whole, since that scales with the whole upload
/// rather than with any one part of it.
///
/// <b>Never extracts to disk.</b> Path canonicalization is checked as a
/// content-integrity gate (rejecting the fixtures the security doc names),
/// not because extraction could otherwise escape a real sandbox directory —
/// ZIP entries are read into memory, never written to a filesystem path.
///
/// <b>One deliberate deviation from `exam-package-format.md`'s illustrated
/// layout.</b> That document's example shows `exam.json` referencing separate
/// per-module `section.json` files. <c>contracts/schemas/exam.schema.json</c>
/// — the file this validator actually delegates to via
/// <see cref="ExamPackageReader"/> — is documented as the single schema *all
/// three* content producers validate against, sections embedded inline. This
/// validator follows the schema that exists, not the older illustration:
/// <c>manifest.exams</c> lists paths to complete, schema-shaped exam
/// documents (one per exam in the package), not a manifest→exam→section
/// three-file chain.
///
/// <b>Not done here, and not silently assumed:</b> malware/AV scanning
/// (flagged `[NEEDS VALIDATION]` in the plan, needs an ADR) and deep media
/// probing with a hardened external tool. Declared asset checksums are
/// verified; codec-level validity is not.
/// </summary>
public sealed class PackageStructuralValidator(
    ExamPackageReader examReader, PackageValidationLimits? limits = null)
{
    private readonly PackageValidationLimits _limits = limits ?? PackageValidationLimits.Default;

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <param name="content">
    /// A single exam JSON file is expected to be small (no embedded media),
    /// so this reads it fully as text — same as always, just from a stream
    /// instead of a pre-materialized byte array.
    /// </param>
    /// <param name="createdBy">
    /// Becomes the resulting <see cref="ExamVersion.AuthorId"/> — the
    /// uploader for a single-source package that imports itself with no
    /// separate confirm step, or the confirming principal when this runs
    /// again at confirm time for a multi-exam ZIP.
    /// </param>
    public PackageValidationOutcome ValidateJson(Stream content, UserId createdBy)
    {
        string json;
        try
        {
            using var reader = new StreamReader(content, System.Text.Encoding.UTF8, leaveOpen: true);
            json = reader.ReadToEnd();
        }
        catch (Exception)
        {
            return PackageValidationOutcome.Rejected(
                [new PackageFinding("structural", "MALFORMED_JSON", "/", "The file is not valid UTF-8 text.")]);
        }

        var result = examReader.Read(json, ExamDefinitionId.New(), 1, createdBy);
        if (!result.IsValid)
            return PackageValidationOutcome.Rejected([.. result.Findings.Select(ToPackageFinding)]);

        return PackageValidationOutcome.Accepted([ToEntry(result.Version!)], [result.Version!]);
    }

    /// <param name="content">
    /// Must be seekable — <see cref="ZipArchive"/> reads the central
    /// directory from the end of the stream first. A GridFS download stream
    /// is forward-only; callers pass a local <c>FileStream</c> instead.
    /// </param>
    /// <param name="createdBy">See the parameter of the same name on <see cref="ValidateJson"/>.</param>
    public PackageValidationOutcome ValidateZip(Stream content, UserId createdBy)
    {
        if (!content.CanSeek)
            throw new ArgumentException("The content stream must be seekable.", nameof(content));

        Span<byte> head = stackalloc byte[2];
        var read = content.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        content.Position = 0;
        if (read < 2 || head[0] != 'P' || head[1] != 'K')
            return Reject("MAGIC_BYTES_MISMATCH", "/", "The file is not a ZIP archive.");

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            return Reject("MALFORMED_ZIP", "/", "The archive could not be read.");
        }

        using (archive)
        {
            var entries = archive.Entries;
            var findings = new List<PackageFinding>();

            if (entries.Count > _limits.MaxEntryCount)
            {
                findings.Add(new PackageFinding(
                    "structural", "ENTRY_COUNT_EXCEEDED", "/",
                    "The archive contains more entries than this pipeline accepts."));
            }

            var totalUncompressed = entries.Sum(e => e.Length);
            if (totalUncompressed > _limits.MaxUncompressedBytes)
            {
                findings.Add(new PackageFinding(
                    "structural", "UNCOMPRESSED_SIZE_EXCEEDED", "/",
                    "The archive's declared uncompressed size is larger than this pipeline accepts."));
            }

            var canonicalPaths = new Dictionary<ZipArchiveEntry, string>();

            foreach (var entry in entries)
            {
                // A directory entry — `Length == 0` and the name ends in `/`.
                // Compression-ratio and symlink checks below do not apply.
                var isDirectory = entry.FullName.EndsWith('/');

                if (!isDirectory && entry.CompressedLength > 0
                    && entry.Length / entry.CompressedLength > _limits.MaxCompressionRatio)
                {
                    findings.Add(new PackageFinding(
                        "structural", "COMPRESSION_RATIO_EXCEEDED", $"/{entry.FullName}",
                        "This entry's compression ratio is higher than this pipeline accepts."));
                }
                else if (!isDirectory && entry.CompressedLength == 0 && entry.Length > 0)
                {
                    findings.Add(new PackageFinding(
                        "structural", "COMPRESSION_RATIO_EXCEEDED", $"/{entry.FullName}",
                        "This entry claims content from zero compressed bytes."));
                }

                if (IsSymlinkOrSpecial(entry))
                {
                    findings.Add(new PackageFinding(
                        "structural", "SYMLINK_ENTRY", $"/{entry.FullName}",
                        "Only regular files and directories are accepted."));
                    continue;
                }

                if (!TryCanonicalize(entry.FullName, out var canonical, out var pathFinding))
                {
                    findings.Add(pathFinding!);
                    continue;
                }

                canonicalPaths[entry] = canonical;
            }

            // Caps and per-entry structural findings are reported together —
            // "report all findings at once", not just the first. Nested
            // archives and content-level checks need the entries that passed
            // the checks above, so they run in a second pass only once we
            // know which entries are safe to open.
            if (findings.Count > 0) return PackageValidationOutcome.Rejected(findings);

            foreach (var (entry, _) in canonicalPaths)
            {
                if (entry.FullName.EndsWith('/')) continue;
                if (LooksLikeNestedArchive(entry) && !IsValidWordOpenXmlDocument(entry))
                {
                    findings.Add(new PackageFinding(
                        "structural", "NESTED_ARCHIVE", $"/{entry.FullName}",
                        "Archives inside archives are not accepted."));
                }
            }

            if (findings.Count > 0) return PackageValidationOutcome.Rejected(findings);

            var byPath = canonicalPaths.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);
            if (byPath.ContainsKey("manifest.json"))
                return ValidateManifestAndContents(canonicalPaths, createdBy);

            var rawEntries = new List<RawPackageInventoryEntry>();
            foreach (var (entry, path) in canonicalPaths)
            {
                if (entry.FullName.EndsWith('/')) continue;
                var mediaType = RawMediaType(path, entry);
                if (mediaType is null) continue;
                using var entryStream = entry.Open();
                var hash = Convert.ToHexString(SHA256.HashData(entryStream)).ToLowerInvariant();
                rawEntries.Add(new RawPackageInventoryEntry(path, mediaType, entry.Length, hash));
            }

            if (!rawEntries.Any(entry => entry.MediaType is "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or "application/pdf"))
                return Reject("RAW_SOURCE_DOCUMENT_MISSING", "/", "The raw archive contains no supported DOCX or PDF source document.");

            return PackageValidationOutcome.Raw(new RawPackageInventory(rawEntries));
        }
    }

    private PackageValidationOutcome ValidateManifestAndContents(
        Dictionary<ZipArchiveEntry, string> canonicalPaths, UserId createdBy)
    {
        var byPath = canonicalPaths.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

        if (!byPath.TryGetValue("manifest.json", out var manifestEntry))
            return Reject("MANIFEST_MISSING", "/manifest.json", "The archive has no manifest.json.");

        var manifestBytes = ReadBounded(manifestEntry, _limits.MaxManifestBytes, out var manifestOverflow);
        if (manifestOverflow)
            return Reject("MANIFEST_TOO_LARGE", "/manifest.json", "manifest.json is larger than this pipeline accepts.");

        JsonNode? manifest;
        try
        {
            manifest = JsonNode.Parse(manifestBytes);
        }
        catch (JsonException)
        {
            return Reject("MANIFEST_INVALID", "/manifest.json", "manifest.json is not valid JSON.");
        }

        if (manifest is not JsonObject root)
            return Reject("MANIFEST_INVALID", "/manifest.json", "manifest.json must be a JSON object.");

        var formatVersion = root["formatVersion"]?.GetValue<string>();
        if (formatVersion is null || !System.Text.RegularExpressions.Regex.IsMatch(formatVersion, @"^1\.[0-9]+$"))
        {
            return Reject(
                "UNSUPPORTED_FORMAT_VERSION", "/manifest.json/formatVersion",
                "This pipeline accepts formatVersion 1.x only.");
        }

        var examsNode = root["exams"] as JsonArray;
        var examPaths = examsNode?.Select(n => n?.GetValue<string>()).Where(p => p is not null).Select(p => p!).ToList()
            ?? [];
        if (examPaths.Count == 0)
            return Reject("MANIFEST_INVALID", "/manifest.json/exams", "manifest.json declares no exam files.");

        if (examPaths.Distinct(StringComparer.Ordinal).Count() != examPaths.Count)
            return Reject("DUPLICATE_EXAM_PATH", "/manifest.json/exams", "The same exam path is declared more than once.");

        var assetsNode = root["assets"] as JsonArray ?? [];
        var declaredAssets = new List<(string Path, string Sha256)>();
        foreach (var node in assetsNode)
        {
            if (node is not JsonObject asset
                || asset["path"]?.GetValue<string>() is not { } path
                || asset["sha256"]?.GetValue<string>() is not { } sha)
            {
                return Reject("MANIFEST_INVALID", "/manifest.json/assets", "Each asset needs a path and a sha256.");
            }

            declaredAssets.Add((path, sha));
        }

        var findings = new List<PackageFinding>();
        var entries = new List<ExamPackageEntry>();
        var versions = new List<ExamVersion>();

        foreach (var examPath in examPaths)
        {
            if (!byPath.TryGetValue(examPath, out var examEntry))
            {
                findings.Add(new PackageFinding(
                    "structural", "EXAM_FILE_NOT_FOUND", $"/manifest.json/exams",
                    $"Declared exam file '{examPath}' is not present in the archive."));
                continue;
            }

            var examBytes = ReadBounded(examEntry, _limits.MaxExamFileBytes, out var overflow);
            if (overflow)
            {
                findings.Add(new PackageFinding(
                    "structural", "EXAM_FILE_TOO_LARGE", $"/{examPath}",
                    "This exam file is larger than this pipeline accepts."));
                continue;
            }

            var result = examReader.Read(
                System.Text.Encoding.UTF8.GetString(examBytes), ExamDefinitionId.New(), 1, createdBy);

            if (!result.IsValid)
            {
                findings.AddRange(result.Findings.Select(f => ToPackageFinding(f) with
                {
                    Pointer = $"/{examPath}{f.Path}",
                }));
                continue;
            }

            entries.Add(ToEntry(result.Version!));
            versions.Add(result.Version!);
        }

        foreach (var (path, sha256) in declaredAssets)
        {
            if (!byPath.TryGetValue(path, out var assetEntry))
            {
                findings.Add(new PackageFinding(
                    "structural", "ASSET_NOT_FOUND", $"/manifest.json/assets",
                    $"Declared asset '{path}' is not present in the archive."));
                continue;
            }

            var assetBytes = ReadBounded(assetEntry, _limits.MaxAssetBytes, out var overflow);
            if (overflow)
            {
                findings.Add(new PackageFinding(
                    "structural", "ASSET_TOO_LARGE", $"/{path}",
                    "This asset is larger than this pipeline accepts."));
                continue;
            }

            var actualHash = Convert.ToHexString(SHA256.HashData(assetBytes)).ToLowerInvariant();
            if (!string.Equals(actualHash, sha256, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new PackageFinding(
                    "structural", "CHECKSUM_MISMATCH", $"/{path}",
                    "This asset's content does not match its declared checksum."));
            }
        }

        var declaredPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "manifest.json",
        };
        declaredPaths.UnionWith(examPaths);
        declaredPaths.UnionWith(declaredAssets.Select(a => a.Path));

        foreach (var path in byPath.Keys)
        {
            if (path.EndsWith('/')) continue; // directory entries carry no content
            if (!declaredPaths.Contains(path))
            {
                findings.Add(new PackageFinding(
                    "structural", "ASSET_UNDECLARED", $"/{path}",
                    $"'{path}' is present in the archive but not declared in manifest.json."));
            }
        }

        return findings.Count > 0
            ? PackageValidationOutcome.Rejected(findings)
            : PackageValidationOutcome.Accepted(entries, versions);
    }

    private static byte[] ReadBounded(ZipArchiveEntry entry, long limit, out bool overflow)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > limit)
            {
                overflow = true;
                return [];
            }

            buffer.Write(chunk, 0, read);
        }

        overflow = false;
        return buffer.ToArray();
    }

    private static bool LooksLikeNestedArchive(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        Span<byte> head = stackalloc byte[4];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        if (read < 4) return false;

        return head[0] == 'P' && head[1] == 'K'
            && ((head[2] == 3 && head[3] == 4) || (head[2] == 5 && head[3] == 6) || (head[2] == 7 && head[3] == 8));
    }

    /// <summary>
    /// A DOCX file is itself a ZIP container, so ZIP magic bytes alone cannot
    /// distinguish a legitimate Word document from an archive nested inside
    /// the uploaded package. Keep the exception deliberately narrow: only the
    /// non-macro Word extension is accepted, and only when the container has
    /// the minimum required OOXML package and Word document parts. A renamed
    /// .zip therefore remains rejected.
    /// </summary>
    private bool IsValidWordOpenXmlDocument(ZipArchiveEntry entry)
    {
        if (!string.Equals(Path.GetExtension(entry.FullName), ".docx", StringComparison.OrdinalIgnoreCase)
            || entry.Length > _limits.MaxAssetBytes)
            return false;

        try
        {
            using var content = entry.Open();
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            buffer.Position = 0;

            using var document = new ZipArchive(buffer, ZipArchiveMode.Read);
            var paths = document.Entries.Select(part => part.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return paths.Contains("[Content_Types].xml")
                && paths.Contains("_rels/.rels")
                && paths.Contains("word/document.xml");
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>
    /// Unix-created archives store `st_mode` in the top 16 bits of
    /// <c>ExternalAttributes</c>; <c>0xA000</c> is <c>S_IFLNK</c>. Windows-
    /// created archives never set this, so a false negative there is
    /// structurally impossible, not a gap.
    /// </summary>
    private static bool IsSymlinkOrSpecial(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static bool TryCanonicalize(string entryPath, out string canonical, out PackageFinding? finding)
    {
        canonical = string.Empty;
        finding = null;

        if (string.IsNullOrEmpty(entryPath) || entryPath.Contains('\0'))
        {
            finding = new PackageFinding("structural", "FILENAME_INVALID", $"/{entryPath}", "The entry path contains a null byte or is empty.");
            return false;
        }

        if (Path.IsPathRooted(entryPath) || entryPath.StartsWith('/') || entryPath.StartsWith('\\')
            || System.Text.RegularExpressions.Regex.IsMatch(entryPath, @"^[A-Za-z]:"))
        {
            finding = new PackageFinding("structural", "PATH_ESCAPE", $"/{entryPath}", "Absolute paths are not accepted.");
            return false;
        }

        var segments = entryPath.Split('/', '\\');
        foreach (var segment in segments)
        {
            var trimmed = segment.TrimEnd('.', ' ');
            var bare = trimmed.Contains('.') ? trimmed[..trimmed.IndexOf('.')] : trimmed;
            if (segment == "..")
            {
                finding = new PackageFinding("structural", "PATH_ESCAPE", $"/{entryPath}", "The entry path escapes the archive root.");
                return false;
            }

            if (bare.Length > 0 && ReservedWindowsNames.Contains(bare))
            {
                finding = new PackageFinding("structural", "FILENAME_INVALID", $"/{entryPath}", $"'{segment}' is a reserved name.");
                return false;
            }
        }

        const string sandboxRoot = "/sandbox";
        var destination = Path.GetFullPath(Path.Combine(sandboxRoot, entryPath));
        var root = Path.GetFullPath(sandboxRoot) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(root, StringComparison.Ordinal) && destination != Path.GetFullPath(sandboxRoot))
        {
            finding = new PackageFinding("structural", "PATH_ESCAPE", $"/{entryPath}", "The entry path escapes the archive root.");
            return false;
        }

        canonical = entryPath.Replace('\\', '/').TrimStart('/');
        return true;
    }

    private bool IsSupportedDocx(ZipArchiveEntry entry) => IsValidWordOpenXmlDocument(entry);

    private string? RawMediaType(string path, ZipArchiveEntry entry)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".docx" && IsSupportedDocx(entry))
            return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        using var stream = entry.Open();
        Span<byte> head = stackalloc byte[12];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        if (extension == ".pdf" && read >= 5 && head[..5].SequenceEqual("%PDF-"u8)) return "application/pdf";
        if (read >= 8 && head[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (read >= 3 && head[..3].SequenceEqual(new byte[] { 255, 216, 255 })) return "image/jpeg";
        if (read >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WAVE"u8)) return "audio/wav";
        if (extension == ".json") return "application/json";
        return null;
    }

    private static PackageValidationOutcome Reject(string code, string pointer, string message) =>
        PackageValidationOutcome.Rejected([new PackageFinding("structural", code, pointer, message)]);

    private static PackageFinding ToPackageFinding(ValidationFinding f) =>
        ExamValidationWire.ToPackageFinding(f);

    private static ExamPackageEntry ToEntry(ExamVersion version) => new(
        version.DefinitionId.Value,
        version.Title,
        version.Sections.Count > 0 ? version.Sections[0].Module : ExamModule.Reading,
        version.Sections.Sum(s => s.Parts.Sum(p => p.Questions.Count)));
}
