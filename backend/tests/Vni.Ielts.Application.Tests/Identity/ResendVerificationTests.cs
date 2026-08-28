using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Identity;

/// <summary>
/// "Send it again" from the profile page.
///
/// <para>
/// This is the action the owner's decision leans on: if signing in no longer
/// waits for verification, the profile is the only place a learner can come
/// back and finish it. So the button has to work, and — while the only
/// configured sender writes the link to a log — it has to be able to say that
/// nothing was actually sent.
/// </para>
/// </summary>
public sealed class ResendVerificationTests
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

            // Numbered like the link tokens above, so a test asserting "each
            // press issues a fresh one" still measures that. A fixed value
            // would make the assertion pass while the property vanished.
            OutstandingCode = $"verify-{Issued.Count}";
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
        public FakeVerificationTokens Tokens { get; } = new();
        public FakeVerificationMessageSender Sender { get; } = new();

        public ResendVerification Sut => new(Users, Tokens, Sender);

        public async Task<User> SeedAsync(bool verified = false, bool suspended = false)
        {
            var user = User.Register(Email.Create("hoc.vien@example.com"), "Học viên", Now);
            if (verified) user.MarkEmailVerified();
            if (suspended) user.Suspend();

            await Users.AddAsync(user, default);
            return user;
        }
    }

    [Fact]
    public async Task An_unverified_account_gets_a_fresh_token_at_its_own_address()
    {
        var h = new Harness();
        var user = await h.SeedAsync();

        var result = await h.Sut.HandleAsync(new ResendVerificationCommand(user.Id), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyVerified);
        Assert.Equal([user.Id], h.Tokens.Issued);
        Assert.Equal([("hoc.vien@example.com", "verify-1")], h.Sender.Verifications);
    }

    [Fact]
    public async Task Each_press_issues_a_new_token_rather_than_reusing_one()
    {
        // Verification tokens are single-use and time-bounded. Handing back the
        // one already issued would mean a learner who let the first link expire
        // could never get a working one.
        var h = new Harness();
        var user = await h.SeedAsync();

        await h.Sut.HandleAsync(new ResendVerificationCommand(user.Id), default);
        await h.Sut.HandleAsync(new ResendVerificationCommand(user.Id), default);

        Assert.Equal(["verify-1", "verify-2"], h.Sender.Verifications.Select(v => v.Token));
    }

    [Fact]
    public async Task It_says_when_nothing_was_actually_sent()
    {
        // The whole point. A profile screen showing "đã gửi email" over a
        // development sender sends the learner to look in an empty mailbox and
        // conclude the product is broken.
        var h = new Harness();
        h.Sender.Delivery = MessageDelivery.NotSent;
        var user = await h.SeedAsync();

        var result = await h.Sut.HandleAsync(new ResendVerificationCommand(user.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(MessageDelivery.NotSent, result.Value!.VerificationMessage);
    }

    [Fact]
    public async Task An_already_verified_account_succeeds_and_sends_nothing()
    {
        // Someone who pressed it twice, or verified in another tab, has nothing
        // to fix. An error would send them looking for a problem that is not
        // there.
        var h = new Harness();
        var user = await h.SeedAsync(verified: true);

        var result = await h.Sut.HandleAsync(new ResendVerificationCommand(user.Id), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyVerified);
        Assert.Empty(h.Sender.Verifications);
        Assert.Empty(h.Tokens.Issued);
    }

    [Fact]
    public async Task A_suspended_account_gets_no_mail()
    {
        var h = new Harness();
        var user = await h.SeedAsync(suspended: true);

        var result = await h.Sut.HandleAsync(new ResendVerificationCommand(user.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AccountSuspended, result.Error.Code);
        Assert.Empty(h.Sender.Verifications);
    }

    [Fact]
    public async Task An_unknown_account_is_a_not_found()
    {
        var h = new Harness();

        var result = await h.Sut.HandleAsync(
            new ResendVerificationCommand(UserId.New()), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.NotFound, result.Error.Code);
    }
}
