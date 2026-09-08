using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Application.Identity;

/// <summary>
/// Two live invitations for the same address collided on the unique pending
/// index. The check-then-insert in InviteStaff is courtesy; this is the
/// guarantee.
/// </summary>
public sealed class DuplicateLiveInvitationException : Exception
{
    public DuplicateLiveInvitationException(string email, Exception? inner = null)
        : base($"A live invitation already exists for '{email}'.", inner)
    {
        Email = email;
    }

    public string Email { get; }
}
