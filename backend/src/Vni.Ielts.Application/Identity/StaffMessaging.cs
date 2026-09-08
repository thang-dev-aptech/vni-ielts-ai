using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

/// <summary>
/// What actually became of a message.
///
/// Exists because the honest answer may be "nothing left the server" while
/// only a logging sender is wired. Callers must surface that rather than
/// claim a send. → G-11
/// </summary>
public enum MessageDelivery
{
    Sent,
    NotSent,
}

/// <summary>
/// Delivers staff and account-security messages.
///
/// Slim port retained for CMS staff invitation / force-reset. Learner email
/// verification was removed on main (ADR-0018); this port does not restore it.
/// </summary>
public interface IVerificationMessageSender
{
    Task<MessageDelivery> SendPasswordResetAsync(Email address, string token, CancellationToken ct);

    /// <summary>A one-time staff-invitation link.</summary>
    Task<MessageDelivery> SendStaffInvitationAsync(Email address, string token, CancellationToken ct);
}

/// <summary>
/// Password-reset tokens for CMS force-reset mail.
///
/// Separate from any verification store: a reset token hands over an account.
/// Main no longer offers learner self-service forgot-password (ADR-0018); this
/// remains for the operator-triggered staff path only.
/// </summary>
public interface IPasswordResetTokens
{
    Task<string> IssueAsync(UserId userId, CancellationToken ct);

    /// <summary>Single use. Expired, spent and never-existed are one answer.</summary>
    Task<UserId?> RedeemAsync(string token, CancellationToken ct);
}
