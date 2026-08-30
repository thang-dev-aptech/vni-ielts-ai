using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>Application-facing adapter over the package reader's schema and invariant checks.</summary>
public sealed class ExamPackageValidator(ExamPackageReader reader) : IExamPackageValidator
{
    public PackageValidationResult Validate(
        string packageJson, ExamDefinitionId definitionId, int versionNumber)
    {
        var result = reader.Read(packageJson, definitionId, versionNumber);
        return new PackageValidationResult(
            result.IsValid,
            result.Version,
            result.Findings.Select(f =>
                new PackageFinding(f.Severity, f.Code, f.Path, f.Message)).ToArray());
    }
}

