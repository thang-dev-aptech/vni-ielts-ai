using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

public sealed class RefreshTokensTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A token service whose redemption outcome the test controls.</summary>
    private sealed class StubTokenService(Result<RefreshOutcome> outcome) : ITokenService
    {
        public List<string?> IssuedFamilies { get; } = [];
        public List<UserId> RevokedAllFor { get; } = [];

        public List<string?> RotatedFrom { get; } = [];

        public Task<TokenPair> IssueAsync(
            User user, IReadOnlyCollection<string> permissions, string? familyId, CancellationToken ct,
            string? rotatedFromHash = null)
        {
            IssuedFamilies.Add(familyId);
            RotatedFrom.Add(rotatedFromHash);
            return Task.FromResult(new TokenPair(
                "access", Now.AddMinutes(15), "refresh", Now.AddDays(30)));
        }

        public Task<Result<RefreshOutcome>> RedeemRefreshTokenAsync(string t, CancellationToken ct) =>
            Task.FromResult(outcome);

        public Task RevokeFamilyAsync(UserId u, string f, CancellationToken ct) => Task.CompletedTask;

        public Task<int> RevokeAllExceptAsync(UserId userId, string keepFamilyId, CancellationToken ct) =>
            Task.FromResult(0);

        public Task RevokeAllForUserAsync(UserId userId, CancellationToken ct)
        {
            RevokedAllFor.Add(userId);
            return Task.CompletedTask;
        }
    }

    private static async Task<(User User, FakeUserRepository Users)> SeedAsync(bool suspended = false)
    {
        var users = new FakeUserRepository();
        var user = User.Register(Email.Create("hoc.vien@example.com"), "Học viên", Now);
        if (suspended) user.Suspend();
        await users.AddAsync(user, default);
        return (user, users);
    }

    [Fact]
    public async Task Rotation_continues_the_existing_family_rather_than_starting_a_new_one()
    {
        // REGRESSION GUARD. The first version of this code called
        // IssueAsync without a family id on refresh, which started a fresh
        // family on every rotation. Reuse detection revokes a *family*, so
        // that bug quietly reduced its reach to the single most recent token —
        // a token stolen two rotations ago would have revoked nothing.
        //
        // Nothing failed visibly when it was wrong, which is exactly why it
        // needs a test rather than a code review.
        var (user, users) = await SeedAsync();
        var tokens = new StubTokenService(new RefreshOutcome(user.Id, "family-abc", "parent-hash"));
        var sut = new RefreshTokens(users, new FakePermissionResolver(), tokens);

        var result = await sut.HandleAsync(new RefreshCommand("some-refresh-token"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(["family-abc"], tokens.IssuedFamilies);
    }

    [Fact]
    public async Task A_reused_token_surfaces_its_own_code_so_the_client_can_sign_out()
    {
        var (_, users) = await SeedAsync();
        var reused = Error.Unauthorized(ErrorCodes.RefreshTokenReused, "reused");
        var sut = new RefreshTokens(users, new FakePermissionResolver(), new StubTokenService(reused));

        var result = await sut.HandleAsync(new RefreshCommand("stolen-token"), default);

        Assert.False(result.IsSuccess);
        // Distinct from REFRESH_TOKEN_INVALID on purpose: the client should
        // clear its session and tell the user why, not retry silently.
        Assert.Equal(ErrorCodes.RefreshTokenReused, result.Error.Code);
    }

    [Fact]
    public async Task An_empty_token_is_rejected_without_touching_the_store()
    {
        var (_, users) = await SeedAsync();
        var tokens = new StubTokenService(
            Error.Unauthorized(ErrorCodes.RefreshTokenInvalid, "should not be reached"));
        var sut = new RefreshTokens(users, new FakePermissionResolver(), tokens);

        var result = await sut.HandleAsync(new RefreshCommand("  "), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.RefreshTokenInvalid, result.Error.Code);
        Assert.Empty(tokens.IssuedFamilies);
    }

    [Fact]
    public async Task Refreshing_into_a_suspended_account_revokes_every_session()
    {
        // A suspension has to end sessions that already exist. Waiting for an
        // access token to expire leaves the account usable for up to 15
        // minutes after it was disabled.
        var (user, users) = await SeedAsync(suspended: true);
        var tokens = new StubTokenService(new RefreshOutcome(user.Id, "family-abc", "parent-hash"));
        var sut = new RefreshTokens(users, new FakePermissionResolver(), tokens);

        var result = await sut.HandleAsync(new RefreshCommand("valid-token"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AccountSuspended, result.Error.Code);
        Assert.Equal([user.Id], tokens.RevokedAllFor);
        Assert.Empty(tokens.IssuedFamilies);
    }
}
