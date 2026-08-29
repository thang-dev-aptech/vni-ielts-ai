using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using Vni.Ielts.Infrastructure.Persistence.Identity;

namespace Vni.Ielts.Infrastructure.Persistence;

/// <summary>
/// Collection access and index definitions in one place.
///
/// Indexes are created here rather than by a migration tool because several of
/// them are <b>correctness constraints, not performance tuning</b> — a unique
/// index is the only thing that makes a uniqueness rule hold under
/// concurrency, and an application-level "check then insert" loses the race
/// every time under the retry pressure mobile clients generate.
/// </summary>
public sealed class MongoContext
{
    private readonly IMongoDatabase _db;

    public MongoContext(IOptions<MongoOptions> options)
    {
        var settings = MongoClientSettings.FromConnectionString(options.Value.ConnectionString);

        /*
         * <b>F4.1 — every database command becomes a span.</b> Subscribed at
         * the cluster level because that is the only place the driver exposes
         * command events; a repository-level wrapper would have to be
         * remembered at each of ~15 call sites and would miss the driver's own
         * traffic entirely.
         *
         * <b>Command TEXT is deliberately not captured.</b> The default is
         * already off, and it stays off: a Mongo command carries filter values
         * — an email address, a learner's answer, a session id — and a span
         * exported to a collector is exactly the kind of place PII should
         * never accumulate. What is recorded is the collection, the operation
         * and the duration, which is what a latency question actually needs.
         * → F4.2
         */
        settings.ClusterConfigurator = cluster =>
            cluster.Subscribe(new DiagnosticsActivityEventSubscriber(
                new InstrumentationOptions { CaptureCommandText = false }));

        _db = new MongoClient(settings).GetDatabase(options.Value.Database);
    }

    /// <summary>Exposed for cross-cutting middleware that stores its own state.</summary>
    public IMongoDatabase Database => _db;

    internal IMongoCollection<UserDocument> Users => _db.GetCollection<UserDocument>("users");

    internal IMongoCollection<UserIdentityDocument> UserIdentities =>
        _db.GetCollection<UserIdentityDocument>("user_identities");

    internal IMongoCollection<RoleDocument> Roles => _db.GetCollection<RoleDocument>("roles");

    internal IMongoCollection<RefreshTokenDocument> RefreshTokens =>
        _db.GetCollection<RefreshTokenDocument>("refresh_tokens");

    internal IMongoCollection<Exams.ExamVersionDocument> ExamVersions =>
        _db.GetCollection<Exams.ExamVersionDocument>("exam_versions");

    /// <summary>
    /// What VNI is allowed to do with each body of source material.
    ///
    /// <b>An empty collection means nothing may be published</b>, which is the
    /// correct reading rather than a degraded one: a source with no record has
    /// no rights. → <c>ContentRightsPolicy</c>, <c>M-53</c>
    /// </summary>
    internal IMongoCollection<Content.ContentSourceDocument> ContentSources =>
        _db.GetCollection<Content.ContentSourceDocument>("content_sources");

    internal IMongoCollection<Exams.ExamSessionDocument> ExamSessions =>
        _db.GetCollection<Exams.ExamSessionDocument>("exam_sessions");

    internal IMongoCollection<Exams.AnswerSheetDocument> AnswerSheets =>
        _db.GetCollection<Exams.AnswerSheetDocument>("answer_sheets");

    internal IMongoCollection<Exams.SectionResultDocument> SectionResults =>
        _db.GetCollection<Exams.SectionResultDocument>("section_results");

    internal IMongoCollection<Exams.SectionMarkingDocument> SectionMarkings =>
        _db.GetCollection<Exams.SectionMarkingDocument>("section_markings");

    /// <summary>
    /// Markings that are owed but not yet produced.
    ///
    /// Separate from <c>section_markings</c> because the two carry different
    /// facts: one is a judgement that exists, the other is the durable record
    /// that a judgement is <i>due</i>. Closing a section writes the second; a
    /// worker turns it into the first. → <c>IMarkingOutbox</c>
    /// </summary>
    internal IMongoCollection<Exams.MarkingJobDocument> MarkingJobs =>
        _db.GetCollection<Exams.MarkingJobDocument>("marking_jobs");

    /// <summary>
    /// Refuses to start against a node that cannot do transactions.
    ///
    /// <para>
    /// <b>Why this exists.</b> On 2026-08-20 the API silently connected to a
    /// Homebrew <c>mongodb-community</c> bound to <c>127.0.0.1:27017</c>
    /// instead of the project's container. Docker binds <c>0.0.0.0</c>, the
    /// Homebrew daemon binds the more specific <c>127.0.0.1</c>, and
    /// <c>localhost</c> picks the latter. Registration, login, refresh
    /// rotation and reuse detection all passed against it — on a standalone
    /// node with no transaction support at all.
    /// </para>
    ///
    /// <para>
    /// Nothing failed, and nothing would have failed until token deduction met
    /// the retry concurrency mobile clients generate by design (threat T22),
    /// at which point a learner gets debited twice. A configuration mistake
    /// that only surfaces in production is not one you catch by being careful.
    /// </para>
    ///
    /// So it is a boot failure. Same reasoning as the JWT signing-key guard:
    /// a deployment that will not start is far cheaper than one that starts
    /// wrong. → ADR-0011, R15
    /// </summary>
    public async Task AssertReplicaSetAsync(CancellationToken ct)
    {
        BsonDocument status;
        try
        {
            status = await _db.RunCommandAsync<BsonDocument>(
                new BsonDocument("hello", 1), cancellationToken: ct);
        }
        catch (MongoException e)
        {
            throw new InvalidOperationException(
                "Could not reach MongoDB to verify its topology. "
                + $"Connection target: {Describe()}. Underlying error: {e.Message}", e);
        }

        // A replica set member reports `setName`. A standalone node does not.
        if (!status.Contains("setName"))
        {
            throw new InvalidOperationException(
                $"""
                MongoDB at {Describe()} is NOT a replica set.

                Multi-document transactions are unavailable on a standalone node, and
                token deduction must be atomic with session creation or an aggressive
                mobile retry debits a learner twice (threat T22).

                This check exists because the failure is otherwise invisible: every
                feature works on a standalone node until real retry concurrency arrives.

                Most likely cause: another mongod is bound to the same port. Docker binds
                0.0.0.0 while a Homebrew or system mongod binds 127.0.0.1, which wins for
                'localhost'. Check with:

                    lsof -nP -iTCP:27017 -sTCP:LISTEN

                Start the project's stack with `pnpm infra:up`; it exposes the replica set
                on host port 27018 precisely to avoid that collision.

                -> docs/decisions/0011-mongodb-single-node-replica-set.md
                """);
        }
    }

    private string Describe() => $"{_db.Client.Settings.Server} (database '{_db.DatabaseNamespace.DatabaseName}')";

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        // One account per address. This is what actually prevents a duplicate
        // registration when two requests arrive together — the check inside
        // RegisterUser is a courtesy that produces a clean error message, not
        // the guarantee.
        await Users.Indexes.CreateOneAsync(
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true, Name = "ux_users_email" }),
            cancellationToken: ct);

        // One identity per (provider, subject). Without it, a replayed OAuth
        // callback creates a second identity row for the same social account.
        await UserIdentities.Indexes.CreateOneAsync(
            new CreateIndexModel<UserIdentityDocument>(
                Builders<UserIdentityDocument>.IndexKeys
                    .Ascending(i => i.Provider)
                    .Ascending(i => i.ProviderUserId),
                new CreateIndexOptions { Unique = true, Name = "ux_identities_provider_subject" }),
            cancellationToken: ct);

        await UserIdentities.Indexes.CreateOneAsync(
            new CreateIndexModel<UserIdentityDocument>(
                Builders<UserIdentityDocument>.IndexKeys.Ascending(i => i.UserId),
                new CreateIndexOptions { Name = "ix_identities_user" }),
            cancellationToken: ct);

        await Roles.Indexes.CreateOneAsync(
            new CreateIndexModel<RoleDocument>(
                Builders<RoleDocument>.IndexKeys.Ascending(r => r.Name),
                new CreateIndexOptions { Unique = true, Name = "ux_roles_name" }),
            cancellationToken: ct);

        await RefreshTokens.Indexes.CreateOneAsync(
            new CreateIndexModel<RefreshTokenDocument>(
                Builders<RefreshTokenDocument>.IndexKeys.Ascending(t => t.TokenHash),
                new CreateIndexOptions { Unique = true, Name = "ux_refresh_token_hash" }),
            cancellationToken: ct);

        // Revoking a family on reuse detection reads by this.
        await RefreshTokens.Indexes.CreateOneAsync(
            new CreateIndexModel<RefreshTokenDocument>(
                Builders<RefreshTokenDocument>.IndexKeys
                    .Ascending(t => t.UserId)
                    .Ascending(t => t.FamilyId),
                new CreateIndexOptions { Name = "ix_refresh_user_family" }),
            cancellationToken: ct);

        // Idempotency records expire themselves after 24 hours. Without a TTL
        // this collection grows forever — every guarded request writes one — and
        // the unique _id is what makes a concurrent duplicate detectable rather
        // than silently executing twice.
        await _db.GetCollection<BsonDocument>("idempotency_keys").Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("createdAt"),
                new CreateIndexOptions
                {
                    Name = "ttl_idempotency",
                    ExpireAfter = TimeSpan.FromHours(24),
                }),
            cancellationToken: ct);

        // The catalogue listing filters on status and sorts by title.
        await ExamVersions.Indexes.CreateOneAsync(
            new CreateIndexModel<Exams.ExamVersionDocument>(
                Builders<Exams.ExamVersionDocument>.IndexKeys
                    .Ascending(v => v.Status)
                    .Ascending(v => v.Title),
                new CreateIndexOptions { Name = "ix_exam_versions_status_title" }),
            cancellationToken: ct);

        /*
         * The publish gate reads the registry by whichever exam id it holds,
         * on every publish attempt. Two indexes because they are two access
         * shapes — and both are multikey, since one source can produce several
         * papers.
         *
         * Not unique. Two records claiming the same exam would be a data
         * defect, but a unique index here would make the *seed* fail rather
         * than the bad grant, and a registry that cannot be seeded fails open
         * for everything else.
         */
        await ContentSources.Indexes.CreateOneAsync(
            new CreateIndexModel<Content.ContentSourceDocument>(
                Builders<Content.ContentSourceDocument>.IndexKeys.Ascending(s => s.ExamVersionIds),
                new CreateIndexOptions { Name = "ix_content_sources_exam_version" }),
            cancellationToken: ct);

        await ContentSources.Indexes.CreateOneAsync(
            new CreateIndexModel<Content.ContentSourceDocument>(
                Builders<Content.ContentSourceDocument>.IndexKeys
                    .Ascending(s => s.ExamDefinitionIds),
                new CreateIndexOptions { Name = "ix_content_sources_exam_definition" }),
            cancellationToken: ct);

        // "Bài đang làm dở" and the attempt history both read by this.
        await ExamSessions.Indexes.CreateOneAsync(
            new CreateIndexModel<Exams.ExamSessionDocument>(
                Builders<Exams.ExamSessionDocument>.IndexKeys
                    .Ascending(s => s.UserId)
                    .Descending(s => s.StartedAt),
                new CreateIndexOptions { Name = "ix_exam_sessions_user_started" }),
            cancellationToken: ct);

        await SectionResults.Indexes.CreateOneAsync(
            new CreateIndexModel<Exams.SectionResultDocument>(
                Builders<Exams.SectionResultDocument>.IndexKeys.Ascending(r => r.SessionId),
                new CreateIndexOptions { Name = "ix_section_results_session" }),
            cancellationToken: ct);

        await SectionMarkings.Indexes.CreateOneAsync(
            new CreateIndexModel<Exams.SectionMarkingDocument>(
                Builders<Exams.SectionMarkingDocument>.IndexKeys.Ascending(m => m.SessionId),
                new CreateIndexOptions { Name = "ix_section_markings_session" }),
            cancellationToken: ct);

        // The worker's claim scans by state and due time on every poll, and a
        // results screen reads by sitting. Two indexes, one per access shape.
        await MarkingJobs.Indexes.CreateOneAsync(
            new CreateIndexModel<Exams.MarkingJobDocument>(
                Builders<Exams.MarkingJobDocument>.IndexKeys
                    .Ascending(j => j.State)
                    .Ascending(j => j.NextAttemptAt),
                new CreateIndexOptions { Name = "ix_marking_jobs_due" }),
            cancellationToken: ct);

        await MarkingJobs.Indexes.CreateOneAsync(
            new CreateIndexModel<Exams.MarkingJobDocument>(
                Builders<Exams.MarkingJobDocument>.IndexKeys.Ascending(j => j.SessionId),
                new CreateIndexOptions { Name = "ix_marking_jobs_session" }),
            cancellationToken: ct);

        // Expired tokens remove themselves. A TTL index does this without a
        // cleanup job — and the collection would otherwise grow forever, since
        // every rotation writes a new row.
        await RefreshTokens.Indexes.CreateOneAsync(
            new CreateIndexModel<RefreshTokenDocument>(
                Builders<RefreshTokenDocument>.IndexKeys.Ascending(t => t.ExpiresAt),
                new CreateIndexOptions
                {
                    Name = "ttl_refresh_expiry",
                    ExpireAfter = TimeSpan.Zero,
                }),
            cancellationToken: ct);
    }
}
