using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

/// <summary>
/// Issues and redeems email-verification tokens.
///
/// <para>
/// <b>Verification does not gate signing in.</b> `[QUYẾT ĐỊNH]` chủ sản phẩm,
/// 27/08/2026: <i>"tạo tài khoản với email pass cho login như bình thường
/// nhưng sẽ xác minh ở trang hồ sơ học sinh sau cũng được"</i>. Registering
/// signs the learner in; proving the address is something they can come back
/// to from their profile.
/// </para>
///
/// <para>
/// <b>What an unverified account may not do is not decided.</b> The owner
/// settled login and nothing else. Entitlement accrual (threat <c>T4</c>) and
/// referral attribution (threat <c>T13</c>) are both farmable with disposable
/// addresses if registration alone is enough, and both are unbuilt — so this
/// mechanism exists, produces a trustworthy fact on the account, and no code
/// anywhere refuses anything on the strength of it. Inventing that rule here
/// would be inventing the policy behind it. → `M-45`, `G-11`
/// </para>
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

    /// <summary>
    /// Issues a six-digit code for one account, replacing any it already has.
    ///
    /// <b>`[QUYẾT ĐỊNH]` chủ sản phẩm, 28/08/2026: xác minh bằng mã 6 số, không
    /// bằng link.</b>
    ///
    /// The reason is the owner's own earlier decision. Registering signs the
    /// learner in, and verification happens later from the profile page — so
    /// the person doing it is <i>already signed in, already on our page</i>.
    /// A link is the wrong shape for that:
    ///
    /// <b>A link opens in whatever browser the mail app chooses.</b> On a phone
    /// that is usually an in-app webview — Gmail's, Zalo's, Facebook's — which
    /// has no session. The learner sees "verified" in a browser they will never
    /// use again while the app they were in still says unverified. That is
    /// exactly the contradiction `verify-realtime.test.tsx` exists to catch,
    /// and the fix it uses — a `BroadcastChannel` and a focus refresh — cannot
    /// cross browsers at all.
    ///
    /// <b>On Capacitor it is worse.</b> A link opens the system browser, not
    /// the app. Getting back into the app needs App Links and Universal Links:
    /// a signed app, `assetlinks.json` and `apple-app-site-association` served
    /// from the domain, and a fallback for when neither fires — real
    /// infrastructure on two platforms, to do something a code does with none.
    ///
    /// <b>And mail security scanners click links.</b> The single-use token is
    /// consumed silently, and the person then clicks and is told the link
    /// expired.
    ///
    /// <b>Six digits is safe here specifically because the redemption is
    /// authenticated.</b> A code is guessable — a million combinations — so
    /// what makes it safe is that the server knows <i>which account</i> is
    /// redeeming and can count attempts against that account. Five wrong
    /// guesses kills the code. Nobody can spray across accounts, because they
    /// would have to sign in to each one first.
    ///
    /// <b>Replacing rather than adding is the point.</b> A learner who presses
    /// "gửi lại" three times must not end up with three live codes — that
    /// multiplies the guessing surface by three for a convenience nobody asked
    /// for. The newest code is the only one that works.
    /// </summary>
    Task<string> IssueCodeAsync(UserId userId, CancellationToken ct);

    /// <summary>
    /// Checks a code against one account's own outstanding code.
    ///
    /// <b>Takes the user id rather than finding the account from the code.</b>
    /// The caller is authenticated, so the account is already known — and
    /// looking an account up <i>by</i> a six-digit code would mean one guess
    /// could match somebody else's, which is the whole reason a bare code is
    /// normally unsafe.
    /// </summary>
    Task<CodeRedemption> RedeemCodeAsync(UserId userId, string code, CancellationToken ct);
}

/// <summary>
/// What happened to a code, in the detail a screen needs.
///
/// <b>Four outcomes, not a boolean.</b> "Wrong code" and "this code has
/// expired" send the learner to different places — one to look again at what
/// they typed, the other to press "gửi lại" — and "too many attempts" has to
/// say why the code they are holding stopped working, or they will keep trying
/// it.
/// </summary>
public enum CodeRedemption
{
    Verified,

    /// <summary>No code outstanding, or it has expired. Ask for a new one.</summary>
    Expired,

    /// <summary>Wrong digits. There are attempts left.</summary>
    Incorrect,

    /// <summary>
    /// Out of attempts, and the code is now dead.
    ///
    /// <b>Distinct from <see cref="Incorrect"/> deliberately.</b> A learner
    /// told only "wrong code" would keep typing the same one from the same
    /// email for ever, because from where they sit nothing has changed.
    /// </summary>
    TooManyAttempts,
}

/// <summary>
/// What actually became of a message.
///
/// <para>
/// <b>This exists because the honest answer today is "nothing left the
/// server".</b> The only implementation of
/// <see cref="IVerificationMessageSender"/> writes the link to a log, so a
/// caller that assumed delivery would have every screen above it saying
/// <i>"đã gửi email"</i> about a message nobody can receive. A confident lie
/// costs more than a visible gap: the learner goes and looks in a mailbox,
/// finds nothing, and concludes the product is broken rather than unfinished.
/// </para>
///
/// <para>
/// Returned from the send rather than read off a
/// <c>bool Delivers</c> property on the port, so the fact cannot drift from
/// what the call actually did.
/// </para>
/// </summary>
public enum MessageDelivery
{
    /// <summary>Handed to a provider that puts it in a mailbox.</summary>
    Sent,

    /// <summary>
    /// Nothing was sent. The development sender writes the link to the server
    /// log instead; a caller must say so rather than claim a send.
    /// </summary>
    NotSent,
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
///
/// <para>
/// <b>Every method reports what happened.</b> Returning
/// <see cref="MessageDelivery"/> rather than a bare <c>Task</c> is what lets
/// the API tell a client the truth while the only sender is the logging one.
/// </para>
/// </summary>
public interface IVerificationMessageSender
{
    Task<MessageDelivery> SendAsync(Email address, string token, CancellationToken ct);

    /// <summary>
    /// The "forgot password" message.
    ///
    /// <para>
    /// A second method rather than a second port, because the two share
    /// everything that matters — the same provider, the same PDPL position,
    /// the same missing production implementation. Splitting them would mean
    /// choosing an email vendor twice.
    /// </para>
    /// </summary>
    Task<MessageDelivery> SendPasswordResetAsync(Email address, string token, CancellationToken ct);
}

/// <summary>
/// Password-reset tokens.
///
/// <para>
/// Deliberately separate from <see cref="IEmailVerificationTokens"/> rather
/// than a shared table with a purpose column. A verification token proves an
/// address is reachable; a reset token hands over an account. Sharing storage
/// means one bug in a filter lets one be redeemed as the other, and the
/// consequences are not comparable.
/// </para>
///
/// <para>
/// <b>One hour, not twenty-four.</b> A reset link sits in a mailbox that may
/// itself be shared or compromised, and the window in which it is worth
/// anything to an attacker should be the window in which its owner is actually
/// using it. → threat T5
/// </para>
/// </summary>
public interface IPasswordResetTokens
{
    Task<string> IssueAsync(UserId userId, CancellationToken ct);

    /// <summary>Single use. Expired, spent and never-existed are one answer.</summary>
    Task<UserId?> RedeemAsync(string token, CancellationToken ct);
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
