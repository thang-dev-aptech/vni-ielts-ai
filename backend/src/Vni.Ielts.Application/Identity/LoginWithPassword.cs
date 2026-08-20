using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

public sealed record LoginCommand(string Email, string Password);

public sealed record LoginResult(TokenPair Tokens, UserId UserId, string DisplayName);

/// <summary>
/// Email and password sign-in.
///
/// <b>Every failure path returns the same error.</b> Unknown address, wrong
/// password, an account that exists but only has a Google identity — all of
/// them produce <c>INVALID_CREDENTIALS</c>. Distinguishing them turns the
/// login endpoint into an account-enumeration oracle, and the distinction
/// helps a legitimate user far less than it helps an attacker.
///
/// The one exception is a suspended account, which reports itself. Someone
/// whose access was withdrawn needs to know that is what happened rather than
/// resetting a password that was never wrong.
/// </summary>
public sealed class LoginWithPassword(
    IUserRepository users,
    IUserIdentityRepository identities,
    IPasswordHasher hasher,
    IPermissionResolver permissions,
    ITokenService tokens)
{
    private static readonly Error Invalid = Error.Unauthorized(
        ErrorCodes.InvalidCredentials, "Email address or password is incorrect.");

    public async Task<Result<LoginResult>> HandleAsync(LoginCommand command, CancellationToken ct)
    {
        if (!Email.TryCreate(command.Email, out var email))
        {
            // Still burn a hash. Returning early on a malformed address makes
            // the response measurably faster than a real attempt, which is a
            // timing oracle for "is this address shaped like one you store".
            hasher.Verify(command.Password ?? string.Empty, _dummyHash.Value);
            return Invalid;
        }

        var identity = await identities.FindByProviderAsync(IdentityProvider.Email, email.Value, ct);

        if (identity?.PasswordHash is null)
        {
            // Covers three cases that must stay indistinguishable: no such
            // account, an account with only a social identity, and an email
            // identity somehow missing its hash.
            hasher.Verify(command.Password ?? string.Empty, _dummyHash.Value);
            return Invalid;
        }

        if (!hasher.Verify(command.Password ?? string.Empty, identity.PasswordHash))
            return Invalid;

        var user = await users.FindByIdAsync(identity.UserId, ct);
        if (user is null)
            return Invalid;

        if (!user.CanAuthenticate)
            return Error.Forbidden(ErrorCodes.AccountSuspended, "This account has been suspended.");

        var granted = await permissions.ResolveAsync(user, ct);
        var pair = await tokens.IssueAsync(user, granted, familyId: null, ct);

        return new LoginResult(pair, user.Id, user.DisplayName);
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
