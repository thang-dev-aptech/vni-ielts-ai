using MongoDB.Driver;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Infrastructure.Persistence.Identity;

/// <summary>
/// Claims an invitation and provisions the account in one replica-set
/// transaction. A second concurrent accept of the same token loses the
/// conditional status update and rolls back — never leaving a user without an
/// identity, and never reusing an accepted/revoked/expired token.
///
/// Adapted for main: <see cref="User.RegisterFromProvider"/> and
/// <see cref="UserIdentity.ForPassword"/> (not feature Email identity).
/// </summary>
internal sealed class MongoStaffInvitationAcceptance(MongoContext ctx) : IStaffInvitationAcceptance
{
    public async Task<Result<AcceptStaffInvitationResult>> AcceptAsync(
        string tokenHash,
        string displayName,
        string passwordHash,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var session = await ctx.Database.Client.StartSessionAsync(cancellationToken: ct);
        try
        {
            return await session.WithTransactionAsync(
                async (s, cancellationToken) =>
                {
                    var invitationDoc = await ctx.StaffInvitations
                        .Find(s, i => i.TokenHash == tokenHash)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (invitationDoc is null
                        || invitationDoc.Status != nameof(StaffInvitationStatus.Pending)
                        || invitationDoc.ExpiresAt <= now.UtcDateTime)
                    {
                        throw new AcceptRejectedException(Error.Validation(
                            ErrorCodes.InvitationInvalid, "This invitation is no longer valid."));
                    }

                    var claim = await ctx.StaffInvitations.UpdateOneAsync(
                        s,
                        Builders<StaffInvitationDocument>.Filter.And(
                            Builders<StaffInvitationDocument>.Filter.Eq(i => i.Id, invitationDoc.Id),
                            Builders<StaffInvitationDocument>.Filter.Eq(
                                i => i.Status, nameof(StaffInvitationStatus.Pending)),
                            Builders<StaffInvitationDocument>.Filter.Gt(i => i.ExpiresAt, now.UtcDateTime),
                            Builders<StaffInvitationDocument>.Filter.Eq(i => i.TokenHash, tokenHash)),
                        Builders<StaffInvitationDocument>.Update.Set(
                            i => i.Status, nameof(StaffInvitationStatus.Accepted)),
                        cancellationToken: cancellationToken);

                    if (claim.ModifiedCount == 0)
                    {
                        throw new AcceptRejectedException(Error.Validation(
                            ErrorCodes.InvitationInvalid, "This invitation is no longer valid."));
                    }

                    var emailExists = await ctx.Users
                        .Find(s, u => u.Email == invitationDoc.Email)
                        .AnyAsync(cancellationToken);
                    if (emailExists)
                    {
                        throw new AcceptRejectedException(Error.Conflict(
                            ErrorCodes.EmailAlreadyRegistered,
                            "That email address is already registered."));
                    }

                    var user = User.RegisterFromProvider(
                        Email.Create(invitationDoc.Email), displayName, now);
                    foreach (var roleId in invitationDoc.RoleIds)
                        user.AssignRole(new RoleId(roleId));

                    try
                    {
                        await ctx.Users.InsertOneAsync(s, user.ToDocument(), cancellationToken: cancellationToken);
                    }
                    catch (MongoWriteException e)
                        when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                    {
                        throw new DuplicateEmailException(invitationDoc.Email, e);
                    }

                    var identity = UserIdentity.ForPassword(user.Id, passwordHash, now);
                    await ctx.UserIdentities.InsertOneAsync(
                        s, identity.ToDocument(), cancellationToken: cancellationToken);

                    return new AcceptStaffInvitationResult(user.Id.Value);
                },
                cancellationToken: ct);
        }
        catch (AcceptRejectedException e)
        {
            return e.Error;
        }
        catch (DuplicateEmailException)
        {
            return Error.Conflict(
                ErrorCodes.EmailAlreadyRegistered, "That email address is already registered.");
        }
    }

    /// <summary>
    /// Logical refusal that must abort the transaction so a claimed invitation
    /// is never left Accepted without a matching account.
    /// </summary>
    private sealed class AcceptRejectedException(Error error) : Exception
    {
        public Error Error { get; } = error;
    }
}
