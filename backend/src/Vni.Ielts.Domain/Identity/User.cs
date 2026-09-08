using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Identity;

public enum UserStatus
{
    Active,
    Suspended,
}

/// <summary>
/// A person with an account.
///
/// Carries no persistence attributes — no <c>[BsonId]</c>, no EF annotations,
/// no driver types. That is CLAUDE.md rule 7 and it is enforced by the
/// architecture tests, not by discipline.
///
/// Login methods live on <see cref="UserIdentity"/>, not here. Collapsing the
/// two would make account linking impossible without a migration (AU-1/2/3).
/// </summary>
public sealed class User
{
    private User(
        UserId id,
        Email? email,
        string displayName,
        PhoneNumber? phone,
        UserStatus status,
        DateTimeOffset createdAt,
        IReadOnlyCollection<RoleId> roleIds,
        string? referralCode,
        UserId? referredByUserId)
    {
        Id = id;
        Email = email;
        DisplayName = displayName;
        Phone = phone;
        Status = status;
        CreatedAt = createdAt;
        _roleIds = [.. roleIds];
        ReferralCode = referralCode;
        ReferredByUserId = referredByUserId;
    }

    private readonly HashSet<RoleId> _roleIds;

    public UserId Id { get; }

    /// <summary>
    /// The account's address, or null.
    ///
    /// <para>
    /// <b>Null is the ordinary state, not an edge case.</b> Registration asks
    /// for a name, a phone number and a password — no address — so every
    /// account created that way starts here with nothing, and the profile shows
    /// an empty field until the learner chooses to fill it.
    /// </para>
    ///
    /// <para>
    /// <b>It identifies the account while it is set, and it can move.</b> A
    /// social sign-in resolves to whichever account currently holds the address
    /// the provider vouches for. Change the address and the account goes with
    /// it, leaving the old one unclaimed for whoever signs in with it next.
    /// That is the owner's decision of 08/09/2026 and it is the opposite of
    /// what <c>AU-7</c> used to say. → ADR-0018
    /// </para>
    /// </summary>
    public Email? Email { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>
    /// The learner's number. Set at registration, and the handle most accounts
    /// sign in with.
    ///
    /// <b>Self-declared.</b> Nothing proves it, and nothing here pretends
    /// otherwise — see <see cref="PhoneNumber"/>. What the product does rely on
    /// is that it is *unique*, which the repository's index enforces.
    /// </summary>
    public PhoneNumber? Phone { get; private set; }

    public UserStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyCollection<RoleId> RoleIds => _roleIds;

    /// <summary>
    /// The code this learner shares so a registration can be credited to
    /// them. Generated at registration; null only on accounts that predate
    /// referrals, which <see cref="EnsureReferralCode"/> repairs on first
    /// read. → `P-16`
    /// </summary>
    public string? ReferralCode { get; private set; }

    /// <summary>
    /// Who brought this learner in, if anyone. Set once at registration and
    /// never changed — the reward it drives is paid when this account is
    /// created, and the attribution must not be able to move after that.
    /// → `P-16`, threat T13
    /// </summary>
    public UserId? ReferredByUserId { get; private set; }

    /// <summary>
    /// A suspended account must not be able to start an exam session or spend
    /// tokens, so the check belongs on the entity rather than being repeated
    /// at every call site.
    /// </summary>
    public bool CanAuthenticate => Status == UserStatus.Active;

    /// <summary>
    /// Something a person can type into the sign-in box and reach this account
    /// with. A password on its own is not one — there has to be a handle in
    /// front of it. → <see cref="EnsureSignInMethodRemains"/>
    /// </summary>
    public bool HasTypedHandle => Email is not null || Phone is not null;

    /// <summary>Registration: a name, a number, and a password held elsewhere.</summary>
    public static User Register(PhoneNumber phone, string displayName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));

        return new User(
            UserId.New(),
            email: null,
            displayName.Trim(),
            phone,
            UserStatus.Active,
            now,
            [],
            Identity.ReferralCode.Generate(),
            referredByUserId: null);
    }

    /// <summary>
    /// The account a social sign-in creates: an address from the provider, no
    /// number and no password. Its only way back in is the provider link, which
    /// is why <see cref="EnsureSignInMethodRemains"/> has to count that link.
    /// </summary>
    public static User RegisterFromProvider(Email email, string displayName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));

        return new User(
            UserId.New(),
            email,
            displayName.Trim(),
            phone: null,
            UserStatus.Active,
            now,
            [],
            Identity.ReferralCode.Generate(),
            referredByUserId: null);
    }

    /// <summary>Rehydration from storage. Infrastructure only.</summary>
    public static User Rehydrate(
        UserId id,
        Email? email,
        string displayName,
        PhoneNumber? phone,
        UserStatus status,
        DateTimeOffset createdAt,
        IReadOnlyCollection<RoleId> roleIds,
        string? referralCode = null,
        UserId? referredByUserId = null) =>
        new(id, email, displayName, phone, status, createdAt, roleIds, referralCode,
            referredByUserId);

    /// <summary>
    /// Records who referred this account. Refuses to overwrite an existing
    /// attribution and refuses self-referral — the first would let a reward
    /// be redirected after the fact, the second is the trivial farm.
    /// </summary>
    public void AttributeReferral(UserId referrer)
    {
        if (referrer == Id)
            throw new InvalidOperationException("An account cannot refer itself. → User.AttributeReferral");
        if (ReferredByUserId is not null)
            throw new InvalidOperationException("Referral attribution is set once. → User.AttributeReferral");

        ReferredByUserId = referrer;
    }

    /// <summary>
    /// Gives an account that predates referrals a code. Returns true when one
    /// was assigned, so the caller knows a save is needed.
    /// </summary>
    public bool EnsureReferralCode()
    {
        if (ReferralCode is not null) return false;
        ReferralCode = Identity.ReferralCode.Generate();
        return true;
    }

    /// <summary>
    /// Sets, changes or clears the address. Null removes it.
    ///
    /// <para>
    /// <b>There is no lock any more, and that is the decision, not an
    /// oversight.</b> The address used to freeze the moment it was verified,
    /// on the reasoning that a proven address is the account's route back in
    /// and a stolen session must not be able to move it to another mailbox.
    /// Verification is gone, so there is nothing left to freeze on — and the
    /// owner asked for the opposite behaviour outright: changing the address
    /// moves the account to it. → ADR-0018
    /// </para>
    /// </summary>
    /// <param name="hasLinkedProvider">
    /// Whether a social identity is attached. The entity cannot see the
    /// identity table and must not reach for it (CLAUDE.md rule 7), so the
    /// caller supplies the one fact the invariant needs.
    /// </param>
    public void ChangeEmail(Email? email, bool hasLinkedProvider)
    {
        EnsureSignInMethodRemains(email, Phone, hasLinkedProvider);
        Email = email;
    }

    /// <summary>Sets or clears the contact number. Null removes it.</summary>
    /// <param name="hasLinkedProvider">See <see cref="ChangeEmail"/>.</param>
    public void SetPhone(PhoneNumber? phone, bool hasLinkedProvider)
    {
        EnsureSignInMethodRemains(Email, phone, hasLinkedProvider);
        Phone = phone;
    }

    /// <summary>
    /// Refuses a change that would leave nobody — including the account holder
    /// — able to reach this account again.
    ///
    /// <para>
    /// Worth stating plainly because the failure is silent and permanent: an
    /// account with no address, no number and no linked provider still holds
    /// its sittings and its recordings, still counts under PDPL, and has no
    /// door. Clearing the last handle is the easy way to produce one, and it
    /// looks like an ordinary profile edit right up until the session expires.
    /// </para>
    ///
    /// <para>
    /// Throwing rather than returning false: reaching here is a defect in the
    /// caller. The use case refuses first, with a message a person can act on.
    /// </para>
    /// </summary>
    private static void EnsureSignInMethodRemains(
        Email? email, PhoneNumber? phone, bool hasLinkedProvider)
    {
        if (email is null && phone is null && !hasLinkedProvider)
        {
            throw new InvalidOperationException(
                "An account must keep at least one way to sign in. "
                + "→ User.EnsureSignInMethodRemains");
        }
    }

    public void Suspend() => Status = UserStatus.Suspended;

    public void Reinstate() => Status = UserStatus.Active;

    public void Rename(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));
        DisplayName = displayName.Trim();
    }

    public void AssignRole(RoleId roleId) => _roleIds.Add(roleId);

    public void RemoveRole(RoleId roleId) => _roleIds.Remove(roleId);

    public bool HasRole(RoleId roleId) => _roleIds.Contains(roleId);
}
