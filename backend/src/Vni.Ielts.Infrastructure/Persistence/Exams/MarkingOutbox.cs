using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

/// <summary>
/// Marking jobs, in Mongo.
///
/// <b>Every write here is one statement.</b> Read-then-write is two, and two
/// workers fit between them — which for this collection means two paid
/// evaluations for one essay. The claim, the renewal, the completion and both
/// failure paths are all a single filtered update, and the filter carries the
/// condition that makes it safe: the state that may be transitioned from, and
/// the lease token of whoever is allowed to do it.
/// </summary>
internal sealed class MongoMarkingOutbox(MongoContext context) : IMarkingOutbox
{
    private IMongoCollection<MarkingJobDocument> Jobs => context.MarkingJobs;

    public async Task<bool> EnqueueAsync(MarkingJob job, CancellationToken ct)
    {
        try
        {
            await Jobs.InsertOneAsync(
                new MarkingJobDocument
                {
                    Id = job.OperationId,
                    SessionId = job.SessionId.Value,
                    Module = job.Module.ToString(),
                    RubricVersion = job.RubricVersion,
                    State = job.State.ToString(),
                    Attempts = 0,
                    CreatedAt = job.CreatedAt.UtcDateTime,
                    NextAttemptAt = job.CreatedAt.UtcDateTime,
                    TraceParent = job.TraceParent,
                },
                cancellationToken: ct);

            return true;
        }
        catch (MongoWriteException e)
            when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            /*
             * <b>Already owed, and that is the ordinary answer.</b> A retried
             * submit, two tabs on "Tiếp theo", the expiry sweep meeting a
             * learner's own submit — every one of them re-closes the same
             * section, and every one of them must leave exactly one job.
             *
             * The unique `_id` is what enforces that, in the database rather
             * than in a caller's memory. Checking first and inserting second
             * would be two statements with a race between them.
             */
            return false;
        }
    }

    public async Task<MarkingJob?> ClaimAsync(
        string leaseToken, DateTimeOffset now, TimeSpan lease, CancellationToken ct)
    {
        var at = now.UtcDateTime;

        /*
         * <b>Claimable means one of three things, and the third is the recovery
         * path.</b> Pending is new work; Retryable is work whose backoff has
         * elapsed; Running with an expired lease is work whose worker died. All
         * three are the same update, because a job that has been abandoned is
         * indistinguishable from one that was never started — which is exactly
         * why the lease exists.
         */
        var claimable = Builders<MarkingJobDocument>.Filter.And(
            Builders<MarkingJobDocument>.Filter.In(
                j => j.State,
                new[]
                {
                    MarkingJobState.Pending.ToString(),
                    MarkingJobState.Retryable.ToString(),
                    MarkingJobState.Running.ToString(),
                }),
            Builders<MarkingJobDocument>.Filter.Lte(j => j.NextAttemptAt, at),
            Builders<MarkingJobDocument>.Filter.Or(
                Builders<MarkingJobDocument>.Filter.Eq(j => j.LeaseUntil, null),
                Builders<MarkingJobDocument>.Filter.Lt(j => j.LeaseUntil, at)));

        var claimed = await Jobs.FindOneAndUpdateAsync(
            claimable,
            Builders<MarkingJobDocument>.Update
                .Set(j => j.State, MarkingJobState.Running.ToString())
                .Set(j => j.LeaseToken, leaseToken)
                .Set(j => j.LeaseUntil, at.Add(lease))
                .Inc(j => j.Attempts, 1),
            new FindOneAndUpdateOptions<MarkingJobDocument>
            {
                ReturnDocument = ReturnDocument.After,
                // Oldest first. A sitting that has been waiting must not be
                // starved by a steady arrival of newer ones.
                Sort = Builders<MarkingJobDocument>.Sort.Ascending(j => j.CreatedAt),
            },
            ct);

        return claimed is null ? null : Map(claimed);
    }

    /// <summary>Every transition names the job and the worker that owns it.</summary>
    private static FilterDefinition<MarkingJobDocument> Mine(string operationId, string leaseToken) =>
        Builders<MarkingJobDocument>.Filter.And(
            Builders<MarkingJobDocument>.Filter.Eq(j => j.Id, operationId),
            Builders<MarkingJobDocument>.Filter.Eq(j => j.LeaseToken, leaseToken));

    public async Task<bool> RenewAsync(
        string operationId, string leaseToken, DateTimeOffset until, CancellationToken ct)
    {
        var renewed = await Jobs.UpdateOneAsync(
            Mine(operationId, leaseToken),
            Builders<MarkingJobDocument>.Update.Set(j => j.LeaseUntil, until.UtcDateTime),
            cancellationToken: ct);

        return renewed.MatchedCount > 0;
    }

    public async Task<bool> CompleteAsync(
        string operationId, string leaseToken, DateTimeOffset at, CancellationToken ct)
    {
        var done = await Jobs.UpdateOneAsync(
            Mine(operationId, leaseToken),
            Builders<MarkingJobDocument>.Update
                .Set(j => j.State, MarkingJobState.Completed.ToString())
                .Set(j => j.CompletedAt, at.UtcDateTime)
                .Set(j => j.LeaseUntil, null)
                .Set(j => j.LeaseToken, null),
            cancellationToken: ct);

        return done.MatchedCount > 0;
    }

    public async Task<bool> RetryAsync(
        string operationId, string leaseToken, DateTimeOffset nextAttemptAt, string error,
        CancellationToken ct)
    {
        var scheduled = await Jobs.UpdateOneAsync(
            Mine(operationId, leaseToken),
            Builders<MarkingJobDocument>.Update
                .Set(j => j.State, MarkingJobState.Retryable.ToString())
                .Set(j => j.NextAttemptAt, nextAttemptAt.UtcDateTime)
                .Set(j => j.LastError, Trim(error))
                .Set(j => j.LeaseUntil, null)
                .Set(j => j.LeaseToken, null),
            cancellationToken: ct);

        return scheduled.MatchedCount > 0;
    }

    public async Task<bool> FailAsync(
        string operationId, string leaseToken, string error, CancellationToken ct)
    {
        var dead = await Jobs.UpdateOneAsync(
            Mine(operationId, leaseToken),
            Builders<MarkingJobDocument>.Update
                .Set(j => j.State, MarkingJobState.Failed.ToString())
                .Set(j => j.LastError, Trim(error))
                .Set(j => j.LeaseUntil, null)
                .Set(j => j.LeaseToken, null),
            cancellationToken: ct);

        return dead.MatchedCount > 0;
    }

    public async Task<IReadOnlyList<MarkingJob>> ListAsync(
        ExamSessionId sessionId, CancellationToken ct)
    {
        var jobs = await Jobs
            .Find(j => j.SessionId == sessionId.Value)
            .ToListAsync(ct);

        return [.. jobs.Select(Map)];
    }

    /// <summary>
    /// A provider's error message is unbounded text this did not write, and it
    /// is stored on a document a results screen reads. Bounded on the way in.
    /// </summary>
    private static string Trim(string error) =>
        error.Length > 1_000 ? error[..1_000] : error;

    /// <summary>
    /// Depth and oldest age, read together. → F4.3
    ///
    /// <b>Sorted-first rather than an aggregation.</b> The count and the
    /// oldest document use the same filter, and `ix_marking_jobs_due` already
    /// orders by the fields involved — so this is one count and one indexed
    /// find, not a pipeline. It runs on a metrics callback, which means it
    /// runs on a schedule forever: cheap matters more here than elegant.
    /// </summary>
    public async Task<QueueBacklog> BacklogAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        var at = asOf.UtcDateTime;

        // Owed = nobody is inside it. Pending and Retryable never are; a
        // Running job whose lease has expired is one whose worker died, and
        // that is backlog again.
        var owed = Builders<MarkingJobDocument>.Filter.And(
            Builders<MarkingJobDocument>.Filter.In(
                j => j.State,
                new[]
                {
                    MarkingJobState.Pending.ToString(),
                    MarkingJobState.Retryable.ToString(),
                    MarkingJobState.Running.ToString(),
                }),
            Builders<MarkingJobDocument>.Filter.Or(
                Builders<MarkingJobDocument>.Filter.Eq(j => j.LeaseUntil, null),
                Builders<MarkingJobDocument>.Filter.Lt(j => j.LeaseUntil, at)));

        var depth = await Jobs.CountDocumentsAsync(owed, cancellationToken: ct);

        if (depth == 0) return QueueBacklog.Empty;

        var oldest = await Jobs
            .Find(owed)
            .SortBy(j => j.CreatedAt)
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        if (oldest is null) return new QueueBacklog(depth, TimeSpan.Zero);

        // Never negative: a clock skew between the writer and this reader
        // would otherwise report an age from the future, which every
        // dashboard renders as nonsense.
        var age = at - oldest.CreatedAt;
        return new QueueBacklog(depth, age > TimeSpan.Zero ? age : TimeSpan.Zero);
    }

    private static MarkingJob Map(MarkingJobDocument d) => new(
        d.Id,
        new ExamSessionId(d.SessionId),
        Enum.TryParse<ExamModule>(d.Module, ignoreCase: true, out var module)
            ? module
            : ExamModule.Writing,
        d.RubricVersion,
        Enum.TryParse<MarkingJobState>(d.State, ignoreCase: true, out var state)
            ? state
            : MarkingJobState.Pending,
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
/// A marking owed, on disk.
///
/// <b>`_id` is the operation id, which is what makes the enqueue idempotent.</b>
/// A unique index would do the same job; using `_id` means the uniqueness is
/// structural rather than a thing somebody has to remember to create.
/// </summary>
internal sealed class MarkingJobDocument
{
    [MongoDB.Bson.Serialization.Attributes.BsonId]
    public string Id { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("module")]
    public string Module { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("rubricVersion")]
    public string RubricVersion { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("state")]
    public string State { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("attempts")]
    public int Attempts { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("nextAttemptAt")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public DateTime? NextAttemptAt { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("leaseUntil")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public DateTime? LeaseUntil { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("leaseToken")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public string? LeaseToken { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("lastError")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public string? LastError { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("completedAt")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// W3C `traceparent` of the request that enqueued this job. → F4.2
    ///
    /// <b>An identifier, not content.</b> A traceparent is a trace id, a span
    /// id and two flags — it names a request without describing it, which is
    /// why it is safe to persist and to export where a request body would not
    /// be.
    /// </summary>
    [MongoDB.Bson.Serialization.Attributes.BsonElement("traceParent")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public string? TraceParent { get; set; }
}
