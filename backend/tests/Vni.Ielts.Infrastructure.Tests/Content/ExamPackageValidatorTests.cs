using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

public sealed class ExamPackageValidatorTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly IExamPackageValidator Validator = new ExamPackageValidator(
        ExamPackageReader.FromSchemaFile(
            Path.Combine(RepoRoot, "contracts", "schemas", "exam.schema.json")));

    [Fact]
    public void Application_port_uses_the_real_deterministic_reader()
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Content", "valid-exam.json"));

        var result = Validator.Validate(json, ExamDefinitionId.New(), 1);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Version);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Malformed_parser_output_is_rejected_through_the_application_port()
    {
        var result = Validator.Validate("{not-json", ExamDefinitionId.New(), 1);

        Assert.False(result.IsValid);
        Assert.Null(result.Version);
        Assert.Equal("MALFORMED_JSON", Assert.Single(result.Findings).Code);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
