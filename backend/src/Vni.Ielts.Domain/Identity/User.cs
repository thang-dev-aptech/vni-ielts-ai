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
        Email email,
        bool emailVerified,
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
        EmailVerified = emailVerified;
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
    public Email Email { get; private set; }
    public bool EmailVerified { get; private set; }
    public string DisplayName { get; private set; }

    /// <summary>
    /// A contact number the learner typed. Null until they add one.
    ///
    /// <b>Self-declared.</b> Nothing proves it, and nothing here pretends
    /// otherwise — see <see cref="PhoneNumber"/>.
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
    /// never changed — the reward it drives is paid when this account verifies
    /// its address, and the attribution must not be able to move after that.
    /// → `P-16`, threat T13
    /// </summary>
    public UserId? ReferredByUserId { get; private set; }

    /// <summary>
    /// A suspended account must not be able to start an exam session or spend
    /// tokens, so the check belongs on the entity rather than being repeated
    /// at every call site.
    /// </summary>
    public bool CanAuthenticate => Status == UserStatus.Active;

    public static User Register(Email email, string displayName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));

        return new User(
            UserId.New(),
            email,
            emailVerified: false,
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
        Email email,
        bool emailVerified,
        string displayName,
        PhoneNumber? phone,
        UserStatus status,
        DateTimeOffset createdAt,
        IReadOnlyCollection<RoleId> roleIds,
        string? referralCode = null,
        UserId? referredByUserId = null) =>
        new(id, email, emailVerified, displayName, phone, status, createdAt, roleIds, referralCode,
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
    /// Verification is what turns an address from a claim into a fact. Several
    /// things wait on it: entitlement accrual (threat T4, so bulk-created
    /// accounts cannot farm rewards) and referral attribution confirmation
    /// (threat T13, so self-referral with disposable addresses does not pay).
    /// </summary>
    public void MarkEmailVerified() => EmailVerified = true;

    /// <summary>
    /// Corrects the address, while it is still only a claim.
    ///
    /// <para>
    /// <b>Locked the moment it is verified, and that is the whole rule.</b>
    /// An unverified address is a typo waiting to be fixed — someone who wrote
    /// `gmial.com` cannot receive the link that would let them fix it any
    /// other way. A verified one is a proven fact and the account's route back
    /// in: letting it change silently would let anyone holding a stolen
    /// session move the account to their own mailbox and keep it.
    /// </para>
    ///
    /// <para>
    /// Throwing rather than returning false: reaching here on a verified
    /// account is a defect in the caller, not an outcome to be reported to a
    /// user. The use case refuses it first, with a message.
    /// </para>
    /// </summary>
    public void ChangeEmail(Email email)
    {
        if (EmailVerified)
        {
            throw new InvalidOperationException(
                "A verified email address cannot be changed. → User.ChangeEmail");
        }

        Email = email;
    }

    /// <summary>Sets or clears the contact number. Null removes it.</summary>
    public void SetPhone(PhoneNumber? phone) => Phone = phone;

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
