using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Application.Identity;

public sealed record ListSessionsQuery(UserId UserId, string? CurrentFamilyId);

/// <summary>
/// The devices signed in to this account.
/// </summary>
public sealed class ListSessions(ISessionDirectory sessions)
{
    public async Task<IReadOnlyList<LearnerSession>> HandleAsync(
        ListSessionsQuery query, CancellationToken ct) =>
        await sessions.ListAsync(query.UserId, query.CurrentFamilyId, ct);
}

public sealed record RevokeSessionCommand(UserId UserId, string FamilyId, string? CurrentFamilyId);

/// <summary>
/// Ends one sign-in.
///
/// <para>
/// <b>Scoped to the caller's own account.</b> The family id comes from the URL,
/// and a family id from someone else's account must not end their session —
/// so the revoke is filtered by user id as well, which
/// <see cref="ITokenService.RevokeFamilyAsync"/> already does. Passing an id
/// that is not yours is indistinguishable from passing one that does not
/// exist. → threat T19
/// </para>
/// </summary>
public sealed class RevokeSession(ITokenService tokens)
{
    public async Task<Result<bool>> HandleAsync(RevokeSessionCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.FamilyId))
            return Error.Validation(ErrorCodes.ValidationFailed, "A session id is required.");

        // Signing out the device you are holding is a different action with a
        // different consequence — it should go through sign-out, which also
        // clears local state. Doing it from this list would leave the client
        // holding a dead token and showing a signed-in header.
        if (command.FamilyId == command.CurrentFamilyId)
        {
            return Error.Conflict(
                ErrorCodes.SessionIsCurrent,
                "This is the device you are using. Sign out from the account menu instead.");
        }

        await tokens.RevokeFamilyAsync(command.UserId, command.FamilyId, ct);
        return true;
    }
}

public sealed record RevokeOtherSessionsCommand(UserId UserId, string? CurrentFamilyId);

/// <summary>
/// Ends every sign-in except this one.
///
/// <para>
/// The action someone actually wants when they see a device they do not
/// recognise: not "work out which of these eleven is wrong" but "get everyone
/// else out". Signing them out one at a time both takes longer and leaves the
/// suspicious session live while they work through the list.
/// </para>
///
/// <para>
/// It refuses without a current family rather than falling back to revoking
/// everything. A token minted before the <c>fam</c> claim existed would
/// otherwise sign the caller out of the device they are holding — a silent,
/// surprising logout at the exact moment they were trying to secure the
/// account.
/// </para>
/// </summary>
public sealed class RevokeOtherSessions(ITokenService tokens)
{
    public async Task<Result<int>> HandleAsync(RevokeOtherSessionsCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.CurrentFamilyId))
        {
            return Error.Validation(
                ErrorCodes.ValidationFailed,
                "This sign-in cannot be identified. Sign in again, then try once more.");
        }

        return await tokens.RevokeAllExceptAsync(command.UserId, command.CurrentFamilyId, ct);
    }
}
