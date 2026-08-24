using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

/// <summary>
/// How this account can be signed in to.
/// </summary>
/// <param name="Providers">
/// Lower-case provider keys — <c>email</c>, <c>google</c>. The client uses
/// this to say true things: a person who only ever used Google has no password
/// to change, and telling them to "enter your current password" is asking for
/// something that does not exist.
/// </param>
/// <param name="HasPassword">
/// Whether an email identity with a password hash exists. Not the same as
/// <c>Providers</c> containing <c>email</c>: linking a provider into an
/// unverified account clears its password and leaves the row behind.
/// → ADR-0013
/// </param>
public sealed record MyAccount(
    UserId UserId,
    string DisplayName,
    string? Email,
    bool EmailVerified,
    string? Phone,
    IReadOnlyCollection<string> Providers,
    bool HasPassword);

/// <summary>
/// The account behind the access token.
///
/// <para>
/// <b>This one reads the database, unlike the rest of <c>/me</c>.</b> Display
/// name, verification and permissions all travel in the token and cost
/// nothing; which providers are linked does not, and cannot — a provider
/// linked five minutes ago must show immediately, not fifteen minutes later
/// when the access token rolls over.
/// </para>
///
/// <para>
/// It is one indexed read by user id plus one by that id on the identity
/// collection. Worth it for a screen that exists to tell people the truth
/// about their own account.
/// </para>
/// </summary>
public sealed class GetMyAccount(IUserRepository users, IUserIdentityRepository identities)
{
    public async Task<MyAccount?> HandleAsync(UserId userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null) return null;

        var linked = await identities.ListForUserAsync(userId, ct);

        return new MyAccount(
            user.Id,
            user.DisplayName,
            user.Email.Value,
            user.EmailVerified,
            user.Phone?.Value,
            [.. linked.Select(i => i.Provider.ToString().ToLowerInvariant()).Distinct()],
            linked.Any(i => i.Provider == IdentityProvider.Email && i.PasswordHash is not null));
    }
}
