using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Identity;

/// <summary>
/// A named bundle of permissions.
///
/// Permission keys are <c>resource.action</c> strings and are <b>seeded data,
/// not an enum</b>, so adding one does not require a deployment. Requirement
/// C-13 states the example keys are explicitly not final, which is the reason
/// they are not compiled in.
/// </summary>
public sealed class Role
{
    private Role(RoleId id, string name, bool isSystem, IReadOnlyCollection<string> permissions)
    {
        Id = id;
        Name = name;
        IsSystem = isSystem;
        _permissions = new HashSet<string>(permissions, StringComparer.Ordinal);
    }

    private readonly HashSet<string> _permissions;

    public RoleId Id { get; }
    public string Name { get; }

    /// <summary>
    /// Locks the seeded roles against edit or deletion. Without it, an admin
    /// can remove their own last path back into the system.
    /// </summary>
    public bool IsSystem { get; }

    public IReadOnlyCollection<string> Permissions => _permissions;

    public static Role Create(string name, bool isSystem, IReadOnlyCollection<string> permissions)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A role name is required.", nameof(name));
        return new Role(RoleId.New(), name.Trim(), isSystem, permissions);
    }

    public static Role Rehydrate(
        RoleId id, string name, bool isSystem, IReadOnlyCollection<string> permissions) =>
        new(id, name, isSystem, permissions);

    public bool Grants(string permission) => _permissions.Contains(permission);

    public void Grant(string permission)
    {
        GuardMutable();
        _permissions.Add(permission);
    }

    public void Revoke(string permission)
    {
        GuardMutable();
        _permissions.Remove(permission);
    }

    private void GuardMutable()
    {
        if (IsSystem)
            throw new InvalidOperationException($"Role '{Name}' is a system role and cannot be modified.");
    }
}
