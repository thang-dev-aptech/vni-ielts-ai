using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content.Import;

/// <summary>
/// The ZIP half of the exam-import front door. Implements
/// docs/security/zip-ingestion-security.md in the order the spec draws it:
/// magic bytes → archive byte cap → central directory only → entry count →
/// declared sizes and ratios → path canonicalisation → non-regular entries →
/// layout. Nothing is written until every one of those has passed, and the
/// write that follows counts the bytes it actually produces rather than
/// believing the header.
///
/// <b>Why the central directory and not the local headers.</b> `ZipArchive`
/// in Read mode over a seekable stream parses the central directory at the
/// end of the file and nothing else; an entry's local header is read only
/// when the entry is opened. So enumerating <c>Entries</c> is exactly the
/// "read the central directory only" step, and no entry is opened during
/// inspection at all.
///
/// <b>What a finding says and what the log says are different things.</b> The
/// caller gets the category and the entry; the log gets the number the entry
/// tripped and the number it was allowed. Telling an attacker the ratio cap is
/// helping them tune the next upload. → spec § Error reporting
///
/// <b>Deliberately blind to content.</b> A DOCX is itself a ZIP; probing entry
/// bytes for archive signatures would refuse every Word document. Nested
/// archives are refused by name, and what a file's bytes really are is decided
/// downstream, per file, by <see cref="SafeSourceDocumentExtractor"/> and the
/// media probe, each with its own bounds.
/// </summary>
public sealed class ExamPackageArchiveInspector(ILogger<ExamPackageArchiveInspector>? logger = null)
    : IExamPackageArchiveInspector
{
    private const string Error = "error";
    private const string Warning = "warning";
    private const string ArchivePath = "/archive";

    /// <summary>
    /// Findings stop being collected past this many. A package with more
    /// problems than this is not being repaired by its uploader; it is being
    /// used to make the response large.
    /// </summary>
    private const int MaxFindings = 100;

    private const int MaxRelativePathLength = 200;
    private const int MaxSegmentLength = 120;
    private const int CopyBufferSize = 81_920;

    private static readonly byte[] LocalFileHeader = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] EndOfCentralDirectory = [0x50, 0x4B, 0x05, 0x06];
    private static readonly byte[] SpannedMarker = [0x50, 0x4B, 0x07, 0x08];

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".jar", ".cab", ".iso", ".zipx",
    };

    private static readonly Dictionary<string, ExamModule> SkillFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reading"] = ExamModule.Reading,
        ["listening"] = ExamModule.Listening,
        ["writing"] = ExamModule.Writing,
        ["speaking"] = ExamModule.Speaking,
    };

    private static readonly string ProbeRoot =
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "vni-package-probe"));

    private readonly ILogger log = logger ?? NullLogger<ExamPackageArchiveInspector>.Instance;

    public async Task<ArchiveInspection> InspectAsync(
        Stream zip, ImportArchiveLimits limits, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentNullException.ThrowIfNull(limits);

        await using var source = await ArchiveSource.OpenAsync(zip, limits.MaxArchiveBytes, ct);
        if (source.Finding is not null)
        {
            LogRefusal(source.Finding, $"archive bytes cap {limits.MaxArchiveBytes}");
            return Rejected(source.Finding);
        }

        var stream = source.Stream;

        // ── Magic bytes ──────────────────────────────────────────────────
        var signature = await ReadSignatureAsync(stream, ct);
        if (signature.SequenceEqual(EndOfCentralDirectory))
            return Rejected(Finding(Error, ArchiveFindingCodes.LayoutEmpty, ArchivePath, "The package contains no files."));
        if (signature.SequenceEqual(SpannedMarker))
            return Rejected(Finding(Error, ArchiveFindingCodes.NotAnArchive, ArchivePath, "Spanned or split archives are not accepted."));
        if (!signature.SequenceEqual(LocalFileHeader))
            return Rejected(Finding(Error, ArchiveFindingCodes.NotAnArchive, ArchivePath, "The upload is not a ZIP archive."));

        // ── Entry count, from the end-of-central-directory record ────────
        // A cheap early filter: the count is read from 22 bytes at the tail
        // before the driver allocates one object per entry. The definitive
        // check is on `Entries.Count` below, which also covers ZIP64.
        var declared = await ReadDeclaredEntryCountAsync(stream, ct);
        if (declared is null)
            return Rejected(Finding(Error, ArchiveFindingCodes.NotAnArchive, ArchivePath, "The archive has no readable central directory."));
        if (declared is < 0xFFFF and var early && early > limits.MaxEntries)
        {
            var finding = Finding(Error, ArchiveFindingCodes.TooManyEntries, ArchivePath, "The package contains more files than are accepted.");
            LogRefusal(finding, $"declared {early} entries, cap {limits.MaxEntries}");
            return Rejected(finding);
        }

        stream.Seek(0, SeekOrigin.Begin);
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException e)
        {
            log.LogWarning(e, "Upload refused: the central directory could not be read.");
            return Rejected(Finding(Error, ArchiveFindingCodes.NotAnArchive, ArchivePath, "The archive's central directory is unreadable."));
        }

        using (archive)
        {
            IReadOnlyCollection<ZipArchiveEntry> entries;
            try
            {
                entries = archive.Entries;
            }
            catch (InvalidDataException e)
            {
                log.LogWarning(e, "Upload refused: an entry in the central directory is malformed.");
                return Rejected(Finding(Error, ArchiveFindingCodes.NotAnArchive, ArchivePath, "The archive's central directory is unreadable."));
            }

            if (entries.Count > limits.MaxEntries)
            {
                var finding = Finding(Error, ArchiveFindingCodes.TooManyEntries, ArchivePath, "The package contains more files than are accepted.");
                LogRefusal(finding, $"{entries.Count} entries, cap {limits.MaxEntries}");
                return Rejected(finding);
            }

            // ── Declared total, from the central directory alone ─────────
            // Summed straight off `entry.Length` before any per-entry check
            // runs, so a total-size violation is caught even when every
            // individual entry would otherwise be refused for something
            // else first (a ratio failure removes an entry from this sum
            // only in the sense that the archive is already rejected).
            var declaredTotal = entries.Sum(e => (long)e.Length);
            if (declaredTotal > limits.MaxTotalUncompressedBytes)
            {
                var finding = Finding(Error, ArchiveFindingCodes.TotalSizeExceeded, ArchivePath, "The package's total unpacked size is larger than is accepted.");
                LogRefusal(finding, $"declared total {declaredTotal}, cap {limits.MaxTotalUncompressedBytes}");
                return Rejected(finding);
            }

            // ── Per-entry checks, all of them, findings collected ────────
            var findings = new List<PackageFinding>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var bySkill = new Dictionary<ExamModule, List<string>>();
            var unknown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (findings.Count >= MaxFindings) break;

                var verdict = Examine(entry, limits, seen);
                if (verdict.Finding is not null)
                {
                    findings.Add(verdict.Finding);
                    continue;
                }

                if (verdict.IsDirectory) continue;

                var segments = verdict.Path.Split('/');
                if (segments.Length >= 2 && SkillFolders.TryGetValue(segments[0], out var module))
                {
                    if (!bySkill.TryGetValue(module, out var list))
                        bySkill[module] = list = [];
                    list.Add(verdict.Path);
                }
                else
                {
                    var key = segments.Length == 1 ? segments[0] : segments[0] + "/";
                    unknown[key] = unknown.GetValueOrDefault(key) + 1;
                }
            }

            // Case-sensitive: an ignore-case sort folds "n" and "N" together
            // and puts "notes.txt" ahead of "__MACOSX/", which is a stable
            // sort but not the one an administrator scanning the list expects.
            foreach (var (key, count) in unknown.OrderBy(u => u.Key, StringComparer.Ordinal))
            {
                findings.Add(Finding(
                    Warning,
                    ArchiveFindingCodes.LayoutUnknownEntry,
                    Display(key),
                    key.EndsWith('/')
                        ? $"Top-level folder is not one of reading/, listening/, writing/ or speaking/; its {count} file(s) are ignored."
                        : "File at the root of the package is not inside a skill folder; it is ignored."));
            }

            if (bySkill.Count == 0)
            {
                findings.Add(Finding(
                    Error,
                    ArchiveFindingCodes.LayoutEmpty,
                    ArchivePath,
                    "The package has no files under reading/, listening/, writing/ or speaking/."));
            }

            var layout = new PackageLayout(
                bySkill.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.AsReadOnly()),
                unknown.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

            var acceptable = findings.All(f => f.Severity != Error);
            return new ArchiveInspection(acceptable, findings, layout);
        }
    }

    public async Task<ArchiveExtraction> ExtractToSandboxAsync(
        Stream zip,
        ArchiveInspection inspection,
        string sandboxRoot,
        ImportArchiveLimits limits,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxRoot);
        ArgumentNullException.ThrowIfNull(limits);

        if (!inspection.IsAcceptable)
        {
            throw new InvalidOperationException(
                "Refusing to extract an archive whose inspection was not acceptable. "
                + "Inspection is the gate; extraction never re-decides it.");
        }

        var accepted = new HashSet<string>(inspection.Layout.AcceptedEntries, StringComparer.Ordinal);

        var root = Path.GetFullPath(sandboxRoot);
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, "pkg-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        var directoryPrefix = directory + Path.DirectorySeparatorChar;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limits.ExtractionTimeout);
        var token = timeout.Token;

        long written = 0;
        var files = 0;

        try
        {
            await using var source = await ArchiveSource.OpenAsync(zip, limits.MaxArchiveBytes, token);
            if (source.Finding is not null)
                return Failed(directory, source.Finding, written, files, $"archive bytes cap {limits.MaxArchiveBytes}");

            using var archive = new ZipArchive(source.Stream, ZipArchiveMode.Read, leaveOpen: true);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var buffer = new byte[CopyBufferSize];

            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();

                // Every check runs again against the archive actually being
                // extracted. The inspection says which entries are wanted; it
                // is never trusted to say the bytes are still the same.
                var verdict = Examine(entry, limits, seen);
                if (verdict.Finding is { Severity: Error } refusal)
                    return Failed(directory, refusal, written, files, "re-check during extraction");
                if (verdict.IsDirectory || !accepted.Contains(verdict.Path))
                    continue;

                var destination = Path.GetFullPath(Path.Combine(directory, verdict.Path));
                if (!destination.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return Failed(
                        directory,
                        Finding(Error, ArchiveFindingCodes.PathEscape, Display(entry.FullName), "Entry resolves outside the package."),
                        written, files, $"resolved to {destination}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                long entryBytes = 0;
                PackageFinding? quotaFinding = null;
                string? quotaDetail = null;

                await using (var input = entry.Open())
                await using (var output = new FileStream(
                    destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    CopyBufferSize, FileOptions.Asynchronous))
                {
                    int read;
                    while ((read = await input.ReadAsync(buffer, token)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        entryBytes += read;
                        written += read;

                        // Counted on the way through, not read off the header.
                        // A header that declares 10 bytes and delivers a gigabyte
                        // passed inspection on purpose; this is where it stops.
                        //
                        // The finding is captured rather than returned here: a
                        // `return` from inside this `await using` still runs
                        // `Failed()` — and its directory delete — before the
                        // block's own dispose releases the file handle just
                        // opened above, so the delete would find the file
                        // still in use and silently fail to clean it up.
                        if (entryBytes > limits.MaxEntryUncompressedBytes
                            || written > limits.MaxTotalUncompressedBytes)
                        {
                            quotaFinding = Finding(Error, ArchiveFindingCodes.ExtractionQuotaExceeded, Display(entry.FullName), "Unpacking exceeded the package's byte quota.");
                            quotaDetail =
                                $"entry {entryBytes} bytes (declared {entry.Length}, cap {limits.MaxEntryUncompressedBytes}); "
                                + $"total {written} (cap {limits.MaxTotalUncompressedBytes})";
                            break;
                        }

                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                    }
                }

                if (quotaFinding is not null)
                    return Failed(directory, quotaFinding, written, files, quotaDetail!);

                if (entryBytes != entry.Length)
                {
                    return Failed(
                        directory,
                        Finding(Error, ArchiveFindingCodes.EntrySizeMismatch, Display(entry.FullName), "Entry's unpacked size differs from what its header declares."),
                        written, files, $"declared {entry.Length}, actual {entryBytes}");
                }

                files++;
            }

            log.LogInformation(
                "Extracted {Files} file(s), {Bytes} bytes, into the import sandbox.", files, written);
            return new ArchiveExtraction(true, directory, [], written, files);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed(
                directory,
                Finding(Error, ArchiveFindingCodes.ExtractionTimeout, ArchivePath, "Unpacking the package did not finish in time."),
                written, files, $"timeout {limits.ExtractionTimeout}");
        }
        catch (OperationCanceledException)
        {
            Cleanup(directory);
            throw;
        }
        catch (InvalidDataException e)
        {
            log.LogWarning(e, "Extraction refused: an entry could not be read as declared.");
            return Failed(
                directory,
                Finding(Error, ArchiveFindingCodes.NotAnArchive, ArchivePath, "An entry could not be unpacked as its header describes it."),
                written, files, e.Message);
        }
        catch
        {
            Cleanup(directory);
            throw;
        }
    }

    // ── Per-entry verdict, shared by inspection and extraction ───────────

    private readonly record struct EntryVerdict(string Path, bool IsDirectory, PackageFinding? Finding);

    private EntryVerdict Examine(ZipArchiveEntry entry, ImportArchiveLimits limits, HashSet<string> seen)
    {
        var shown = Display(entry.FullName);

        var (path, isDirectory, pathFinding) = Canonicalise(entry.FullName);
        if (pathFinding is not null)
        {
            LogRefusal(pathFinding, $"raw name {shown}");
            return new EntryVerdict(string.Empty, false, pathFinding);
        }

        // A directory can also be declared by mode bits alone, on entries
        // some tools write without the trailing slash.
        isDirectory |= IsDirectoryByAttributes(entry) && entry.Length == 0;

        if (!seen.Add(path))
        {
            var duplicate = Finding(Error, ArchiveFindingCodes.PathInvalid, shown, "Entry name is a duplicate of another entry after normalisation.");
            LogRefusal(duplicate, $"normalised {path}");
            return new EntryVerdict(path, isDirectory, duplicate);
        }

        if (NonRegularReason(entry, isDirectory) is { } reason)
        {
            var finding = Finding(Error, ArchiveFindingCodes.EntryNotRegular, shown, $"Entry is not a regular file or folder ({reason}).");
            LogRefusal(finding, $"external attributes 0x{entry.ExternalAttributes:X8}");
            return new EntryVerdict(path, isDirectory, finding);
        }

        if (!isDirectory && ArchiveExtensions.Contains(Path.GetExtension(path)))
        {
            var finding = Finding(Error, ArchiveFindingCodes.NestedArchive, shown, "Archives inside the package are not accepted.");
            LogRefusal(finding, "nested archive by extension");
            return new EntryVerdict(path, isDirectory, finding);
        }

        if (entry.Length > limits.MaxEntryUncompressedBytes)
        {
            var finding = Finding(Error, ArchiveFindingCodes.EntryTooLarge, shown, "Entry's unpacked size is larger than is accepted.");
            LogRefusal(finding, $"declared {entry.Length}, cap {limits.MaxEntryUncompressedBytes}");
            return new EntryVerdict(path, isDirectory, finding);
        }

        if (ExceedsRatio(entry, limits.MaxCompressionRatio))
        {
            var finding = Finding(Error, ArchiveFindingCodes.CompressionRatio, shown, "Entry expands far more than a document or recording does.");
            LogRefusal(finding, $"compressed {entry.CompressedLength}, declared {entry.Length}, cap {limits.MaxCompressionRatio}:1");
            return new EntryVerdict(path, isDirectory, finding);
        }

        return new EntryVerdict(path, isDirectory, null);
    }

    /// <summary>
    /// Ratio guard. A zero compressed length with a non-zero declared length
    /// is an infinite ratio, which is the most suspicious value of all.
    /// </summary>
    private static bool ExceedsRatio(ZipArchiveEntry entry, int maxRatio)
    {
        if (entry.Length <= 0) return false;
        if (entry.CompressedLength <= 0) return true;
        return entry.Length > (long)maxRatio * entry.CompressedLength;
    }

    private static bool IsDirectoryByAttributes(ZipArchiveEntry entry)
    {
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        const int FileAttributeDirectory = 0x10;
        return unixType == 0x4000 || (entry.ExternalAttributes & FileAttributeDirectory) != 0;
    }

    /// <summary>
    /// Unix mode bits live in the high 16 bits of the external attributes
    /// when the entry was made on a Unix host; Windows attributes in the low
    /// bits. Anything that is not a plain file or a directory is refused —
    /// symlinks first, because a symlink entry is how a later write becomes
    /// an arbitrary-file write.
    /// </summary>
    private static string? NonRegularReason(ZipArchiveEntry entry, bool isDirectory)
    {
        var attributes = entry.ExternalAttributes;
        var unixType = (attributes >> 16) & 0xF000;

        if (unixType == 0xA000) return "symbolic link";
        if (unixType is not (0 or 0x8000 or 0x4000)) return "special file";

        const int FileAttributeDevice = 0x40;
        const int FileAttributeReparsePoint = 0x400;
        if ((attributes & FileAttributeReparsePoint) != 0) return "reparse point";
        if ((attributes & FileAttributeDevice) != 0) return "device";

        if (isDirectory && entry.Length > 0) return "folder entry carrying data";
        return null;
    }

    // ── Path canonicalisation ────────────────────────────────────────────

    /// <summary>
    /// Normalises separators and Unicode form, then refuses everything the
    /// spec's A1 and A4 name, and finally proves the result stays under a
    /// root by resolving it. The string checks are the cheap filter; the
    /// resolution is the check.
    /// </summary>
    private static (string Path, bool IsDirectory, PackageFinding? Finding) Canonicalise(string rawName)
    {
        var shown = Display(rawName);

        static (string, bool, PackageFinding?) Invalid(string shown, string why) =>
            (string.Empty, false, Finding(Error, ArchiveFindingCodes.PathInvalid, shown, why));
        static (string, bool, PackageFinding?) Escape(string shown, string why) =>
            (string.Empty, false, Finding(Error, ArchiveFindingCodes.PathEscape, shown, why));

        if (string.IsNullOrEmpty(rawName))
            return Invalid(shown, "Entry has an empty name.");

        if (HasForbiddenCharacters(rawName))
            return Invalid(shown, "Entry name contains control, invisible or malformed characters.");

        var name = rawName.Replace('\\', '/').Normalize(NormalizationForm.FormC);

        if (name.StartsWith('/'))
            return Escape(shown, "Entry name is an absolute path.");
        if (name.Length >= 2 && char.IsAsciiLetter(name[0]) && name[1] == ':')
            return Escape(shown, "Entry name starts with a drive letter.");

        var isDirectory = name.EndsWith('/');
        if (isDirectory) name = name[..^1];

        if (name.Length == 0)
            return Invalid(shown, "Entry has an empty name.");
        if (name.Length > MaxRelativePathLength)
            return Invalid(shown, "Entry name is too long.");

        foreach (var segment in name.Split('/'))
        {
            if (segment.Length == 0)
                return Invalid(shown, "Entry name contains an empty path segment.");
            if (segment == "..")
                return Escape(shown, "Entry name climbs out of its folder.");
            if (segment == ".")
                return Invalid(shown, "Entry name contains a '.' segment.");
            if (segment.Length > MaxSegmentLength)
                return Invalid(shown, "A folder or file name in the entry is too long.");
            if (segment[^1] is '.' or ' ')
                return Invalid(shown, "A name in the entry ends with a dot or a space.");

            var stem = segment.Split('.', 2)[0].TrimEnd();
            if (ReservedDeviceNames.Contains(stem))
                return Invalid(shown, "A name in the entry is a reserved device name.");

            foreach (var c in segment)
            {
                if (!IsAllowedPathCharacter(c))
                    return Invalid(shown, "Entry name contains a character that is not allowed in a file name.");
            }
        }

        // The resolution is the check. Everything above is a filter that makes
        // the message specific; this is what would still hold if a trick got
        // past every filter.
        var resolved = Path.GetFullPath(Path.Combine(ProbeRoot, name));
        if (!resolved.StartsWith(ProbeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Escape(shown, "Entry resolves outside the package.");

        var expected = ProbeRoot + Path.DirectorySeparatorChar
            + name.Replace('/', Path.DirectorySeparatorChar);
        if (!string.Equals(resolved, expected, StringComparison.Ordinal))
            return Invalid(shown, "Entry name changes when resolved to a file path.");

        return (name, isDirectory, null);
    }

    private static bool HasForbiddenCharacters(string name)
    {
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= name.Length || !char.IsLowSurrogate(name[i + 1])) return true;
                var category = CharUnicodeInfo.GetUnicodeCategory(char.ConvertToUtf32(c, name[i + 1]));
                if (IsForbiddenCategory(category)) return true;
                i++;
                continue;
            }

            if (char.IsLowSurrogate(c)) return true;
            if (IsForbiddenCategory(CharUnicodeInfo.GetUnicodeCategory(c))) return true;
        }

        return false;
    }

    private static bool IsForbiddenCategory(UnicodeCategory category) => category is
        UnicodeCategory.Control
        or UnicodeCategory.Format
        or UnicodeCategory.Surrogate
        or UnicodeCategory.PrivateUse
        or UnicodeCategory.OtherNotAssigned
        or UnicodeCategory.LineSeparator
        or UnicodeCategory.ParagraphSeparator;

    /// <summary>
    /// Allowlist by Unicode category, so Vietnamese names — including
    /// decomposed forms carrying combining marks — pass, while the Windows
    /// filename metacharacters and every invisible category do not.
    /// </summary>
    private static bool IsAllowedPathCharacter(char c)
    {
        if (c is '<' or '>' or ':' or '"' or '|' or '?' or '*' or '/' or '\\') return false;
        if (char.IsSurrogate(c)) return true; // pairs were validated in HasForbiddenCharacters

        return CharUnicodeInfo.GetUnicodeCategory(c) is
            UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber
            or UnicodeCategory.OtherNumber
            or UnicodeCategory.SpaceSeparator
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation
            or UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol
            or UnicodeCategory.ModifierSymbol
            or UnicodeCategory.OtherSymbol;
    }

    // ── Archive-level readers ────────────────────────────────────────────

    private static async Task<byte[]> ReadSignatureAsync(Stream stream, CancellationToken ct)
    {
        stream.Seek(0, SeekOrigin.Begin);
        var buffer = new byte[4];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0) break;
            total += read;
        }
        stream.Seek(0, SeekOrigin.Begin);
        return total == buffer.Length ? buffer : [];
    }

    /// <summary>
    /// The entry count from the end-of-central-directory record, or null when
    /// no such record exists. Returns 0xFFFF for ZIP64 archives, where the
    /// real count lives elsewhere and the driver's count is used instead.
    /// </summary>
    private static async Task<int?> ReadDeclaredEntryCountAsync(Stream stream, CancellationToken ct)
    {
        const int RecordLength = 22;
        const int MaxCommentLength = 0xFFFF;

        var length = stream.Length;
        if (length < RecordLength) return null;

        var tail = (int)Math.Min(length, RecordLength + MaxCommentLength);
        var buffer = new byte[tail];
        stream.Seek(length - tail, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(buffer, ct);

        for (var i = tail - RecordLength; i >= 0; i--)
        {
            if (buffer.AsSpan(i, 4).SequenceEqual(EndOfCentralDirectory))
                return BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(i + 10, 2));
        }

        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static PackageFinding Finding(string severity, string code, string path, string message) =>
        new(severity, code, path, message);

    private static ArchiveInspection Rejected(PackageFinding finding) =>
        new(false, [finding], PackageLayout.Empty);

    private ArchiveExtraction Failed(
        string directory, PackageFinding finding, long written, int files, string detail)
    {
        Cleanup(directory);
        LogRefusal(finding, detail);
        return new ArchiveExtraction(false, null, [finding], written, files);
    }

    private void Cleanup(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogError(e, "Import sandbox {Directory} could not be removed after a failed extraction.", directory);
        }
    }

    /// <summary>The specifics go to the log; the finding carries only the category.</summary>
    private void LogRefusal(PackageFinding finding, string detail) =>
        log.LogWarning(
            "Package refused: {Code} at {Path} — {Message} [{Detail}]",
            finding.Code, finding.Path, finding.Message, detail);

    /// <summary>
    /// An entry name as it may be shown to an administrator: invisible and
    /// control characters replaced, length bounded. The raw name is hostile
    /// input and never reaches a response verbatim.
    /// </summary>
    private static string Display(string raw)
    {
        const int MaxShown = 160;
        var builder = new StringBuilder(Math.Min(raw.Length, MaxShown) + 1);
        foreach (var c in raw)
        {
            if (builder.Length >= MaxShown) { builder.Append('…'); break; }
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            builder.Append(IsForbiddenCategory(category) && !char.IsSurrogate(c) ? '?' : c);
        }
        return builder.ToString();
    }

    /// <summary>
    /// A seekable view of the upload. A stream that can seek is used as is;
    /// one that cannot is spooled to a temporary file that deletes itself on
    /// close, with the archive byte cap enforced during the copy so that an
    /// oversized upload is refused before it has been stored whole.
    /// </summary>
    private sealed class ArchiveSource : IAsyncDisposable
    {
        private readonly bool ownsStream;

        private ArchiveSource(Stream stream, bool ownsStream, PackageFinding? finding)
        {
            Stream = stream;
            this.ownsStream = ownsStream;
            Finding = finding;
        }

        public Stream Stream { get; }
        public PackageFinding? Finding { get; }

        public static async Task<ArchiveSource> OpenAsync(Stream zip, long maxBytes, CancellationToken ct)
        {
            var tooLarge = Finding(Error, ArchiveFindingCodes.TooLarge, ArchivePath, "The upload is larger than is accepted.");

            if (zip.CanSeek)
            {
                zip.Seek(0, SeekOrigin.Begin);
                return new ArchiveSource(zip, ownsStream: false, zip.Length > maxBytes ? tooLarge : null);
            }

            var path = Path.Combine(Path.GetTempPath(), "vni-upload-" + Guid.NewGuid().ToString("n") + ".zip");
            var spool = new FileStream(
                path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                CopyBufferSize, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

            try
            {
                var buffer = new byte[CopyBufferSize];
                long copied = 0;
                int read;
                while ((read = await zip.ReadAsync(buffer, ct)) > 0)
                {
                    copied += read;
                    if (copied > maxBytes)
                        return new ArchiveSource(spool, ownsStream: true, tooLarge);
                    await spool.WriteAsync(buffer.AsMemory(0, read), ct);
                }

                spool.Seek(0, SeekOrigin.Begin);
                return new ArchiveSource(spool, ownsStream: true, null);
            }
            catch
            {
                await spool.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (ownsStream) await Stream.DisposeAsync();
        }
    }
}
