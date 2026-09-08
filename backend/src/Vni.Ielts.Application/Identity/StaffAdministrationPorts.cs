using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

public interface IStaffInvitationRepository
{
    Task<StaffInvitation?> FindByIdAsync(string id, CancellationToken ct);
    Task<StaffInvitation?> FindLiveByEmailAsync(Email email, DateTimeOffset now, CancellationToken ct);
    Task<StaffInvitation?> FindByTokenHashAsync(string tokenHash, CancellationToken ct);
    Task<(IReadOnlyList<StaffInvitation> Invitations, long Total)> ListPendingAsync(
        int skip, int take, DateTimeOffset now, CancellationToken ct);
    Task AddAsync(StaffInvitation invitation, CancellationToken ct);
    Task SaveAsync(StaffInvitation invitation, CancellationToken ct);

    /// <summary>
    /// Marks pending invitations whose expiry has passed as Expired so a new
    /// live invite can take the unique pending-email slot.
    /// </summary>
    Task ExpireStalePendingByEmailAsync(Email email, DateTimeOffset now, CancellationToken ct);
}

/// <summary>
/// Accepts a staff invitation as one all-or-nothing write: claim the
/// invitation, create the user, and create the password identity. Concurrent
/// accepts of the same token leave at most one account.
/// </summary>
public interface IStaffInvitationAcceptance
{
    Task<Result<AcceptStaffInvitationResult>> AcceptAsync(
        string tokenHash,
        string displayName,
        string passwordHash,
        DateTimeOffset now,
        CancellationToken ct);
}

public interface IPrivacyRequestRepository
{
    Task<PrivacyRequest?> FindByIdAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<PrivacyRequest>> ListForSubjectAsync(UserId subjectId, CancellationToken ct);
    Task AddAsync(PrivacyRequest request, CancellationToken ct);
    Task SaveAsync(PrivacyRequest request, CancellationToken ct);
}

public interface IPersonalDataExportStore
{
    Task SaveAsync(PersonalDataExportJob job, CancellationToken ct);
    Task<PersonalDataExportJob?> FindAsync(string id, CancellationToken ct);
}

public sealed record PersonalDataExportJob(
    string Id,
    UserId SubjectId,
    UserId RequestedBy,
    DateTimeOffset CreatedAt,
    string Status,
    string? ObjectKey,
    DateTimeOffset? ExpiresAt,
    string? FailureCode);
