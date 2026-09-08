using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

public sealed record LoginCommand(string Identifier, string Password);

public sealed record LoginResult(TokenPair Tokens, UserId UserId, string DisplayName);

/// <summary>
/// Sign-in with a password, against either handle.
///
/// <b>One box, two kinds of handle.</b> `[QUYẾT ĐỊNH]` chủ sản phẩm,
/// 08/09/2026. Accounts registered since the change have a phone number and no
/// address; accounts made before it, accounts created through Google that later
/// set a password, and every operator account in the CMS have an address and no
/// number. A single field that accepts both is what keeps all of them working
/// without asking anyone which kind of account they have.
///
/// <b>The account is resolved before the credential.</b> The password row is
/// keyed by account id, not by a handle, so this looks the <c>User</c> up by
/// whichever handle was typed and then reads their one password row. The old
/// shape — look the identity up *by the address* — is what forced the address
/// to be stored twice, and made changing it break sign-in silently.
///
/// <b>Every failure path returns the same error.</b> Unknown handle, wrong
/// password, an account that exists but only has a Google identity — all of
/// them produce <c>INVALID_CREDENTIALS</c>. Distinguishing them turns the
/// login endpoint into an account-enumeration oracle, and the distinction
/// helps a legitimate user far less than it helps an attacker.
///
/// The one exception is a suspended account, which reports itself. Someone
/// whose access was withdrawn needs to know that is what happened rather than
/// resetting a password that was never wrong.
///
/// <b>Bounded guessing.</b> Ten consecutive failures for one handle lock it
/// for fifteen minutes. The HTTP rate limiter cannot do this job — it
/// partitions on IP and has to stay loose because of carrier NAT, and
/// credential stuffing spreads a few guesses per account across many accounts
/// precisely to stay under such a limit. → threats T4, T5
/// </summary>
public sealed class LoginWithPassword(
    IUserRepository users,
    IUserIdentityRepository identities,
    IPasswordHasher hasher,
    IPermissionResolver permissions,
    ITokenService tokens,
    ILoginThrottle throttle)
{
    private static readonly Error Invalid = Error.Unauthorized(
        ErrorCodes.InvalidCredentials, "Phone number, email address or password is incorrect.");

    private static readonly Error Locked = Error.TooManyRequests(
        ErrorCodes.TooManyAttempts,
        "Too many failed sign-in attempts for this account. Try again in a few minutes.");

    public async Task<Result<LoginResult>> HandleAsync(LoginCommand command, CancellationToken ct)
    {
        /*
         * <b>Normalised before the lock is checked, not after.</b> The throttle
         * keys on whatever string it is handed, and a phone number has four
         * common spellings — `0912345678`, `+84912345678`, `091 234 5678`,
         * `(091) 234-5678`. Keyed raw, those are four independent counters for
         * one account, so ten attempts becomes forty. Normalising is cheap
         * enough to stay in front of the Argon2id derivation, which is the
         * ordering this check exists for.
         */
        var handle = Normalise(command.Identifier);

        // Keys on the submitted handle whether or not it exists, so it cannot
        // become an account-existence oracle — every handle locks the same way.
        if (await throttle.IsLockedAsync(handle.Key, ct)) return Locked;

        var user = handle.Kind switch
        {
            HandleKind.Email => await users.FindByEmailAsync(handle.Email, ct),
            HandleKind.Phone => await users.FindByPhoneAsync(handle.Phone, ct),
            _ => null,
        };

        var identity = user is null
            ? null
            : (await identities.ListForUserAsync(user.Id, ct))
                .FirstOrDefault(i => i.PasswordHash is not null);

        if (user is null || identity?.PasswordHash is null)
        {
            /*
             * Still burn a hash. Returning early makes the response measurably
             * faster than a real attempt, which is a timing oracle for "is this
             * handle one you store". Covers four cases that must stay
             * indistinguishable: a handle that is not even shaped like one, no
             * such account, an account with only a social identity, and a
             * password row somehow missing its hash.
             */
            hasher.Verify(command.Password ?? string.Empty, _dummyHash.Value);
            if (handle.Kind is not HandleKind.Unusable)
                await throttle.RecordFailureAsync(handle.Key, ct);
            return Invalid;
        }

        if (!hasher.Verify(command.Password ?? string.Empty, identity.PasswordHash))
        {
            await throttle.RecordFailureAsync(handle.Key, ct);
            return Invalid;
        }

        if (!user.CanAuthenticate)
            return Error.Forbidden(ErrorCodes.AccountSuspended, "This account has been suspended.");

        // Cleared only on a sign-in that actually succeeds. A suspended
        // account returns above without clearing, so an attacker cannot use a
        // correct password on a locked-out suspended account to reset the
        // counter.
        await throttle.ClearAsync(handle.Key, ct);

        var granted = await permissions.ResolveAsync(user, ct);
        var pair = await tokens.IssueAsync(user, granted, familyId: null, ct);

        return new LoginResult(pair, user.Id, user.DisplayName);
    }

    private enum HandleKind { Unusable, Email, Phone }

    private readonly record struct Handle(HandleKind Kind, Email Email, PhoneNumber Phone, string Key);

    /// <summary>
    /// Works out which kind of handle was typed, and what to key the throttle on.
    ///
    /// <para>
    /// An `@` decides it. Nothing else can: a phone number never contains one,
    /// and an address always does. Trying the phone parser first would let
    /// `0912345678@example.com` be read as a number.
    /// </para>
    ///
    /// <para>
    /// An unusable handle still gets a key — the trimmed lower-cased text — so
    /// that garbage cannot be used to probe timing for free. It just does not
    /// count as a failure, because there is no account it could belong to and
    /// counting it would let anyone lock a queue of nonsense keys.
    /// </para>
    /// </summary>
    private static Handle Normalise(string? raw)
    {
        var typed = (raw ?? string.Empty).Trim();

        if (typed.Contains('@'))
        {
            return Email.TryCreate(typed, out var email)
                ? new Handle(HandleKind.Email, email, default, email.Value)
                : new Handle(HandleKind.Unusable, default, default, typed.ToLowerInvariant());
        }

        return PhoneNumber.TryCreate(typed, out var phone)
            ? new Handle(HandleKind.Phone, default, phone, phone.Value)
            : new Handle(HandleKind.Unusable, default, default, typed.ToLowerInvariant());
    }

    /// <summary>
    /// A hash nothing will match, used to keep a failed lookup costing the same
    /// as a genuine verification.
    ///
    /// <para>
    /// <b>Derived at runtime by the real hasher, not hard-coded.</b> An earlier
    /// version pasted a literal encoded hash, and measurement showed it verified
    /// in 62 ms against 73 ms for a genuine stored hash — a 15% gap, because the
    /// literal carried a 13-byte salt while the hasher produces 16. Over enough
    /// samples that difference is measurable across a network, which hands back
    /// the account-existence oracle this field exists to remove.
    /// </para>
    ///
    /// <para>
    /// Deriving it from the same <see cref="IPasswordHasher"/> guarantees
    /// identical parameters, identical salt length, and therefore identical
    /// cost — whatever those parameters are later tuned to. <c>Lazy</c> because
    /// it costs one Argon2id derivation, paid once per process rather than per
    /// request.
    /// </para>
    /// </summary>
    private readonly Lazy<string> _dummyHash = new(
        () => hasher.Hash(Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))),
        LazyThreadSafetyMode.ExecutionAndPublication);
}
