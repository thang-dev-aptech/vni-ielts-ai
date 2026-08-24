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

/// <summary>
/// A unique-index violation on <c>(provider, providerUserId)</c>.
///
/// <para>
/// Raised when two sign-ins for the same provider account arrive together —
/// two tabs, or a client retrying a callback it already sent. Without this the
/// loser of that race gets a driver exception and a 500, which reads to the
/// person as "sign-in is broken" when in fact they are signed in.
/// </para>
///
/// <para>
/// Declared here rather than in Infrastructure for the same reason as
/// <see cref="DuplicateEmailException"/>: the use case catches it without
/// knowing which database raised it, and the catch site should not move during
/// the PostgreSQL migration.
/// </para>
/// </summary>
public sealed class DuplicateIdentityException(string provider, string subject, Exception? inner = null)
    : Exception($"An identity already exists for {provider} subject '{subject}'.", inner)
{
    public string Provider { get; } = provider;
    public string Subject { get; } = subject;
}
