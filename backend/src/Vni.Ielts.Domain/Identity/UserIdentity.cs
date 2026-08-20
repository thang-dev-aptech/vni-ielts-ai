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
    public string ProviderUserId { get; }

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
}
