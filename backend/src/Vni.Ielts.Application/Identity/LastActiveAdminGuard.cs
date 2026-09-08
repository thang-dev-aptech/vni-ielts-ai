using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

public enum LastAdminOperation
{
    Suspend,
    RevokeAdminRole,
    Erase,
}

/// <summary>
/// Read-only preview of the last-admin invariant (privacy execute, UI hints).
/// Writes that can clear the last admin must go through
/// <see cref="IProtectedAdminMutation"/> — never count-then-save.
/// </summary>
public interface ILastActiveAdminGuard
{
    Task<bool> WouldLeaveZeroActiveAdminsAsync(
        User target, LastAdminOperation operation, CancellationToken ct);
}

public sealed class LastActiveAdminGuard(IUserRepository users, IRoleRepository roles)
    : ILastActiveAdminGuard
{
    public async Task<bool> WouldLeaveZeroActiveAdminsAsync(
        User target, LastAdminOperation operation, CancellationToken ct)
    {
        var admin = await roles.FindByNameAsync(SystemRoles.Admin, ct);
        if (admin is null) return false;
        if (!target.HasRole(admin.Id)) return false;
        if (operation == LastAdminOperation.Suspend && target.Status == UserStatus.Suspended)
            return false;

        var others = await users.CountActiveWithRoleAsync(admin.Id, target.Id, ct);
        return others == 0;
    }
}
