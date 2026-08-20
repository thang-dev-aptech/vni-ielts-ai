namespace Vni.Ielts.Application.Identity;

/// <summary>
/// A unique-index violation on the email address, raised by the persistence
/// layer and translated by the use case into <c>EMAIL_ALREADY_REGISTERED</c>.
///
/// Declared in Application rather than Infrastructure so the use case can catch
/// it without knowing which database raised it — the same translation will be
/// needed after the PostgreSQL migration, and the catch site should not move.
/// </summary>
public sealed class DuplicateEmailException(string email, Exception? inner = null)
    : Exception($"An account already exists for '{email}'.", inner)
{
    public string Email { get; } = email;
}
