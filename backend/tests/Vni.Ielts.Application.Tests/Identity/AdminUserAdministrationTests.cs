using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

public sealed class LastActiveAdminGuardTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly Role _admin = Role.Create(SystemRoles.Admin, true, [PermissionKeys.UserSuspend]);
    private readonly DateTimeOffset _now = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

    public LastActiveAdminGuardTests()
    {
        _roles.AddAsync(_admin, default).GetAwaiter().GetResult();
    }

    private LastActiveAdminGuard Guard() => new(_users, _roles);

    private async Task<User> AddAdminAsync(string email, UserStatus status = UserStatus.Active)
    {
        var user = User.RegisterFromProvider(Email.Create(email), "Admin", _now);
        user.AssignRole(_admin.Id);
        if (status == UserStatus.Suspended) user.Suspend();
        await _users.AddAsync(user, default);
        return user;
    }

    [Fact]
    public async Task Refuses_to_suspend_the_sole_active_admin()
    {
        var sole = await AddAdminAsync("sole@example.test");
        Assert.True(await Guard().WouldLeaveZeroActiveAdminsAsync(sole, LastAdminOperation.Suspend, default));
    }

    [Fact]
    public async Task Refuses_to_revoke_admin_from_the_sole_active_admin()
    {
        var sole = await AddAdminAsync("sole@example.test");
        Assert.True(await Guard().WouldLeaveZeroActiveAdminsAsync(sole, LastAdminOperation.RevokeAdminRole, default));
    }

    [Fact]
    public async Task Allows_suspend_when_another_active_admin_remains()
    {
        var first = await AddAdminAsync("a@example.test");
        await AddAdminAsync("b@example.test");
        Assert.False(await Guard().WouldLeaveZeroActiveAdminsAsync(first, LastAdminOperation.Suspend, default));
    }

    [Fact]
    public async Task Suspended_admin_does_not_satisfy_the_guard()
    {
        var active = await AddAdminAsync("live@example.test");
        await AddAdminAsync("parked@example.test", UserStatus.Suspended);
        Assert.True(await Guard().WouldLeaveZeroActiveAdminsAsync(active, LastAdminOperation.Suspend, default));
    }
}

public sealed class BulkSuspendUsersTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeTokenService _tokens = new();
    private readonly Role _admin = Role.Create(SystemRoles.Admin, true, [PermissionKeys.UserSuspend]);
    private readonly DateTimeOffset _now = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

    public BulkSuspendUsersTests()
    {
        _roles.AddAsync(_admin, default).GetAwaiter().GetResult();
    }

    private BulkSuspendUsers Sut() =>
        new(new FakeProtectedAdminMutation(_users, _roles), _tokens,
            Options.Create(new AdminUserOperationOptions { BulkUserOperationMax = 50 }));

    [Fact]
    public async Task Mixed_request_reports_per_user_and_dedupes_and_skips_self()
    {
        var live = User.RegisterFromProvider(Email.Create("a@example.test"), "A", _now);
        await _users.AddAsync(live, default);
        var missingId = UserId.New().Value;
        var actor = User.RegisterFromProvider(Email.Create("admin@example.test"), "Admin", _now);
        await _users.AddAsync(actor, default);

        var result = await Sut().HandleAsync(
            new BulkSuspendCommand(actor.Id, [live.Id.Value, live.Id.Value, missingId, actor.Id.Value], "incident"),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal("suspended", result.Value!.Items.Single(i => i.UserId == live.Id.Value).Outcome);
        Assert.Equal("not-found", result.Value.Items.Single(i => i.UserId == missingId).Outcome);
        Assert.Equal("refused", result.Value.Items.Single(i => i.UserId == actor.Id.Value).Outcome);
        Assert.Equal(3, result.Value.Items.Count);
        Assert.Contains(live.Id, _tokens.RevokedAllFor);
    }

    [Fact]
    public async Task Refuses_last_admin_in_a_mixed_batch_and_still_suspends_others()
    {
        var sole = User.RegisterFromProvider(Email.Create("admin@example.test"), "Admin", _now);
        sole.AssignRole(_admin.Id);
        await _users.AddAsync(sole, default);
        var other = User.RegisterFromProvider(Email.Create("user@example.test"), "User", _now);
        await _users.AddAsync(other, default);
        var actor = User.RegisterFromProvider(Email.Create("ops@example.test"), "Ops", _now);
        await _users.AddAsync(actor, default);

        var result = await Sut().HandleAsync(
            new BulkSuspendCommand(actor.Id, [sole.Id.Value, other.Id.Value], "incident"),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal("refused", result.Value!.Items.Single(i => i.UserId == sole.Id.Value).Outcome);
        Assert.Equal("suspended", result.Value.Items.Single(i => i.UserId == other.Id.Value).Outcome);
    }

    [Fact]
    public async Task Concurrent_suspend_of_last_two_admins_through_fake_leaves_one()
    {
        var a = User.RegisterFromProvider(Email.Create("a@example.test"), "A", _now);
        a.AssignRole(_admin.Id);
        await _users.AddAsync(a, default);
        var b = User.RegisterFromProvider(Email.Create("b@example.test"), "B", _now);
        b.AssignRole(_admin.Id);
        await _users.AddAsync(b, default);

        var mutation = new FakeProtectedAdminMutation(_users, _roles);
        var first = mutation.TrySuspendAsync(a.Id, default);
        var second = mutation.TrySuspendAsync(b.Id, default);
        await Task.WhenAll(first, second);

        var outcomes = new[] { (await first).Outcome, (await second).Outcome };
        Assert.Contains(ProtectedAdminMutationOutcome.Applied, outcomes);
        Assert.Contains(ProtectedAdminMutationOutcome.WouldLeaveZeroActiveAdmins, outcomes);
        Assert.Equal(1, await _users.CountActiveWithRoleAsync(_admin.Id, excluding: null, default));
    }
}

public sealed class StaffInvitationTests
{
    [Fact]
    public void Pending_invitation_is_live_until_expiry()
    {
        var now = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        var invitation = StaffInvitation.Create(
            Email.Create("staff@example.test"),
            [RoleId.New()],
            UserId.New(),
            "hash",
            now,
            TimeSpan.FromHours(1));

        Assert.True(invitation.IsLive(now.AddMinutes(59)));
        Assert.False(invitation.IsLive(now.AddHours(2)));
    }

    [Fact]
    public void Revoked_or_accepted_tokens_are_not_live()
    {
        var now = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        var invitation = StaffInvitation.Create(
            Email.Create("staff@example.test"),
            [RoleId.New()],
            UserId.New(),
            "hash",
            now,
            TimeSpan.FromHours(1));
        invitation.Revoke();
        Assert.False(invitation.IsLive(now));
    }

    [Fact]
    public async Task Accept_succeeds_once_and_rejects_reuse()
    {
        var now = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        var invitations = new FakeStaffInvitationRepository();
        var users = new FakeUserRepository();
        var identities = new FakeUserIdentityRepository();
        var invitation = StaffInvitation.Create(
            Email.Create("staff@example.test"),
            [RoleId.New()],
            UserId.New(),
            OneTimeToken.Hash("raw-token-value-12"),
            now,
            TimeSpan.FromHours(1));
        await invitations.AddAsync(invitation, default);

        var sut = new AcceptStaffInvitation(
            new FakeStaffInvitationAcceptance(invitations, users, identities),
            new FakePasswordHasher(),
            new FixedClock(now));

        var first = await sut.HandleAsync(
            new AcceptStaffInvitationCommand("raw-token-value-12", "mot-mat-khau-du-dai", "Staff"), default);
        Assert.True(first.IsSuccess);

        var second = await sut.HandleAsync(
            new AcceptStaffInvitationCommand("raw-token-value-12", "mot-mat-khau-du-dai", "Staff"), default);
        Assert.False(second.IsSuccess);
        Assert.Equal(ErrorCodes.InvitationInvalid, second.Error.Code);
    }

    [Fact]
    public async Task Concurrent_accept_leaves_one_account()
    {
        var now = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        var invitations = new FakeStaffInvitationRepository();
        var users = new FakeUserRepository();
        var identities = new FakeUserIdentityRepository();
        var invitation = StaffInvitation.Create(
            Email.Create("race@example.test"),
            [RoleId.New()],
            UserId.New(),
            OneTimeToken.Hash("race-token-value-12"),
            now,
            TimeSpan.FromHours(1));
        await invitations.AddAsync(invitation, default);

        var acceptance = new FakeStaffInvitationAcceptance(invitations, users, identities);
        var sut = new AcceptStaffInvitation(acceptance, new FakePasswordHasher(), new FixedClock(now));

        var a = sut.HandleAsync(
            new AcceptStaffInvitationCommand("race-token-value-12", "mot-mat-khau-du-dai", "A"), default);
        var b = sut.HandleAsync(
            new AcceptStaffInvitationCommand("race-token-value-12", "mot-mat-khau-du-dai", "B"), default);
        await Task.WhenAll(a, b);

        var outcomes = new[] { (await a).IsSuccess, (await b).IsSuccess };
        Assert.Equal(1, outcomes.Count(x => x));
        Assert.Equal(1, (await users.ListAsync(UserListQuery.SearchOnly(null, 0, 50), default)).Total);
    }

    [Fact]
    public async Task Identity_write_failure_leaves_no_partial_account()
    {
        var now = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        var invitations = new FakeStaffInvitationRepository();
        var users = new FakeUserRepository();
        var identities = new FakeUserIdentityRepository();
        var invitation = StaffInvitation.Create(
            Email.Create("partial@example.test"),
            [RoleId.New()],
            UserId.New(),
            OneTimeToken.Hash("partial-token-12x"),
            now,
            TimeSpan.FromHours(1));
        await invitations.AddAsync(invitation, default);

        var acceptance = new FakeStaffInvitationAcceptance(invitations, users, identities)
        {
            FailIdentityWrite = true,
        };
        var sut = new AcceptStaffInvitation(acceptance, new FakePasswordHasher(), new FixedClock(now));

        var result = await sut.HandleAsync(
            new AcceptStaffInvitationCommand("partial-token-12x", "mot-mat-khau-du-dai", "Partial"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, (await users.ListAsync(UserListQuery.SearchOnly(null, 0, 50), default)).Total);
        Assert.True((await invitations.FindByIdAsync(invitation.Id, default))!.IsLive(now));
    }

    [Fact]
    public async Task Invite_rejects_non_cms_role_and_duplicate_live_invite()
    {
        var now = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        var roles = new FakeRoleRepository();
        await roles.AddAsync(Role.Create(SystemRoles.ExamAuthor, true, [PermissionKeys.ExamReadOwn]), default);
        var invitations = new FakeStaffInvitationRepository();
        var users = new FakeUserRepository();
        var sender = new RecordingInvitationSender();
        var sut = new InviteStaff(
            users, roles, invitations, sender, new FixedClock(now),
            Options.Create(new AdminUserOperationOptions { InvitationLifetimeHours = 24 }));

        var learner = await sut.HandleAsync(
            new InviteStaffCommand(UserId.New(), "a@example.test", "A", ["learner"]), default);
        Assert.False(learner.IsSuccess);
        Assert.Equal(ErrorCodes.NotFound, learner.Error.Code);

        var first = await sut.HandleAsync(
            new InviteStaffCommand(UserId.New(), "a@example.test", "A", ["exam-author"]), default);
        Assert.True(first.IsSuccess);
        Assert.False(string.IsNullOrEmpty(sender.LastRawToken));

        invitations.ThrowDuplicateOnNextAdd = true;
        var duplicate = await sut.HandleAsync(
            new InviteStaffCommand(UserId.New(), "b@example.test", "B", ["exam-author"]), default);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(ErrorCodes.EmailAlreadyInvited, duplicate.Error.Code);
    }

    [Fact]
    public async Task Resend_invalidates_previous_token_hash()
    {
        var now = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        var invitations = new FakeStaffInvitationRepository();
        var invitation = StaffInvitation.Create(
            Email.Create("resend@example.test"),
            [RoleId.New()],
            UserId.New(),
            "old-hash",
            now,
            TimeSpan.FromHours(1));
        await invitations.AddAsync(invitation, default);
        var sender = new RecordingInvitationSender();

        var result = await new ResendStaffInvitation(
            invitations, sender, new FixedClock(now),
            Options.Create(new AdminUserOperationOptions { InvitationLifetimeHours = 24 }))
            .HandleAsync(new ResendStaffInvitationCommand(UserId.New(), invitation.Id), default);

        Assert.True(result.IsSuccess);
        var stored = await invitations.FindByIdAsync(invitation.Id, default);
        Assert.NotEqual("old-hash", stored!.TokenHash);
        Assert.Equal(OneTimeToken.Hash(sender.LastRawToken!), stored.TokenHash);
    }

    [Fact]
    public void Admin_ChangeEmail_replaces_address_on_main_identity()
    {
        var user = User.RegisterFromProvider(Email.Create("old@example.test"), "Name", DateTimeOffset.UnixEpoch);
        user.ChangeEmail(Email.Create("new@example.test"), hasLinkedProvider: true);
        Assert.Equal("new@example.test", user.Email?.ToString());
    }
}

internal sealed class RecordingInvitationSender : IVerificationMessageSender
{
    public string? LastRawToken { get; private set; }

    public Task<MessageDelivery> SendPasswordResetAsync(Email email, string token, CancellationToken ct) =>
        Task.FromResult(MessageDelivery.Sent);

    public Task<MessageDelivery> SendStaffInvitationAsync(Email email, string token, CancellationToken ct)
    {
        LastRawToken = token;
        return Task.FromResult(MessageDelivery.Sent);
    }
}

public sealed class ExecutePrivacyRequestTests
{
    [Fact]
    public async Task Execute_is_fail_closed_until_policy_is_configured()
    {
        var users = new FakeUserRepository();
        var roles = new FakeRoleRepository();
        var requests = new FakePrivacyRequests();
        var subject = User.RegisterFromProvider(Email.Create("learner@example.test"), "L", DateTimeOffset.UnixEpoch);
        await users.AddAsync(subject, default);
        var created = PrivacyRequest.Create(subject.Id, PrivacyRequestType.Anonymize, "dsar", UserId.New(), DateTimeOffset.UnixEpoch);
        created.Approve(UserId.New(), DateTimeOffset.UnixEpoch);
        await requests.AddAsync(created, default);

        var result = await new ExecutePrivacyRequest(
            requests, users, new LastActiveAdminGuard(users, roles),
            Options.Create(new PrivacyOptions())).HandleAsync(
            new ExecutePrivacyRequestCommand(UserId.New(), created.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PolicyNotConfigured, result.Error.Code);
    }

    [Fact]
    public async Task Approve_refuses_self_approval()
    {
        var users = new FakeUserRepository();
        var requests = new FakePrivacyRequests();
        var subject = User.RegisterFromProvider(Email.Create("learner@example.test"), "L", DateTimeOffset.UnixEpoch);
        await users.AddAsync(subject, default);
        var requester = UserId.New();
        var created = PrivacyRequest.Create(
            subject.Id, PrivacyRequestType.Anonymize, "dsar", requester, DateTimeOffset.UnixEpoch);
        await requests.AddAsync(created, default);

        var result = await new ApprovePrivacyRequest(requests, new FixedClock(DateTimeOffset.UnixEpoch))
            .HandleAsync(new ApprovePrivacyRequestCommand(requester, created.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error.Code);
    }
}

internal sealed class FakePrivacyRequests : IPrivacyRequestRepository
{
    private readonly Dictionary<string, PrivacyRequest> _all = [];

    public Task<PrivacyRequest?> FindByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult(_all.GetValueOrDefault(id));

    public Task<IReadOnlyList<PrivacyRequest>> ListForSubjectAsync(UserId subjectId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PrivacyRequest>>([.. _all.Values.Where(r => r.SubjectId == subjectId)]);

    public Task AddAsync(PrivacyRequest request, CancellationToken ct)
    {
        _all[request.Id] = request;
        return Task.CompletedTask;
    }

    public Task SaveAsync(PrivacyRequest request, CancellationToken ct)
    {
        _all[request.Id] = request;
        return Task.CompletedTask;
    }
}
