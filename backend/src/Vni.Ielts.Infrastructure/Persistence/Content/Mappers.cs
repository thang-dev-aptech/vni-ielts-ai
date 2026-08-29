using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Content;

/// <summary>
/// Hand-written both ways, like every other mapper here.
///
/// <b>An unreadable environment name is dropped, not guessed.</b> A stored
/// value this build does not recognise cannot be turned into a right without
/// inventing one, and the safe direction is to have fewer rights rather than
/// more. Dropping it makes the record more restrictive; mapping it to a
/// default would eventually make one less so.
/// </summary>
internal static class ContentRightsMappers
{
    public static ContentSourceDocument ToDocument(this ContentSource source) => new()
    {
        Id = source.Id.Value,
        Title = source.Title,
        Owner = source.Owner,
        Proof = source.Proof is { } proof
            ? new RightsProofDocument
            {
                Reference = proof.Reference,
                Reviewer = proof.Reviewer,
                ReviewedAt = proof.ReviewedAt.UtcDateTime,
            }
            : null,
        AllowedEnvironments = [.. source.AllowedEnvironments.Select(e => e.ToString())],
        ExpiresAt = source.ExpiresAt?.UtcDateTime,
        RootPath = source.RootPath,
        Files =
        [
            .. source.Files.Select(f => new ContentFileDocument
            {
                Path = f.RelativePath,
                Sha256 = f.Sha256,
                SizeBytes = f.SizeBytes,
            }),
        ],
        ExamVersionIds = [.. source.BoundExamVersionIds],
        ExamDefinitionIds = [.. source.BoundExamDefinitionIds],
    };

    public static ContentSource ToDomain(this ContentSourceDocument document)
    {
        var environments = document.AllowedEnvironments
            .Select(name => Enum.TryParse<ContentEnvironment>(name, out var parsed)
                ? (ContentEnvironment?)parsed
                : null)
            .OfType<ContentEnvironment>()
            .ToArray();

        // Rehydrate, not Register: a stored grant with no proof behind it must
        // not take the whole listing down. `ContentRightsPolicy` refuses it
        // instead, which costs one refusal rather than an outage.
        return ContentSource.Rehydrate(
            new ContentSourceId(document.Id),
            document.Title,
            document.Owner,
            document.Proof is { } proof
                ? new RightsProof(
                    proof.Reference, proof.Reviewer,
                    new DateTimeOffset(DateTime.SpecifyKind(proof.ReviewedAt, DateTimeKind.Utc)))
                : null,
            environments,
            document.ExpiresAt is { } expires
                ? new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc))
                : null,
            document.RootPath,
            [.. document.Files.Select(f => new ContentFileRef(f.Path, f.Sha256, f.SizeBytes))],
            [.. document.ExamVersionIds.Select(id => new ExamVersionId(id))],
            [.. document.ExamDefinitionIds.Select(id => new ExamDefinitionId(id))]);
    }
}
