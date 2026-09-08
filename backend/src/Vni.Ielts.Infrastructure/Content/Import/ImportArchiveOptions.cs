using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Infrastructure.Content.Import;

/// <summary>
/// <c>Import:Archive</c> — the caps applied to an uploaded exam package before
/// anything is extracted. A configured seam: the numbers live in
/// <c>appsettings.json</c> where an operator can change them without a deploy,
/// and are restated here so a host without the section gets the same values.
///
/// <b>[QUYẾT ĐỊNH kỹ thuật, 2026-09-07]</b> — starting values, and why:
///
/// <list type="bullet">
/// <item><b>MaxEntries 2,000.</b> A four-skill package is tens of files, not
/// thousands; the spec's own starting figure (5,000) was written before the
/// folder layout was decided. Many-small-files is a denial-of-service shape,
/// not a content shape.</item>
/// <item><b>MaxTotalUncompressedBytes 512 MB.</b> One Cambridge book runs to
/// ~250 MB of PDF and audio; a package is one test, not one book. Half a
/// gigabyte is room for a full test with lossless audio and no more.</item>
/// <item><b>MaxEntryUncompressedBytes 200 MB.</b> The largest legitimate
/// single entry is a Listening recording or a scanned PDF. Matches the
/// operator CLI's per-source cap in <see cref="SourceExtractionLimits.Operator"/>
/// so a package the CLI can read is not refused here on size alone.</item>
/// <item><b>MaxCompressionRatio 100.</b> The spec's figure. Text compresses
/// 3–5:1, media barely at all; a hundredfold expansion inside one entry is
/// a bomb, not a transcript.</item>
/// <item><b>MaxArchiveBytes 300 MB.</b> The compressed upload itself. Set
/// above the total-uncompressed cap divided by a realistic ratio so that
/// media-heavy packages, which do not compress, still fit.</item>
/// <item><b>ExtractionTimeoutSeconds 120.</b> Writing half a gigabyte to local
/// disk takes seconds; two minutes is the ceiling past which the process is
/// being starved, not working.</item>
/// </list>
///
/// The cost of being wrong is one refused package and one setting change.
/// None of these values ever appears in a finding returned to the caller.
/// </summary>
public sealed class ImportArchiveOptions
{
    public const string SectionName = "Import:Archive";

    public int MaxEntries { get; set; } = 2_000;
    public long MaxTotalUncompressedBytes { get; set; } = 512L * 1024 * 1024;
    public long MaxEntryUncompressedBytes { get; set; } = 200L * 1024 * 1024;
    public int MaxCompressionRatio { get; set; } = 100;
    public long MaxArchiveBytes { get; set; } = 300L * 1024 * 1024;
    public int ExtractionTimeoutSeconds { get; set; } = 120;

    public ImportArchiveLimits ToLimits() => new(
        MaxEntries,
        MaxTotalUncompressedBytes,
        MaxEntryUncompressedBytes,
        MaxCompressionRatio,
        MaxArchiveBytes,
        TimeSpan.FromSeconds(ExtractionTimeoutSeconds));

    /// <summary>
    /// Every way these caps can be nonsensical, so the startup gate can report
    /// them all at once. Empty when the configuration can work.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        if (MaxEntries <= 0)
            problems.Add($"{SectionName}:MaxEntries is {MaxEntries}. No package could contain a file.");
        if (MaxTotalUncompressedBytes <= 0)
            problems.Add($"{SectionName}:MaxTotalUncompressedBytes is {MaxTotalUncompressedBytes}. Nothing could be extracted.");
        if (MaxEntryUncompressedBytes <= 0)
            problems.Add($"{SectionName}:MaxEntryUncompressedBytes is {MaxEntryUncompressedBytes}. No entry could be extracted.");
        if (MaxEntryUncompressedBytes > MaxTotalUncompressedBytes && MaxTotalUncompressedBytes > 0)
        {
            problems.Add(
                $"{SectionName}:MaxEntryUncompressedBytes ({MaxEntryUncompressedBytes}) exceeds "
                + $"MaxTotalUncompressedBytes ({MaxTotalUncompressedBytes}). The per-entry cap can never be reached.");
        }
        if (MaxCompressionRatio <= 0)
            problems.Add($"{SectionName}:MaxCompressionRatio is {MaxCompressionRatio}. Every compressed entry would be refused.");
        if (MaxArchiveBytes <= 0)
            problems.Add($"{SectionName}:MaxArchiveBytes is {MaxArchiveBytes}. No upload could be accepted.");
        if (ExtractionTimeoutSeconds <= 0)
        {
            problems.Add(
                $"{SectionName}:ExtractionTimeoutSeconds is {ExtractionTimeoutSeconds}. Zero times out "
                + "before the first entry; negative crashes the timer.");
        }

        return problems;
    }
}
