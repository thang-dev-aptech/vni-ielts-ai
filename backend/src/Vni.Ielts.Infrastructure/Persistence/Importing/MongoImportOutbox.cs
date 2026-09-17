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
