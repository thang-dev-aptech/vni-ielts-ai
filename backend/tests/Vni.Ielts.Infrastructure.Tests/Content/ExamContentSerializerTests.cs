using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

public sealed class ExamContentSerializerTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly ExamPackageReader Reader =
        ExamPackageReader.FromSchemaFile(Path.Combine(RepoRoot, "contracts/schemas/exam.schema.json"));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void A_saved_version_serializes_back_through_the_same_reader()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content", "valid-exam.json"));
        var accepted = Reader.Read(json, ExamDefinitionId.New(), 1);
        Assert.True(accepted.IsValid, string.Join("; ", accepted.Findings.Select(f => f.Message)));

        var roundTrip = Reader.Read(
            ExamContentSerializer.ToJson(accepted.Version!), accepted.Version!.DefinitionId, 1);

        Assert.True(roundTrip.IsValid,
            "Serialized content rejected: " + string.Join("; ", roundTrip.Findings.Select(f => $"{f.Path} {f.Message}")));
        Assert.Equal(accepted.Version.Title, roundTrip.Version!.Title);
        Assert.Equal(accepted.Version.Sections[0].AutoScoredMarks, roundTrip.Version.Sections[0].AutoScoredMarks);
    }

    [Fact]
    public void A_blank_draft_serializes_without_inventing_timing_or_band_tables()
    {
        var blank = ExamVersion.CreateBlankDraft(
            ExamDefinitionId.New(), 1, "Empty", ExamVariant.Academic, UserId.New());

        var node = ExamContentSerializer.ToNode(blank);
        Assert.Equal("1.0", node["formatVersion"]!.GetValue<string>());
        Assert.Empty(node["sections"]!.AsArray());
        Assert.Empty(node["timingProfile"]!["sections"]!.AsObject());
        Assert.Empty(node["scoringProfile"]!["rawToBand"]!.AsObject());
        Assert.Null(node["scoringProfile"]!["moduleScore"]);
    }
}
