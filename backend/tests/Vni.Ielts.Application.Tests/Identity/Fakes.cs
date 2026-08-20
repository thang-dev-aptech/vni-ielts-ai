using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// In-memory ports. Use cases are tested against these rather than a database:
/// the behaviour under test is the decision logic, and a Mongo round-trip adds
/// seconds per test while proving nothing extra about it.
/// </summary>
internal sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<string, User> _byId = [];

    public Task<User?> FindByIdAsync(UserId id, CancellationToken ct) =>
        Task.FromResult(_byId.GetValueOrDefault(id.Value));

    public Task<User?> FindByEmailAsync(Email email, CancellationToken ct) =>
        Task.FromResult(_byId.Values.FirstOrDefault(u => u.Email == email));

    public Task<bool> EmailExistsAsync(Email email, CancellationToken ct) =>
        Task.FromResult(_byId.Values.Any(u => u.Email == email));

    public Task AddAsync(User user, CancellationToken ct)
    {
        _byId[user.Id.Value] = user;
        return Task.CompletedTask;
    }

    public Task SaveAsync(User user, CancellationToken ct)
    {
        _byId[user.Id.Value] = user;
        return Task.CompletedTask;
    }
}

internal sealed class FakeUserIdentityRepository : IUserIdentityRepository
{
    private readonly List<UserIdentity> _all = [];

    public Task<UserIdentity?> FindByProviderAsync(
        IdentityProvider provider, string providerUserId, CancellationToken ct) =>
        Task.FromResult(_all.FirstOrDefault(
            i => i.Provider == provider && i.ProviderUserId == providerUserId));

    public Task<IReadOnlyList<UserIdentity>> ListForUserAsync(UserId userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UserIdentity>>(
            [.. _all.Where(i => i.UserId == userId)]);

    public Task AddAsync(UserIdentity identity, CancellationToken ct)
    {
        _all.Add(identity);
        return Task.CompletedTask;
    }

    public Task SaveAsync(UserIdentity identity, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class FakeRoleRepository : IRoleRepository
{
    private readonly List<Role> _all =
        [Role.Create(SystemRoles.Learner, isSystem: true, [PermissionKeys.ExamRead])];

    public Task<Role?> FindByIdAsync(RoleId id, CancellationToken ct) =>
        Task.FromResult(_all.FirstOrDefault(r => r.Id == id));

    public Task<Role?> FindByNameAsync(string name, CancellationToken ct) =>
        Task.FromResult(_all.FirstOrDefault(r => r.Name == name));

    public Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Role>>(_all);

    public Task AddAsync(Role role, CancellationToken ct)
    {
        _all.Add(role);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Reversible and fast. Substituting this for Argon2id in tests is the whole
/// reason <see cref="IPasswordHasher"/> is a port — a real Argon2id hash is
/// deliberately expensive, and paying that in every test makes the suite slow
/// enough that people stop running it.
/// </summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public int VerifyCallCount { get; private set; }

    public string Hash(string password) => "fake:" + password;

    public bool Verify(string password, string hash)
    {
        VerifyCallCount++;
        return hash == "fake:" + password;
    }
}

internal sealed class FakePermissionResolver(params string[] permissions) : IPermissionResolver
{
    public Task<IReadOnlyCollection<string>> ResolveAsync(User user, CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<string>>(permissions);
}

internal sealed class FakeTokenService : ITokenService
{
    public int IssueCallCount { get; private set; }
    public List<UserId> RevokedAllFor { get; } = [];

    public List<string?> IssuedFamilies { get; } = [];

    public Task<TokenPair> IssueAsync(
        User user, IReadOnlyCollection<string> permissions, string? familyId, CancellationToken ct)
    {
        IssueCallCount++;
        IssuedFamilies.Add(familyId);
        var now = DateTimeOffset.UnixEpoch;
        return Task.FromResult(new TokenPair(
            "access", now.AddMinutes(15), "refresh", now.AddDays(30)));
    }

    public Task<Result<RefreshOutcome>> RedeemRefreshTokenAsync(string refreshToken, CancellationToken ct) =>
        Task.FromResult<Result<RefreshOutcome>>(
            Error.Unauthorized(ErrorCodes.RefreshTokenInvalid, "not configured"));

    public Task RevokeFamilyAsync(UserId userId, string familyId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task RevokeAllForUserAsync(UserId userId, CancellationToken ct)
    {
        RevokedAllFor.Add(userId);
        return Task.CompletedTask;
    }
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}
