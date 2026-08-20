namespace Vni.Ielts.Application.Common;

/// <summary>
/// Stable, machine-readable error codes.
///
/// <b>Clients branch on these, never on the title or the detail text.</b>
/// Which means renaming one is a breaking API change even though nothing
/// about it looks like a contract — and the mobile apps cannot be
/// force-updated. Treat this file as versioned surface.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string EmailInvalid = "EMAIL_INVALID";
    public const string PasswordTooWeak = "PASSWORD_TOO_WEAK";
    public const string EmailAlreadyRegistered = "EMAIL_ALREADY_REGISTERED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountSuspended = "ACCOUNT_SUSPENDED";
    public const string EmailNotVerified = "EMAIL_NOT_VERIFIED";
    public const string VerificationTokenInvalid = "VERIFICATION_TOKEN_INVALID";
    public const string RefreshTokenInvalid = "REFRESH_TOKEN_INVALID";

    /// <summary>
    /// A replayed refresh token. The whole family is revoked when this fires —
    /// rotation without reuse detection is not enough on its own, because a
    /// stolen token stays usable until it expires. → threat T3
    /// </summary>
    public const string RefreshTokenReused = "REFRESH_TOKEN_REUSED";

    /// <summary>
    /// A social sign-in whose email matches an existing verified account.
    /// Deliberately NOT an automatic link: an attacker controlling a social
    /// account bearing a victim's address would inherit the victim's account.
    /// → threat T1, decision M-1
    /// </summary>
    public const string IdentityLinkRequired = "IDENTITY_LINK_REQUIRED";

    public const string NotFound = "NOT_FOUND";
    public const string Forbidden = "FORBIDDEN";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string IdempotencyKeyMissing = "IDEMPOTENCY_KEY_MISSING";

    /// <summary>Always accompanied by a Retry-After header.</summary>
    public const string RateLimited = "RATE_LIMITED";

    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
}
