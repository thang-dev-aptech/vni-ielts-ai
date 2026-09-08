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

    public Task<User?> FindByPhoneAsync(PhoneNumber phone, CancellationToken ct) =>
        Task.FromResult(_byId.Values.FirstOrDefault(u => u.Phone == phone));

    public Task<bool> PhoneExistsAsync(PhoneNumber phone, CancellationToken ct) =>
        Task.FromResult(_byId.Values.Any(u => u.Phone == phone));

    /// <summary>
    /// Set to make the next <see cref="AddAsync"/> lose the unique-index race
    /// on the address, which is the only way to exercise the concurrent-signup
    /// path. The flag clears itself so the retry inside the handler succeeds,
    /// exactly as the real index behaves once the winner has committed.
    /// </summary>
    public bool ThrowDuplicateOnNextAdd { get; set; }

    /// <summary>The same, for the phone index — the one registration hits.</summary>
    public bool ThrowDuplicatePhoneOnNextAdd { get; set; }

    /// <summary>
    /// And the same two for <see cref="SaveAsync"/>.
    ///
    /// <para>
    /// Worth having separately: <c>SetPhone</c> and <c>ChangeEmail</c> reach
    /// the index through a replace, not an insert, and the real repository
    /// translated nothing on that path until 08/09/2026 — so the catch blocks
    /// around it had never once been exercised.
    /// </para>
    ///
    /// <para>
    /// Which index was violated is set by the caller rather than guessed from
    /// the document: an account can hold both a number and an address, so
    /// inferring it here would make the fake decide the very thing the test is
    /// checking the handler distinguishes.
    /// </para>
    /// </summary>
    public bool ThrowDuplicateEmailOnNextSave { get; set; }

    /// <inheritdoc cref="ThrowDuplicateEmailOnNextSave"/>
    public bool ThrowDuplicatePhoneOnNextSave { get; set; }

    public Task<(IReadOnlyList<User> Users, long Total)> ListAsync(
        string? search, int skip, int take, CancellationToken ct) =>
        ListAsync(UserListQuery.SearchOnly(search, skip, take), ct);

    public Task<(IReadOnlyList<User> Users, long Total)> ListAsync(
        UserListQuery query, CancellationToken ct)
    {
        var matches = _byId.Values.Where(u =>
        {
            if (!string.IsNullOrWhiteSpace(query.Search)
                && !(u.Email?.Value.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false)
                && !(u.Phone?.Value.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false)
                && !u.DisplayName.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
                return false;
            if (query.RoleId is { } role && !u.HasRole(role)) return false;
            if (query.Status is { } status && u.Status != status) return false;
            if (query.HasEmail is { } hasEmail && (u.Email is not null) != hasEmail) return false;
            return true;
        }).ToList();

        return Task.FromResult<(IReadOnlyList<User>, long)>(
            ([.. matches.Skip(query.Skip).Take(query.Take)], matches.Count));
    }

    public Task<long> CountActiveWithRoleAsync(RoleId roleId, UserId? excluding, CancellationToken ct) =>
        Task.FromResult(_byId.Values.LongCount(u =>
            u.Status == UserStatus.Active
            && u.HasRole(roleId)
            && (excluding is null || u.Id != excluding.Value)));

    public Task AddAsync(User user, CancellationToken ct)
    {
        if (ThrowDuplicatePhoneOnNextAdd)
        {
            ThrowDuplicatePhoneOnNextAdd = false;
            throw new DuplicatePhoneException(user.Phone?.Value ?? string.Empty);
        }

        if (ThrowDuplicateOnNextAdd)
        {
            ThrowDuplicateOnNextAdd = false;
            throw new DuplicateEmailException(user.Email?.Value ?? string.Empty);
        }

        _byId[user.Id.Value] = user;
        return Task.CompletedTask;
    }

    public Task SaveAsync(User user, CancellationToken ct)
    {
        if (ThrowDuplicateEmailOnNextSave)
        {
            ThrowDuplicateEmailOnNextSave = false;
            throw new DuplicateEmailException(user.Email?.Value ?? string.Empty);
        }

        if (ThrowDuplicatePhoneOnNextSave)
        {
            ThrowDuplicatePhoneOnNextSave = false;
            throw new DuplicatePhoneException(user.Phone?.Value ?? string.Empty);
        }

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

    public Task SaveAsync(UserIdentity identity, CancellationToken ct)
    {
        var index = _all.FindIndex(i => i.Id == identity.Id);
        if (index >= 0) _all[index] = identity;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(UserIdentityId id, CancellationToken ct)
    {
        _all.RemoveAll(i => i.Id == id);
        return Task.CompletedTask;
    }
}

internal sealed class FakeRoleRepository : IRoleRepository
{
    private readonly List<Role> _all =
        [Role.Create(SystemRoles.Learner, isSystem: true, [PermissionKeys.ExamReadOwn])];

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

    /// <summary>
    /// Reads without spending, so a test can assert <i>which account</i> a
    /// callback resolved to. Consuming would make the assertion itself change
    /// the state under test.
    /// </summary>
    public Task<UserId?> ResolveAsync(string code) =>
        Task.FromResult(_codes.TryGetValue(code, out var id) ? id : (UserId?)null);

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

    public Task<bool> IsLockedAsync(string handle, CancellationToken ct) =>
        Task.FromResult(_failures.GetValueOrDefault(Key(handle)) >= MaxFailures);

    public Task RecordFailureAsync(string handle, CancellationToken ct)
    {
        _failures[Key(handle)] = _failures.GetValueOrDefault(Key(handle)) + 1;
        return Task.CompletedTask;
    }

    public Task ClearAsync(string handle, CancellationToken ct)
    {
        _failures.Remove(Key(handle));
        return Task.CompletedTask;
    }

    /// <summary>
    /// <b>Trim and lower-case only — deliberately no phone normalisation.</b>
    /// The real store does exactly this, so the counter is only per-account if
    /// the caller normalises before it gets here. Doing it in the fake too
    /// would hide the bug where four spellings of one number become four
    /// independent budgets of ten guesses.
    /// </summary>
    private static string Key(string handle) => handle.Trim().ToLowerInvariant();
}

/// <summary>
/// In-process serialisation of the last-admin check+write. Mirrors the Mongo
/// coordinator for unit tests; integration tests prove the real transaction.
/// </summary>
internal sealed class FakeProtectedAdminMutation(FakeUserRepository users, FakeRoleRepository roles)
    : IProtectedAdminMutation
{
    private readonly object _gate = new();
    private readonly LastActiveAdminGuard _guard = new(users, roles);

    public Task<ProtectedAdminMutationResult> TrySuspendAsync(UserId targetId, CancellationToken ct)
    {
        lock (_gate)
        {
            var user = users.FindByIdAsync(targetId, ct).GetAwaiter().GetResult();
            if (user is null)
                return Task.FromResult(new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.NotFound, null));
            if (user.Status == UserStatus.Suspended)
                return Task.FromResult(new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.AlreadyApplied, user));
            if (_guard.WouldLeaveZeroActiveAdminsAsync(user, LastAdminOperation.Suspend, ct).GetAwaiter().GetResult())
                return Task.FromResult(new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.WouldLeaveZeroActiveAdmins, user));

            user.Suspend();
            users.SaveAsync(user, ct).GetAwaiter().GetResult();
            return Task.FromResult(new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.Applied, user));
        }
    }

    public Task<ProtectedAdminMutationResult> TryRevokeAdminRoleAsync(UserId targetId, CancellationToken ct)
    {
        lock (_gate)
        {
            var user = users.FindByIdAsync(targetId, ct).GetAwaiter().GetResult();
            if (user is null)
                return Task.FromResult(new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.NotFound, null));

            var admin = roles.FindByNameAsync(SystemRoles.Admin, ct).GetAwaiter().GetResult();
            if (admin is null)
                return Task.FromResult(new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.NotFound, user));
            if (!user.HasRole(admin.Id))
                return Task.FromResult(new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.AlreadyApplied, user));
            if (_guard.WouldLeaveZeroActiveAdminsAsync(user, LastAdminOperation.RevokeAdminRole, ct).GetAwaiter().GetResult())
                return Task.FromResult(new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.WouldLeaveZeroActiveAdmins, user));

            user.RemoveRole(admin.Id);
            users.SaveAsync(user, ct).GetAwaiter().GetResult();
            return Task.FromResult(new ProtectedAdminMutationResult(ProtectedAdminMutationOutcome.Applied, user));
        }
    }
}

internal sealed class FakeStaffInvitationRepository : IStaffInvitationRepository
{
    private readonly Dictionary<string, StaffInvitation> _byId = [];

    public bool ThrowDuplicateOnNextAdd { get; set; }

    public Task<StaffInvitation?> FindByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public Task<StaffInvitation?> FindLiveByEmailAsync(Email email, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(_byId.Values.FirstOrDefault(i => i.Email == email && i.IsLive(now)));

    public Task<StaffInvitation?> FindByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(_byId.Values.FirstOrDefault(i => i.TokenHash == tokenHash));

    public Task<(IReadOnlyList<StaffInvitation> Invitations, long Total)> ListPendingAsync(
        int skip, int take, DateTimeOffset now, CancellationToken ct)
    {
        var live = _byId.Values.Where(i => i.IsLive(now)).ToList();
        return Task.FromResult<(IReadOnlyList<StaffInvitation>, long)>(
            ([.. live.Skip(skip).Take(take)], live.Count));
    }

    public Task AddAsync(StaffInvitation invitation, CancellationToken ct)
    {
        if (ThrowDuplicateOnNextAdd)
        {
            ThrowDuplicateOnNextAdd = false;
            throw new DuplicateLiveInvitationException(invitation.Email.Value);
        }

        _byId[invitation.Id] = invitation;
        return Task.CompletedTask;
    }

    public Task SaveAsync(StaffInvitation invitation, CancellationToken ct)
    {
        _byId[invitation.Id] = invitation;
        return Task.CompletedTask;
    }

    public Task ExpireStalePendingByEmailAsync(Email email, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var invitation in _byId.Values
                     .Where(i => i.Email == email
                                 && i.Status == StaffInvitationStatus.Pending
                                 && now >= i.ExpiresAt)
                     .ToList())
        {
            invitation.MarkExpired();
            _byId[invitation.Id] = invitation;
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// In-process atomic accept. Identity-write failure leaves neither user nor
/// accepted invitation — the same all-or-nothing contract as the Mongo port.
/// </summary>
internal sealed class FakeStaffInvitationAcceptance(
    FakeStaffInvitationRepository invitations,
    FakeUserRepository users,
    FakeUserIdentityRepository identities) : IStaffInvitationAcceptance
{
    private readonly object _gate = new();

    public bool FailIdentityWrite { get; set; }

    public Task<Result<AcceptStaffInvitationResult>> AcceptAsync(
        string tokenHash,
        string displayName,
        string passwordHash,
        DateTimeOffset now,
        CancellationToken ct)
    {
        lock (_gate)
        {
            var invitation = invitations.FindByTokenHashAsync(tokenHash, ct).GetAwaiter().GetResult();
            if (invitation is null || !invitation.IsLive(now))
            {
                return Task.FromResult<Result<AcceptStaffInvitationResult>>(
                    Error.Validation(ErrorCodes.InvitationInvalid, "This invitation is no longer valid."));
            }

            if (users.EmailExistsAsync(invitation.Email, ct).GetAwaiter().GetResult())
            {
                return Task.FromResult<Result<AcceptStaffInvitationResult>>(
                    Error.Conflict(ErrorCodes.EmailAlreadyRegistered, "That email address is already registered."));
            }

            var user = User.RegisterFromProvider(invitation.Email, displayName, now);
            foreach (var roleId in invitation.RoleIds)
                user.AssignRole(roleId);

            if (FailIdentityWrite)
            {
                return Task.FromResult<Result<AcceptStaffInvitationResult>>(
                    Error.Conflict(ErrorCodes.ValidationFailed, "Identity write failed."));
            }

            users.AddAsync(user, ct).GetAwaiter().GetResult();
            identities.AddAsync(
                UserIdentity.ForPassword(user.Id, passwordHash, now), ct)
                .GetAwaiter().GetResult();
            invitation.Accept(now);
            invitations.SaveAsync(invitation, ct).GetAwaiter().GetResult();
            return Task.FromResult<Result<AcceptStaffInvitationResult>>(
                new AcceptStaffInvitationResult(user.Id.Value));
        }
    }
}
