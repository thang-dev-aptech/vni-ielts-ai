using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Importing;

/// <summary>
/// Exam-import jobs, in Mongo.
///
/// <b>Twin of <see cref="Vni.Ielts.Infrastructure.Persistence.Exams.MongoMarkingOutbox"/>,
/// deliberately, and for the same reason.</b> A section marking and an exam
/// import are the same shape of problem — work that has to survive a crashed
/// worker without being done twice, because "twice" is a second paid provider
/// call. So the same rules: every write here is one statement, and every
/// lease-guarded transition filters on the caller's own lease token, so a
/// worker whose lease was reclaimed gets <c>false</c> rather than silently
/// overwriting the new owner's work.
/// </summary>
internal sealed class MongoImportOutbox(MongoContext context, IClock clock) : IImportOutbox
{
    private IMongoCollection<ImportJobDocument> Jobs => context.ImportJobs;

    public async Task<bool> EnqueueAsync(ImportJob job, CancellationToken ct)
    {
        try
        {
            await Jobs.InsertOneAsync(ToDocument(job), cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException e)
            when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            /*
             * Already owed — a retried upload of identical bytes under the
             * same definition, version and parse prompt version. The unique
             * index on `operationId` is what makes that harmless in the
             * database, not in a caller's memory: checking first and
             * inserting second would be two statements with a race between
             * them.
             */
            return false;
        }
    }

    public async Task<ImportJob?> ClaimAsync(string leaseToken, TimeSpan lease, CancellationToken ct)
    {
        var at = clock.UtcNow.UtcDateTime;

        /*
         * <b>Claimable means one of three things, and the third is the
         * recovery path.</b> Pending is new work; Retryable is work whose
         * backoff has elapsed; Running with an expired lease is work whose
         * worker died. All three are the same update, because a job that has
         * been abandoned is indistinguishable from one that was never
         * started — which is exactly why the lease exists.
         *
         * <b>This has to be ONE FindOneAndUpdateAsync.</b> Finding a due job
         * and then updating it is two statements, and two workers slip
         * between them and both take the same job.
         */
        var claimable = Builders<ImportJobDocument>.Filter.And(
            Builders<ImportJobDocument>.Filter.In(
                j => j.State,
                new[]
                {
                    ImportJobState.Pending.ToString(),
                    ImportJobState.Retryable.ToString(),
                    ImportJobState.Running.ToString(),
                }),
            Builders<ImportJobDocument>.Filter.Lte(j => j.NextAttemptAt, at),
            Builders<ImportJobDocument>.Filter.Or(
                Builders<ImportJobDocument>.Filter.Eq(j => j.LeaseUntil, null),
                Builders<ImportJobDocument>.Filter.Lt(j => j.LeaseUntil, at)));

        var claimed = await Jobs.FindOneAndUpdateAsync(
            claimable,
            Builders<ImportJobDocument>.Update
                .Set(j => j.State, ImportJobState.Running.ToString())
                .Set(j => j.LeaseToken, leaseToken)
                .Set(j => j.LeaseUntil, at.Add(lease))
                .Inc(j => j.Attempts, 1),
            new FindOneAndUpdateOptions<ImportJobDocument>
            {
                ReturnDocument = ReturnDocument.After,
                // Oldest first. A package that has been waiting must not be
                // starved by a steady arrival of newer uploads.
                Sort = Builders<ImportJobDocument>.Sort.Ascending(j => j.CreatedAt),
            },
            ct);

        if (claimed is null) return null;

        try
        {
            return Map(claimed);
        }
        catch (ImportJobStageUnreadableException e)
        {
            /*
             * <b>The one path the <see cref="AdvanceAsync"/> guard does not
             * cover.</b> That guard stops a caller from writing a stage
             * earlier than the one already recorded; it cannot stop an older
             * binary from failing to *read* a stage a newer one wrote and
             * then rolled back. Left unhandled, the caller of `ClaimAsync`
             * would receive a job silently reset to `Extracting` — an
             * invented "start over" answer to a question this store cannot
             * actually answer, and a resumed worker re-buys the parse it
             * already paid for. `G-11`, and the same reasoning
             * `FabricatedAnswerKeyGuard` already applies to an unreadable
             * answer key: no answer is safer than an invented one.
             *
             * This job was already claimed by the update above — the lease
             * and the Running state are already written. So "refuse" here
             * means: keep it from ever being handed to a caller as workable,
             * by immediately dead-lettering it under the very lease this call
             * just took, and reporting nothing to claim. A person has to look
             * at it; nobody re-parses a Cambridge paper to find out why.
             */
            await Jobs.UpdateOneAsync(
                Mine(e.OperationId, leaseToken),
                Builders<ImportJobDocument>.Update
                    .Set(j => j.State, ImportJobState.Failed.ToString())
                    .Set(j => j.LastError, Trim(e.Message))
                    .Set(j => j.LeaseUntil, null)
                    .Set(j => j.LeaseToken, null),
                cancellationToken: ct);

            return null;
        }
    }

    /// <summary>Every lease-guarded transition names the job and the worker that owns it.</summary>
    private static FilterDefinition<ImportJobDocument> Mine(string operationId, string leaseToken) =>
        Builders<ImportJobDocument>.Filter.And(
            Builders<ImportJobDocument>.Filter.Eq(j => j.OperationId, operationId),
            Builders<ImportJobDocument>.Filter.Eq(j => j.LeaseToken, leaseToken));

    public async Task<bool> RenewAsync(
        string operationId, string leaseToken, TimeSpan lease, CancellationToken ct)
    {
        var until = clock.UtcNow.Add(lease).UtcDateTime;

        var renewed = await Jobs.UpdateOneAsync(
            Mine(operationId, leaseToken),
            Builders<ImportJobDocument>.Update.Set(j => j.LeaseUntil, until),
            cancellationToken: ct);

        return renewed.MatchedCount > 0;
    }

    /// <summary>
    /// Records how far a job got, and refuses to move it backwards.
    ///
    /// <b>This store guards a rewind that <c>MongoMarkingOutbox</c> does not,
    /// and the difference is real rather than drift.</b> A marking job pays
    /// for exactly one provider call, so a stale write moving its (nonexistent)
    /// stage field backwards would cost nothing — the twin never needed a
    /// guard because it has nothing to guard. An import pays separately for
    /// the parse, the transcription and forty explanations; a caller that
    /// advances a job to a stage earlier than the one already recorded makes
    /// a resumed worker re-enter a paid stage and buy it a second time — the
    /// exact failure <see cref="ImportJobStage"/> exists to prevent. So the
    /// filter, not just the caller's good behaviour, is what stops it: the
    /// update only matches when the stored stage is at or before the
    /// requested one, in the same statement as the lease check. A rewind
    /// attempt is refused the same way a stolen lease is — <c>false</c>,
    /// nothing written.
    /// </summary>
    public async Task<bool> AdvanceAsync(
        string operationId, string leaseToken, ImportJobStage stage, Guid? draftId, CancellationToken ct)
    {
        var forwardOnly = Builders<ImportJobDocument>.Filter.And(
            Mine(operationId, leaseToken),
            Builders<ImportJobDocument>.Filter.Lte(j => j.Stage, (int)stage));

        // DraftId is left untouched when the caller has none yet, rather than
        // overwritten with null.
        var update = Builders<ImportJobDocument>.Update.Set(j => j.Stage, (int)stage);
        if (draftId is { } id) update = update.Set(j => j.DraftId, id.ToString("D"));

        var advanced = await Jobs.UpdateOneAsync(forwardOnly, update, cancellationToken: ct);

        return advanced.MatchedCount > 0;
    }

    public async Task<bool> CompleteAsync(string operationId, string leaseToken, CancellationToken ct)
    {
        var done = await Jobs.UpdateOneAsync(
            Mine(operationId, leaseToken),
            Builders<ImportJobDocument>.Update
                .Set(j => j.State, ImportJobState.Completed.ToString())
                .Set(j => j.CompletedAt, clock.UtcNow.UtcDateTime)
                .Set(j => j.LeaseUntil, null)
                .Set(j => j.LeaseToken, null),
            cancellationToken: ct);

        return done.MatchedCount > 0;
    }

    public async Task<bool> RetryAsync(
        string operationId, string leaseToken, DateTimeOffset nextAttemptAt, string error,
        CancellationToken ct)
    {
        // Never resets Stage: a transient failure after Keying must resume at
        // Keying, not pay for Parsing again.
        var scheduled = await Jobs.UpdateOneAsync(
            Mine(operationId, leaseToken),
            Builders<ImportJobDocument>.Update
                .Set(j => j.State, ImportJobState.Retryable.ToString())
                .Set(j => j.NextAttemptAt, nextAttemptAt.UtcDateTime)
                .Set(j => j.LastError, Trim(error))
                .Set(j => j.LeaseUntil, null)
                .Set(j => j.LeaseToken, null),
            cancellationToken: ct);

        return scheduled.MatchedCount > 0;
    }

    public async Task<bool> FailAsync(string operationId, string leaseToken, string error, CancellationToken ct)
    {
        // Also never resets Stage — a permanently-failed job's stage is still
        // the honest record of how far the money was spent, for whoever looks
        // at the draft next.
        var dead = await Jobs.UpdateOneAsync(
            Mine(operationId, leaseToken),
            Builders<ImportJobDocument>.Update
                .Set(j => j.State, ImportJobState.Failed.ToString())
                .Set(j => j.LastError, Trim(error))
                .Set(j => j.LeaseUntil, null)
                .Set(j => j.LeaseToken, null),
            cancellationToken: ct);

        return dead.MatchedCount > 0;
    }

    /// <summary>
    /// One filtered update, and the filter is the whole guard: the state term
    /// is what makes it impossible to reopen a job a worker is currently
    /// inside. Reading the state and then writing would be two statements with
    /// a claim able to slip between them.
    ///
    /// <c>Stage</c> and <c>DraftId</c> are untouched — see the port's remarks.
    /// </summary>
    public async Task<bool> ReopenAsync(string operationId, CancellationToken ct)
    {
        var failed = Builders<ImportJobDocument>.Filter.And(
            Builders<ImportJobDocument>.Filter.Eq(j => j.OperationId, operationId),
            Builders<ImportJobDocument>.Filter.Eq(j => j.State, ImportJobState.Failed.ToString()));

        var reopened = await Jobs.UpdateOneAsync(
            failed,
            Builders<ImportJobDocument>.Update
                .Set(j => j.State, ImportJobState.Pending.ToString())
                .Set(j => j.Attempts, 0)
                .Set(j => j.NextAttemptAt, clock.UtcNow.UtcDateTime)
                .Set(j => j.CompletedAt, null)
                .Set(j => j.LastError, null)
                .Set(j => j.LeaseUntil, null)
                .Set(j => j.LeaseToken, null),
            cancellationToken: ct);

        return reopened.MatchedCount > 0;
    }

    /// <summary>
    /// A read, not a claim. If the stored stage is unreadable this throws
    /// <see cref="ImportJobStageUnreadableException"/> rather than inventing
    /// `Extracting` — see the comment on that type. Unlike <see cref="ClaimAsync"/>,
    /// there is no lease here to dead-letter the job under, so the explicit
    /// failure is the whole of the guard: a caller finds out rather than
    /// being shown a job that looks like it never started.
    /// </summary>
    public async Task<ImportJob?> FindAsync(string operationId, CancellationToken ct)
    {
        var doc = await Jobs
            .Find(Builders<ImportJobDocument>.Filter.Eq(j => j.OperationId, operationId))
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : Map(doc);
    }

    /// <summary>
    /// A provider's or a parser's error message is unbounded text this did
    /// not write, and it lands on a document an admin screen can read. Bounded
    /// on the way in, and never the provider response body verbatim — callers
    /// pass a summary, not a payload.
    /// </summary>
    private static string Trim(string error) =>
        error.Length > 1_000 ? error[..1_000] : error;

    private static ImportJobDocument ToDocument(ImportJob job) => new()
    {
        OperationId = job.OperationId,
        DefinitionId = job.DefinitionId.Value,
        VersionNumber = job.VersionNumber,
        SourceSha256 = job.SourceSha256,
        ParsePromptVersion = job.ParsePromptVersion,
        ArchiveKey = job.ArchiveKey,
        DraftId = job.DraftId?.ToString("D"),
        Stage = (int)job.Stage,
        State = job.State.ToString(),
        Attempts = job.Attempts,
        CreatedAt = job.CreatedAt.UtcDateTime,
        NextAttemptAt = job.NextAttemptAt?.UtcDateTime,
        LeaseUntil = job.LeaseUntil?.UtcDateTime,
        LeaseToken = job.LeaseToken,
        LastError = job.LastError,
        CompletedAt = job.CompletedAt?.UtcDateTime,
        TraceParent = job.TraceParent,
    };

    private static ImportJob Map(ImportJobDocument d) => new(
        d.OperationId,
        new ExamDefinitionId(d.DefinitionId),
        d.VersionNumber,
        d.SourceSha256,
        d.ParsePromptVersion,
        d.ArchiveKey,
        d.DraftId is null ? null : Guid.Parse(d.DraftId),
        Enum.IsDefined(typeof(ImportJobStage), d.Stage)
            ? (ImportJobStage)d.Stage
            : throw new ImportJobStageUnreadableException(d.OperationId, d.Stage),
        Enum.TryParse<ImportJobState>(d.State, ignoreCase: true, out var state)
            ? state
            : ImportJobState.Pending,
        d.Attempts,
        new DateTimeOffset(d.CreatedAt, TimeSpan.Zero),
        d.NextAttemptAt is { } next ? new DateTimeOffset(next, TimeSpan.Zero) : null,
        d.LeaseUntil is { } until ? new DateTimeOffset(until, TimeSpan.Zero) : null,
        d.LeaseToken,
        d.LastError,
        d.CompletedAt is { } done ? new DateTimeOffset(done, TimeSpan.Zero) : null,
        d.TraceParent);
}

/// <summary>
/// An import owed, on disk.
///
/// <b>`_id` is an opaque generated string, not the operation id.</b> Unlike
/// <c>MarkingJobDocument</c>, uniqueness on <see cref="OperationId"/> is a
/// named index created in <see cref="MongoContext.EnsureIndexesAsync"/>
/// rather than structural — deliberately, so index creation for this
/// collection lives in one place with every other index in the system
/// instead of being split between a constructor and a startup gate.
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class ImportJobDocument
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [BsonElement("operationId")]
    public string OperationId { get; set; } = string.Empty;

    [BsonElement("definitionId")]
    public string DefinitionId { get; set; } = string.Empty;

    [BsonElement("versionNumber")]
    public int VersionNumber { get; set; }

    [BsonElement("sourceSha256")]
    public string SourceSha256 { get; set; } = string.Empty;

    [BsonElement("parsePromptVersion")]
    public string ParsePromptVersion { get; set; } = string.Empty;

    [BsonElement("archiveKey")]
    public string ArchiveKey { get; set; } = string.Empty;

    [BsonElement("draftId")]
    [BsonIgnoreIfNull]
    public string? DraftId { get; set; }

    /// <summary>
    /// <see cref="ImportJobStage"/>'s underlying int, not its name.
    ///
    /// <b>Stored as an ordinal, deliberately, unlike <c>State</c>.</b> The
    /// only thing <see cref="MongoImportOutbox.AdvanceAsync"/> needs from
    /// Mongo is "is the value I'm about to write at or after the value
    /// already there" — a single indexed integer comparison the database can
    /// do inside the same filtered update as the lease check. A string
    /// comparison would sort stage names alphabetically, not by the order
    /// money is spent, and enforcing the rule in C# after a plain read would
    /// turn one atomic write back into the find-then-update race this whole
    /// store exists to avoid.
    /// </summary>
    [BsonElement("stage")]
    public int Stage { get; set; }

    [BsonElement("state")]
    public string State { get; set; } = string.Empty;

    [BsonElement("attempts")]
    public int Attempts { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("nextAttemptAt")]
    [BsonIgnoreIfNull]
    public DateTime? NextAttemptAt { get; set; }

    [BsonElement("leaseUntil")]
    [BsonIgnoreIfNull]
    public DateTime? LeaseUntil { get; set; }

    [BsonElement("leaseToken")]
    [BsonIgnoreIfNull]
    public string? LeaseToken { get; set; }

    [BsonElement("lastError")]
    [BsonIgnoreIfNull]
    public string? LastError { get; set; }

    [BsonElement("completedAt")]
    [BsonIgnoreIfNull]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// W3C `traceparent` of the request that enqueued this job. An
    /// identifier, not content — safe to persist and export where a request
    /// body would not be.
    /// </summary>
    [BsonElement("traceParent")]
    [BsonIgnoreIfNull]
    public string? TraceParent { get; set; }
}

/// <summary>
/// Package-upload history, in Mongo.
///
/// <b>A different collection from <see cref="ImportJobDocument"/> on
/// purpose.</b> The outbox row is the work queue and is keyed on the bytes;
/// this row is the attempt, including the ones that never became a job. Mixing
/// them would force a job row for a bomb the door refused, which is the
/// archive-retention the door exists to prevent.
/// </summary>
internal sealed class MongoPackageImportHistoryStore(MongoContext context, IClock clock)
    : IPackageImportHistoryStore
{
    private IMongoCollection<PackageImportHistoryDocument> History =>
        context.PackageImportHistory;

    public async Task RecordDoorRejectionAsync(PackageImportHistory record, CancellationToken ct)
    {
        var now = clock.UtcNow.UtcDateTime;
        var document = ToDocument(
            record with
            {
                Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
                OperationId = null,
                OriginalFileName = PackageImportHistoryBounds.SanitizeFileName(record.OriginalFileName),
                Findings = PackageImportHistoryBounds.BoundFindings(record.Findings),
                Result = PackageImportHistoryResult.DoorRejected,
                DraftId = null,
                Stage = null,
                CreatedAt = new DateTimeOffset(now, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(now, TimeSpan.Zero),
            });

        await History.InsertOneAsync(document, cancellationToken: ct);
    }

    public async Task RecordQueuedAsync(PackageImportHistory record, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(record.OperationId))
            throw new ArgumentException("A queued history row needs an operation id.", nameof(record));

        var now = clock.UtcNow.UtcDateTime;
        var fileName = PackageImportHistoryBounds.SanitizeFileName(record.OriginalFileName);
        var findings = PackageImportHistoryBounds.BoundFindings(record.Findings);
        var id = record.Id == Guid.Empty ? Guid.NewGuid().ToString("n") : record.Id.ToString("n");

        var filter = Builders<PackageImportHistoryDocument>.Filter.Eq(
            d => d.OperationId, record.OperationId);

        var upsert = Builders<PackageImportHistoryDocument>.Update
            .SetOnInsert(d => d.Id, id)
            .SetOnInsert(d => d.OperationId, record.OperationId)
            .SetOnInsert(d => d.CreatedAt, now)
            .SetOnInsert(d => d.Result, PackageImportHistoryResult.Queued.ToString())
            .SetOnInsert(d => d.Stage, (int?)ImportJobStage.Extracting)
            .SetOnInsert(d => d.DefinitionId, record.DefinitionId?.Value)
            .SetOnInsert(d => d.VersionNumber, record.VersionNumber)
            .SetOnInsert(d => d.SourceSha256, record.SourceSha256)
            .SetOnInsert(d => d.Findings, findings.Select(ToFinding).ToList())
            .Set(d => d.ActorId, record.ActorId)
            .Set(d => d.OriginalFileName, fileName)
            .Set(d => d.UpdatedAt, now);

        try
        {
            await History.UpdateOneAsync(
                filter, upsert, new UpdateOptions { IsUpsert = true }, ct);
        }
        catch (MongoWriteException e)
            when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Two accepted uploads of the same bytes raced the insert. The
            // unique index on operationId decided; refresh the actor on the
            // row that won.
            await History.UpdateOneAsync(
                filter,
                Builders<PackageImportHistoryDocument>.Update
                    .Set(d => d.ActorId, record.ActorId)
                    .Set(d => d.OriginalFileName, fileName)
                    .Set(d => d.UpdatedAt, now),
                cancellationToken: ct);
        }

        // A re-upload of a failed attempt is the operator asking to try
        // again. Completed and Running stay put: the first is already done,
        // and resetting a live worker's result would lie about work in hand.
        await History.UpdateOneAsync(
            Builders<PackageImportHistoryDocument>.Filter.And(
                filter,
                Builders<PackageImportHistoryDocument>.Filter.In(
                    d => d.Result,
                    new[]
                    {
                        PackageImportHistoryResult.Failed.ToString(),
                        PackageImportHistoryResult.Rejected.ToString(),
                    })),
            Builders<PackageImportHistoryDocument>.Update
                .Set(d => d.Result, PackageImportHistoryResult.Queued.ToString())
                .Set(d => d.Stage, (int?)ImportJobStage.Extracting)
                .Set(d => d.Findings, findings.Select(ToFinding).ToList())
                .Set(d => d.UpdatedAt, now),
            cancellationToken: ct);
    }

    public async Task ApplyTransitionAsync(
        string operationId,
        PackageImportHistoryResult result,
        ImportJobStage? stage,
        Guid? draftId,
        IReadOnlyList<PackageFinding>? findings,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var now = clock.UtcNow.UtcDateTime;
        var id = Guid.NewGuid().ToString("n");

        var update = Builders<PackageImportHistoryDocument>.Update
            .SetOnInsert(d => d.Id, id)
            .SetOnInsert(d => d.OperationId, operationId)
            .SetOnInsert(d => d.ActorId, string.Empty)
            .SetOnInsert(d => d.OriginalFileName, "unnamed.zip")
            .SetOnInsert(d => d.CreatedAt, now)
            .Set(d => d.Result, result.ToString())
            .Set(d => d.UpdatedAt, now);

        if (stage is { } recorded)
            update = update.Set(d => d.Stage, (int?)recorded);

        if (draftId is { } draft)
            update = update.Set(d => d.DraftId, draft.ToString("D"));

        if (findings is not null)
        {
            update = update.Set(
                d => d.Findings,
                PackageImportHistoryBounds.BoundFindings(findings).Select(ToFinding).ToList());
        }

        try
        {
            await History.UpdateOneAsync(
                Builders<PackageImportHistoryDocument>.Filter.Eq(d => d.OperationId, operationId),
                update,
                new UpdateOptions { IsUpsert = true },
                ct);
        }
        catch (MongoWriteException e)
            when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The door's queued insert won the race. Retry without upsert so
            // the transition still lands on that row.
            var retry = Builders<PackageImportHistoryDocument>.Update
                .Set(d => d.Result, result.ToString())
                .Set(d => d.UpdatedAt, now);

            if (stage is { } recordedAgain)
                retry = retry.Set(d => d.Stage, (int?)recordedAgain);

            if (draftId is { } draftAgain)
                retry = retry.Set(d => d.DraftId, draftAgain.ToString("D"));

            if (findings is not null)
            {
                retry = retry.Set(
                    d => d.Findings,
                    PackageImportHistoryBounds.BoundFindings(findings).Select(ToFinding).ToList());
            }

            await History.UpdateOneAsync(
                Builders<PackageImportHistoryDocument>.Filter.Eq(d => d.OperationId, operationId),
                retry,
                cancellationToken: ct);
        }
    }

    public async Task<PackageImportHistory?> FindByOperationAsync(
        string operationId, CancellationToken ct)
    {
        var document = await History
            .Find(d => d.OperationId == operationId)
            .FirstOrDefaultAsync(ct);

        return document is null ? null : Map(document);
    }

    public async Task<PackageImportHistory?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        var document = await History
            .Find(d => d.Id == id.ToString("n"))
            .FirstOrDefaultAsync(ct);

        return document is null ? null : Map(document);
    }

    public async Task<PackageImportHistoryPage> QueryAsync(
        PackageImportHistoryQuery query, CancellationToken ct)
    {
        query.Validate();

        var filter = Builders<PackageImportHistoryDocument>.Filter.Empty;

        if (query.Result is { } result)
        {
            filter &= Builders<PackageImportHistoryDocument>.Filter.Eq(
                d => d.Result, result.ToString());
        }

        if (query.Stage is { } stage)
        {
            filter &= Builders<PackageImportHistoryDocument>.Filter.Eq(
                d => d.Stage, (int?)stage);
        }

        if (query.ActorId is { Length: > 0 } actor)
        {
            filter &= Builders<PackageImportHistoryDocument>.Filter.Eq(d => d.ActorId, actor);
        }

        if (query.From is { } from)
        {
            filter &= Builders<PackageImportHistoryDocument>.Filter.Gte(
                d => d.UpdatedAt, from.UtcDateTime);
        }

        if (query.To is { } to)
        {
            filter &= Builders<PackageImportHistoryDocument>.Filter.Lte(
                d => d.UpdatedAt, to.UtcDateTime);
        }

        var sort = Builders<PackageImportHistoryDocument>.Sort
            .Descending(d => d.UpdatedAt)
            .Descending(d => d.CreatedAt)
            .Descending(d => d.Id);

        var total = await History.CountDocumentsAsync(filter, cancellationToken: ct);
        var skip = (query.Page - 1) * query.PageSize;

        var items = await History
            .Find(filter)
            .Sort(sort)
            .Skip(skip)
            .Limit(query.PageSize)
            .ToListAsync(ct);

        return new PackageImportHistoryPage(
            [.. items.Select(Map)], total, query.Page, query.PageSize);
    }

    private static PackageImportFindingDocument ToFinding(PackageFinding finding) => new()
    {
        Severity = finding.Severity,
        Code = finding.Code,
        Path = finding.Path,
        Message = finding.Message,
    };

    private static PackageImportHistoryDocument ToDocument(PackageImportHistory record) => new()
    {
        Id = record.Id.ToString("n"),
        OperationId = record.OperationId,
        ActorId = record.ActorId,
        OriginalFileName = record.OriginalFileName,
        DefinitionId = record.DefinitionId?.Value,
        VersionNumber = record.VersionNumber,
        SourceSha256 = record.SourceSha256,
        DraftId = record.DraftId?.ToString("D"),
        Stage = record.Stage is { } stage ? (int?)stage : null,
        Result = record.Result.ToString(),
        Findings = record.Findings.Select(ToFinding).ToList(),
        CreatedAt = record.CreatedAt.UtcDateTime,
        UpdatedAt = record.UpdatedAt.UtcDateTime,
    };

    private static PackageImportHistory Map(PackageImportHistoryDocument d) => new(
        Guid.TryParse(d.Id, out var id) ? id : Guid.Empty,
        d.OperationId,
        d.ActorId,
        d.OriginalFileName,
        string.IsNullOrEmpty(d.DefinitionId) ? null : new ExamDefinitionId(d.DefinitionId),
        d.VersionNumber,
        d.SourceSha256,
        d.DraftId is null ? null : Guid.Parse(d.DraftId),
        d.Stage is { } stage && Enum.IsDefined(typeof(ImportJobStage), stage)
            ? (ImportJobStage)stage
            : null,
        Enum.TryParse<PackageImportHistoryResult>(d.Result, ignoreCase: true, out var result)
            ? result
            : PackageImportHistoryResult.Queued,
        d.Findings.Select(f => new PackageFinding(f.Severity, f.Code, f.Path, f.Message)).ToArray(),
        new DateTimeOffset(d.CreatedAt, TimeSpan.Zero),
        new DateTimeOffset(d.UpdatedAt, TimeSpan.Zero));
}

[BsonIgnoreExtraElements]
internal sealed class PackageImportHistoryDocument
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [BsonElement("operationId")]
    [BsonIgnoreIfNull]
    public string? OperationId { get; set; }

    [BsonElement("actorId")]
    public string ActorId { get; set; } = string.Empty;

    [BsonElement("originalFileName")]
    public string OriginalFileName { get; set; } = "unnamed.zip";

    [BsonElement("definitionId")]
    [BsonIgnoreIfNull]
    public string? DefinitionId { get; set; }

    [BsonElement("versionNumber")]
    [BsonIgnoreIfNull]
    public int? VersionNumber { get; set; }

    [BsonElement("sourceSha256")]
    [BsonIgnoreIfNull]
    public string? SourceSha256 { get; set; }

    [BsonElement("draftId")]
    [BsonIgnoreIfNull]
    public string? DraftId { get; set; }

    [BsonElement("stage")]
    [BsonIgnoreIfNull]
    public int? Stage { get; set; }

    [BsonElement("result")]
    public string Result { get; set; } = string.Empty;

    [BsonElement("findings")]
    public List<PackageImportFindingDocument> Findings { get; set; } = [];

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class PackageImportFindingDocument
{
    [BsonElement("severity")]
    public string Severity { get; set; } = string.Empty;

    [BsonElement("code")]
    public string Code { get; set; } = string.Empty;

    [BsonElement("path")]
    public string Path { get; set; } = string.Empty;

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;
}
