using System.Text.Json.Nodes;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

/// <summary>
/// The paper's own description, from the package file to the thing a learner
/// reads on a card.
///
/// ── Why this needed a test rather than a glance ───────────────────────────
///
/// <b><c>description</c> was in the schema, and in every shipped fixture, from
/// the day the format was written — and the reader dropped it.</b> Nothing
/// failed. The JSON validated, the exam imported, the catalogue listed it, and
/// the sentence an author wrote to tell one paper from another simply never
/// left the file. A field that is parsed by nobody is indistinguishable from a
/// field that is absent, right up until sixteen papers called "VOL 9 Test 3"
/// and "VOL 9 Test 4" arrive and a learner has to pick one.
///
/// So these tests pin the whole run: the reader keeps it, blank counts as
/// absent, and the two papers actually on the shelf carry one.
/// </summary>
public sealed class ExamCatalogueDescriptionTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly ExamPackageReader Reader = ExamPackageReader.FromSchemaFile(
        Path.Combine(RepoRoot, "contracts", "schemas", "exam.schema.json"));

    private static string ValidExamJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content", "valid-exam.json"));

    [Fact]
    public void A_description_in_the_package_reaches_the_exam_version()
    {
        const string written = "Đề lẻ, chỉ có Reading. Chấm ngay theo đáp án.";

        var version = ReadWithDescription(JsonValue.Create(written));

        Assert.Equal(written, version.Description);
    }

    /// <summary>
    /// <b>Absent, empty and whitespace-only are one state, normalised once.</b>
    /// A package carrying <c>" "</c> should render as a card with no
    /// description, not as a blank line under the title — and deciding that at
    /// each of the four places that read the field is how three of them end up
    /// deciding differently.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void Blank_text_is_the_same_as_no_description(string blank)
    {
        Assert.Null(ReadWithDescription(JsonValue.Create(blank)).Description);
    }

    [Fact]
    public void A_package_with_no_description_field_reads_as_null()
    {
        Assert.Null(ReadWithDescription(null).Description);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        Assert.Equal("Đề 1", ReadWithDescription(JsonValue.Create("  Đề 1  ")).Description);
    }

    /// <summary>
    /// <b>The two papers on the shelf, checked as content rather than as
    /// code.</b> The categories a learner picks from — a full four-skill paper
    /// and a single-skill one — are derived from which modules a version
    /// carries, so a fixture losing a module silently moves between categories.
    /// This asserts the shape of what actually ships, and that each paper says
    /// what it is.
    /// </summary>
    // SkippableTheory, not Theory: `Skip.IfNot` below throws SkipException,
    // which a plain Theory reports as a failure. Seen when cam17-test-3/4 were
    // listed here before the batch had produced them.
    [SkippableTheory]
    [InlineData("exam-1.json", 4)]
    [InlineData("vol9-test-1.json", 2)]
    [InlineData("cam17-test-1.json", 2)]
    [InlineData("cam17-test-2.json", 2)]
    [InlineData("cam17-test-3.json", 2)]
    [InlineData("cam17-test-4.json", 2)]
    // Cam 21 ships Reading only: the book's audio is not on disk, and a
    // Listening section nobody can hear is a paper nobody can sit.
    [InlineData("cam21-test-1.json", 1)]
    [InlineData("cam21-test-2.json", 1)]
    [InlineData("cam21-test-3.json", 1)]
    [InlineData("cam21-test-4.json", 1)]
    [InlineData("cam16-test-1.json", 2)]
    [InlineData("cam16-test-2.json", 2)]
    [InlineData("cam16-test-3.json", 2)]
    [InlineData("cam16-test-4.json", 2)]
    [InlineData("cam18-test-1.json", 2)]
    [InlineData("cam18-test-2.json", 2)]
    [InlineData("cam18-test-3.json", 2)]
    [InlineData("cam18-test-4.json", 2)]
    [InlineData("cam19-test-1.json", 2)]
    [InlineData("cam19-test-2.json", 2)]
    [InlineData("cam19-test-3.json", 2)]
    [InlineData("cam19-test-4.json", 2)]
    public void Every_shipped_paper_states_what_it_is(string fixture, int expectedModules)
    {
        var path = Path.Combine(RepoRoot, "fixtures", "exams", fixture);
        Skip.IfNot(File.Exists(path), $"{fixture} is not present in this checkout.");

        var result = Reader.Read(File.ReadAllText(path), ExamDefinitionId.New(), 1);

        Assert.True(result.IsValid, Findings(result));
        Assert.NotNull(result.Version);

        // `expectedModules` is what the book offers. A test whose Listening has
        // not cleared the key gate ships Reading-only for now — one section,
        // and a description that says so, rather than a Listening nobody can
        // sit (Cam 18 T2, Cam 19 T2 on 2026-09-04).
        Assert.InRange(result.Version.Sections.Count, 1, expectedModules);
        if (result.Version.Sections.Count < expectedModules)
        {
            Assert.Contains("Reading", result.Version.Description ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(
                result.Version.Sections, s => s.Module == Vni.Ielts.Domain.Exams.ExamModule.Listening);
        }

        Assert.False(
            string.IsNullOrWhiteSpace(result.Version.Description),
            $"{fixture} has no description. A card for it would show a title and a question count, "
            + "which is not enough to choose between papers whose titles differ by one digit.");
    }

    private static ExamVersion ReadWithDescription(JsonNode? description)
    {
        var package = JsonNode.Parse(ValidExamJson())!.AsObject();

        if (description is null) package.Remove("description");
        else package["description"] = description;

        var result = Reader.Read(package.ToJsonString(), ExamDefinitionId.New(), 1);

        Assert.True(result.IsValid, Findings(result));
        Assert.NotNull(result.Version);

        return result.Version;
    }

    private static string Findings(ExamPackageResult result) =>
        string.Join(" | ", result.Findings.Select(f => $"{f.Code} {f.Path}: {f.Message}"));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "contracts", "schemas")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test binary.");
    }
}
