using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The six-digit code, against a real database.
///
/// <b>`[QUYẾT ĐỊNH]` chủ sản phẩm, 28/08/2026: xác minh bằng mã 6 số.</b> A
/// million combinations is not much on its own — what makes it safe is that the
/// redemption is authenticated, so the attempt count is per account and the
/// code dies on the fifth wrong answer.
///
/// <b>Every rule below is one an in-memory dictionary gets right for free and a
/// database does not.</b> An atomic increment that two concurrent guesses
/// cannot both slip past, a replace that leaves one live code rather than two,
/// a TTL. A fake under a lock would agree with the bug for as long as it
/// existed — and the bug this mechanism can have is an attempt cap that counts
/// nothing.
/// </summary>
public sealed class VerificationCodeTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private IServiceScope Scope() => app.Services.CreateScope();

    private static IEmailVerificationTokens TokensIn(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IEmailVerificationTokens>();

    [SkippableFact]
    public async Task The_code_that_was_sent_verifies_the_account()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var tokens = TokensIn(scope);
        var user = UserId.New();

        var code = await tokens.IssueCodeAsync(user, default);

        Assert.Equal(6, code.Length);
        Assert.True(code.All(char.IsAsciiDigit), $"'{code}' is not six digits.");

        Assert.Equal(CodeRedemption.Verified, await tokens.RedeemCodeAsync(user, code, default));
    }

    [SkippableFact]
    public async Task A_code_is_single_use()
    {
        // Otherwise a code read over somebody's shoulder, or left in a mailbox
        // somebody else can reach, stays usable for its whole lifetime.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var tokens = TokensIn(scope);
        var user = UserId.New();

        var code = await tokens.IssueCodeAsync(user, default);

        Assert.Equal(CodeRedemption.Verified, await tokens.RedeemCodeAsync(user, code, default));
        Assert.Equal(CodeRedemption.Expired, await tokens.RedeemCodeAsync(user, code, default));
    }

    [SkippableFact]
    public async Task Pressing_resend_leaves_one_live_code_rather_than_two()
    {
        /*
         * <b>Replacing, not adding.</b> A learner who presses "gửi lại" three
         * times must not end up with three live codes — that multiplies the
         * guessing surface by three for a convenience nobody asked for.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var tokens = TokensIn(scope);
        var user = UserId.New();

        var first = await tokens.IssueCodeAsync(user, default);
        var second = await tokens.IssueCodeAsync(user, default);

        Skip.If(first == second, "The two codes collided by chance; nothing to tell apart.");

        Assert.Equal(CodeRedemption.Incorrect, await tokens.RedeemCodeAsync(user, first, default));
        Assert.Equal(CodeRedemption.Verified, await tokens.RedeemCodeAsync(user, second, default));
    }

    [SkippableFact]
    public async Task Five_wrong_guesses_kill_the_code()
    {
        /*
         * <b>This is the whole reason six digits is safe here.</b> A million
         * combinations falls to a script in seconds if guesses are free. The
         * cap is what turns it into five chances out of a million, after which
         * the attacker has to trigger a fresh email to the address they are
         * trying to steal — which is rate-limited and visible to its owner.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var tokens = TokensIn(scope);
        var user = UserId.New();

        var code = await tokens.IssueCodeAsync(user, default);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(
                CodeRedemption.Incorrect,
                await tokens.RedeemCodeAsync(user, wrong, default));
        }

        Assert.Equal(
            CodeRedemption.TooManyAttempts, await tokens.RedeemCodeAsync(user, wrong, default));

        // And the real code no longer works either. A cap that let the correct
        // answer through afterwards would cap nothing.
        Assert.Equal(
            CodeRedemption.TooManyAttempts, await tokens.RedeemCodeAsync(user, code, default));
    }

    [SkippableFact]
    public async Task Concurrent_guesses_cannot_slip_past_the_attempt_cap()
    {
        /*
         * <b>The cap is only a cap if the increment is atomic.</b> Reading the
         * document, comparing, and then incrementing is three statements, and
         * an attacker who can make requests concurrently fits as many guesses
         * as they like between the first and the third.
         *
         * Twenty guesses at once against a cap of five: at most five may be
         * counted as ordinary wrong answers, and everything after that has to
         * be refused as out of attempts.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var tokens = TokensIn(scope);
        var user = UserId.New();

        var code = await tokens.IssueCodeAsync(user, default);
        var wrong = code == "000000" ? "111111" : "000000";

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<CodeRedemption> GuessAsync()
        {
            await gate.Task;
            return await tokens.RedeemCodeAsync(user, wrong, default);
        }

        var guesses = Enumerable.Range(0, 20).Select(_ => GuessAsync()).ToArray();
        gate.SetResult();

        var outcomes = await Task.WhenAll(guesses);

        Assert.True(
            outcomes.Count(o => o == CodeRedemption.Incorrect) <= 5,
            $"{outcomes.Count(o => o == CodeRedemption.Incorrect)} guesses were counted as "
            + "ordinary wrong answers against a cap of five. The increment is not atomic, so "
            + "the cap counts nothing under concurrency — which is the only way it is attacked.");
    }

    [SkippableFact]
    public async Task One_accounts_code_does_not_verify_another()
    {
        /*
         * <b>The code is looked up by account, never the account by code.</b>
         * Finding an account <i>from</i> six digits would mean one guess could
         * match somebody else's — which is exactly why a bare code is normally
         * unsafe, and exactly what this design avoids by being authenticated.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var tokens = TokensIn(scope);

        var mine = UserId.New();
        var theirs = UserId.New();

        var theirCode = await tokens.IssueCodeAsync(theirs, default);
        await tokens.IssueCodeAsync(mine, default);

        // Their code, presented on my account. There is no path by which it
        // could work, and that is the property.
        var outcome = await tokens.RedeemCodeAsync(mine, theirCode, default);

        Assert.NotEqual(CodeRedemption.Verified, outcome);
    }

    [SkippableFact]
    public async Task The_plaintext_code_is_never_stored()
    {
        // Same rule as every other secret here: a database dump must not hand
        // out working codes.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var tokens = TokensIn(scope);
        var user = UserId.New();

        var code = await tokens.IssueCodeAsync(user, default);

        var stored = await app.Services.GetRequiredService<IMongoDatabase>()
            .GetCollection<BsonDocument>("email_verification_codes")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", user.Value))
            .FirstOrDefaultAsync();

        Assert.NotNull(stored);
        Assert.DoesNotContain(code, stored!.ToJson(), StringComparison.Ordinal);
    }
}
