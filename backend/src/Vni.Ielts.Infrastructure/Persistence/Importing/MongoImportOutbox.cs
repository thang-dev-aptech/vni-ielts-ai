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

        return claimed is null ? null : Map(claimed);
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

    public async Task<bool> AdvanceAsync(
        string operationId, string leaseToken, ImportJobStage stage, Guid? draftId, CancellationToken ct)
    {
        // A stage already reached is money already spent — the update never
        // moves it backwards, it only ever records the caller's own forward
        // progress under its own lease. DraftId is left untouched when the
        // caller has none yet, rather than overwritten with null.
        var update = Builders<ImportJobDocument>.Update.Set(j => j.Stage, stage.ToString());
        if (draftId is { } id) update = update.Set(j => j.DraftId, id.ToString("D"));

        var advanced = await Jobs.UpdateOneAsync(Mine(operationId, leaseToken), update, cancellationToken: ct);

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
        Stage = job.Stage.ToString(),
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
        Enum.TryParse<ImportJobStage>(d.Stage, ignoreCase: true, out var stage)
            ? stage
            : ImportJobStage.Extracting,
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

    [BsonElement("stage")]
    public string Stage { get; set; } = string.Empty;

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
