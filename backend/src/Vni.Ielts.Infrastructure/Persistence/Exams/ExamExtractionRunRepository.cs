using MongoDB.Driver;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

/// <summary>
/// Append-and-replace storage for extraction run records.
///
/// <para>
/// <b>Upsert rather than insert, keyed by package and provider request.</b> The
/// worker that writes this record has already made the provider call, so a
/// crash between the call and the write must not produce two records for one
/// call when the work is redelivered. There is no version and no
/// compare-and-swap: nothing edits a run afterwards, which is what makes it an
/// audit fact rather than a document.
/// </para>
/// </summary>
internal sealed class MongoExamExtractionRunStore(MongoContext context) : IExamExtractionRunStore
{
    public async Task RecordAsync(ExamExtractionRunMetadata run, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);

        var document = run.ToDocument();

        await context.ExamExtractionRuns.ReplaceOneAsync(
            stored => stored.Id == document.Id,
            document,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }
}
