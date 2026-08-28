using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

/// <summary>
/// Repository ports.
///
/// These live in Application; their implementations and every persistence
/// model live in Infrastructure. That single split is what makes the
/// MongoDB to PostgreSQL migration a rewrite of one project.
///
/// Note what is deliberately absent: no generic <c>IRepository&lt;T&gt;</c>,
/// no Specification pattern, no shared Unit of Work. A generic repository
/// leaks storage semantics into Application and produces awkward queries in
/// both databases; a shared Unit of Work leaks because Mongo and Postgres
/// transaction semantics differ enough that the abstraction cannot hold.
/// Transactions stay inside Infrastructure. → ADR-0004
/// </summary>
public interface IUserRepository
{
    Task<User?> FindByIdAsync(UserId id, CancellationToken ct);

    /// <summary>
    /// A page of accounts for the CMS.
    ///
    /// Paged from the start rather than "list them all, filter in the UI":
    /// the second one works on the fifty accounts a dev database holds and
    /// falls over on the first real week.
    /// </summary>
    Task<(IReadOnlyList<User> Users, long Total)> ListAsync(
        string? search, int skip, int take, CancellationToken ct);
    Task<User?> FindByEmailAsync(Email email, CancellationToken ct);
    Task<bool> EmailExistsAsync(Email email, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
    Task SaveAsync(User user, CancellationToken ct);
}

public interface IUserIdentityRepository
{
    Task<UserIdentity?> FindByProviderAsync(
        IdentityProvider provider, string providerUserId, CancellationToken ct);
    Task<IReadOnlyList<UserIdentity>> ListForUserAsync(UserId userId, CancellationToken ct);
    Task AddAsync(UserIdentity identity, CancellationToken ct);
    Task SaveAsync(UserIdentity identity, CancellationToken ct);
}

public interface IRoleRepository
{
    Task<Role?> FindByIdAsync(RoleId id, CancellationToken ct);
    Task<Role?> FindByNameAsync(string name, CancellationToken ct);
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct);
    Task AddAsync(Role role, CancellationToken ct);
}

/// <summary>
/// Password hashing. Argon2id in Infrastructure.
///
/// A port rather than a static call so the algorithm can be replaced, and so
/// the test suite can substitute something fast — an Argon2id hash is
/// deliberately expensive, and paying that cost in several hundred unit tests
/// makes the suite slow enough that people stop running it.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Constant-time. A comparison that short-circuits leaks length and prefix.</summary>
    bool Verify(string password, string hash);
}

/// <summary>An access token plus the refresh token that can renew it.</summary>
public sealed record TokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

/// <summary>
/// Counts consecutive failed sign-ins for one address, and refuses once there
/// have been too many.
///
/// <b>Why this is not the rate limiter.</b> The HTTP limiter partitions on IP
/// because sign-in happens before authentication, and its bound has to stay
/// loose: Vietnamese carrier NAT and any school or office put large numbers of
/// legitimate users behind one address, so a tight per-IP limit is a
/// self-inflicted outage. A loose per-IP limit does nothing against credential
/// stuffing, which spreads a few guesses per account across many accounts and
/// many addresses. Stopping that needs a counter on the thing being attacked —
/// the account. → threats T4, T5
///
/// <b>Keyed on the submitted address whether or not it exists.</b> Counting
/// only real accounts would make the lockout itself an enumeration oracle:
/// an attacker would learn which addresses are registered by seeing which ones
/// can be locked. Every address behaves identically, so the 429 tells an
/// attacker nothing that the 401 did not already tell them.
/// </summary>
public interface ILoginThrottle
{
    /// <summary>True while the address is in its cooldown.</summary>
    Task<bool> IsLockedAsync(string email, CancellationToken ct);

    Task RecordFailureAsync(string email, CancellationToken ct);

    /// <summary>Called on a successful sign-in. A success clears the count.</summary>
    Task ClearAsync(string email, CancellationToken ct);
}

/// <summary>
/// Issues and validates tokens.
///
/// Refresh tokens rotate, and rotation carries <b>reuse detection</b>: a
/// replayed refresh token revokes the entire family rather than just failing.
/// Rotation on its own is not enough — a stolen token remains usable until it
/// expires, and the theft is never noticed. → threat T3
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Issues a pair. <paramref name="familyId"/> continues an existing family
    /// on rotation; null starts a new one for a fresh sign-in.
    ///
    /// This parameter is not incidental. Reuse detection works by revoking a
    /// *family*, so if every rotation started a new family the detection would
    /// only ever cover the single most recent token — a stolen token from two
    /// rotations ago would still revoke nothing.
    /// </summary>
    /// <param name="rotatedFromHash">
    /// The token this one replaces, when it replaces one.
    ///
    /// <b>Recorded so a lost response is not mistaken for a theft.</b> If the
    /// response carrying this new token never reaches the client, the client
    /// retries with the one it has — the one just marked used — and reuse
    /// detection would revoke the whole family for a network blip. Knowing
    /// which token replaced which is what tells "nobody ever received the
    /// successor" from "two parties hold tokens from this chain".
    /// → <c>RedeemRefreshTokenAsync</c>, threat `T3`
    /// </param>
    Task<TokenPair> IssueAsync(
        User user, IReadOnlyCollection<string> permissions, string? familyId, CancellationToken ct,
        string? rotatedFromHash = null);

    Task<Result<RefreshOutcome>> RedeemRefreshTokenAsync(string refreshToken, CancellationToken ct);

    Task RevokeFamilyAsync(UserId userId, string familyId, CancellationToken ct);

    Task RevokeAllForUserAsync(UserId userId, CancellationToken ct);

    /// <summary>
    /// Ends every sign-in except one — "sign out everywhere else".
    ///
    /// <para>
    /// A separate operation rather than a loop over the session list, and the
    /// difference matters: someone reaching for this has usually just realised
    /// a device is not theirs, and a loop leaves a window where half the
    /// sessions are still live. One statement closes them together.
    /// </para>
    /// </summary>
    Task<int> RevokeAllExceptAsync(UserId userId, string keepFamilyId, CancellationToken ct);
}

/// <param name="RedeemedTokenHash">
/// The token that was just spent, so the successor can name its parent.
/// </param>
public sealed record RefreshOutcome(UserId UserId, string FamilyId, string RedeemedTokenHash);

/// <summary>
/// Resolves the effective permission set for a user by unioning their roles.
/// Separate from <c>IRoleRepository</c> because the API and the worker both
/// need it and neither should be assembling it by hand.
/// </summary>
public interface IPermissionResolver
{
    Task<IReadOnlyCollection<string>> ResolveAsync(User user, CancellationToken ct);
}

/// <summary>
/// What the current request says about the device it came from.
///
/// <para>
/// A port because Infrastructure must not know about ASP.NET Core, and the
/// only place a User-Agent exists is an HTTP request. The Api implements it.
/// </para>
///
/// <para>
/// <b>The user agent, and nothing else.</b> No IP address: the purpose here is
/// letting someone recognise their own devices in a list, and an IP does not
/// help with that — it changes on every network and it is the field that turns
/// a session record into location history. Collecting less is the whole point.
/// → PDPL, <c>B-2</c>
/// </para>
/// </summary>
public interface IRequestDevice
{
    string? UserAgent { get; }
}

/// <summary>
/// One sign-in, as the person who owns it would recognise it.
///
/// A "session" here is a <b>refresh-token family</b>. That is not a new
/// concept invented for this screen: a family already means "one sign-in and
/// everything it rotated into", and revoking one is already how reuse
/// detection ends a compromised chain. Listing them is the same idea pointed
/// at the account owner instead of at an attacker.
/// </summary>
/// <param name="IsCurrent">
/// True for the family the calling token belongs to. The client needs this to
/// avoid offering "sign out" on the session doing the asking, and to label it.
/// </param>
public sealed record LearnerSession(
    string FamilyId,
    string? UserAgent,
    DateTimeOffset SignedInAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);

public interface ISessionDirectory
{
    /// <summary>Live families only — revoked and expired ones are not devices anyone still has.</summary>
    Task<IReadOnlyList<LearnerSession>> ListAsync(UserId userId, string? currentFamilyId, CancellationToken ct);
}

/// <summary>
/// The audit trail.
///
/// <b>There is no update and no delete, and there never will be.</b> That is
/// the interface making constraint 6 of the CMS specification structural: a
/// caller cannot rewrite history because there is no method through which to
/// try. → threat `T21`
/// </summary>
public interface IAuditLog
{
    Task AppendAsync(Vni.Ielts.Domain.Audit.AuditEntry entry, CancellationToken ct);

    /// <summary>Newest first. Filters are optional and combine with AND.</summary>
    Task<(IReadOnlyList<Vni.Ielts.Domain.Audit.AuditEntry> Entries, long Total)> ListAsync(
        string? actorId, string? action, int skip, int take, CancellationToken ct);
}
