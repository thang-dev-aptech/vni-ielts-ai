using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// Device management.
///
/// The rules worth pinning are both about what it refuses: it will not end the
/// session doing the asking, and it will not reach into another account.
/// </summary>
public sealed class SessionManagementTests
{
    private static readonly UserId Owner = new("user-1");

    private sealed class FakeSessionDirectory(params LearnerSession[] sessions) : ISessionDirectory
    {
        public Task<IReadOnlyList<LearnerSession>> ListAsync(
            UserId userId, string? currentFamilyId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LearnerSession>>(
                [.. sessions.Select(s => s with { IsCurrent = s.FamilyId == currentFamilyId })]);
    }

    private static LearnerSession Session(string familyId) =>
        new(familyId, "Mozilla/5.0", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddDays(30), IsCurrent: false);

    [Fact]
    public async Task The_calling_device_is_marked_as_current()
    {
        var sut = new ListSessions(new FakeSessionDirectory(Session("fam-a"), Session("fam-b")));

        var listed = await sut.HandleAsync(new ListSessionsQuery(Owner, "fam-b"), default);

        Assert.False(listed.Single(s => s.FamilyId == "fam-a").IsCurrent);
        Assert.True(listed.Single(s => s.FamilyId == "fam-b").IsCurrent);
    }

    [Fact]
    public async Task Another_device_can_be_signed_out()
    {
        var tokens = new FakeTokenService();
        var sut = new RevokeSession(tokens);

        var result = await sut.HandleAsync(new RevokeSessionCommand(Owner, "fam-a", "fam-b"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal([(Owner, "fam-a")], tokens.RevokedFamilies);
    }

    [Fact]
    public async Task The_device_doing_the_asking_cannot_sign_itself_out_from_the_list()
    {
        // It would leave the client holding a dead token while still rendering
        // a signed-in header. Sign-out is the action that also clears local
        // state, and it lives in the account menu.
        var tokens = new FakeTokenService();
        var sut = new RevokeSession(tokens);

        var result = await sut.HandleAsync(new RevokeSessionCommand(Owner, "fam-b", "fam-b"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SessionIsCurrent, result.Error.Code);
        Assert.Empty(tokens.RevokedFamilies);
    }

    [Fact]
    public async Task A_revoke_is_always_scoped_to_the_caller()
    {
        // The family id arrives from the URL. Passing someone else's must not
        // end their session — the user id travels with it and the repository
        // filters on both. → threat T19
        var tokens = new FakeTokenService();
        var sut = new RevokeSession(tokens);

        await sut.HandleAsync(new RevokeSessionCommand(Owner, "someone-elses-family", null), default);

        Assert.Equal([(Owner, "someone-elses-family")], tokens.RevokedFamilies);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_session_id_is_refused(string familyId)
    {
        var sut = new RevokeSession(new FakeTokenService());

        var result = await sut.HandleAsync(new RevokeSessionCommand(Owner, familyId, null), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error.Code);
    }
}
