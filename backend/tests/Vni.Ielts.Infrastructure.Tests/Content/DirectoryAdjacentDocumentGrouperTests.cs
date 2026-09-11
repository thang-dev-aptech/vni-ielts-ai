using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

public sealed class DirectoryAdjacentDocumentGrouperTests
{
    private readonly DirectoryAdjacentDocumentGrouper _grouper = new();

    [Fact]
    public void Groups_only_by_exact_parent_directory_in_deterministic_order()
    {
        var proposal = _grouper.Propose([
            Document("package-1", "LISTENING/TEST 1-L.docx"),
            Document("package-1", "READING/TEST 2-R.docx"),
            Document("package-1", "READING/TEST 1-R.docx"),
            Document("package-1", "instructions.pdf"),
        ]);

        Assert.True(proposal.NeedsReview);
        Assert.Equal(0, proposal.Version);
        Assert.Equal(
            ["directory-1", "directory-2", "directory-3"],
            proposal.Groups.Select(group => group.Id));
        Assert.Collection(proposal.Groups,
            group =>
            {
                Assert.Null(group.DirectoryPath);
                Assert.Equal(["instructions.pdf"], group.EntryPaths);
            },
            group =>
            {
                Assert.Equal("LISTENING", group.DirectoryPath);
                Assert.Equal(["LISTENING/TEST 1-L.docx"], group.EntryPaths);
            },
            group =>
            {
                Assert.Equal("READING", group.DirectoryPath);
                Assert.Equal(
                    ["READING/TEST 1-R.docx", "READING/TEST 2-R.docx"],
                    group.EntryPaths);
            });
    }

    [Fact]
    public void Rejects_documents_from_multiple_packages()
    {
        var exception = Assert.Throws<InvalidDataException>(() => _grouper.Propose([
            Document("package-1", "READING/test.docx"),
            Document("package-2", "READING/test.docx"),
        ]));

        Assert.Contains("same package", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Proposal_model_has_no_exam_classification_field()
    {
        Assert.DoesNotContain(
            typeof(SourceDocumentGroupingProposal).GetProperties(),
            property => property.PropertyType == typeof(ParsedExamClassification)
                || property.Name.Contains("module", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("classification", StringComparison.OrdinalIgnoreCase));
    }

    private static ExtractedSourceDocument Document(string packageId, string path) => new(
        packageId,
        path,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "sha256",
        DocumentExtractionOutcome.Extracted,
        [new ExtractedTextChunk("paragraph-1", 1, "Synthetic", null, "paragraph")]);
}
