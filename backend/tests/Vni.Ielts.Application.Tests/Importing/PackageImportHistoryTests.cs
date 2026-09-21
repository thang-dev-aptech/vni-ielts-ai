using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Application.Tests.Importing;

public sealed class PackageImportHistoryTests
{
    [Theory]
    [InlineData("../../etc/passwd.zip", "passwd.zip")]
    [InlineData(@"..\\..\\evil.zip", "evil.zip")]
    [InlineData("..\\..\\evil.zip", "evil.zip")]
    [InlineData("/tmp/nested/paper.zip", "paper.zip")]
    [InlineData("C:\\Users\\x\\paper.zip", "paper.zip")]
    [InlineData("plain.zip", "plain.zip")]
    [InlineData("", "unnamed.zip")]
    [InlineData("   ", "unnamed.zip")]
    [InlineData("...", "unnamed.zip")]
    public void A_malicious_filename_is_reduced_to_its_leaf(string raw, string expected) =>
        Assert.Equal(expected, PackageImportHistoryBounds.SanitizeFileName(raw));

    [Fact]
    public void Control_characters_in_a_filename_are_stripped()
    {
        var raw = "file\0name\n.zip";
        var cleaned = PackageImportHistoryBounds.SanitizeFileName(raw);

        Assert.Equal("file_name_.zip", cleaned);
        Assert.DoesNotContain('\0', cleaned);
        Assert.DoesNotContain('\n', cleaned);
    }

    [Fact]
    public void A_filename_longer_than_the_bound_is_truncated()
    {
        var raw = new string('a', PackageImportHistoryBounds.MaxFileNameChars + 40) + ".zip";
        var cleaned = PackageImportHistoryBounds.SanitizeFileName(raw);

        Assert.Equal(PackageImportHistoryBounds.MaxFileNameChars, cleaned.Length);
        Assert.StartsWith("aaa", cleaned);
    }

    [Fact]
    public void Findings_are_capped_in_count_and_in_each_string()
    {
        var findings = Enumerable.Range(0, PackageImportHistoryBounds.MaxFindings + 7)
            .Select(i => new PackageFinding(
                "error",
                new string('C', PackageImportHistoryBounds.MaxFindingCodeChars + 8),
                new string('P', PackageImportHistoryBounds.MaxFindingPathChars + 8),
                new string('M', PackageImportHistoryBounds.MaxFindingMessageChars + 8)))
            .ToArray();

        var bounded = PackageImportHistoryBounds.BoundFindings(findings);

        Assert.Equal(PackageImportHistoryBounds.MaxFindings, bounded.Count);
        Assert.All(bounded, f =>
        {
            Assert.Equal(PackageImportHistoryBounds.MaxFindingCodeChars, f.Code.Length);
            Assert.Equal(PackageImportHistoryBounds.MaxFindingPathChars, f.Path.Length);
            Assert.Equal(PackageImportHistoryBounds.MaxFindingMessageChars, f.Message.Length);
        });
    }

    [Fact]
    public void A_null_finding_list_becomes_empty_rather_than_null() =>
        Assert.Empty(PackageImportHistoryBounds.BoundFindings(null));
}
