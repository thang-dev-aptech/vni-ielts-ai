using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

/// <summary>
/// Domain to document and back for <see cref="ExamPackage"/>.
///
/// Every <c>DateTimeOffset</c> crosses explicitly, same rule as
/// <c>ExamMappers</c> — a BSON date read back as <c>Unspecified</c> kind
/// silently shifts by the server's timezone.
/// </summary>
internal static class ExamPackageMappers
{
    private static DateTime Utc(DateTimeOffset value) => value.UtcDateTime;

    private static DateTimeOffset Offset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static ExamPackageDocument ToDocument(this ExamPackage package) => new()
    {
        Id = package.Id,
        SourceKind = package.SourceKind.ToString(),
        UploadedBy = package.UploadedBy.Value,
        Sha256 = package.Sha256,
        FileName = package.FileName,
        UploadRef = package.UploadRef,
        Status = package.Status.ToString(),
        Findings = [.. package.Findings.Select(f => new PackageFindingDocument
        {
            Stage = f.Stage,
            Code = f.Code,
            Pointer = f.Pointer,
            Message = f.Message,
        })],
        Entries = [.. package.Entries.Select(e => new ExamPackageEntryDocument
        {
            ProposedDefinitionId = e.ProposedDefinitionId,
            Title = e.Title,
            Module = e.Module.ToString(),
            QuestionCount = e.QuestionCount,
        })],
        CreatedVersionIds = [.. package.CreatedVersionIds],
        Version = package.Version,
        UploadPurged = package.UploadPurged,
        UploadPurgeState = package.UploadPurgeState,
        UploadPurgeClaimId = package.UploadPurgeClaimId,
        UploadPurgeClaimedAt = package.UploadPurgeClaimedAt?.UtcDateTime,
        CreatedAt = Utc(package.CreatedAt),
        UpdatedAt = Utc(package.UpdatedAt),
    };

    public static ExamPackage ToDomain(this ExamPackageDocument document) => ExamPackage.Rehydrate(
        document.Id,
        Enum.Parse<ExamPackageSourceKind>(document.SourceKind),
        new UserId(document.UploadedBy),
        document.Sha256,
        document.FileName,
        document.UploadRef,
        Enum.Parse<PackageImportStatus>(document.Status),
        document.Findings.Select(f => new PackageFinding(f.Stage, f.Code, f.Pointer, f.Message)),
        document.Entries.Select(e => new ExamPackageEntry(
            e.ProposedDefinitionId, e.Title, Enum.Parse<ExamModule>(e.Module), e.QuestionCount)),
        document.CreatedVersionIds,
        document.Version,
        document.UploadPurged,
        Offset(document.CreatedAt),
        Offset(document.UpdatedAt),
        document.UploadPurgeState,
        document.UploadPurgeClaimId,
        document.UploadPurgeClaimedAt is { } claimedAt ? Offset(claimedAt) : null);
}
