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

    /// <summary>
    /// Set to make the next <see cref="AddAsync"/> lose the unique-index race,
    /// which is the only way to exercise the concurrent-signup path. The flag
    /// clears itself so the retry inside the handler succeeds, exactly as the
    /// real index behaves once the winner has committed.
    /// </summary>
    public bool ThrowDuplicateOnNextAdd { get; set; }

    public Task<(IReadOnlyList<User> Users, long Total)> ListAsync(
        string? search, int skip, int take, CancellationToken ct)
    {
        var matches = _byId.Values
            .Where(u => string.IsNullOrWhiteSpace(search)
                || u.Email.Value.Contains(search, StringComparison.OrdinalIgnoreCase)
                || u.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult<(IReadOnlyList<User>, long)>(
            ([.. matches.Skip(skip).Take(take)], matches.Count));
    }

    public Task AddAsync(User user, CancellationToken ct)
    {
        if (ThrowDuplicateOnNextAdd)
        {
            ThrowDuplicateOnNextAdd = false;
            throw new DuplicateEmailException(user.Email.Value);
        }

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

    /// <summary>
    /// Identity to insert behind the caller's back on the next
    /// <see cref="AddAsync"/>, so the unique index rejects theirs. This is the
    /// only way to reproduce two tabs finishing a sign-in at the same instant.
    /// </summary>
    public UserIdentity? LoseNextAddTo { get; set; }

    public Task AddAsync(UserIdentity identity, CancellationToken ct)
    {
        if (LoseNextAddTo is not null)
        {
            var winner = LoseNextAddTo;
            LoseNextAddTo = null;
            _all.Add(winner);

            throw new DuplicateIdentityException(
                identity.Provider.ToString(), identity.ProviderUserId);
        }

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

    /// <summary>The parent each issued token named, so a test can assert the chain.</summary>
    public List<string?> RotatedFrom { get; } = [];

    public Task<TokenPair> IssueAsync(
        User user, IReadOnlyCollection<string> permissions, string? familyId, CancellationToken ct,
        string? rotatedFromHash = null)
    {
        IssueCallCount++;
        IssuedFamilies.Add(familyId);
        RotatedFrom.Add(rotatedFromHash);
        var now = DateTimeOffset.UnixEpoch;
        return Task.FromResult(new TokenPair(
            "access", now.AddMinutes(15), "refresh", now.AddDays(30)));
    }

    public Task<Result<RefreshOutcome>> RedeemRefreshTokenAsync(string refreshToken, CancellationToken ct) =>
        Task.FromResult<Result<RefreshOutcome>>(
            Error.Unauthorized(ErrorCodes.RefreshTokenInvalid, "not configured"));

    public List<(UserId User, string Family)> RevokedFamilies { get; } = [];

    public Task RevokeFamilyAsync(UserId userId, string familyId, CancellationToken ct)
    {
        RevokedFamilies.Add((userId, familyId));
        return Task.CompletedTask;
    }

    public List<(UserId User, string Kept)> RevokedAllExcept { get; } = [];

    public Task<int> RevokeAllExceptAsync(UserId userId, string keepFamilyId, CancellationToken ct)
    {
        RevokedAllExcept.Add((userId, keepFamilyId));
        return Task.FromResult(3);
    }

    public Task RevokeAllForUserAsync(UserId userId, CancellationToken ct)
    {
        RevokedAllFor.Add(userId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A mail sender whose delivery answer is set per test.
///
/// <para>
/// <b><see cref="Delivery"/> defaults to <c>Sent</c>, and the tests that
/// matter set it to <c>NotSent</c>.</b> The only sender that exists in the
/// product today writes the link to a log and sends nothing, so
/// <c>NotSent</c> is not an exotic branch — it is production-as-configured,
/// and the reason every use case here reports what happened instead of
/// letting a caller assume.
/// </para>
/// </summary>
internal sealed class FakeVerificationMessageSender : IVerificationMessageSender
{
    public MessageDelivery Delivery { get; set; } = MessageDelivery.Sent;

    public List<(string Address, string Token)> Verifications { get; } = [];
    public List<(string Address, string Token)> Resets { get; } = [];

    /// <summary>Addresses a verification message was sent to, in order.</summary>
    public List<string> SentTo => [.. Verifications.Select(v => v.Address)];

    public Task<MessageDelivery> SendAsync(Email address, string token, CancellationToken ct)
    {
        Verifications.Add((address.Value, token));
        return Task.FromResult(Delivery);
    }

    public Task<MessageDelivery> SendPasswordResetAsync(
        Email address, string token, CancellationToken ct)
    {
        Resets.Add((address.Value, token));
        return Task.FromResult(Delivery);
    }
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}

/// <summary>
/// A provider whose behaviour is set per test.
///
/// <para>
/// <see cref="AssertsEmailVerification"/> is settable here and deliberately
/// <b>not</b> settable in production code — it is a fact about a provider's
/// protocol, not a policy. Making it configurable in the fake is what lets one
/// test class cover both the Google-shaped and Facebook-shaped cases without
/// two adapters. → ADR-0013
/// </para>
/// </summary>
internal sealed class FakeExternalIdentityProvider(
    IdentityProvider provider = IdentityProvider.Google,
    bool assertsEmailVerification = true) : IExternalIdentityProvider
{
    public IdentityProvider Provider { get; } = provider;
    public bool AssertsEmailVerification { get; } = assertsEmailVerification;

    public string Key => Provider.ToString().ToLowerInvariant();

    /// <summary>What the exchange returns. Null means the exchange fails.</summary>
    public ExternalIdentity? Result { get; set; }

    public AuthorizationRequest? LastRequest { get; private set; }
    public string? LastCode { get; private set; }
    public string? LastVerifier { get; private set; }
    public string? LastNonce { get; private set; }

    public Task<Uri> BuildAuthorizationUrlAsync(AuthorizationRequest request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(new Uri(
            $"https://provider.example/authorize?state={request.State}"
            + $"&code_challenge={request.CodeChallenge}&nonce={request.Nonce}"));
    }

    public Task<Result<ExternalIdentity>> ExchangeCodeAsync(
        string code, string codeVerifier, string nonce, CancellationToken ct)
    {
        LastCode = code;
        LastVerifier = codeVerifier;
        LastNonce = nonce;

        return Task.FromResult<Result<ExternalIdentity>>(
            Result is null
                ? Error.Unauthorized(ErrorCodes.SsoExchangeFailed, "Sign-in could not be completed.")
                : Result);
    }
}

internal sealed class FakeProviderRegistry(params IExternalIdentityProvider[] providers)
    : IExternalIdentityProviderRegistry
{
    public IReadOnlyCollection<IExternalIdentityProvider> Enabled { get; } = providers;

    public bool TryResolve(string providerKey, out IExternalIdentityProvider provider)
    {
        provider = Enabled.FirstOrDefault(
            p => string.Equals(p.Provider.ToString(), providerKey, StringComparison.OrdinalIgnoreCase))!;
        return provider is not null;
    }
}

/// <summary>
/// In-memory, and single-use like the real one — a state store that hands the
/// same value back twice provides no CSRF protection, so the fake must not be
/// more forgiving than the implementation it stands in for.
/// </summary>
internal sealed class FakeSsoStateStore : ISsoStateStore
{
    private readonly Dictionary<string, SsoState> _states = [];

    public Task StoreAsync(SsoState state, CancellationToken ct)
    {
        _states[state.State] = state;
        return Task.CompletedTask;
    }

    public Task<SsoState?> ConsumeAsync(string state, CancellationToken ct)
    {
        if (!_states.Remove(state, out var found))
            return Task.FromResult<SsoState?>(null);

        return Task.FromResult<SsoState?>(found);
    }
}

internal sealed class FakeHandoffCodeStore : IHandoffCodeStore
{
    private readonly Dictionary<string, UserId> _codes = [];
    private int _next;

    public Task<string> IssueAsync(UserId userId, CancellationToken ct)
    {
        var code = $"handoff-{++_next}";
        _codes[code] = userId;
        return Task.FromResult(code);
    }

    public Task<UserId?> ConsumeAsync(string code, CancellationToken ct) =>
        Task.FromResult(_codes.Remove(code, out var userId) ? userId : (UserId?)null);
}

/// <summary>
/// The login throttle, counting in memory.
///
/// Mirrors the production threshold rather than picking a convenient small
/// one: a test that locks after two failures would pass against an
/// implementation that locks after two, which is not the behaviour shipped.
/// </summary>
internal sealed class FakeLoginThrottle : ILoginThrottle
{
    private const int MaxFailures = 10;
    private readonly Dictionary<string, int> _failures = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> Failures => _failures;

    public Task<bool> IsLockedAsync(string email, CancellationToken ct) =>
        Task.FromResult(_failures.GetValueOrDefault(Key(email)) >= MaxFailures);

    public Task RecordFailureAsync(string email, CancellationToken ct)
    {
        _failures[Key(email)] = _failures.GetValueOrDefault(Key(email)) + 1;
        return Task.CompletedTask;
    }

    public Task ClearAsync(string email, CancellationToken ct)
    {
        _failures.Remove(Key(email));
        return Task.CompletedTask;
    }

    private static string Key(string email) => email.Trim().ToLowerInvariant();
}
