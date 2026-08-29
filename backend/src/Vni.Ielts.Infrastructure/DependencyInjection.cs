using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Dictation;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure.Storage;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Content;
using Vni.Ielts.Infrastructure.Persistence.Exams;
using Vni.Ielts.Infrastructure.Persistence.Identity;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Assessment;
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
        services.AddScoped<IEmailVerificationTokens, MongoEmailVerificationTokens>();
        services.AddScoped<IPasswordResetTokens, MongoPasswordResetTokens>();

        // No production email sender exists yet. Registering the logging one
        // unconditionally would mean a production deployment silently never
        // sends a verification message while reporting success — so the caller
        // must opt in, and Program.cs only does so outside Production.
        /*
         * ── Who delivers a verification or reset link ──────────────────────
         *
         * <b>SMTP when it is configured; the log when it is not.</b> The
         * logging sender writes the link to the server log and reports
         * `NotSent`, so every screen that would say "check your inbox" says
         * the truth instead. That is the right development behaviour and it is
         * an outright lie in production, which is why the startup gate refuses
         * to boot without a real one.
         *
         * <b>Configured wins even in Development</b>, so a real provider can be
         * exercised locally by pointing at one — a mail path nobody runs before
         * production is a mail path nobody has tested.
         */
        var email = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>()
            ?? new SmtpOptions();

        if (email.IsConfigured)
        {
            services.AddSingleton(email);
            services.AddScoped<IVerificationMessageSender, SmtpMessageSender>();
        }
        else
        {
            services.AddScoped<IVerificationMessageSender, LoggingVerificationMessageSender>();
        }

        services.AddScoped<IExamCatalogue, MongoExamCatalogue>();
        services.AddScoped<IExamSessionRepository, MongoExamSessionRepository>();
        services.AddScoped<IAnswerSheetStore, MongoAnswerSheetStore>();
        services.AddScoped<IRecordingStore, GridFsRecordingStore>();
        services.AddScoped<ISectionResultStore, MongoSectionResultStore>();
        services.AddScoped<ISectionMarkingStore, MongoSectionMarkingStore>();

        // The durable record that a marking is owed. Closing a section writes
        // one; the worker turns it into a band. → `IMarkingOutbox`
        services.AddScoped<IMarkingOutbox, MongoMarkingOutbox>();

        // Reconciles stored audio against the sheets that reference it. Run by
        // the worker, and off unless configured on. → `RecordingReconciliation`
        services.AddScoped<RecordingReconciliation>();

        /*
         * Writing and Speaking marking: the pipeline is whole, the evaluator
         * is not there, and both of those are deliberate.
         *
         * `CriterionMarking` validates a model's claim and refuses it rather
         * than repairing it; the runner turns a refusal, a missing transcript
         * or a missing key into a stated reason instead of a band. What is
         * absent is the adapter that calls a provider — `B-2`, the Vietnam
         * PDPL cross-border position, is unresolved, so no real learner essay
         * may cross a border yet, and speech-to-text has not been selected at
         * all.
         *
         * Registering the absence rather than leaving the port unbound is the
         * point: `IsConfigured` answers honestly and the call refuses loudly,
         * where an unregistered port would be a null nobody checked. → `G-11`
         */
        services.AddSingleton<IRubricSource, ConfiguredRubricSource>();
        services.AddSingleton<ITranscriptSource, NoTranscriptSource>();
        services.AddSingleton<ISectionEvaluator>(_ => new UnconfiguredEvaluator(ExamModule.Writing));
        services.AddSingleton<ISectionEvaluator>(_ => new UnconfiguredEvaluator(ExamModule.Speaking));
        services.AddScoped<SectionMarkingRunner>();

        services.AddScoped<ListExams>();
        services.AddScoped<StartExamSession>();
        services.AddScoped<GetExamSession>();
        services.AddScoped<SaveAnswers>();
        services.AddScoped<SubmitSpeakingRecording>();
        services.AddScoped<AdvanceSection>();
        services.AddScoped<SubmitExamSession>();
        services.AddScoped<SetStopwatch>();
        services.AddScoped<SetTargetTime>();
        services.AddScoped<GetSessionResults>();
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

        if (!services.AddObjectStorage(storage) && isDevelopment)
        {
            services.AddSingleton<IExamAssetStore, FixtureAssetStore>();
        }

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
        services.AddScoped<VerifyEmail>();
        services.AddScoped<StartSsoSignIn>();
        services.AddScoped<SignInWithSso>();
        services.AddScoped<CompleteSsoSignIn>();
        services.AddScoped<GetMyAccount>();
        services.AddScoped<ListSessions>();
        services.AddScoped<RevokeSession>();
        services.AddScoped<RevokeOtherSessions>();
        services.AddScoped<RequestPasswordReset>();
        services.AddScoped<ResetPassword>();
        services.AddScoped<SetPassword>();
        services.AddScoped<SetPhone>();
        services.AddScoped<ChangeEmail>();
        services.AddScoped<ResendVerification>();
        services.AddScoped<ConfirmEmailCode>();

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

        return services;
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
        await ctx.EnsureIndexesAsync(ct);
        await MongoEmailVerificationTokens.EnsureIndexesAsync(ctx.Database, ct);
        await MongoEmailVerificationTokens.EnsureCodeIndexesAsync(ctx.Database, ct);
        await MongoPasswordResetTokens.EnsureIndexesAsync(ctx.Database, ct);
        await MongoSsoStateStore.EnsureIndexesAsync(ctx.Database, ct);
        await MongoHandoffCodeStore.EnsureIndexesAsync(ctx.Database, ct);
        await MongoAuditLog.EnsureIndexesAsync(ctx.Database, ct);
        await MongoLoginThrottle.EnsureIndexesAsync(ctx.Database, ct);

        // Development only, and registered only there — see AddInfrastructure.
        // It loads fixtures/exams through the package reader, which is the same
        // validator the ZIP importer and CMS authoring go through.
        if (scope.ServiceProvider.GetService<DevelopmentExamSeeder>() is { } seeder)
            await seeder.SeedAsync(ct);

        var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        foreach (var (name, permissions) in SeedRoles)
        {
            if (await roles.FindByNameAsync(name, ct) is not null)
                continue;

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
    /// The seeded roles. `M-11` settled that the first release has no teacher
    /// role, so there is deliberately no fourth entry here.
    ///
    /// Note <c>learner</c> holds only <c>exam.read</c>. A learner's ability to
    /// sit an exam is not a CMS permission — it is governed by entitlement and
    /// session ownership, and conflating the two would put learner behaviour
    /// behind the admin permission model.
    /// </summary>
    private static readonly (string Name, string[] Permissions)[] SeedRoles =
    [
        (SystemRoles.Learner, [PermissionKeys.ExamRead]),

        (SystemRoles.ContentEditor,
        [
            PermissionKeys.ExamRead, PermissionKeys.ExamCreate, PermissionKeys.ExamUpdate,
            PermissionKeys.ExamDelete, PermissionKeys.PackageRead, PermissionKeys.PackageUpload,
            PermissionKeys.EvaluationRead,
            // Deliberately NOT ExamPublish — importing content and shipping it
            // to learners are separate authorities. → threat T20
        ]),

        (SystemRoles.Support,
        [
            PermissionKeys.ExamRead, PermissionKeys.PackageRead, PermissionKeys.EvaluationRead,
            PermissionKeys.LearnerContentRead, PermissionKeys.UserRead,
        ]),

        (SystemRoles.Admin,
        [
            PermissionKeys.ExamRead, PermissionKeys.ExamCreate, PermissionKeys.ExamUpdate,
            PermissionKeys.ExamDelete, PermissionKeys.ExamPublish, PermissionKeys.ExamUnpublish,
            PermissionKeys.PackageUpload, PermissionKeys.PackageRead, PermissionKeys.PackageDelete,
            PermissionKeys.EvaluationRead, PermissionKeys.EvaluationRerun,
            PermissionKeys.EvaluationOverride, PermissionKeys.LearnerContentRead,
            PermissionKeys.UserRead, PermissionKeys.UserUpdate, PermissionKeys.UserSuspend,
            PermissionKeys.UserDelete, PermissionKeys.UserExport,
            PermissionKeys.RoleRead, PermissionKeys.RoleAssign, PermissionKeys.RoleManage,
            PermissionKeys.ConfigRead, PermissionKeys.ConfigUpdate, PermissionKeys.AuditRead,
        ]),
    ];
}
