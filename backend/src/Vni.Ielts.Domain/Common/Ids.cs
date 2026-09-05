namespace Vni.Ielts.Domain.Common;

/// <summary>
/// Strongly-typed identifiers.
///
/// The domain never sees <c>ObjectId</c>. That type belongs to the MongoDB
/// driver, and letting it into an entity would put a storage concept in the
/// layer the PostgreSQL migration depends on staying clean — the architecture
/// tests fail the build for exactly this.
///
/// The value is a string because it must survive a database change without
/// the domain noticing. Infrastructure maps it to whatever the store wants.
///
/// They are separate types rather than one <c>Id</c> because
/// <c>GetUser(roleId)</c> should not compile.
/// </summary>
public readonly record struct UserId(string Value)
{
    public override string ToString() => Value;
    public static UserId New() => new(Guid.NewGuid().ToString("n"));
}

public readonly record struct UserIdentityId(string Value)
{
    public override string ToString() => Value;
    public static UserIdentityId New() => new(Guid.NewGuid().ToString("n"));
}

public readonly record struct RoleId(string Value)
{
    public override string ToString() => Value;
    public static RoleId New() => new(Guid.NewGuid().ToString("n"));
}
