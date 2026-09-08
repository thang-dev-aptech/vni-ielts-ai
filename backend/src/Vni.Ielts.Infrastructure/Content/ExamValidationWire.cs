using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// One wire shape for every finding the authoring checklist and the package
/// import pipeline produce from <see cref="ExamPackageReader"/>.
///
/// Import already maps reader findings to stage <c>schema</c>
/// (<c>PackageStructuralValidator.ToPackageFinding</c>). The CMS checklist
/// used to stamp <c>structural</c> instead, so the same invalid JSON showed
/// two codes depending on which door it entered. → Phase 3 Plan 02
/// </summary>
public static class ExamValidationWire
{
    public const string SchemaStage = "schema";

    public static PackageFinding ToPackageFinding(ValidationFinding finding) =>
        new(SchemaStage, finding.Code, finding.Path, finding.Message);

    public static object ToChecklist(ValidationFinding finding) => new
    {
        stage = SchemaStage,
        code = finding.Code,
        pointer = finding.Path,
        message = finding.Message,
    };
}
