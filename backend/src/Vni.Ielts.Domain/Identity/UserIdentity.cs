using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Identity;

/// <summary>
/// How a user proves who they are. A password, or a social provider.
/// </summary>
public enum IdentityProvider
{
    /// <summary>
    /// <b>Retired, and deliberately still here.</b> Password identities used to
    /// be keyed by the account's email address under this name. Stored rows are
    /// read back through <c>Enum.Parse</c>, so deleting the value would turn a
    /// migration that stopped halfway into a service that cannot boot: every
    /// un-rewritten row would throw at the mapper. It is parsed, never written.
    /// Remove it one release after the migration has run everywhere.
    /// </summary>
    [Obsolete("Rewritten to Password by the identity migration. Parsed, never written.")]
    Email,

    Google,
    Facebook,

    /// <summary>
    /// Email-or-phone plus a password. Keyed by the <see cref="UserId"/> — see
    /// <see cref="UserIdentity.ProviderUserId"/> for why that matters.
    /// </summary>
    Password,
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
    /// The subject as the provider knows it. For a social provider it is the
    /// stable subject claim — never the email, which a user can change at the
    /// provider. For <see cref="IdentityProvider.Password"/> it is the
    /// <see cref="UserId"/>.
    ///
    /// <para>
    /// <b>The password row is keyed by the account, not by a handle, and that
    /// is load-bearing.</b> It used to hold the normalised email address, which
    /// meant the address lived in two places — here and on <c>User</c> — and
    /// changing one without the other produced an account that displayed its
    /// new address and could no longer be signed in to at either. Sign-in now
    /// resolves the <c>User</c> from whatever handle was typed (address or
    /// phone) and then reads this row by account, so there is one address, one
    /// phone, and nothing to keep in step. It also means an account with both
    /// a phone and an address needs one password row rather than two carrying
    /// duplicate hashes.
    /// </para>
    /// </summary>
    public string ProviderUserId { get; private set; }

    /// <summary>Argon2id. Null for every provider except Password.</summary>
    public string? PasswordHash { get; private set; }

    public DateTimeOffset LinkedAt { get; }

    public static UserIdentity ForPassword(UserId userId, string passwordHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("A password identity requires a password hash.", nameof(passwordHash));

        return new UserIdentity(
            UserIdentityId.New(), userId, IdentityProvider.Password, userId.Value, passwordHash, now);
    }

    public static UserIdentity ForSocial(
        UserId userId, IdentityProvider provider, string providerUserId, DateTimeOffset now)
    {
        if (provider is IdentityProvider.Password)
            throw new ArgumentException("Use ForPassword for the password provider.", nameof(provider));
        if (string.IsNullOrWhiteSpace(providerUserId))
            throw new ArgumentException("A provider subject is required.", nameof(providerUserId));

        return new UserIdentity(
            UserIdentityId.New(), userId, provider, providerUserId, passwordHash: null, now);
    }

    /// <summary>
    /// This password was set by somebody other than its owner, and has to be
    /// replaced before the account is usable.
    ///
    /// <para>
    /// <b>An operator reset is the only recovery path there is.</b>
    /// Registration collects no address (<see href="ADR-0018"/>), so a locked
    /// out learner reaches a human, and the human types a password and reads
    /// it back over Zalo. That password is therefore known to at least two
    /// people and has travelled through a chat log — which is fine for one
    /// sign-in and not fine as the account's standing credential.
    /// </para>
    ///
    /// <para>
    /// <b>False for a password its owner chose</b>, including the one they
    /// choose to clear this flag. Nothing else sets it: a learner changing
    /// their own password is not recovering from anything.
    /// </para>
    /// </summary>
    public bool MustChangePassword { get; private set; }

    public static UserIdentity Rehydrate(
        UserIdentityId id,
        UserId userId,
        IdentityProvider provider,
        string providerUserId,
        string? passwordHash,
        DateTimeOffset linkedAt,
        bool mustChangePassword = false) =>
        new(id, userId, provider, providerUserId, passwordHash, linkedAt)
        {
            MustChangePassword = mustChangePassword,
        };

    /// <param name="mustChange">
    /// True only when somebody other than the owner set this password. The
    /// default is the owner's own change, which clears the flag — so a reset
    /// followed by a real change leaves no residue, and forgetting to pass
    /// anything can never accidentally mark an account as compromised.
    /// </param>
    public void SetPasswordHash(string passwordHash, bool mustChange = false)
    {
        if (Provider != IdentityProvider.Password)
            throw new InvalidOperationException("Only a password identity carries a password.");
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("A password hash is required.", nameof(passwordHash));
        PasswordHash = passwordHash;
        MustChangePassword = mustChange;
    }
}
