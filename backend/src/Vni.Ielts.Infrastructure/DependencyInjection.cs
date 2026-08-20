using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Identity;
using Vni.Ielts.Infrastructure.Security;

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
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<MongoContext>();
        services.AddSingleton(sp => sp.GetRequiredService<MongoContext>().Database);

        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IEmailVerificationTokens, MongoEmailVerificationTokens>();

        // No production email sender exists yet. Registering the logging one
        // unconditionally would mean a production deployment silently never
        // sends a verification message while reporting success — so the caller
        // must opt in, and Program.cs only does so outside Production.
        services.AddScoped<IVerificationMessageSender, LoggingVerificationMessageSender>();

        services.AddScoped<IUserRepository, MongoUserRepository>();
        services.AddScoped<IUserIdentityRepository, MongoUserIdentityRepository>();
        services.AddScoped<IRoleRepository, MongoRoleRepository>();
        services.AddScoped<IPermissionResolver, MongoPermissionResolver>();

        services.AddScoped<RegisterUser>();
        services.AddScoped<LoginWithPassword>();
        services.AddScoped<RefreshTokens>();
        services.AddScoped<VerifyEmail>();

        return services;
    }

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
