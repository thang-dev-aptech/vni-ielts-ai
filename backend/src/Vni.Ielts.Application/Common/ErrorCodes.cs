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

    /// <summary>
    /// Too many consecutive failed sign-ins for one address.
    ///
    /// Distinct from <see cref="InvalidCredentials"/> on purpose. It leaks
    /// nothing — an unregistered address locks exactly as a registered one
    /// does — and a legitimate user who has mistyped their password needs to
    /// be told why the right password has stopped working.
    /// </summary>
    public const string TooManyAttempts = "TOO_MANY_ATTEMPTS";
    public const string EmailNotVerified = "EMAIL_NOT_VERIFIED";
    public const string VerificationTokenInvalid = "VERIFICATION_TOKEN_INVALID";
    public const string RefreshTokenInvalid = "REFRESH_TOKEN_INVALID";

    // ── Exams and sittings ───────────────────────────────────────────────

    public const string ExamNotFound = "EXAM_NOT_FOUND";

    /// <summary>A draft, or a version whose module the request asked for does not exist.</summary>
    public const string ExamNotSittable = "EXAM_NOT_SITTABLE";

    /// <summary>
    /// Also returned for a sitting that belongs to someone else. A 403 would
    /// confirm the id exists, which turns the id space into an oracle for
    /// enumerating other learners' sittings.
    /// </summary>
    public const string SessionNotFound = "SESSION_NOT_FOUND";

    /// <summary>
    /// The deadline has passed and the learner is the one asking. Named in
    /// `key-flows.md` §2 as the case the client must handle rather than retry:
    /// what was saved before the deadline is kept, and the sitting closes.
    /// </summary>
    public const string SessionExpired = "SESSION_EXPIRED";

    public const string SessionNotInProgress = "SESSION_NOT_IN_PROGRESS";

    /// <summary>
    /// "Tiếp theo" on a single-skill sitting. Its next step is a new test,
    /// which is a different operation with a different entitlement effect.
    /// → CLAUDE.md rule 10
    /// </summary>
    public const string NotAFullTest = "NOT_A_FULL_TEST";

    /// <summary>
    /// The caller is authenticated and lacks the permission the route needs.
    ///
    /// A 403, not a 404 — unlike an exam session, where hiding existence stops
    /// one learner enumerating another's. A CMS operator is named, and needs
    /// to be told which permission they are missing.
    /// </summary>
    public const string PermissionDenied = "PERMISSION_DENIED";

    /// <summary>A Speaking upload larger than any real answer could be.</summary>
    public const string RecordingTooLarge = "RECORDING_TOO_LARGE";

    /// <summary>
    /// A replayed refresh token. The whole family is revoked when this fires —
    /// rotation without reuse detection is not enough on its own, because a
    /// stolen token stays usable until it expires. → threat T3
    /// </summary>
    public const string RefreshTokenReused = "REFRESH_TOKEN_REUSED";

    /// <summary>
    /// A social sign-in whose email matches an existing account, from a
    /// provider that will not vouch for the address.
    ///
    /// <para>
    /// <b>This is now the narrow case, not the general rule.</b> M-1 was
    /// resolved on 2026-08-21: one email is one account, and a matching
    /// address links silently — but only when the provider asserts
    /// <c>email_verified</c>. Google does; Facebook does not, and a Facebook
    /// address is therefore a claim rather than a fact. Linking on an
    /// unvouched claim is the account-takeover vector in threat T1, so those
    /// providers still stop here. → ADR-0013
    /// </para>
    /// </summary>
    public const string IdentityLinkRequired = "IDENTITY_LINK_REQUIRED";

    /// <summary>The provider segment in the URL matches nothing configured.</summary>
    public const string SsoProviderUnknown = "SSO_PROVIDER_UNKNOWN";

    /// <summary>
    /// A callback whose <c>state</c> is unknown, expired, or already spent.
    ///
    /// One code for all three, for the same reason the verification token has
    /// one: distinguishing them tells an attacker whether a guessed value was
    /// ever real. It is also what a stale browser tab produces, so the client
    /// message has to read as "start again", not as an accusation.
    /// </summary>
    public const string SsoStateInvalid = "SSO_STATE_INVALID";

    /// <summary>
    /// The provider refused the code exchange, or returned an ID token that
    /// failed validation. Never carries the provider's own message — that text
    /// is attacker-influenced and belongs in the log, not in a response.
    /// </summary>
    public const string SsoExchangeFailed = "SSO_EXCHANGE_FAILED";

    /// <summary>
    /// The provider returned no email address. Facebook does this when the
    /// account was created with a phone number, and there is nothing to link
    /// or create an account from. → V-5
    /// </summary>
    public const string SsoEmailMissing = "SSO_EMAIL_MISSING";

    /// <summary>The user declined consent at the provider, or cancelled.</summary>
    public const string SsoDenied = "SSO_DENIED";

    /// <summary>
    /// The handoff code is unknown, expired, or already redeemed. Sixty
    /// seconds and single use, so this is a normal outcome for a replayed
    /// browser back-button, not necessarily an attack. → ADR-0014
    /// </summary>
    public const string SsoHandoffInvalid = "SSO_HANDOFF_INVALID";

    /// <summary>
    /// An attempt to end the session doing the asking. That is sign-out, which
    /// also clears local state; doing it from the device list would leave the
    /// client holding a dead token while still showing a signed-in header.
    /// </summary>
    public const string SessionIsCurrent = "SESSION_IS_CURRENT";

    /// <summary>
    /// A password-reset link that is spent, expired, or was never real. One
    /// code for all three: distinguishing them tells an attacker whether a
    /// guessed token ever existed.
    /// </summary>
    public const string ResetTokenInvalid = "RESET_TOKEN_INVALID";

    /// <summary>
    /// Changing a password without proving the current one. A stolen access
    /// token must not be enough to lock the real owner out.
    /// </summary>
    public const string CurrentPasswordWrong = "CURRENT_PASSWORD_WRONG";

    public const string PhoneInvalid = "PHONE_INVALID";

    /// <summary>
    /// An attempt to change an address that has already been verified. It is
    /// the account's route back in, and a stolen session must not be able to
    /// move it somewhere else.
    /// </summary>
    public const string EmailLocked = "EMAIL_LOCKED";

    public const string NotFound = "NOT_FOUND";
    public const string Forbidden = "FORBIDDEN";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string IdempotencyKeyMissing = "IDEMPOTENCY_KEY_MISSING";

    /// <summary>Always accompanied by a Retry-After header.</summary>
    public const string RateLimited = "RATE_LIMITED";

    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
}
