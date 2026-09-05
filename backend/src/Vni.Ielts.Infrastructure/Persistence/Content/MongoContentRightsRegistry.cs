using MongoDB.Driver;
using Vni.Ielts.Application.Content;
using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Content;

internal sealed class MongoContentRightsRegistry(MongoContext context) : IContentRightsRegistry
{
    public async Task<ContentSource?> FindAsync(ContentSourceId id, CancellationToken ct)
    {
        var doc = await context.ContentSources
            .Find(s => s.Id == id.Value)
            .FirstOrDefaultAsync(ct);

        return doc?.ToDomain();
    }

    /// <summary>
    /// The read the publish gate makes.
    ///
    /// <b>Either binding matches.</b> A version id follows the content — the
    /// seeder derives it from a fingerprint — so a paper that was corrected
    /// carries a version id the registry has never seen. The definition id
    /// does not move, which is what keeps the record attached to its material
    /// across an edit.
    /// </summary>
    public async Task<ContentSource?> FindForExamAsync(
        ExamVersionId examVersionId, ExamDefinitionId examDefinitionId, CancellationToken ct)
    {
        var filter = Builders<ContentSourceDocument>.Filter.Or(
            Builders<ContentSourceDocument>.Filter.AnyEq(s => s.ExamVersionIds, examVersionId.Value),
            Builders<ContentSourceDocument>.Filter.AnyEq(
                s => s.ExamDefinitionIds, examDefinitionId.Value));

        var doc = await context.ContentSources.Find(filter).FirstOrDefaultAsync(ct);

        // No document is a normal answer, and it means no rights. The caller
        // hands `null` straight to `ContentRightsPolicy`, which refuses.
        return doc?.ToDomain();
    }

    public async Task<IReadOnlyList<ContentSource>> ListAsync(CancellationToken ct)
    {
        var docs = await context.ContentSources
            .Find(Builders<ContentSourceDocument>.Filter.Empty)
            .SortBy(s => s.Id)
            .ToListAsync(ct);

        return [.. docs.Select(d => d.ToDomain())];
    }

    /// <summary>
    /// Inserts only when the id is free.
    ///
    /// <para>
    /// <b>Deliberately not an upsert, and the difference is a safety
    /// property.</b> A rights grant is an act by a named reviewer. If a
    /// deployment rewrote existing records from the seed, then the day
    /// <c>M-53</c> is answered and an operator grants a paper its
    /// <c>learner-production</c> right in the CMS, the next restart would
    /// silently revoke it — and the failure would look like "publishing
    /// randomly stopped working". Seeding fills gaps; it never overwrites a
    /// decision.
    /// </para>
    ///
    /// <para>
    /// Several API instances start at once and all of them run the seed, so
    /// losing the check-then-insert race is the expected outcome rather than
    /// an error. The unique <c>_id</c> is the guarantee.
    /// </para>
    /// </summary>
    public async Task<bool> RegisterIfAbsentAsync(ContentSource source, CancellationToken ct)
    {
        try
        {
            await context.ContentSources.InsertOneAsync(source.ToDocument(), null, ct);
            return true;
        }
        catch (MongoWriteException e)
            when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }
}
