using MongoDB.Driver;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Infrastructure.Persistence.Identity;

/// <summary>
/// Serialises last-admin-sensitive writes across API instances.
///
/// <b>Why a coordination document.</b> Counting active admins then saving the
/// target is a classic TOCTOU race: two concurrent suspends of the last two
/// admins can both see <c>others == 1</c> and both commit. Snapshot isolation
/// alone does not help — both reads are consistent with the pre-write state.
/// Bumping one shared document inside the same multi-document transaction
/// forces a write conflict; the loser retries and re-evaluates against the
/// winner's committed state. The replica set in <c>infra/docker</c> exists so
/// this works in every environment. → ADR-0011
/// </summary>
internal sealed class MongoProtectedAdminMutation(MongoContext ctx) : IProtectedAdminMutation
{
    internal const string CoordinationId = "last-active-admin";

    public Task<ProtectedAdminMutationResult> TrySuspendAsync(UserId targetId, CancellationToken ct) =>
        RunAsync(targetId, LastAdminOperation.Suspend, ct);

    public Task<ProtectedAdminMutationResult> TryRevokeAdminRoleAsync(UserId targetId, CancellationToken ct) =>
        RunAsync(targetId, LastAdminOperation.RevokeAdminRole, ct);

    private async Task<ProtectedAdminMutationResult> RunAsync(
        UserId targetId, LastAdminOperation operation, CancellationToken ct)
    {
        using var session = await ctx.Database.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(
            async (s, cancellationToken) =>
            {
                await BumpCoordinationAsync(s, cancellationToken);

                var adminRole = await ctx.Roles
                    .Find(s, r => r.Name == SystemRoles.Admin)
                    .FirstOrDefaultAsync(cancellationToken);

                var doc = await ctx.Users
                    .Find(s, u => u.Id == targetId.Value)
                    .FirstOrDefaultAsync(cancellationToken);
                if (doc is null)
                    return new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.NotFound, null);

                var user = doc.ToDomain();

                return operation switch
                {
                    LastAdminOperation.Suspend => await SuspendInSessionAsync(
                        s, user, adminRole, cancellationToken),
                    LastAdminOperation.RevokeAdminRole => await RevokeAdminInSessionAsync(
                        s, user, adminRole, cancellationToken),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
                };
            },
            cancellationToken: ct);
    }

    private async Task<ProtectedAdminMutationResult> SuspendInSessionAsync(
        IClientSessionHandle session,
        User user,
        RoleDocument? adminRole,
        CancellationToken ct)
    {
        if (user.Status == UserStatus.Suspended)
            return new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.AlreadyApplied, user);

        if (adminRole is not null
            && user.HasRole(new RoleId(adminRole.Id))
            && await CountOtherActiveAdminsAsync(session, adminRole.Id, user.Id, ct) == 0)
        {
            return new ProtectedAdminMutationResult(
                ProtectedAdminMutationOutcome.WouldLeaveZeroActiveAdmins, user);
        }

        user.Suspend();
        await ctx.Users.ReplaceOneAsync(
            session,
            u => u.Id == user.Id.Value,
            user.ToDocument(),
            cancellationToken: ct);
        return new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.Applied, user);
    }

    private async Task<ProtectedAdminMutationResult> RevokeAdminInSessionAsync(
        IClientSessionHandle session,
        User user,
        RoleDocument? adminRole,
        CancellationToken ct)
    {
        if (adminRole is null)
            return new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.NotFound, user);

        var adminRoleId = new RoleId(adminRole.Id);
        if (!user.HasRole(adminRoleId))
            return new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.AlreadyApplied, user);

        if (user.Status == UserStatus.Active
            && await CountOtherActiveAdminsAsync(session, adminRole.Id, user.Id, ct) == 0)
        {
            return new ProtectedAdminMutationResult(
                ProtectedAdminMutationOutcome.WouldLeaveZeroActiveAdmins, user);
        }

        user.RemoveRole(adminRoleId);
        await ctx.Users.ReplaceOneAsync(
            session,
            u => u.Id == user.Id.Value,
            user.ToDocument(),
            cancellationToken: ct);
        return new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.Applied, user);
    }

    private Task BumpCoordinationAsync(IClientSessionHandle session, CancellationToken ct) =>
        ctx.AdminPrivilegeCoordination.UpdateOneAsync(
            session,
            Builders<AdminPrivilegeCoordinationDocument>.Filter.Eq(d => d.Id, CoordinationId),
            Builders<AdminPrivilegeCoordinationDocument>.Update.Inc(d => d.Seq, 1),
            new UpdateOptions { IsUpsert = true },
            ct);

    private Task<long> CountOtherActiveAdminsAsync(
        IClientSessionHandle session, string adminRoleId, UserId excluding, CancellationToken ct)
    {
        var filter = Builders<UserDocument>.Filter.And(
            Builders<UserDocument>.Filter.Eq(u => u.Status, nameof(UserStatus.Active)),
            Builders<UserDocument>.Filter.AnyEq(u => u.RoleIds, adminRoleId),
            Builders<UserDocument>.Filter.Ne(u => u.Id, excluding.Value));

        return ctx.Users.CountDocumentsAsync(session, filter, cancellationToken: ct);
    }
}
