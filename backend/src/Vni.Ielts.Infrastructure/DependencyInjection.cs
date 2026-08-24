using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Dictation;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Content;
using Vni.Ielts.Infrastructure.Persistence.Exams;
using Vni.Ielts.Infrastructure.Persistence.Identity;
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
        services.AddScoped<IVerificationMessageSender, LoggingVerificationMessageSender>();

        services.AddScoped<IExamCatalogue, MongoExamCatalogue>();
        services.AddScoped<IExamSessionRepository, MongoExamSessionRepository>();
        services.AddScoped<IAnswerSheetStore, MongoAnswerSheetStore>();
        services.AddScoped<IRecordingStore, GridFsRecordingStore>();
        services.AddScoped<ISectionResultStore, MongoSectionResultStore>();

        services.AddScoped<ListExams>();
        services.AddScoped<StartExamSession>();
        services.AddScoped<GetExamSession>();
        services.AddScoped<SaveAnswers>();
        services.AddScoped<AdvanceSection>();
        services.AddScoped<SubmitExamSession>();
        services.AddScoped<GetSessionResults>();
        services.AddScoped<ListMySittings>();

        // Development only: it publishes what it loads, which is a reviewed
        // administrative act anywhere else.
        if (isDevelopment) services.AddScoped<DevelopmentExamSeeder>();

        // Development only: real packages carry their media into object
        // storage, which is what MinIO is in the local stack for.
        if (isDevelopment) services.AddSingleton<IExamAssetStore, FixtureAssetStore>();

        // Dictation has no authoring surface yet, so its content is a file
        // read once at startup rather than a repository over an empty table.
        services.AddSingleton<IDictationCatalogue, FixtureDictationCatalogue>();
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

        AddSsoProviders(services, configuration, isDevelopment);

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
