using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Identity;

/// <summary>
/// How a user proves who they are. Email and password, or a social provider.
/// </summary>
public enum IdentityProvider
{
    Email,
    Google,
    Facebook,
}

/// <summary>
/// One login method belonging to one <see cref="User"/>.
///
/// Separate from <c>User</c> so an account can carry several login methods
/// (AU-1/2/3), and so AU-6 — accommodating multiple SSO providers without
/// rework — is structural rather than a promise.
///
/// <para>
/// <b>The password hash lives here, not on User.</b> A user who signs in only
/// with Google has no password at all, and modelling that as a nullable field
/// on <c>User</c> invites code that assumes one exists.
/// </para>
/// </summary>
public sealed class UserIdentity
{
    private UserIdentity(
        UserIdentityId id,
        UserId userId,
        IdentityProvider provider,
        string providerUserId,
        string? passwordHash,
        DateTimeOffset linkedAt)
    {
        Id = id;
        UserId = userId;
        Provider = provider;
        ProviderUserId = providerUserId;
        PasswordHash = passwordHash;
        LinkedAt = linkedAt;
    }

    public UserIdentityId Id { get; }
    public UserId UserId { get; }
    public IdentityProvider Provider { get; }

    /// <summary>
    /// The subject as the provider knows it. For <see cref="IdentityProvider.Email"/>
    /// this is the normalised address; for a social provider it is the stable
    /// subject claim — never the email, which a user can change at the provider.
    /// </summary>
    public string ProviderUserId { get; private set; }

    /// <summary>Argon2id. Null for every provider except Email.</summary>
    public string? PasswordHash { get; private set; }

    public DateTimeOffset LinkedAt { get; }

    public static UserIdentity ForEmail(UserId userId, Email email, string passwordHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("An email identity requires a password hash.", nameof(passwordHash));

        return new UserIdentity(
            UserIdentityId.New(), userId, IdentityProvider.Email, email.Value, passwordHash, now);
    }

    public static UserIdentity ForSocial(
        UserId userId, IdentityProvider provider, string providerUserId, DateTimeOffset now)
    {
        if (provider == IdentityProvider.Email)
            throw new ArgumentException("Use ForEmail for the email provider.", nameof(provider));
        if (string.IsNullOrWhiteSpace(providerUserId))
            throw new ArgumentException("A provider subject is required.", nameof(providerUserId));

        return new UserIdentity(
            UserIdentityId.New(), userId, provider, providerUserId, passwordHash: null, now);
    }

    public static UserIdentity Rehydrate(
        UserIdentityId id,
        UserId userId,
        IdentityProvider provider,
        string providerUserId,
        string? passwordHash,
        DateTimeOffset linkedAt) =>
        new(id, userId, provider, providerUserId, passwordHash, linkedAt);

    public void SetPasswordHash(string passwordHash)
    {
        if (Provider != IdentityProvider.Email)
            throw new InvalidOperationException("Only an email identity carries a password.");
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("A password hash is required.", nameof(passwordHash));
        PasswordHash = passwordHash;
    }

    /// <summary>
    /// Removes the password, leaving the identity unusable for sign-in.
    ///
    /// <para>
    /// This exists for one situation and should not be reached for by anything
    /// else: a social identity is being linked to an account whose email
    /// address was never verified. Registration creates a <c>User</c> before
    /// the address is proven, so anyone can register an address they do not
    /// own and set a password on it. If that account is later merged with a
    /// provider-verified sign-in for the same address, the squatter's password
    /// would keep working on the real owner's account. → ADR-0013, threat T1
    /// </para>
    ///
    /// <para>
    /// Clearing the hash rather than deleting the identity is deliberate: the
    /// row records that this address was once registered directly, which is
    /// worth keeping. <see cref="LoginWithPassword"/> already treats a null
    /// hash as indistinguishable from an unknown account.
    /// </para>
    /// </summary>
    /// <summary>
    /// Follows a change of the account's email address.
    ///
    /// <para>
    /// <b>This has to happen or password sign-in breaks silently.</b> For the
    /// email provider, <see cref="ProviderUserId"/> <i>is</i> the normalised
    /// address, and that is what <c>LoginWithPassword</c> looks up — not
    /// <c>User.Email</c>. Change one without the other and the account still
    /// exists, still shows the new address in its profile, and can no longer
    /// be signed in to with a password at either address.
    /// </para>
    /// </summary>
    public void ChangeEmailAddress(Email email)
    {
        if (Provider != IdentityProvider.Email)
            throw new InvalidOperationException("Only an email identity is keyed by an address.");

        ProviderUserId = email.Value;
    }

    public void ClearPassword()
    {
        if (Provider != IdentityProvider.Email)
            throw new InvalidOperationException("Only an email identity carries a password.");
        PasswordHash = null;
    }
}
