using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// Registration, after the 27/08/2026 owner decision.
///
/// <para>
/// <i>"tạo tài khoản với email pass cho login như bình thường nhưng sẽ xác
/// minh ở trang hồ sơ học sinh sau cũng được"</i> — create the account with an
/// email and a password, let them sign in as normal, verify later from the
/// profile page.
/// </para>
///
/// <para>
/// Two of these tests exist to stop a well-meaning reinstatement. The comment
/// this handler used to carry argued that a fresh account must not be signed
/// in because the address is an unproven claim, and it was internally
/// consistent — it was just not what the owner asked for, and no code
/// anywhere actually refused an unverified account anything.
/// </para>
/// </summary>
public sealed class RegisterUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    private sealed class FakeVerificationTokens : IEmailVerificationTokens
    {
        public List<UserId> Issued { get; } = [];

        public Task<string> IssueAsync(UserId userId, CancellationToken ct)
        {
            Issued.Add(userId);
            return Task.FromResult($"verify-{Issued.Count}");
        }

        public Task<UserId?> RedeemAsync(string token, CancellationToken ct) =>
            Task.FromResult<UserId?>(null);

        /*
         * <b>The code flow is modelled, not stubbed to succeed.</b> A fake that
         * returned `Verified` for anything would let every test above it pass
         * while the attempt cap — the thing that makes six digits safe — did
         * not exist.
         */
        public string? OutstandingCode { get; private set; }

        public int Attempts { get; private set; }

        public Task<string> IssueCodeAsync(UserId userId, CancellationToken ct)
        {
            Issued.Add(userId);
            OutstandingCode = "123456";
            Attempts = 0;
            return Task.FromResult(OutstandingCode);
        }

        public Task<CodeRedemption> RedeemCodeAsync(
            UserId userId, string code, CancellationToken ct)
        {
            if (OutstandingCode is null) return Task.FromResult(CodeRedemption.Expired);
            if (Attempts >= 5) return Task.FromResult(CodeRedemption.TooManyAttempts);

            Attempts++;

            if (code != OutstandingCode)
            {
                return Task.FromResult(
                    Attempts >= 5 ? CodeRedemption.TooManyAttempts : CodeRedemption.Incorrect);
            }

            OutstandingCode = null;
            return Task.FromResult(CodeRedemption.Verified);
        }
    }

    private sealed class Harness
    {
        public FakeUserRepository Users { get; } = new();
        public FakeUserIdentityRepository Identities { get; } = new();
        public FakeRoleRepository Roles { get; } = new();
        public FakePasswordHasher Hasher { get; } = new();
        public FakeVerificationTokens Tokens { get; } = new();
        public FakeVerificationMessageSender Sender { get; } = new();
        public FakeTokenService Sessions { get; } = new();

        public RegisterUser Sut => new(
            Users, Identities, Roles, Hasher, Tokens, Sender,
            new FakePermissionResolver(PermissionKeys.ExamRead), Sessions, new FixedClock(Now));
    }

    private static RegisterUserCommand Command(string email = "hoc.vien@example.com") =>
        new(email, "mot-mat-khau-du-dai-2026", "Học viên");

    [Fact]
    public async Task Registering_signs_the_new_account_in()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("access", result.Value!.Session.Tokens.AccessToken);
        Assert.Equal("refresh", result.Value.Session.Tokens.RefreshToken);
        Assert.Equal("Học viên", result.Value.Session.DisplayName);
        Assert.Equal(1, h.Sessions.IssueCallCount);
    }

    [Fact]
    public async Task The_session_starts_a_new_token_family()
    {
        // `familyId: null`, exactly as a password sign-in does. Continuing a
        // family that does not exist is how reuse detection ends up covering
        // nothing. → ITokenService.IssueAsync
        var h = new Harness();

        await h.Sut.HandleAsync(Command(), default);

        Assert.Equal([null], h.Sessions.IssuedFamilies);
    }

    [Fact]
    public async Task The_account_is_created_unverified_and_that_stops_nothing()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command(), default);

        var user = await h.Users.FindByIdAsync(result.Value!.Session.UserId, default);
        Assert.False(user!.EmailVerified);

        // Unverified and holding a working session, which is the decision.
        Assert.True(result.IsSuccess);
        Assert.Equal(1, h.Sessions.IssueCallCount);
    }

    [Fact]
    public async Task A_verification_token_is_issued_and_sent_to_the_address()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command(), default);

        Assert.Equal([result.Value!.Session.UserId], h.Tokens.Issued);
        Assert.Equal([("hoc.vien@example.com", "verify-1")], h.Sender.Verifications);
    }

    [Fact]
    public async Task It_reports_that_nothing_was_sent_when_nothing_was_sent()
    {
        // The configured sender writes the link to a log. A result that did not
        // carry this would leave the API free to answer "we emailed you",
        // which is the lie the whole MessageDelivery type exists to prevent.
        var h = new Harness();
        h.Sender.Delivery = MessageDelivery.NotSent;

        var result = await h.Sut.HandleAsync(Command(), default);

        Assert.Equal(MessageDelivery.NotSent, result.Value!.VerificationMessage);
    }

    [Fact]
    public async Task It_reports_a_send_when_a_real_provider_delivered_one()
    {
        var h = new Harness();
        h.Sender.Delivery = MessageDelivery.Sent;

        var result = await h.Sut.HandleAsync(Command(), default);

        Assert.Equal(MessageDelivery.Sent, result.Value!.VerificationMessage);
    }

    [Fact]
    public async Task The_learner_role_is_assigned()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command(), default);

        var learner = await h.Roles.FindByNameAsync(SystemRoles.Learner, default);
        var user = await h.Users.FindByIdAsync(result.Value!.Session.UserId, default);
        Assert.True(user!.HasRole(learner!.Id));
    }

    [Fact]
    public async Task A_password_identity_is_created_so_the_learner_can_come_back()
    {
        var h = new Harness();

        await h.Sut.HandleAsync(Command(), default);

        var identity = await h.Identities.FindByProviderAsync(
            IdentityProvider.Email, "hoc.vien@example.com", default);

        Assert.NotNull(identity);
        Assert.Equal(h.Hasher.Hash("mot-mat-khau-du-dai-2026"), identity!.PasswordHash);
    }

    [Fact]
    public async Task An_address_that_already_has_an_account_is_refused_without_a_session()
    {
        var h = new Harness();
        await h.Sut.HandleAsync(Command(), default);

        var again = await h.Sut.HandleAsync(Command(), default);

        Assert.False(again.IsSuccess);
        Assert.Equal(ErrorCodes.EmailAlreadyRegistered, again.Error.Code);

        // One session from the first registration, none from the refusal.
        Assert.Equal(1, h.Sessions.IssueCallCount);
    }

    [Fact]
    public async Task Losing_the_unique_index_race_reads_as_the_same_conflict()
    {
        var h = new Harness();
        h.Users.ThrowDuplicateOnNextAdd = true;

        var result = await h.Sut.HandleAsync(Command(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.EmailAlreadyRegistered, result.Error.Code);
        Assert.Equal(0, h.Sessions.IssueCallCount);
    }

    [Theory]
    [InlineData("khong-phai-email", ErrorCodes.EmailInvalid)]
    [InlineData("", ErrorCodes.EmailInvalid)]
    public async Task A_malformed_address_is_refused(string address, string code)
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(Command(address), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(0, h.Sessions.IssueCallCount);
    }

    [Fact]
    public async Task A_weak_password_is_refused_before_anything_is_written()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(
            new RegisterUserCommand("hoc.vien@example.com", "ngan", "Học viên"), default);

        Assert.False(result.IsSuccess);
        Assert.Empty(h.Sender.Verifications);
        Assert.Equal(0, h.Sessions.IssueCallCount);
    }

    [Fact]
    public async Task A_missing_display_name_is_refused()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(
            new RegisterUserCommand("hoc.vien@example.com", "mot-mat-khau-du-dai-2026", "  "),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error.Code);
    }
}
