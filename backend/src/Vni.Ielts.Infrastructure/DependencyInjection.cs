using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Content.Library;
using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Application.Dictation;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Application.Learning;
using Vni.Ielts.Application.Practice;
using Vni.Ielts.Application.Usage;
using Vni.Ielts.Infrastructure.Content.Import;
using Vni.Ielts.Infrastructure.Persistence.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure.Storage;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Content;
using Vni.Ielts.Infrastructure.Persistence.Exams;
using Vni.Ielts.Infrastructure.Persistence.Identity;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Ai.Extraction;
using Vni.Ielts.Infrastructure.Ai.Writing;
using Vni.Ielts.Infrastructure.Assessment;
using Vni.Ielts.Infrastructure.Explanations;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Infrastructure.Observability;
using Vni.Ielts.Infrastructure.Persistence.Media;
using Vni.Ielts.Infrastructure.Security;
using Vni.Ielts.Infrastructure.Security.Sso;
using Microsoft.Extensions.Logging;

namespace Vni.Ielts.Infrastructure;

/// <summary>
/// The one place vendor types are bound to ports.
///
/// Api and Worker both call this. Neither knows what implements anything —
/// which is the property that makes swapping MongoDB for PostgreSQL a change
/// to this project alone.
/// </summary>
public static class DependencyInjection
{
    /// <param name="isDevelopment">
    /// Passed in rather than read from an environment variable here, so the
    /// one switch that can turn on a fake sign-in provider is gated by the
    /// host's own notion of its environment.
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, bool isDevelopment = false)
    {
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SsoOptions>(configuration.GetSection(SsoOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<AssessmentOptions>(
            configuration.GetSection(AssessmentOptions.SectionName));

        /*
         * The provider exclusion is checked at boot, not at the call site.
         *
         * A misconfiguration that only surfaces when a learner submits an
         * essay surfaces on the worst possible day, to the worst possible
         * person, and by then the request has already been made. Failing to
         * start is the cheap version of the same news.
         */
        var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        foreach (var (section, provider) in new[] { ("OpenAi", ai.OpenAi), ("Gemini", ai.Gemini) })
        {
            if (AiProviderPolicy.Rejects(section, provider) is { } reason)
                throw new InvalidOperationException(reason);
        }

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<MongoContext>();
        services.AddSingleton(sp => sp.GetRequiredService<MongoContext>().Database);

        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();

        /*
         * <b>There is no mail sender, because nothing sends mail.</b>
         * Verification and password reset were the only two callers and both
         * are gone (owner, 08/09/2026): registration takes no address, and
         * recovery is a Zalo link plus an operator. Keeping a dormant SMTP
         * registration would leave a boot-time configuration gate guarding a
         * capability nothing uses. → ADR-0018
         */

        services.AddScoped<IExamCatalogue, MongoExamCatalogue>();
        services.AddScoped<IExamPackageRepository, MongoExamPackageRepository>();
        services.AddScoped<IPackageUploadIdempotencyStore, MongoPackageUploadIdempotencyStore>();
        services.AddScoped<IPackageUploadStore, GridFsPackageUploadStore>();
        services.AddScoped<IPackageImportTransaction, MongoPackageImportTransaction>();
        services.AddScoped<IRawPackageParsingTransaction, MongoRawPackageParsingTransaction>();
        services.AddScoped<IPackageUploadTransaction, MongoPackageUploadTransaction>();
        services.AddScoped<IPackageUploadReconciliationStore, MongoPackageUploadReconciliationStore>();
        services.AddSingleton<IPackageCascadeDeleteHooks, NoOpPackageCascadeDeleteHooks>();
        services.AddScoped<IPackageCascadeDelete, MongoPackageCascadeDelete>();
        services.AddSingleton<IPackageRetentionHooks, NoOpPackageRetentionHooks>();
        services.AddScoped<IParsedExamCandidateRepository, MongoParsedExamCandidateRepository>();
        services.AddScoped<ISourceDocumentGroupingProposalRepository, MongoSourceDocumentGroupingProposalRepository>();
        services.AddScoped<ReviewParsedExamCandidate>();
        services.AddScoped<GetParsedExamCandidate>();
        services.AddScoped<ListParsedExamCandidates>();
        services.AddSingleton<IConfirmedCandidatePackageBuilder, ConfirmedCandidatePackageBuilder>();
        services.AddScoped<IConfirmedCandidateDraftCreator, MongoConfirmedCandidateDraftCreator>();
        services.AddScoped<CreateDraftFromParsedCandidate>();
        /*
         * ── Who parses a raw exam package ─────────────────────────────────
         *
         * Two implementations, one seam, and the choice is made from
         * configuration rather than from whichever was registered last.
         *
         * `ConfiguredExamExtractionParser` calls a provider and produces a
         * candidate with content in it. `NeedsReviewExamContentParser`
         * proposes nothing at all and sends the package to staff. The second
         * is not a stub to be deleted: it is the null implementation this
         * product runs on when no provider is configured, when the prompt
         * version does not match the build, or when the egress guard refuses
         * the endpoint — and an install in that state is a working install,
         * because a package still reaches review. → `G-11`
         *
         * The gate is evaluated once, here, so a half-configured install
         * cannot discover the fact from inside a background job after the
         * upload has already been accepted.
         */
        services.Configure<ExamParsingOptions>(configuration.GetSection(ExamParsingOptions.SectionName));
        services.Configure<PackageRetentionOptions>(configuration.GetSection(PackageRetentionOptions.SectionName));

        var examParsing = configuration.GetSection(ExamParsingOptions.SectionName)
            .Get<ExamParsingOptions>() ?? new ExamParsingOptions();

        if (examParsing.Enabled && examParsing.Problems(ai) is { Count: > 0 } examParsingProblems)
        {
            throw new InvalidOperationException(
                "ExamParsing is enabled but cannot run: "
                + string.Join(" ", examParsingProblems));
        }

        services.AddHttpClient(nameof(OpenAiExamExtractionClient));
        services.AddHttpClient(nameof(GeminiExamExtractionClient));
        services.AddSingleton<IExamExtractionClient, OpenAiExamExtractionClient>();
        services.AddSingleton<IExamExtractionClient, GeminiExamExtractionClient>();
        services.AddScoped<IExamExtractionRunStore, MongoExamExtractionRunStore>();
        services.AddSingleton<ExamParsingMetrics>();
        services.AddScoped<ConfiguredExamExtractionParser>();
        services.AddSingleton<NeedsReviewExamContentParser>();

        if (examParsing.IsConfiguredFor(ai))
        {
            services.AddScoped<IExamContentParser>(sp =>
                sp.GetRequiredService<ConfiguredExamExtractionParser>());
        }
        else
        {
            services.AddScoped<IExamContentParser>(sp =>
                sp.GetRequiredService<NeedsReviewExamContentParser>());
        }
        services.AddSingleton(configuration.GetSection("ContentImport:Extraction").Get<DocumentExtractionLimits>()
            ?? DocumentExtractionLimits.Conservative);
        services.AddSingleton<IDocxTextExtractor, OoxmlDocxTextExtractor>();
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<SourceDocumentExtractor>();
        services.AddSingleton<DirectoryAdjacentDocumentGrouper>();
        // ExamPackageReader is registered once below (S6b / LocateExamSchemaPath).
        services.AddSingleton<PackageStructuralValidator>();
        services.AddScoped<RawPackageParsingProcessor>(sp => new RawPackageParsingProcessor(
            sp.GetRequiredService<IExamPackageRepository>(),
            sp.GetRequiredService<IPackageUploadStore>(),
            sp.GetRequiredService<PackageStructuralValidator>(),
            sp.GetRequiredService<SourceDocumentExtractor>(),
            sp.GetRequiredService<DirectoryAdjacentDocumentGrouper>(),
            sp.GetRequiredService<IExamContentParser>(),
            sp.GetRequiredService<IRawPackageParsingTransaction>(),
            sp.GetRequiredService<IClock>()));
        services.AddScoped<PackageIngestionProcessor>();
        services.AddScoped<PackageRetentionProcessor>();
        services.AddScoped<IExamSessionRepository, MongoExamSessionRepository>();
        services.AddScoped<IAnswerSheetStore, MongoAnswerSheetStore>();
        services.AddScoped<ISectionResultStore, MongoSectionResultStore>();
        services.AddScoped<ISectionMarkingStore, MongoSectionMarkingStore>();
        services.AddScoped<ISpeakingRecordingMetadataStore, MongoSpeakingRecordingMetadataStore>();

        // The durable record that a marking is owed. Closing a section writes
        // one; the worker turns it into a band. → `IMarkingOutbox`
        services.AddScoped<IMarkingOutbox, MongoMarkingOutbox>();

        // Reconciles stored audio against the sheets that reference it, and
        // aborts stale pending inits. Run by the worker; off unless configured
        // on. → `RecordingReconciliation`, FS8.3 / FS8.6
        services.AddScoped<AbortStaleSpeakingUploads>();
        services.AddScoped<RecordingReconciliation>(sp =>
            new RecordingReconciliation(
                sp.GetRequiredService<IRecordingStore>(),
                sp.GetRequiredService<IAnswerSheetStore>(),
                sp.GetRequiredService<AbortStaleSpeakingUploads>(),
                sp.GetRequiredService<IClock>()));
        services.AddScoped<PurgeSpeakingRecordings>();

        /*
         * Writing and Speaking marking: the pipeline is whole; Writing may be
         * wired when Assessment:WritingMarking:Enabled and a provider are set.
         * Speaking remains on NoTranscriptSource until ASR is selected.
         */
        services.AddSingleton<IRubricSource, ConfiguredRubricSource>();

        // The Writing Task 1 : Task 2 ratio, from Assessment:Writing:TaskWeights.
        // Unset means no combined Writing band — never a default. → `P-12`, `G-11`
        services.AddSingleton<IWritingTaskWeighting>(_ =>
            WritingTaskWeightOptions.ToPolicy(
                configuration.GetSection(AssessmentOptions.SectionName).Get<AssessmentOptions>()
                    ?.Writing.TaskWeights));
        services.AddSingleton<ITranscriptSource, NoTranscriptSource>();

        services.AddHttpClient(nameof(OpenAiWritingEvaluationClient));
        services.AddHttpClient(nameof(GeminiWritingEvaluationClient));
        services.AddHttpClient(nameof(OpenAiExplanationGenerator), client =>
        {
            // A hung provider fails as EXPLANATION_PROVIDER_TIMEOUT within a
            // configured bound, not HttpClient's 100 s default. → AiOptions.ExplanationTimeoutSeconds
            var timeout = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>()
                ?.ExplanationTimeoutSeconds ?? new AiOptions().ExplanationTimeoutSeconds;
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeout, 5, 300));
        });
        services.AddSingleton<IWritingEvaluationCostMetric, NullWritingEvaluationCostMetric>();
        services.AddSingleton<WritingEvaluationRouter>();
        services.AddSingleton<OpenAiWritingEvaluationClient>();
        services.AddSingleton<GeminiWritingEvaluationClient>();
        services.AddSingleton<IWritingEvaluationClient>(sp => sp.GetRequiredService<OpenAiWritingEvaluationClient>());
        services.AddSingleton<IWritingEvaluationClient>(sp => sp.GetRequiredService<GeminiWritingEvaluationClient>());

        services.AddSingleton<ISectionEvaluator>(sp =>
        {
            var assessment = configuration.GetSection(AssessmentOptions.SectionName).Get<AssessmentOptions>()
                ?? new AssessmentOptions();
            var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();

            return WritingSectionEvaluator.IsConfiguredFor(assessment, ai)
                ? sp.GetRequiredService<WritingSectionEvaluator>()
                : new UnconfiguredEvaluator(ExamModule.Writing);
        });

        services.AddSingleton<WritingSectionEvaluator>();
        services.AddSingleton<ISectionEvaluator>(_ => new UnconfiguredEvaluator(ExamModule.Speaking));
        services.AddScoped<SectionMarkingRunner>();

        services.AddSingleton<RecordedExplanationGenerator>();
        services.AddSingleton<OpenAiExplanationGenerator>();
        services.AddSingleton<IReadingListeningExplanationGenerator>(sp =>
        {
            var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
            return ai.OpenAi.IsConfigured
                ? sp.GetRequiredService<OpenAiExplanationGenerator>()
                : sp.GetRequiredService<RecordedExplanationGenerator>();
        });
        services.AddScoped<ICanonicalExplanationCache, MongoCanonicalExplanationCache>();
        services.AddScoped<IPersonalizedExplanationStore, MongoPersonalizedExplanationStore>();
        services.AddScoped<CanonicalExplanationWorkflow>();
        services.AddScoped<PersonalizedExplanationService>();

        services.AddScoped<ListExams>();
        services.AddScoped<ListPracticeUnits>();
        services.AddScoped<StartExamSession>();
        services.AddScoped<StartPracticeUnitSession>();
        services.AddScoped<GetExamSession>();
        services.AddScoped<SaveAnswers>();
        services.AddScoped<SubmitSpeakingRecording>();
        services.AddScoped<InitSpeakingRecording>();
        services.AddScoped<CompleteSpeakingRecording>();
        services.AddScoped<GetSpeakingRecordingPlaybackUrl>();
        services.AddScoped<AdvanceSection>();
        services.AddScoped<SubmitExamSession>();
        services.AddScoped<SetStopwatch>();
        services.AddScoped<SetTargetTime>();
        services.AddScoped<GetSessionResults>();
        services.AddScoped<RequestPersonalizedExplanation>();
        services.AddScoped<ListMySittings>();

        // Development only: it publishes what it loads, which is a reviewed
        // administrative act anywhere else.
        if (isDevelopment) services.AddScoped<DevelopmentExamSeeder>();

        /*
         * ── Where media comes from ────────────────────────────────────────
         *
         * <b>Object storage when it is configured; the fixtures directory when
         * it is not and this is Development.</b>
         *
         * Until 2026-08-28 there was only the second, registered only in
         * Development — so a production process had no exam audio, no exam
         * images and no dictation audio at all, and the failure would have
         * looked like a broken player rather than a missing adapter. The
         * startup gate now refuses to boot a production process with neither.
         *
         * <b>Configured wins even in Development</b>, so the local MinIO in
         * `infra/docker/compose.yaml` can be exercised by pointing at it rather
         * than by changing code. An adapter nobody runs before production is an
         * adapter nobody has tested.
         */
        var storage = configuration.GetSection(ObjectStorageOptions.SectionName)
            .Get<ObjectStorageOptions>() ?? new ObjectStorageOptions();

        var objectStorageRegistered = services.AddObjectStorage(storage);

        if (objectStorageRegistered
            && !string.IsNullOrWhiteSpace(storage.SpeakingRecordingsBucket))
        {
            services.AddScoped<IRecordingStore, S3SpeakingRecordingStore>();
        }
        else
        {
            services.AddScoped<IRecordingStore, GridFsRecordingStore>();
            services.AddSingleton<ISpeakingRecordingBlobStore, UnconfiguredSpeakingRecordingBlobStore>();
            services.AddSingleton(new ObjectStorageSpeakingOptions());
        }

        if (!objectStorageRegistered && isDevelopment)
        {
            services.AddSingleton<IExamAssetStore, FixtureAssetStore>();
        }

        // Media Library (CMS package / media endpoints). Uses the same
        // ObjectStorage config section as Storage.ObjectStorageOptions above —
        // MediaObjectStorageOptions is the Media-scoped binding shape.
        services.Configure<MediaObjectStorageOptions>(configuration.GetSection(MediaObjectStorageOptions.SectionName));
        services.AddScoped<IMediaAssetRepository, MongoMediaAssetRepository>();
        services.AddSingleton<S3ObjectStorage>();
        services.AddSingleton<Application.Media.IObjectStorage>(sp => sp.GetRequiredService<S3ObjectStorage>());
        services.AddSingleton<IMediaOrphanReconciliationHooks, NoOpMediaOrphanReconciliationHooks>();
        services.AddScoped<IMediaOrphanReconciliation, MongoMediaOrphanReconciliation>();

        // Dictation has no authoring surface yet, so its content is a file
        // read once at startup rather than a repository over an empty table.
        services.AddSingleton<IDictationCatalogue, FixtureDictationCatalogue>();

        // Its audio follows the same rule as exam media: object storage when
        // configured, the fixtures directory when not.
        if (!storage.IsConfigured)
            services.AddSingleton<IDictationAssetStore, FixtureDictationAssetStore>();
        services.AddScoped<ListDictationSets>();
        services.AddScoped<GetDictationSet>();
        services.AddScoped<CheckDictationSentence>();

        services.AddScoped<IUserRepository, MongoUserRepository>();
        services.AddScoped<IUserIdentityRepository, MongoUserIdentityRepository>();
        services.AddScoped<IRoleRepository, MongoRoleRepository>();
        services.AddScoped<IPermissionResolver, MongoPermissionResolver>();

        services.AddScoped<IAuditLog, MongoAuditLog>();
        services.AddScoped<ILoginThrottle, MongoLoginThrottle>();
        services.AddScoped<ISessionDirectory, MongoSessionDirectory>();
        services.AddScoped<ISsoStateStore, MongoSsoStateStore>();
        services.AddScoped<IHandoffCodeStore, MongoHandoffCodeStore>();

        services.AddScoped<RegisterUser>();
        services.AddScoped<LoginWithPassword>();
        services.AddScoped<RefreshTokens>();
        services.AddScoped<StartSsoSignIn>();
        services.AddScoped<SignInWithSso>();
        services.AddScoped<CompleteSsoSignIn>();
        services.AddScoped<GetMyAccount>();
        services.AddScoped<ListSessions>();
        services.AddScoped<RevokeSession>();
        services.AddScoped<RevokeOtherSessions>();
        services.AddScoped<SetPassword>();
        services.AddScoped<SetPhone>();
        services.AddScoped<ChangeEmail>();

        services.Configure<AdminUserOperationOptions>(configuration.GetSection(AdminUserOperationOptions.SectionName));
        services.Configure<PrivacyOptions>(configuration.GetSection(PrivacyOptions.SectionName));
        services.AddScoped<ILastActiveAdminGuard, LastActiveAdminGuard>();
        services.AddScoped<IProtectedAdminMutation, Persistence.Identity.MongoProtectedAdminMutation>();
        services.AddScoped<IStaffInvitationRepository, Persistence.Identity.MongoStaffInvitationRepository>();
        services.AddScoped<IStaffInvitationAcceptance, Persistence.Identity.MongoStaffInvitationAcceptance>();
        services.AddScoped<IPrivacyRequestRepository, Persistence.Identity.MongoPrivacyRequestRepository>();
        services.AddScoped<IPersonalDataExportStore, Persistence.Identity.MongoPersonalDataExportStore>();
        services.AddScoped<IPasswordResetTokens, Security.MongoPasswordResetTokens>();
        services.AddScoped<IVerificationMessageSender, Security.LoggingStaffMessageSender>();
        services.AddScoped<CreateStaffAccount>();
        services.AddScoped<InviteStaff>();
        services.AddScoped<ResendStaffInvitation>();
        services.AddScoped<RevokeStaffInvitation>();
        services.AddScoped<AcceptStaffInvitation>();
        services.AddScoped<BulkSuspendUsers>();
        services.AddScoped<UpdateAdminUserProfile>();
        services.AddScoped<ForceStaffPasswordReset>();
        services.AddScoped<CreatePrivacyRequest>();
        services.AddScoped<ApprovePrivacyRequest>();
        services.AddScoped<ExecutePrivacyRequest>();

        // The two libraries (P-22): documents and articles, learner reads and
        // CMS writes. Files are a URL string in this slice — no upload yet.
        services.AddScoped<ILibraryDocumentStore, Persistence.Library.MongoLibraryDocumentStore>();
        services.AddScoped<IArticleStore, Persistence.Library.MongoArticleStore>();
        services.AddScoped<ListLibraryDocuments>();
        services.AddScoped<GetLibraryDocument>();
        services.AddScoped<ListAllLibraryDocuments>();
        services.AddScoped<GetLibraryDocumentForEditing>();
        services.AddScoped<CreateLibraryDocument>();
        services.AddScoped<UpdateLibraryDocument>();
        services.AddScoped<DeleteLibraryDocument>();
        services.AddScoped<ChangeLibraryDocumentStatus>();
        services.AddScoped<ListArticles>();
        services.AddScoped<GetArticleBySlug>();
        services.AddScoped<ListAllArticles>();
        services.AddScoped<GetArticleForEditing>();
        services.AddScoped<CreateArticle>();
        services.AddScoped<UpdateArticle>();
        services.AddScoped<DeleteArticle>();
        services.AddScoped<ChangeArticleStatus>();

        // The usage ledger (P-14): record, never block. Every amount defaults
        // to zero when `Usage` is absent from configuration — the row still
        // exists, the price is the business's to set. → `G-11`
        services.AddSingleton(_ =>
            configuration.GetSection(UsageOptions.SectionName).Get<UsageOptions>()
                ?? new UsageOptions());
        services.AddScoped<IUsageLedger, Persistence.Usage.MongoUsageLedger>();
        services.AddScoped<IReferralDirectory, Persistence.Usage.MongoReferralDirectory>();
        services.AddScoped<Vni.Ielts.Application.Usage.UsageRecorder>();
        services.AddScoped<GetMyUsage>();

        // Learning: goal, coaching, daily activity. The advisor is the AI half
        // and is gated exactly like the Writing marker; the rest is arithmetic.
        services.AddScoped<ILearnerGoalStore, Persistence.Learning.MongoLearnerGoalStore>();
        services.AddScoped<ILearnerActivityLog, Persistence.Learning.MongoLearnerActivityLog>();
        services.AddScoped<ICoachingAdviceCache, Persistence.Learning.MongoCoachingAdviceCache>();
        services.AddSingleton<ILearnerCalendar, Learning.LearnerCalendar>();
        services.AddHttpClient(nameof(Ai.Coaching.OpenAiCoachingAdvisor));
        services.AddSingleton<ICoachingAdvisor, Ai.Coaching.OpenAiCoachingAdvisor>();
        services.AddScoped<GetLearnerGoal>();
        services.AddScoped<SetLearnerGoal>();
        services.AddScoped<GetCoaching>();
        services.AddScoped<GetLearnerActivity>();
        services.AddScoped<LearnerPresence>();

        AddSsoProviders(services, configuration, isDevelopment);

        /*
         * Folded in from `Program.cs`, where FS0.1 had to leave it because a
         * task file boundary put this file out of that agent's reach.
         *
         * It belongs here for the reason `ContentRightsRegistration` states in
         * its own summary: splitting DI across two entry points is how a
         * service ends up registered in the API and missing in the Worker.
         * The publish guard refuses by default, so a Worker that resolved it
         * from an empty registration would not fail loudly — it would refuse
         * quietly, which is the harder bug to see.
         */
        services.AddContentRights(configuration);

        // The ZIP front door for exam import (S6). Caps come from
        // Import:Archive; the class defaults apply when the section is absent.
        // Stateless, so a singleton. → docs/security/zip-ingestion-security.md
        services.Configure<Content.Import.ImportArchiveOptions>(
            configuration.GetSection(Content.Import.ImportArchiveOptions.SectionName));
        services.AddSingleton<Application.Importing.IExamPackageArchiveInspector,
            Content.Import.ExamPackageArchiveInspector>();

        /*
         * ── The rest of the front door: the endpoint-facing pipeline (S6b) ──
         *
         * <b>The first production caller of `IExamPackageValidator`.</b>
         * Before this it was only ever built ad hoc — by
         * `DevelopmentExamSeeder` for a Development-only fixture load, and by
         * the operator CLI's own `Main`. Both discover the schema path by
         * walking up from the running process; `LocateExamSchemaPath` below
         * does the same walk rather than inventing a second discovery
         * mechanism, but throws instead of skipping, because this one backs
         * a real admin endpoint rather than a convenience seed.
         */
        services.AddSingleton(sp => Content.ExamPackageReader.FromSchemaFile(LocateExamSchemaPath()));
        services.AddSingleton<IExamPackageValidator>(
            sp => new Content.ExamPackageValidator(sp.GetRequiredService<Content.ExamPackageReader>()));

        /*
         * <b>No AI parser is wired in here.</b> `IExamSourceParser` is
         * registered as the null implementation until AI-assisted parsing of
         * raw exam source documents is productised for an unattended HTTP
         * caller — see `UnconfiguredExamSourceParser`'s own remarks. Only the
         * structured route (an archive holding one ready `exam.json`) is
         * unaffected by this; it never touches a parser.
         */
        services.AddScoped<IPrivateImportAssetStore, DiscardedImportAssetStore>();
        services.AddScoped<ISourceDocumentExtractor, Content.SafeSourceDocumentExtractor>();
        services.AddScoped<IExamSourceParser, UnconfiguredExamSourceParser>();

        if (objectStorageRegistered)
            services.AddScoped<IImportExamAssetStore, Storage.S3ImportExamAssetStore>();
        else
            services.AddScoped<IImportExamAssetStore, Storage.ExamAssetStoreBackedImportExamAssetStore>();

        services.AddScoped<ExamImportWorkflow>();
        services.AddScoped<ImportReviewWorkflow>();
        services.AddScoped<IImportDraftStore, MongoImportDraftStore>();
        services.AddSingleton<IImportApprovalCommitHooks, NoOpImportApprovalCommitHooks>();
        services.AddSingleton<IImportLinkageCommitHooks, NoOpImportLinkageCommitHooks>();
        services.AddScoped<IImportApprovalCommitter, MongoImportApprovalCommitter>();
        services.AddScoped<IImportBatchCheckpointStore, MongoImportBatchCheckpointStore>();
        services.AddScoped<ExamPackageImportPipeline>();

        return services;
    }

    /// <summary>
    /// Walks up from the running process to find
    /// <c>contracts/schemas/exam.schema.json</c> — the same search
    /// <c>DevelopmentExamSeeder.LocateFixtures</c> and the operator CLI's
    /// <c>FindRepositoryRoot</c> already do, because every one of these
    /// processes runs from a build output directory nested somewhere under
    /// the repository root.
    ///
    /// <b>Throws rather than degrading, unlike the seeder.</b> The seeder is
    /// Development-only convenience that logs and continues; this validator
    /// is what the admin import endpoints (S6b) exist for, so silently having
    /// no validator would surface as every upload failing for an unexplained
    /// reason. Failing fast at the first resolution is the cheaper version of
    /// the same news.
    /// </summary>
    private static string LocateExamSchemaPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "contracts", "schemas", "exam.schema.json");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate contracts/schemas/exam.schema.json above " + AppContext.BaseDirectory
            + ". The exam-import validator (S6b) needs it to be resolvable at startup.");
    }

    /// <summary>
    /// Registers one adapter per configured provider, and nothing for the
    /// providers this deployment has no credentials for.
    ///
    /// <para>
    /// Singletons on purpose. Each adapter owns an <c>HttpClient</c> and a
    /// discovery-document cache; creating them per request would mean a
    /// metadata fetch and a fresh socket pool on every sign-in.
    /// </para>
    /// </summary>
    private static void AddSsoProviders(
        IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        var sso = configuration.GetSection(SsoOptions.SectionName).Get<SsoOptions>() ?? new SsoOptions();
        var timeout = TimeSpan.FromSeconds(Math.Clamp(sso.ProviderTimeoutSeconds, 1, 60));

        // The stub is an unauthenticated route to a session. Refusing to start
        // is the only safe response to finding it switched on in a real
        // deployment — the alternative is an authentication bypass that nobody
        // notices because everything appears to work.
        if (sso.EnableStubProvider && !isDevelopment)
        {
            throw new InvalidOperationException(
                "Sso:EnableStubProvider is on outside Development. It signs in a fixed account "
                + "without contacting any provider, which is a complete authentication bypass.");
        }

        // <b>Real credentials win.</b> The stub exists because no client
        // secret is available yet, so the moment one is, it has served its
        // purpose. The earlier order was the other way round, and it meant
        // that supplying a real client id and secret changed nothing at all —
        // the flag stayed on from appsettings.Development.json and silently
        // kept the fake provider. That is a confusing failure precisely when
        // someone is trying to verify their first real sign-in.
        if (sso.Google.IsConfigured)
        {
            services.AddSingleton<IExternalIdentityProvider>(provider =>
            {
                var loggers = provider.GetRequiredService<ILoggerFactory>();
                loggers.CreateLogger("Vni.Ielts.Sso").LogInformation(
                    "Google sign-in is using real credentials for client {ClientId}.",
                    Redact(sso.Google.ClientId));

                return GoogleProvider.Create(sso.Google, timeout, loggers);
            });
        }
        else if (sso.EnableStubProvider)
        {
            services.AddSingleton<IExternalIdentityProvider>(provider =>
            {
                provider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Vni.Ielts.Sso")
                    .LogWarning(
                        "DEV ONLY — Google sign-in is faked. No credentials are configured, so "
                        + "every Google sign-in returns the same test account without contacting "
                        + "Google. Set Sso__Google__ClientId and Sso__Google__ClientSecret to use "
                        + "the real thing.");

                return new StubIdentityProvider(sso);
            });
        }

        // Facebook (AU-3) is deliberately absent rather than half-wired. Its
        // web login is OAuth 2.0 with a Graph profile call and no ID token, so
        // it is a different adapter, and it cannot assert email_verified —
        // which changes what the linking rule is allowed to do. → ADR-0013

        services.AddSingleton<IExternalIdentityProviderRegistry, ProviderRegistry>();
    }

    /// <summary>
    /// A client id is not a secret, but it is still an identifier worth not
    /// printing whole into a log that may be shipped somewhere.
    /// </summary>
    private static string Redact(string clientId) =>
        clientId.Length <= 8 ? "…" : clientId[..8] + "…";

    /// <summary>
    /// Creates indexes and seeds the system roles.
    ///
    /// Runs at startup and is idempotent, which matters because several API
    /// instances start at once and all of them will run it. Index creation is
    /// already idempotent in Mongo; the role seed checks before inserting.
    /// </summary>
    public static async Task InitialiseInfrastructureAsync(
        this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MongoContext>();

        await ctx.AssertReplicaSetAsync(ct);

        // Before the indexes, deliberately: it clears the two things that stop
        // them being created at all. → IdentityReworkMigration
        await Persistence.Identity.IdentityReworkMigration.RunAsync(ctx.Database, ct);

        await ctx.EnsureIndexesAsync(ct);
        await MongoSsoStateStore.EnsureIndexesAsync(ctx.Database, ct);
        await MongoHandoffCodeStore.EnsureIndexesAsync(ctx.Database, ct);
        await MongoAuditLog.EnsureIndexesAsync(ctx.Database, ct);
        await MongoLoginThrottle.EnsureIndexesAsync(ctx.Database, ct);
        await MongoSpeakingRecordingMetadataStore.EnsureIndexesAsync(ctx.Database, ct);

        // Development only, and registered only there — see AddInfrastructure.
        // It loads fixtures/exams through the package reader, which is the same
        // validator the ZIP importer and CMS authoring go through.
        if (scope.ServiceProvider.GetService<DevelopmentExamSeeder>() is { } seeder)
            await seeder.SeedAsync(ct);

        var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        foreach (var (name, permissions) in SeedRoles)
        {
            if (await roles.FindByNameAsync(name, ct) is not null)
            {
                /*
                 * <b>A system role that already exists still receives keys
                 * added to the seed later.</b> Before 2026-09-07 the seed was
                 * insert-only, so `document.write` and friends would have
                 * reached a fresh database and never an existing one — the
                 * CMS would show the permission column, and no operator could
                 * ever hold it. `$addToSet` is idempotent and never removes
                 * a grant made by hand, so the seed is a floor, not a mirror.
                 * Done at the collection rather than through `Role.Grant`,
                 * which (correctly) refuses to mutate a system role.
                 */
                await ctx.Roles.UpdateOneAsync(
                    r => r.Name == name && r.IsSystem,
                    Builders<RoleDocument>.Update.AddToSetEach(r => r.Permissions, permissions),
                    cancellationToken: ct);
                continue;
            }

            try
            {
                await roles.AddAsync(Role.Create(name, isSystem: true, permissions), ct);
            }
            catch (MongoWriteException e)
                when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Several API instances start together and all run this. The
                // check-then-insert loses the race for all but one, and without
                // this the losers crash on startup — turning a routine rolling
                // deploy into an outage. The unique index on roles.name is the
                // guarantee; arriving second is the expected outcome, not an error.
            }
        }
    }

    /// <summary>
    /// The seeded roles. Reseeded for the 6-state CMS lifecycle +
    /// ownership-scoped RBAC: `content-editor`/`support` folded into `admin`
    /// (`C-25`) — their permissions moved onto `admin`'s list below, not
    /// dropped — and a content-authoring role (`M-11b`) plus a review role
    /// (`Đ4`) replace them.
    ///
    /// Note <c>learner</c> holds only <c>exam.read.own</c>. A learner's
    /// ability to sit an exam is not a CMS permission — it is governed by
    /// entitlement and session ownership, and conflating the two would put
    /// learner behaviour behind the admin permission model.
    /// </summary>
    private static readonly (string Name, string[] Permissions)[] SeedRoles =
    [
        (SystemRoles.Learner, [PermissionKeys.ExamReadOwn]),

        (SystemRoles.ExamAuthor,
        [
            PermissionKeys.ExamReadOwn, PermissionKeys.ExamCreate, PermissionKeys.ExamUpdateOwn,
            PermissionKeys.ExamDeleteOwn, PermissionKeys.ExamSubmit, PermissionKeys.ExamPreview,
            // Upload + shared inbox; confirming another author's package needs package.confirm.
            PermissionKeys.PackageUpload, PermissionKeys.PackageRead,
            PermissionKeys.MediaUpload, PermissionKeys.MediaRead,
            PermissionKeys.DocumentWrite, PermissionKeys.ArticleWrite,
        ]),

        (SystemRoles.AcademicLead,
        [
            PermissionKeys.ExamReadOwn, PermissionKeys.ExamCreate, PermissionKeys.ExamUpdateOwn,
            PermissionKeys.ExamDeleteOwn, PermissionKeys.ExamSubmit, PermissionKeys.ExamPreview,
            PermissionKeys.PackageUpload, PermissionKeys.PackageRead, PermissionKeys.PackageConfirm,
            PermissionKeys.ExamReadAny, PermissionKeys.ExamUpdateAny, PermissionKeys.ExamReview,
            PermissionKeys.EvaluationRead,
            PermissionKeys.MediaUpload, PermissionKeys.MediaRead, PermissionKeys.MediaRetire,
            PermissionKeys.DocumentWrite, PermissionKeys.ArticleWrite,
        ]),

        (SystemRoles.Admin,
        [
            PermissionKeys.ExamReadAny, PermissionKeys.ExamCreate, PermissionKeys.ExamUpdateAny,
            PermissionKeys.ExamDeleteAny, PermissionKeys.ExamSubmit, PermissionKeys.ExamReview,
            PermissionKeys.ExamPreview, PermissionKeys.ExamPublish, PermissionKeys.ExamUnpublish,
            PermissionKeys.PackageUpload, PermissionKeys.PackageRead, PermissionKeys.PackageConfirm,
            PermissionKeys.PackageDelete,
            PermissionKeys.ContentRightsManage,
            PermissionKeys.MediaUpload, PermissionKeys.MediaRead, PermissionKeys.MediaRetire,
            PermissionKeys.EvaluationRead, PermissionKeys.EvaluationRerun,
            PermissionKeys.EvaluationOverride, PermissionKeys.LearnerContentRead,
            PermissionKeys.UserRead, PermissionKeys.UserUpdate, PermissionKeys.UserSuspend,
            PermissionKeys.UserDelete, PermissionKeys.UserExport, PermissionKeys.TokenRead,
            PermissionKeys.UserResetPassword,
            PermissionKeys.RoleRead, PermissionKeys.RoleAssign, PermissionKeys.RoleManage,
            PermissionKeys.ConfigRead, PermissionKeys.ConfigUpdate, PermissionKeys.AuditRead,
            PermissionKeys.DocumentWrite, PermissionKeys.DocumentPublish,
            PermissionKeys.ArticleWrite, PermissionKeys.ArticlePublish,
        ]),
    ];
}
