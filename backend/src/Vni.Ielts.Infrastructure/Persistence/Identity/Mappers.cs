using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Infrastructure.Persistence.Identity;

/// <summary>
/// The boundary. Domain entity in, persistence document out, and back.
///
/// Hand-written rather than convention-mapped on purpose: an automapper here
/// would silently start working when someone adds a driver attribute to a
/// domain entity, which is precisely the failure the architecture tests exist
/// to catch loudly.
/// </summary>
internal static class IdentityMappers
{
    public static UserDocument ToDocument(this User user) => new()
    {
        Id = user.Id.Value,
        Email = user.Email.Value,
        EmailVerified = user.EmailVerified,
        DisplayName = user.DisplayName,
        Phone = user.Phone?.Value,
        Status = user.Status.ToString(),
        CreatedAt = user.CreatedAt.UtcDateTime,
        RoleIds = [.. user.RoleIds.Select(r => r.Value)],
    };

    public static User ToDomain(this UserDocument doc) => User.Rehydrate(
        new UserId(doc.Id),
        Email.Create(doc.Email),
        doc.EmailVerified,
        doc.DisplayName,
        doc.Phone is null ? null : PhoneNumber.Create(doc.Phone),
        Enum.TryParse<UserStatus>(doc.Status, out var status) ? status : UserStatus.Active,
        // Mongo stores UTC. Re-attaching Zero offset rather than letting
        // DateTime.Kind decide, because an Unspecified kind here silently
        // becomes local time and every deadline shifts by the server's offset.
        new DateTimeOffset(DateTime.SpecifyKind(doc.CreatedAt, DateTimeKind.Utc)),
        [.. doc.RoleIds.Select(r => new RoleId(r))]);

    public static UserIdentityDocument ToDocument(this UserIdentity identity) => new()
    {
        Id = identity.Id.Value,
        UserId = identity.UserId.Value,
        Provider = identity.Provider.ToString(),
        ProviderUserId = identity.ProviderUserId,
        PasswordHash = identity.PasswordHash,
        LinkedAt = identity.LinkedAt.UtcDateTime,
    };

    public static UserIdentity ToDomain(this UserIdentityDocument doc) => UserIdentity.Rehydrate(
        new UserIdentityId(doc.Id),
        new UserId(doc.UserId),
        Enum.Parse<IdentityProvider>(doc.Provider),
        doc.ProviderUserId,
        doc.PasswordHash,
        new DateTimeOffset(DateTime.SpecifyKind(doc.LinkedAt, DateTimeKind.Utc)));

    public static RoleDocument ToDocument(this Role role) => new()
    {
        Id = role.Id.Value,
        Name = role.Name,
        IsSystem = role.IsSystem,
        Permissions = [.. role.Permissions],
    };

    public static Role ToDomain(this RoleDocument doc) =>
        Role.Rehydrate(new RoleId(doc.Id), doc.Name, doc.IsSystem, doc.Permissions);
}
