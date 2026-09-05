using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Content;

/// <summary>
/// Where content rights records are kept.
///
/// <b>The interface lives here and every implementation lives in
/// Infrastructure.</b> That is the one boundary ADR-0004 asks for, and it is
/// what makes the MongoDB → PostgreSQL move a rewrite of one project.
/// </summary>
public interface IContentRightsRegistry
{
    Task<ContentSource?> FindAsync(ContentSourceId id, CancellationToken ct);

    /// <summary>
    /// The source an exam was built from, or <c>null</c>.
    ///
    /// <b>Both ids, because a version id follows the content.</b> The seeder
    /// derives a version id from a fingerprint of the paper, so a corrected
    /// typo mints a new one; the definition id survives that. A record bound
    /// to either matches.
    ///
    /// <b><c>null</c> is a normal answer, not an error.</b> An exam that
    /// reached the catalogue by a route the registry does not know about
    /// resolves to nothing here — and nothing is refused. → <see cref="ContentPublishGuard"/>
    /// </summary>
    Task<ContentSource?> FindForExamAsync(
        ExamVersionId examVersionId, ExamDefinitionId examDefinitionId, CancellationToken ct);

    Task<IReadOnlyList<ContentSource>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Stores a record only if that id is not already present, and reports
    /// whether it inserted.
    ///
    /// <b>Deliberately not an upsert.</b> A rights grant is an act by a named
    /// reviewer; a deployment that rewrote existing records from a seed could
    /// silently revoke — or silently restore — a decision somebody made in the
    /// CMS. Seeding fills gaps and never overwrites.
    /// </summary>
    Task<bool> RegisterIfAbsentAsync(ContentSource source, CancellationToken ct);
}

/// <summary>
/// Reads one recorded file and reports what is actually there.
///
/// Separate from the registry because it touches the filesystem, and because
/// the answer "not present" is the normal one: the material is gitignored, so
/// a clean checkout and every CI run see none of it.
/// </summary>
public interface IContentFileProbe
{
    Task<ContentFileObservation> ObserveAsync(string relativePath, CancellationToken ct);
}
