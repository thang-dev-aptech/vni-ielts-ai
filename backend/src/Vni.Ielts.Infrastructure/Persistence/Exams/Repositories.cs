using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

internal sealed class MongoExamCatalogue(MongoContext context) : IExamCatalogue
{
    public async Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct)
    {
        // Filtered in the query, not in memory. A draft is content nobody has
        // reviewed; letting it reach a listing and relying on the caller to
        // skip it is one forgotten `.Where` away from a learner sitting it.
        var docs = await context.ExamVersions
            .Find(v => v.Status == nameof(ExamVersionStatus.Published))
            .SortBy(v => v.Title)
            .ToListAsync(ct);

        return [.. docs.Select(d => d.ToDomain())];
    }

    public async Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct)
    {
        var docs = await context.ExamVersions
            .Find(Builders<ExamVersionDocument>.Filter.Empty)
            .SortBy(v => v.Title)
            .ToListAsync(ct);

        return [.. docs.Select(d => d.ToDomain())];
    }

    public async Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct)
    {
        var doc = await context.ExamVersions.Find(v => v.Id == id.Value).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public Task UpsertAsync(ExamVersion version, CancellationToken ct) =>
        context.ExamVersions.ReplaceOneAsync(
            v => v.Id == version.Id.Value,
            version.ToDocument(),
            new ReplaceOptions { IsUpsert = true },
            ct);
}

internal sealed class MongoExamSessionRepository(MongoContext context) : IExamSessionRepository
{
    public async Task<ExamSession?> FindAsync(ExamSessionId id, CancellationToken ct)
    {
        var doc = await context.ExamSessions.Find(s => s.Id == id.Value).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<ExamSession?> FindOpenForUserAsync(UserId userId, CancellationToken ct)
    {
        var doc = await context.ExamSessions
            .Find(s => s.UserId == userId.Value && s.Status == nameof(SessionStatus.InProgress))
            .SortByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

        return doc?.ToDomain();
    }

    public async Task<IReadOnlyList<ExamSession>> ListForUserAsync(
        UserId userId, int limit, CancellationToken ct)
    {
        var docs = await context.ExamSessions
            .Find(s => s.UserId == userId.Value)
            .SortByDescending(s => s.StartedAt)
            .Limit(limit)
            .ToListAsync(ct);

        return [.. docs.Select(d => d.ToDomain())];
    }

    public Task AddAsync(ExamSession session, CancellationToken ct) =>
        context.ExamSessions.InsertOneAsync(session.ToDocument(), cancellationToken: ct);

    public Task SaveAsync(ExamSession session, CancellationToken ct) =>
        context.ExamSessions.ReplaceOneAsync(
            s => s.Id == session.Id.Value, session.ToDocument(), cancellationToken: ct);
}

internal sealed class MongoAnswerSheetStore(MongoContext context) : IAnswerSheetStore
{
    private static string KeyFor(ExamSessionId sessionId, ExamModule module) =>
        $"{sessionId.Value}:{module}";

    public async Task<IReadOnlyDictionary<string, string?>> LoadAsync(
        ExamSessionId sessionId, ExamModule module, CancellationToken ct)
    {
        var id = KeyFor(sessionId, module);
        var doc = await context.AnswerSheets.Find(a => a.Id == id).FirstOrDefaultAsync(ct);

        if (doc is null) return new Dictionary<string, string?>();

        // Last write wins on a duplicate question id. A sheet should never
        // contain one, but a corrupt document should not throw and lock the
        // learner out of their own exam.
        var answers = new Dictionary<string, string?>();
        foreach (var entry in doc.Answers) answers[entry.QuestionId] = entry.Value;
        return answers;
    }

    public Task SaveAsync(
        ExamSessionId sessionId, ExamModule module,
        IReadOnlyDictionary<string, string?> answers, DateTimeOffset at, CancellationToken ct)
    {
        var id = KeyFor(sessionId, module);

        return context.AnswerSheets.ReplaceOneAsync(
            a => a.Id == id,
            new AnswerSheetDocument
            {
                Id = id,
                SessionId = sessionId.Value,
                Module = module.ToString(),
                Answers =
                [
                    .. answers.Select(kv => new AnswerDocument
                    {
                        QuestionId = kv.Key, Value = kv.Value,
                    }),
                ],
                UpdatedAt = at.UtcDateTime,
            },
            new ReplaceOptions { IsUpsert = true },
            ct);
    }
}

internal sealed class MongoSectionResultStore(MongoContext context, IClock clock) : ISectionResultStore
{
    private static string KeyFor(ExamSessionId sessionId, ExamModule module) =>
        $"{sessionId.Value}:{module}";

    /// <summary>
    /// <b>Insert-if-absent, never overwrite.</b> A section is marked once. An
    /// upsert here would let a replayed submit re-score a sitting against a
    /// sheet that has since changed, which is how a band silently moves after
    /// a learner has already seen it.
    /// </summary>
    public async Task SaveAsync(ExamSessionId sessionId, SectionScore score, CancellationToken ct)
    {
        var id = KeyFor(sessionId, score.Module);

        var document = new SectionResultDocument
        {
            Id = id,
            SessionId = sessionId.Value,
            Module = score.Module.ToString(),
            RawScore = score.RawScore,
            MaxScore = score.MaxScore,
            Band = score.Band.Value,
            Questions =
            [
                .. score.Questions.Select(q => new QuestionResultDocument
                {
                    QuestionId = q.QuestionId, Submitted = q.Submitted, IsCorrect = q.IsCorrect,
                }),
            ],
            ScoredAt = clock.UtcNow.UtcDateTime,
        };

        try
        {
            await context.SectionResults.InsertOneAsync(document, cancellationToken: ct);
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Already marked. The second caller is a retry, and the first
            // result stands.
        }
    }

    public async Task<IReadOnlyList<SectionScore>> ListAsync(
        ExamSessionId sessionId, CancellationToken ct)
    {
        var docs = await context.SectionResults
            .Find(r => r.SessionId == sessionId.Value)
            .ToListAsync(ct);

        return [.. docs.Select(d => d.ToDomain())];
    }
}
