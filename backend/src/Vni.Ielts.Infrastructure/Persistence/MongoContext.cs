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

    internal IMongoCollection<StaffInvitationDocument> StaffInvitations =>
        _db.GetCollection<StaffInvitationDocument>("staff_invitations");

    internal IMongoCollection<PrivacyRequestDocument> PrivacyRequests =>
        _db.GetCollection<PrivacyRequestDocument>("privacy_requests");

    internal IMongoCollection<PersonalDataExportDocument> PersonalDataExports =>
        _db.GetCollection<PersonalDataExportDocument>("personal_data_exports");

    internal IMongoCollection<AdminPrivilegeCoordinationDocument> AdminPrivilegeCoordination =>
        _db.GetCollection<AdminPrivilegeCoordinationDocument>("admin_privilege_coordination");

    internal IMongoCollection<RefreshTokenDocument> RefreshTokens =>
        _db.GetCollection<RefreshTokenDocument>("refresh_tokens");

    internal IMongoCollection<Exams.ExamVersionDocument> ExamVersions =>
        _db.GetCollection<Exams.ExamVersionDocument>("exam_versions");

    internal IMongoCollection<Exams.ExamPackageDocument> ExamPackages =>
        _db.GetCollection<Exams.ExamPackageDocument>("exam_packages");

    internal IMongoCollection<Exams.ParsedExamCandidateDocument> ParsedExamCandidates =>
        _db.GetCollection<Exams.ParsedExamCandidateDocument>("parsed_exam_candidates");

    internal IMongoCollection<Exams.SourceDocumentGroupingProposalDocument> SourceDocumentGroupingProposals =>
        _db.GetCollection<Exams.SourceDocumentGroupingProposalDocument>("source_document_grouping_proposals");

    /// <summary>
    /// AI extraction attempt log — provider call metadata for a package parse,
    /// an audit fact. → <c>ExamExtractionRunMetadata</c>
    /// </summary>
    internal IMongoCollection<Exams.ExamExtractionRunDocument> ExamExtractionRuns =>
        _db.GetCollection<Exams.ExamExtractionRunDocument>("exam_extraction_runs");

    internal IMongoCollection<Media.MediaAssetDocument> MediaAssets =>
        _db.GetCollection<Media.MediaAssetDocument>("media_assets");

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

    internal IMongoCollection<Explanations.PersonalizedExplanationDocument> PersonalizedExplanations =>
        _db.GetCollection<Explanations.PersonalizedExplanationDocument>("personalized_explanations");

    internal IMongoCollection<Explanations.CanonicalExplanationDocument> CanonicalExplanations =>
        _db.GetCollection<Explanations.CanonicalExplanationDocument>("canonical_explanations");

    /// <summary>
    /// The two libraries — <c>P-22</c>. Independent collections; neither
    /// references <c>exam_versions</c> yet, and <c>relatedExamIds</c> is the
    /// reserved seam for the day one does.
    /// </summary>
    internal IMongoCollection<Library.LibraryDocumentDocument> LibraryDocuments =>
        _db.GetCollection<Library.LibraryDocumentDocument>("library_documents");

    internal IMongoCollection<Library.ArticleDocument> Articles =>
        _db.GetCollection<Library.ArticleDocument>("articles");

    /// <summary>
    /// The usage ledger — <c>P-14</c>. Append and read only; see
    /// <see cref="Vni.Ielts.Application.Usage.IUsageLedger"/> for why there is
    /// no update or delete anywhere above this property.
    /// </summary>
    internal IMongoCollection<Usage.UsageEntryDocument> UsageLedger =>
        _db.GetCollection<Usage.UsageEntryDocument>("usage_ledger");

    internal IMongoCollection<Learning.LearnerGoalDocument> LearnerGoals =>
        _db.GetCollection<Learning.LearnerGoalDocument>("learner_goals");

    internal IMongoCollection<Learning.LearnerActivityDayDocument> LearnerActivityDays =>
        _db.GetCollection<Learning.LearnerActivityDayDocument>("learner_activity_days");

    internal IMongoCollection<Learning.CoachingAdviceDocument> CoachingAdvice =>
        _db.GetCollection<Learning.CoachingAdviceDocument>("coaching_advice");

    /// <summary>
    /// The exam-import front door (<c>S6b</c>). One draft per
    /// <c>(definitionId, versionNumber, route, packageHash)</c> — the id
    /// itself is derived from that tuple by <c>ExamImportWorkflow.StableDraftId</c>,
    /// which is what makes a retried upload of identical content a no-op
    /// insert rather than a duplicate draft.
    /// </summary>
    internal IMongoCollection<Importing.ExamImportDraftDocument> ImportDrafts =>
        _db.GetCollection<Importing.ExamImportDraftDocument>("import_drafts");

    /// <summary>Resume state for <c>ImportBatchRunner</c>, keyed by <c>{batchId}:{itemId}</c>.</summary>
    internal IMongoCollection<Importing.ImportBatchCheckpointDocument> ImportBatchCheckpoints =>
        _db.GetCollection<Importing.ImportBatchCheckpointDocument>("import_batch_checkpoints");

    internal IMongoCollection<Importing.ImportAssetCleanupIntentDocument> ImportAssetCleanupIntents =>
        _db.GetCollection<Importing.ImportAssetCleanupIntentDocument>("import_asset_cleanup_intents");

    internal IMongoCollection<Importing.ImportAssetCleanupCoordinationDocument> ImportAssetCleanupCoordination =>
        _db.GetCollection<Importing.ImportAssetCleanupCoordinationDocument>("import_asset_cleanup_coordination");

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

    /// <summary>
    /// The unique index on the address. Named here because
    /// <c>MongoUserRepository</c> matches on it to tell one duplicate-key
    /// violation from another.
    /// </summary>
    public const string EmailIndexName = "ux_users_email";

    /// <summary>The unique index on the phone number. See <see cref="EmailIndexName"/>.</summary>
    public const string PhoneIndexName = "ux_users_phone";

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        /*
         * One account per address, and one per number — but only among the
         * accounts that have one.
         *
         * <b>Partial, not sparse, and the difference is not cosmetic.</b> A
         * sparse index skips documents where the field is *missing*; it does
         * not skip documents where the field is present and null. Registration
         * creates accounts with no address at all, so the moment a mapper
         * writes `email: null` instead of omitting the key — one dropped
         * `[BsonIgnoreIfNull]` — a sparse unique index would reject the second
         * such account with a duplicate-key error nobody could explain. The
         * partial filter says what is actually meant: index the accounts whose
         * address is a string.
         */
        await Users.Indexes.CreateOneAsync(
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions<UserDocument>
                {
                    Unique = true,
                    Name = EmailIndexName,
                    PartialFilterExpression = Builders<UserDocument>.Filter.Type(
                        u => u.Email, BsonType.String),
                }),
            cancellationToken: ct);

        // The handle registration actually asks for. Unique because sign-in
        // accepts it: two accounts sharing a number makes "sign in with your
        // phone number" ambiguous, and nothing downstream could resolve it.
        await Users.Indexes.CreateOneAsync(
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys.Ascending(u => u.Phone),
                new CreateIndexOptions<UserDocument>
                {
                    Unique = true,
                    Name = PhoneIndexName,
                    PartialFilterExpression = Builders<UserDocument>.Filter.Type(
                        u => u.Phone, BsonType.String),
                }),
            cancellationToken: ct);

        // Staff invitations: unique live email (pending only), unique token hash.
        await StaffInvitations.Indexes.CreateOneAsync(
            new CreateIndexModel<StaffInvitationDocument>(
                Builders<StaffInvitationDocument>.IndexKeys.Ascending(i => i.TokenHash),
                new CreateIndexOptions { Unique = true, Name = "ux_staff_invitations_token" }),
            cancellationToken: ct);

        await StaffInvitations.Indexes.CreateOneAsync(
            new CreateIndexModel<StaffInvitationDocument>(
                Builders<StaffInvitationDocument>.IndexKeys.Ascending(i => i.Email),
                new CreateIndexOptions<StaffInvitationDocument>
                {
                    Unique = true,
                    Name = "ux_staff_invitations_live_email",
                    PartialFilterExpression = Builders<StaffInvitationDocument>.Filter.Eq(
                        i => i.Status, nameof(Domain.Identity.StaffInvitationStatus.Pending)),
                }),
            cancellationToken: ct);

        await StaffInvitations.Indexes.CreateOneAsync(
            new CreateIndexModel<StaffInvitationDocument>(
                Builders<StaffInvitationDocument>.IndexKeys
                    .Ascending(i => i.Status)
                    .Descending(i => i.CreatedAt),
                new CreateIndexOptions { Name = "ix_staff_invitations_status_created" }),
            cancellationToken: ct);

        await PrivacyRequests.Indexes.CreateOneAsync(
            new CreateIndexModel<PrivacyRequestDocument>(
                Builders<PrivacyRequestDocument>.IndexKeys.Ascending(r => r.SubjectId),
                new CreateIndexOptions { Name = "ix_privacy_requests_subject" }),
            cancellationToken: ct);

        // Sparse: most historical rows have none until `EnsureReferralCode`
        // backfills them on first read. Unique so a CSPRNG collision — astronomically
        // unlikely, but not impossible — is caught at write time, not silently
        // shared by two accounts. → `P-16`
        await Users.Indexes.CreateOneAsync(
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys.Ascending(u => u.ReferralCode),
                new CreateIndexOptions { Unique = true, Sparse = true, Name = "ux_users_referral_code" }),
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

        // The heatmap reads one learner's days in a date range.
        await LearnerActivityDays.Indexes.CreateOneAsync(
            new CreateIndexModel<Learning.LearnerActivityDayDocument>(
                Builders<Learning.LearnerActivityDayDocument>.IndexKeys
                    .Ascending(d => d.UserId)
                    .Ascending(d => d.Day),
                new CreateIndexOptions { Name = "ix_activity_user_day" }),
            cancellationToken: ct);

        // Advice for one standing is worth a week; after that the model is
        // asked again so a prompt improvement reaches learners who have not
        // moved.
        await CoachingAdvice.Indexes.CreateOneAsync(
            new CreateIndexModel<Learning.CoachingAdviceDocument>(
                Builders<Learning.CoachingAdviceDocument>.IndexKeys.Ascending(a => a.CreatedAt),
                new CreateIndexOptions { Name = "ttl_coaching_advice", ExpireAfter = TimeSpan.FromDays(7) }),
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

        // An article's slug is its address, and a draft reserves it. The
        // handler's lookup gives a clean 409; this index is what holds when
        // two editors save the same slug at once.
        await Articles.Indexes.CreateOneAsync(
            new CreateIndexModel<Library.ArticleDocument>(
                Builders<Library.ArticleDocument>.IndexKeys.Ascending(a => a.Slug),
                new CreateIndexOptions { Unique = true, Name = "ux_articles_slug" }),
            cancellationToken: ct);

        // Both learner listings filter on status and sort newest-published first.
        await Articles.Indexes.CreateOneAsync(
            new CreateIndexModel<Library.ArticleDocument>(
                Builders<Library.ArticleDocument>.IndexKeys
                    .Ascending(a => a.Status)
                    .Descending(a => a.PublishedAt),
                new CreateIndexOptions { Name = "ix_articles_status_published" }),
            cancellationToken: ct);

        await LibraryDocuments.Indexes.CreateOneAsync(
            new CreateIndexModel<Library.LibraryDocumentDocument>(
                Builders<Library.LibraryDocumentDocument>.IndexKeys
                    .Ascending(d => d.Status)
                    .Descending(d => d.PublishedAt),
                new CreateIndexOptions { Name = "ix_library_documents_status_published" }),
            cancellationToken: ct);

        // A learner's own history, newest first — exactly `IUsageLedger.ListAsync`'s
        // query shape.
        await UsageLedger.Indexes.CreateOneAsync(
            new CreateIndexModel<Usage.UsageEntryDocument>(
                Builders<Usage.UsageEntryDocument>.IndexKeys
                    .Ascending(e => e.UserId)
                    .Descending(e => e.At),
                new CreateIndexOptions { Name = "ix_usage_ledger_user_at" }),
            cancellationToken: ct);

        await PersonalizedExplanations.Indexes.CreateOneAsync(
            new CreateIndexModel<Explanations.PersonalizedExplanationDocument>(
                Builders<Explanations.PersonalizedExplanationDocument>.IndexKeys
                    .Ascending(j => j.SessionId)
                    .Ascending(j => j.QuestionId)
                    .Ascending(j => j.AnswerHash),
                new CreateIndexOptions { Name = "ix_personalized_explanations_lookup" }),
            cancellationToken: ct);

        // The CMS's "who is reviewing what" screen reads a definition's
        // drafts. Not unique — StableDraftId already makes the natural key
        // unique, this index just makes the by-definition query cheap.
        await ImportDrafts.Indexes.CreateOneAsync(
            new CreateIndexModel<Importing.ExamImportDraftDocument>(
                Builders<Importing.ExamImportDraftDocument>.IndexKeys
                    .Ascending(d => d.DefinitionId)
                    .Ascending(d => d.VersionNumber),
                new CreateIndexOptions { Name = "ix_import_drafts_definition_version" }),
            cancellationToken: ct);

        // ImportBatchRunner resumes a batch by scanning its own items; a
        // status screen for one batch reads the same shape.
        await ImportBatchCheckpoints.Indexes.CreateOneAsync(
            new CreateIndexModel<Importing.ImportBatchCheckpointDocument>(
                Builders<Importing.ImportBatchCheckpointDocument>.IndexKeys.Ascending(d => d.BatchId),
                new CreateIndexOptions { Name = "ix_import_batch_checkpoints_batch" }),
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

        await ParsedExamCandidates.Indexes.CreateOneAsync(
            new CreateIndexModel<Exams.ParsedExamCandidateDocument>(
                Builders<Exams.ParsedExamCandidateDocument>.IndexKeys.Ascending(c => c.PackageId),
                new CreateIndexOptions { Name = "ix_parsed_exam_candidates_package" }),
            cancellationToken: ct);

        await MediaAssets.Indexes.CreateOneAsync(
            new CreateIndexModel<Media.MediaAssetDocument>(
                Builders<Media.MediaAssetDocument>.IndexKeys.Descending(a => a.UploadedAt),
                new CreateIndexOptions { Name = "ix_media_assets_uploaded_at" }),
            cancellationToken: ct);
    }
}
