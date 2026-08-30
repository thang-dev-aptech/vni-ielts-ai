using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Assessment;
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

    public async Task UpsertAsync(ExamVersion version, CancellationToken ct)
    {
        var document = version.ToDocument();

        /*
         * <b>The condition is in the filter, so the write itself refuses.</b>
         *
         * Reading the stored version, comparing content and then writing is
         * three statements, and a concurrent publish fits between the second
         * and the third. One filtered replace has no gap.
         *
         * <b>Three ways to match, and each is a case that must be allowed.</b>
         * A version that has never been published may be replaced freely — that
         * is what a draft is for. A version whose content is unchanged may be
         * replaced, which is how publish and unpublish work. A document written
         * before the fingerprint existed has none, and may be replaced once —
         * which lets an existing deployment adopt this check without a
         * migration.
         */
        var replaceable = Builders<ExamVersionDocument>.Filter.And(
            Builders<ExamVersionDocument>.Filter.Eq(v => v.Id, version.Id.Value),
            Builders<ExamVersionDocument>.Filter.Or(
                Builders<ExamVersionDocument>.Filter.Eq(v => v.PublishedAt, null),
                Builders<ExamVersionDocument>.Filter.Eq(v => v.ContentHash, document.ContentHash),
                Builders<ExamVersionDocument>.Filter.Eq(v => v.ContentHash, null)));

        try
        {
            var written = await context.ExamVersions.ReplaceOneAsync(
                replaceable, document, new ReplaceOptions { IsUpsert = true }, ct);

            // Matched nothing and inserted nothing: the only document with this
            // id is one the filter excluded, which is a published one whose
            // content differs.
            if (written.MatchedCount == 0 && written.UpsertedId is null)
                throw new PublishedExamVersionIsImmutableException(version.Id);
        }
        catch (MongoWriteException e)
            when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The upsert tried to insert over an id the filter excluded. Same
            // conclusion, arriving through the other door.
            throw new PublishedExamVersionIsImmutableException(version.Id);
        }
        catch (MongoCommandException e) when (e.Code == 11000)
        {
            throw new PublishedExamVersionIsImmutableException(version.Id);
        }
    }
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

    /// <summary>
    /// <b>The guard and the write are one statement, and they have to be.</b>
    ///
    /// Read the sitting, decide, then replace is three statements, and a
    /// competing advance or submit fits between any two of them — which is the
    /// race this exists to close, reintroduced by the code closing it. Here the
    /// filter carries the state the caller found, so the server matches nothing
    /// once the sitting has moved and reports zero documents modified.
    ///
    /// <b>The open section is named, not counted.</b> A revision counter would
    /// refuse a submit because an autosave had landed, which is a different
    /// document and none of this method's business. Naming the section says the
    /// actual precondition: <i>this</i> sitting is still in progress and
    /// <i>that</i> section is still the open one.
    /// </summary>
    public async Task<bool> TrySaveAsync(
        ExamSession session, SessionState from, CancellationToken ct)
    {
        var filter = Builders<ExamSessionDocument>.Filter.And(
            Builders<ExamSessionDocument>.Filter.Eq(s => s.Id, session.Id.Value),
            Builders<ExamSessionDocument>.Filter.Eq(s => s.Status, from.Status.ToString()),
            OpenSectionIs(from.OpenModule, from.OpenSectionRunning, from.OpenPartId));

        var result = await context.ExamSessions.ReplaceOneAsync(
            filter, session.ToDocument(), cancellationToken: ct);

        /*
         * <b><c>MatchedCount</c>, not <c>ModifiedCount</c>.</b> The question
         * this method answers is "was the sitting still in the state the caller
         * found it in", and that is what matched. Whether the replacement
         * happened to differ from what was there is a different question, and
         * answering it here reports a race that did not occur.
         *
         * Reachable: an advance against a sitting that is in progress with
         * every attempt already closed returns `NoOpenSection` without
         * mutating, so the document written back is byte-identical. Mongo then
         * reports one matched and zero modified, and the caller was told
         * somebody had beaten it to a transition nobody made.
         */
        return result.MatchedCount > 0;
    }

    /// <summary>
    /// <c>{ submittedAt: null }</c> matches a missing field as well as a null
    /// one, which is what the attempt documents actually hold — the mapper
    /// omits the key entirely while a section is open.
    ///
    /// The null-module branch is the sitting that is in progress with every
    /// attempt already closed. No transition here produces that state, so it
    /// comes from a bad write; the expiry sweep still has to be able to close
    /// it, and it still has to lose to a concurrent submit.
    /// </summary>
    /// <summary>
    /// <b>And whether its stopwatch was running.</b> Pause and resume change
    /// nothing else on the sitting, so a guard that named only the section let
    /// two simultaneous pauses both through — each reading the same
    /// <c>runningSince</c> and each adding the same interval to the total.
    /// </summary>
    private static FilterDefinition<ExamSessionDocument> OpenSectionIs(
        ExamModule? module, bool running, string? partId) =>
        module is { } open
            ? Builders<ExamSessionDocument>.Filter.ElemMatch(
                s => s.Attempts,
                Builders<AttemptDocument>.Filter.And(
                    Builders<AttemptDocument>.Filter.Eq(a => a.Module, open.ToString()),
                    Builders<AttemptDocument>.Filter.Eq(a => a.PartId, partId),
                    Builders<AttemptDocument>.Filter.Eq(a => a.SubmittedAt, null),
                    running
                        ? Builders<AttemptDocument>.Filter.Ne(a => a.RunningSince, null)
                        : Builders<AttemptDocument>.Filter.Eq(a => a.RunningSince, null)))
            : Builders<ExamSessionDocument>.Filter.Not(
                Builders<ExamSessionDocument>.Filter.ElemMatch(
                    s => s.Attempts,
                    Builders<AttemptDocument>.Filter.Eq(a => a.SubmittedAt, null)));
}

internal sealed class MongoAnswerSheetStore(MongoContext context) : IAnswerSheetStore
{
    private static string KeyFor(ExamSessionId sessionId, ExamModule module) =>
        $"{sessionId.Value}:{module}";

    /// <summary>
    /// The sheet a document holds, legacy array first and the map on top.
    ///
    /// <b>Both layers, not one or the other.</b> Documents written before
    /// 27/08/2026 keep their answers in the array; every write since lands in
    /// the map. Preferring the map alone would make an old sitting's earlier
    /// answers vanish the moment its next autosave arrived — the learner would
    /// watch a section they had filled in empty itself, mid-exam, with the chip
    /// reading "Đã lưu".
    ///
    /// Last write wins on a duplicate question id inside the array. A sheet
    /// should never contain one, but a corrupt document must not throw and lock
    /// a learner out of their own exam.
    /// </summary>
    private static Dictionary<string, string?> Merge(AnswerSheetDocument doc)
    {
        var answers = new Dictionary<string, string?>();
        foreach (var entry in doc.Answers) answers[entry.QuestionId] = entry.Value;
        foreach (var entry in doc.Entries) answers[entry.Key] = entry.Value;
        return answers;
    }

    /// <summary>
    /// <c>.</c> is a path separator to Mongo and a leading <c>$</c> is an
    /// operator, so either one in a question id sends the write somewhere other
    /// than where it was addressed — into a nested object nobody reads, or into
    /// a command. The handler validates ids against the exam's own questions;
    /// this is the second line, for every caller that is not that handler.
    /// </summary>
    private static void GuardKey(string questionId)
    {
        if (questionId.Length == 0 || questionId.Contains('.') || questionId[0] == '$')
            throw new ArgumentException(
                $"'{questionId}' cannot be a question id: a Mongo field name may not be empty, "
                + "contain '.', or begin with '$'.", nameof(questionId));
    }

    public async Task<IReadOnlyDictionary<string, string?>> LoadAsync(
        ExamSessionId sessionId, ExamModule module, CancellationToken ct)
    {
        var id = KeyFor(sessionId, module);
        var doc = await context.AnswerSheets.Find(a => a.Id == id).FirstOrDefaultAsync(ct);

        return doc is null ? new Dictionary<string, string?>() : Merge(doc);
    }

    public async Task<AnswerSheet> ReadAsync(
        ExamSessionId sessionId, ExamModule module, CancellationToken ct)
    {
        var id = KeyFor(sessionId, module);
        var doc = await context.AnswerSheets.Find(a => a.Id == id).FirstOrDefaultAsync(ct);

        return doc is null ? AnswerSheet.Empty : new AnswerSheet(Merge(doc), doc.Revision, doc.Sequences);
    }

    /// <summary>
    /// <b>One statement, and no compare.</b>
    ///
    /// The version this replaced sent the whole sheet under a
    /// compare-and-swap, which was correct about the danger and wrong about the
    /// remedy: it could *detect* that another writer had moved the sheet but it
    /// had nothing to do about it, so the client took the new revision and
    /// re-sent its whole local sheet — performing the exact overwrite the
    /// compare had just refused, one beat later. The race was reported and then
    /// completed.
    ///
    /// A patch removes the conflict instead of reporting it. Two writers
    /// touching different questions each <c>$set</c> their own key and both are
    /// right; there is nothing to reconcile because nothing collided. Two
    /// writers touching the *same* question resolve to the later one, which is
    /// what the learner typed last and therefore what they meant.
    ///
    /// The revision survives, doing the one job it is actually good at: telling
    /// a caller its view is behind, so it can take in what another tab wrote.
    /// It no longer decides whether the write happens.
    /// </summary>
    public async Task<PatchedSheet> PatchAsync(
        ExamSessionId sessionId, ExamModule module,
        IReadOnlyDictionary<string, string?> changes,
        DateTimeOffset at, CancellationToken ct,
        IReadOnlyDictionary<string, long>? sequences = null)
    {
        // An empty patch must not touch the document. Bumping the revision for
        // a write that changed nothing would tell every other tab it was stale
        // and pull the whole sheet back on a timer, for ever.
        if (changes.Count == 0)
        {
            var current = await ReadAsync(sessionId, module, ct);
            return new PatchedSheet(current, current.Revision);
        }

        foreach (var key in changes.Keys) GuardKey(key);

        var id = KeyFor(sessionId, module);

        var update = Builders<AnswerSheetDocument>.Update
            .SetOnInsert(a => a.SessionId, sessionId.Value)
            .SetOnInsert(a => a.Module, module.ToString())
            .Set(a => a.UpdatedAt, at.UtcDateTime)
            .Inc(a => a.Revision, 1);

        update = changes.Aggregate(
            update, (acc, change) => acc.Set($"entries.{change.Key}", change.Value));

        /*
         * <b>With ordering tokens the write becomes a pipeline, because the
         * decision is per field and it needs the stored value to make it.</b>
         *
         * `$set` overwrites unconditionally, which is the whole defect: the
         * later-arriving write wins whether or not it is the later edit. A
         * pipeline update can read `$seqs.<id>` while writing, so each entry
         * keeps its current value unless the incoming token is strictly
         * greater. `$ifNull … -1` makes a question that has never carried a
         * token accept the first one.
         *
         * The pipeline replaces the builder above rather than composing with
         * it — the driver cannot mix the two — so everything the builder set is
         * restated here. That duplication is the cost of the guarantee and it
         * is kept adjacent so the two cannot drift apart unnoticed.
         */
        PipelineDefinition<AnswerSheetDocument, AnswerSheetDocument>? ordered = null;

        if (sequences is { Count: > 0 })
        {
            var fields = new BsonDocument
            {
                { "sessionId", new BsonDocument("$ifNull", new BsonArray { "$sessionId", sessionId.Value }) },
                { "module", new BsonDocument("$ifNull", new BsonArray { "$module", module.ToString() }) },
                { "updatedAt", at.UtcDateTime },
                { "revision", new BsonDocument("$add", new BsonArray {
                    new BsonDocument("$ifNull", new BsonArray { "$revision", 0 }), 1 }) },
            };

            foreach (var (questionId, value) in changes)
            {
                // A question the caller sent no token for is written outright.
                // Mixing the two in one request is not something this client
                // does, and refusing it would be inventing a rule; taking the
                // write is the behaviour that was there before tokens existed.
                if (!sequences.TryGetValue(questionId, out var seq))
                {
                    fields[$"entries.{questionId}"] = value is null ? BsonNull.Value : value;
                    continue;
                }

                var newer = new BsonDocument("$gt", new BsonArray
                {
                    seq,
                    new BsonDocument("$ifNull", new BsonArray { $"$seqs.{questionId}", -1L }),
                });

                fields[$"entries.{questionId}"] = new BsonDocument("$cond", new BsonArray
                {
                    newer,
                    value is null ? BsonNull.Value : value,
                    new BsonDocument("$ifNull", new BsonArray { $"$entries.{questionId}", BsonNull.Value }),
                });

                fields[$"seqs.{questionId}"] = new BsonDocument("$cond", new BsonArray
                {
                    newer,
                    seq,
                    new BsonDocument("$ifNull", new BsonArray { $"$seqs.{questionId}", -1L }),
                });
            }

            ordered = new BsonDocument[] { new("$set", fields) };
        }

        var options = new FindOneAndUpdateOptions<AnswerSheetDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After,
        };

        /*
         * <b>The filter carries the closure check, so it is the write itself
         * that refuses.</b>
         *
         * Reading the document, checking `closedAt` and then writing is three
         * statements, and a freeze fits between the second and the third. The
         * failure that produces is the one this whole protocol exists to
         * prevent: an answer accepted after its section was marked, with the
         * learner told it was saved.
         *
         * With `closedAt` in the filter there is one statement. A frozen sheet
         * matches nothing, the upsert then attempts an insert on an `_id` that
         * exists, and the duplicate key is how this code learns it lost. That
         * is also how two racing first-writes present, so the two are told
         * apart by reading the document afterwards rather than by guessing.
         */
        var open = Builders<AnswerSheetDocument>.Filter.And(
            Builders<AnswerSheetDocument>.Filter.Eq(a => a.Id, id),
            Builders<AnswerSheetDocument>.Filter.Eq(a => a.ClosedAt, null));

        Task<AnswerSheetDocument> ApplyAsync() =>
            ordered is null
                ? context.AnswerSheets.FindOneAndUpdateAsync(open, update, options, ct)
                : context.AnswerSheets.FindOneAndUpdateAsync(open, ordered, options, ct);

        AnswerSheetDocument written;
        try
        {
            written = await ApplyAsync();
        }
        catch (MongoCommandException e) when (e.Code == 11000)
        {
            written = await RetryOrRefuseAsync(id, module, ApplyAsync, ct);
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            written = await RetryOrRefuseAsync(id, module, ApplyAsync, ct);
        }

        // `$inc` is by one and by one only, so the revision this write amended
        // is exactly one below the one it produced. Stated rather than derived
        // by the caller, because the day the increment changes is the day a
        // caller doing its own arithmetic starts lying quietly.
        return new PatchedSheet(
            new AnswerSheet(Merge(written), written.Revision, written.Sequences),
            written.Revision - 1);
    }

    /// <summary>
    /// A duplicate key on an upsert means one of two different things.
    ///
    /// <b>Either two first-writes for one section raced and this one lost the
    /// insert</b> — the document exists now, the same update applies as an
    /// update, and both patches land, which is the point — <b>or the sheet has
    /// been frozen</b> and the filter no longer matches it, in which case there
    /// is nothing to retry and the write must be refused.
    ///
    /// Told apart by reading the document, which is safe because by this point
    /// it certainly exists. A closed sheet never re-opens, so this read cannot
    /// go stale in the direction that matters.
    /// </summary>
    private async Task<AnswerSheetDocument> RetryOrRefuseAsync(
        string id, ExamModule module,
        Func<Task<AnswerSheetDocument>> apply,
        CancellationToken ct)
    {
        var existing = await context.AnswerSheets.Find(a => a.Id == id).FirstOrDefaultAsync(ct);

        if (existing?.ClosedAt is not null) throw new SectionSheetClosedException(module);

        var written = await apply();

        // It was open a moment ago and is not now: a freeze landed between the
        // read above and this write. Rare, and exactly the case that must not
        // be reported as a save.
        return written ?? throw new SectionSheetClosedException(module);
    }

    /// <summary>
    /// Freezes the sheet. Idempotent, and it returns what is frozen.
    ///
    /// <b>One statement, and the filter is what makes it safe.</b> A freeze
    /// that read first and wrote second would let a patch in between, which is
    /// the interleaving the freeze exists to close.
    ///
    /// The upsert covers a section nobody answered: without it a learner who
    /// wrote nothing would leave no document, and a late write would find
    /// nothing to refuse it.
    ///
    /// A second call finds the sheet already frozen, matches nothing, collides
    /// on `_id`, and returns the existing document unchanged. That is the
    /// required behaviour rather than a tolerated one — two tabs on "Nộp bài",
    /// a submit meeting the expiry sweep, and a retried request all arrive
    /// here, and re-freezing at a later revision would change the content
    /// marking already read.
    /// </summary>
    public async Task<AnswerSheet> CloseAsync(
        ExamSessionId sessionId, ExamModule module, DateTimeOffset at, CancellationToken ct)
    {
        var id = KeyFor(sessionId, module);

        var open = Builders<AnswerSheetDocument>.Filter.And(
            Builders<AnswerSheetDocument>.Filter.Eq(a => a.Id, id),
            Builders<AnswerSheetDocument>.Filter.Eq(a => a.ClosedAt, null));

        var options = new FindOneAndUpdateOptions<AnswerSheetDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After,
        };

        async Task<AnswerSheetDocument> AlreadyClosedAsync()
        {
            var existing = await context.AnswerSheets.Find(a => a.Id == id).FirstOrDefaultAsync(ct);

            return existing
                ?? throw new InvalidOperationException(
                    $"The {module} sheet for {sessionId.Value} collided on insert and then "
                    + "could not be read. Nothing deletes an answer sheet, so this is a "
                    + "database that has lost a document rather than a race.");
        }

        AnswerSheetDocument written;
        try
        {
            /*
             * <b>`closedRevision` is set from the revision this statement
             * produces, and it does not move it.</b> `Revision` is not
             * incremented here: a freeze changes no answer, and bumping it
             * would tell every open tab it was behind and pull the whole sheet
             * back for a section that has just closed.
             */
            written = await context.AnswerSheets.FindOneAndUpdateAsync(
                open,
                Builders<AnswerSheetDocument>.Update
                    .SetOnInsert(a => a.SessionId, sessionId.Value)
                    .SetOnInsert(a => a.Module, module.ToString())
                    .Set(a => a.ClosedAt, at.UtcDateTime),
                options,
                ct);
        }
        catch (MongoCommandException e) when (e.Code == 11000)
        {
            written = await AlreadyClosedAsync();
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            written = await AlreadyClosedAsync();
        }

        written ??= await AlreadyClosedAsync();

        // Written on the way out rather than inside the update, because the
        // revision is only known after the statement that froze the sheet — and
        // only for the caller that actually froze it. A second caller finds it
        // already set and leaves it alone.
        if (written.ClosedRevision is null)
        {
            await context.AnswerSheets.UpdateOneAsync(
                Builders<AnswerSheetDocument>.Filter.And(
                    Builders<AnswerSheetDocument>.Filter.Eq(a => a.Id, id),
                    Builders<AnswerSheetDocument>.Filter.Eq(a => a.ClosedRevision, null)),
                Builders<AnswerSheetDocument>.Update.Set(a => a.ClosedRevision, written.Revision),
                cancellationToken: ct);
        }

        return new AnswerSheet(Merge(written), written.Revision, written.Sequences);
    }

    /// <summary>
    /// One entry, in place.
    ///
    /// <b>A single statement now, where it used to be two that could race.</b>
    /// The array version had to try a positional update and then fall back to a
    /// push, and two parts finishing within a second of each other could both
    /// take the push branch and leave two entries for one question. It was
    /// survivable — the read took the last one — but it was survivable by
    /// accident. Against a map there is no branch: the key is set whether or
    /// not it was there.
    /// </summary>
    /// <para>
    /// <b>It takes no revision, and that is not an omission.</b> A field-level
    /// update cannot lose a neighbour's write, so there is no conflict for a
    /// caller to resolve — a compare here would invent a failure and then have
    /// nothing to do about it. It does bump the revision, because a reader
    /// holding an older view is genuinely behind once this lands.
    /// </para>
    public async Task SetAnswerAsync(
        ExamSessionId sessionId, ExamModule module, string questionId, string? value,
        DateTimeOffset at, CancellationToken ct)
    {
        GuardKey(questionId);

        var id = KeyFor(sessionId, module);

        /*
         * <b>The freeze applies here too, and it has to.</b>
         *
         * Speaking's sheet is written through this method, not through a patch
         * — the recording id is the server's own index of what was uploaded.
         * So a Speaking upload that finishes after its section closed would
         * walk straight past the closure protocol, file a recording into a
         * sheet marking has already read, and report success. The learner's
         * only copy of a spoken answer would exist, be filed, and never be
         * marked. → `I2.2`
         */
        var open = Builders<AnswerSheetDocument>.Filter.And(
            Builders<AnswerSheetDocument>.Filter.Eq(a => a.Id, id),
            Builders<AnswerSheetDocument>.Filter.Eq(a => a.ClosedAt, null));

        var update = Builders<AnswerSheetDocument>.Update
            .SetOnInsert(a => a.SessionId, sessionId.Value)
            .SetOnInsert(a => a.Module, module.ToString())
            .Set($"entries.{questionId}", value)
            .Set(a => a.UpdatedAt, at.UtcDateTime)
            .Inc(a => a.Revision, 1);

        /*
         * <b>A duplicate key here is ambiguous, and reading it as "closed"
         * broke a case that was already correct.</b>
         *
         * Two recordings finishing at the same moment on a sheet that does not
         * exist yet both upsert; one inserts and the other collides. That is a
         * first-write race, not a freeze, and both writes have to land — which
         * is the property `Two_recordings_filed_at_once_both_reach_the_sheet`
         * holds. The two are told apart by reading the document, exactly as the
         * patch path does.
         */
        try
        {
            var written = await context.AnswerSheets.UpdateOneAsync(
                open, update, new UpdateOptions { IsUpsert = true }, ct);

            // Matched nothing and inserted nothing: the only document with this
            // `_id` is one the filter excluded, which is a frozen one.
            if (written.MatchedCount == 0 && written.UpsertedId is null)
                throw new SectionSheetClosedException(module);
        }
        catch (MongoCommandException e) when (e.Code == 11000)
        {
            await RetrySetOrRefuseAsync(id, module, open, update, ct);
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            await RetrySetOrRefuseAsync(id, module, open, update, ct);
        }
    }

    /// <summary>The <see cref="SetAnswerAsync"/> half of <see cref="RetryOrRefuseAsync"/>.</summary>
    private async Task RetrySetOrRefuseAsync(
        string id, ExamModule module,
        FilterDefinition<AnswerSheetDocument> open,
        UpdateDefinition<AnswerSheetDocument> update,
        CancellationToken ct)
    {
        var existing = await context.AnswerSheets.Find(a => a.Id == id).FirstOrDefaultAsync(ct);

        if (existing?.ClosedAt is not null) throw new SectionSheetClosedException(module);

        var written = await context.AnswerSheets.UpdateOneAsync(
            open, update, new UpdateOptions { IsUpsert = true }, ct);

        // Open a moment ago and not now: a freeze landed in between, and this
        // write must not be reported as having succeeded.
        if (written.MatchedCount == 0 && written.UpsertedId is null)
            throw new SectionSheetClosedException(module);
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
            Band = score.Band?.Value,
            Questions =
            [
                .. score.Questions.Select(q => new QuestionResultDocument
                {
                    QuestionId = q.QuestionId,
                    Submitted = q.Submitted,
                    IsCorrect = q.IsCorrect,
                    CorrectAnswer = q.CorrectAnswer,
                    Slots = q.Slots is null ? null :
                    [
                        .. q.Slots.Select(s => new SlotResultDocument
                        {
                            SlotId = s.SlotId,
                            Number = s.Number,
                            Submitted = s.Submitted,
                            Status = s.Status.ToString().ToLowerInvariant(),
                            CorrectAnswer = s.CorrectAnswer,
                        }),
                    ],
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

/// <summary>
/// Marked Writing and Speaking sections.
///
/// Same insert-if-absent rule as <see cref="MongoSectionResultStore"/>, and for
/// the same reason: a section is marked once. Here the reason bites harder — a
/// second evaluation of the same essay would cost another call to a provider
/// and could return a different band, so an upsert would let a learner's mark
/// move because someone retried a request.
/// </summary>
internal sealed class MongoSectionMarkingStore(MongoContext context, IClock clock)
    : ISectionMarkingStore
{
    /// <summary>
    /// <b>The task number is part of the key.</b> Writing produces two markings
    /// for one module; keying on session and module alone would make the second
    /// task a duplicate of the first and silently drop it.
    /// </summary>
    private static string KeyFor(ExamSessionId sessionId, SectionMarking marking) =>
        marking.TaskNumber is { } task
            ? $"{sessionId.Value}:{marking.Module}:{task}"
            : $"{sessionId.Value}:{marking.Module}";

    public async Task SaveAsync(
        ExamSessionId sessionId, SectionMarking marking, CancellationToken ct)
    {
        var document = new SectionMarkingDocument
        {
            Id = KeyFor(sessionId, marking),
            SessionId = sessionId.Value,
            Module = marking.Module.ToString(),
            TaskNumber = marking.TaskNumber,
            RubricVersion = marking.RubricVersion,
            Band = marking.Band.Value,
            ReportedBand = marking.ReportedBand?.Value,
            Criteria =
            [
                .. marking.Criteria.Select(c => new CriterionAssessmentDocument
                {
                    Criterion = c.Criterion,
                    Band = c.Band.Value,
                    Feedback = c.Feedback,
                    Evidence = [.. c.Evidence],
                }),
            ],
            Flags = [.. marking.Flags.Select(f => f.ToString())],
            UngroundedEvidence = [.. marking.UngroundedEvidence],
            MarkedAt = clock.UtcNow.UtcDateTime,
        };

        try
        {
            await context.SectionMarkings.InsertOneAsync(document, cancellationToken: ct);
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Already marked. The second caller is a retry, and the first
            // marking stands — re-evaluating would bill again and could return
            // a different band for work that has not changed.
        }
    }

    public async Task<IReadOnlyList<SectionMarking>> ListAsync(
        ExamSessionId sessionId, CancellationToken ct)
    {
        var docs = await context.SectionMarkings
            .Find(m => m.SessionId == sessionId.Value)
            .ToListAsync(ct);

        return [.. docs.Select(d => d.ToDomain())];
    }
}
