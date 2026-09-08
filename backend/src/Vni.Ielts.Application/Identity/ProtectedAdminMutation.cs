using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

/// <summary>
/// Outcome of a last-admin-protected write. Persistence owns the atomicity —
/// Application never count-then-saves across a race window.
/// </summary>
public enum ProtectedAdminMutationOutcome
{
    Applied,
    NotFound,
    AlreadyApplied,
    WouldLeaveZeroActiveAdmins,
}

public sealed record ProtectedAdminMutationResult(
    ProtectedAdminMutationOutcome Outcome,
    User? User);

/// <summary>
/// Cross-instance-safe mutations that can remove the last active admin.
///
/// Implemented in Infrastructure with a Mongo multi-document transaction that
/// bumps a coordination document before the user write, so concurrent API
/// instances cannot both clear the last admin. The same port covers single
/// suspend, admin-role revocation, bulk suspend, and future erasure.
/// </summary>
public interface IProtectedAdminMutation
{
    Task<ProtectedAdminMutationResult> TrySuspendAsync(UserId targetId, CancellationToken ct);

    Task<ProtectedAdminMutationResult> TryRevokeAdminRoleAsync(UserId targetId, CancellationToken ct);
}
