using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

/// <summary>
/// Issues and redeems email-verification tokens.
///
/// Verification is a real gate, not a formality — entitlement accrual (threat
/// <c>T4</c>) and referral attribution confirmation (threat <c>T13</c>) both
/// wait on it, and both are farmable with disposable addresses if registration
/// alone is enough.
/// </summary>
public interface IEmailVerificationTokens
{
    /// <summary>Returns the plaintext token. Only the hash is stored.</summary>
    Task<string> IssueAsync(UserId userId, CancellationToken ct);

    /// <summary>
    /// Redeems single-use. A token that has already been used, expired, or
    /// never existed all return null — they are indistinguishable to a caller.
    /// </summary>
    Task<UserId?> RedeemAsync(string token, CancellationToken ct);
}

/// <summary>
/// Delivers the verification message.
///
/// <para>
/// <b>A port with no production implementation yet.</b> That is a deliberate,
/// visible gap rather than a hidden one: the token mechanism is the part that
/// carries security properties, and it is built and testable now. Choosing a
/// delivery provider is a separate decision that also touches PDPL — an email
/// address sent to a foreign provider is a cross-border transfer with the same
/// obligation as everything else (<c>B-2</c>).
/// </para>
/// </summary>
public interface IVerificationMessageSender
{
    Task SendAsync(Email address, string token, CancellationToken ct);
}

public sealed record VerifyEmailCommand(string Token);

public sealed class VerifyEmail(IUserRepository users, IEmailVerificationTokens tokens)
{
    public async Task<Result<UserId>> HandleAsync(VerifyEmailCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return Error.Validation(ErrorCodes.VerificationTokenInvalid, "A token is required.");

        var userId = await tokens.RedeemAsync(command.Token, ct);
        if (userId is null)
        {
            // Expired, already used, and never-existed are one response on
            // purpose. Distinguishing them tells an attacker whether a guessed
            // token was ever real.
            return Error.Validation(
                ErrorCodes.VerificationTokenInvalid,
                "This verification link is no longer valid. Request a new one.");
        }

        var user = await users.FindByIdAsync(userId.Value, ct);
        if (user is null)
            return Error.Validation(ErrorCodes.VerificationTokenInvalid, "This link is no longer valid.");

        // Idempotent by nature: verifying an already-verified account is a
        // no-op, which is what a user double-clicking the link produces.
        user.MarkEmailVerified();
        await users.SaveAsync(user, ct);

        return user.Id;
    }
}
