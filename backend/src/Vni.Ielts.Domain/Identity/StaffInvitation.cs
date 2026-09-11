using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Identity;

public enum StaffInvitationStatus
{
    Pending,
    Accepted,
    Revoked,
    Expired,
}

/// <summary>
/// An unused staff invitation. The raw token never lives here — only its hash.
/// </summary>
public sealed class StaffInvitation
{
    private StaffInvitation(
        string id,
        Email email,
        IReadOnlyList<RoleId> roleIds,
        UserId invitedBy,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string tokenHash,
        StaffInvitationStatus status)
    {
        Id = id;
        Email = email;
        RoleIds = roleIds;
        InvitedBy = invitedBy;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        TokenHash = tokenHash;
        Status = status;
    }

    public string Id { get; }
    public Email Email { get; }
    public IReadOnlyList<RoleId> RoleIds { get; }
    public UserId InvitedBy { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string TokenHash { get; private set; }
    public StaffInvitationStatus Status { get; private set; }

    public bool IsLive(DateTimeOffset now) =>
        Status == StaffInvitationStatus.Pending && now < ExpiresAt;

    public static StaffInvitation Create(
        Email email,
        IReadOnlyList<RoleId> roleIds,
        UserId invitedBy,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan lifetime) =>
        new(
            Guid.NewGuid().ToString("n"),
            email,
            roleIds,
            invitedBy,
            now,
            now.Add(lifetime),
            tokenHash,
            StaffInvitationStatus.Pending);

    public static StaffInvitation Rehydrate(
        string id,
        Email email,
        IReadOnlyList<RoleId> roleIds,
        UserId invitedBy,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string tokenHash,
        StaffInvitationStatus status) =>
        new(id, email, roleIds, invitedBy, createdAt, expiresAt, tokenHash, status);

    public void ReplaceHash(string tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        if (Status is not StaffInvitationStatus.Pending and not StaffInvitationStatus.Expired)
            throw new InvalidOperationException("Only a pending invitation can be resent.");
        TokenHash = tokenHash;
        Status = StaffInvitationStatus.Pending;
        ExpiresAt = now.Add(lifetime);
    }

    public void Revoke()
    {
        if (Status == StaffInvitationStatus.Accepted)
            throw new InvalidOperationException("An accepted invitation cannot be revoked.");
        Status = StaffInvitationStatus.Revoked;
    }

    public void Accept(DateTimeOffset now)
    {
        if (!IsLive(now))
            throw new InvalidOperationException("This invitation is no longer valid.");
        Status = StaffInvitationStatus.Accepted;
    }

    public void MarkExpired() => Status = StaffInvitationStatus.Expired;
}
