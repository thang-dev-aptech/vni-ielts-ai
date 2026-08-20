using Vni.Ielts.Application.Common;

namespace Vni.Ielts.Application.Identity;

/// <summary>
/// What counts as an acceptable password.
///
/// Deliberately <b>no composition rules</b> — no "one uppercase, one digit,
/// one symbol". Those rules push people towards <c>Password1!</c>, which is
/// both predictable and annoying. Length carries far more entropy than
/// character-class variety, so length is what is enforced.
///
/// The upper bound is not a strength rule. It stops a multi-megabyte input
/// being fed to a deliberately expensive hash function, which is a cheap
/// denial-of-service otherwise. → threat T5
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 256;

    public static Result<string> Validate(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return Error.Validation(ErrorCodes.PasswordTooWeak, "A password is required.");

        if (password.Length < MinLength)
            return Error.Validation(
                ErrorCodes.PasswordTooWeak,
                $"Password must be at least {MinLength} characters.");

        if (password.Length > MaxLength)
            return Error.Validation(
                ErrorCodes.PasswordTooWeak,
                $"Password must be at most {MaxLength} characters.");

        // Whitespace-only passes the length check and is almost certainly a
        // paste accident rather than an intent.
        if (string.IsNullOrWhiteSpace(password))
            return Error.Validation(ErrorCodes.PasswordTooWeak, "A password cannot be only whitespace.");

        return password;
    }
}
