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
    Task<TokenPair> IssueAsync(
        User user, IReadOnlyCollection<string> permissions, string? familyId, CancellationToken ct);

    Task<Result<RefreshOutcome>> RedeemRefreshTokenAsync(string refreshToken, CancellationToken ct);

    Task RevokeFamilyAsync(UserId userId, string familyId, CancellationToken ct);

    Task RevokeAllForUserAsync(UserId userId, CancellationToken ct);
}

public sealed record RefreshOutcome(UserId UserId, string FamilyId);

/// <summary>
/// Resolves the effective permission set for a user by unioning their roles.
/// Separate from <c>IRoleRepository</c> because the API and the worker both
/// need it and neither should be assembling it by hand.
/// </summary>
public interface IPermissionResolver
{
    Task<IReadOnlyCollection<string>> ResolveAsync(User user, CancellationToken ct);
}
