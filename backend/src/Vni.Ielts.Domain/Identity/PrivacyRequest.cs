using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Identity;

public enum PrivacyRequestType
{
    Anonymize,
    HardDelete,
}

public enum PrivacyRequestStatus
{
    PendingReview,
    Approved,
    Executing,
    Completed,
    Failed,
}

/// <summary>
/// A PDPL erasure/anonymization case. Execution is a separate step and stays
/// fail-closed until a policy mode is configured.
/// </summary>
public sealed class PrivacyRequest
{
    private PrivacyRequest(
        string id,
        UserId subjectId,
        PrivacyRequestType type,
        string reason,
        UserId requesterId,
        DateTimeOffset createdAt,
        PrivacyRequestStatus status,
        UserId? approverId,
        DateTimeOffset? approvedAt)
    {
        Id = id;
        SubjectId = subjectId;
        Type = type;
        Reason = reason;
        RequesterId = requesterId;
        CreatedAt = createdAt;
        Status = status;
        ApproverId = approverId;
        ApprovedAt = approvedAt;
    }

    public string Id { get; }
    public UserId SubjectId { get; }
    public PrivacyRequestType Type { get; }
    public string Reason { get; }
    public UserId RequesterId { get; }
    public DateTimeOffset CreatedAt { get; }
    public PrivacyRequestStatus Status { get; private set; }
    public UserId? ApproverId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }

    public static PrivacyRequest Create(
        UserId subjectId, PrivacyRequestType type, string reason, UserId requesterId, DateTimeOffset now) =>
        new(Guid.NewGuid().ToString("n"), subjectId, type, reason, requesterId, now,
            PrivacyRequestStatus.PendingReview, null, null);

    public static PrivacyRequest Rehydrate(
        string id, UserId subjectId, PrivacyRequestType type, string reason,
        UserId requesterId, DateTimeOffset createdAt, PrivacyRequestStatus status,
        UserId? approverId, DateTimeOffset? approvedAt) =>
        new(id, subjectId, type, reason, requesterId, createdAt, status, approverId, approvedAt);

    public void Approve(UserId approverId, DateTimeOffset now)
    {
        if (Status != PrivacyRequestStatus.PendingReview)
            throw new InvalidOperationException("Only a pending request can be approved.");
        if (approverId == RequesterId)
            throw new InvalidOperationException("The requester cannot approve their own request.");
        ApproverId = approverId;
        ApprovedAt = now;
        Status = PrivacyRequestStatus.Approved;
    }
}
