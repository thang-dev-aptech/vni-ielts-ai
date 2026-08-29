using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Infrastructure.Security;

/// <summary>
/// Verification tokens, stored hashed with a TTL.
///
/// Same reasoning as refresh tokens: 256 bits from a CSPRNG needs SHA-256, not
/// Argon2id — there is no dictionary to defend against, and a slow hash would
/// only add latency. Storing the plaintext would mean a database dump hands out
/// working verification links.
/// </summary>
internal sealed class MongoEmailVerificationTokens(IMongoDatabase db, IClock clock)
    : IEmailVerificationTokens
{
    private const string CollectionName = "email_verification_tokens";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private IMongoCollection<BsonDocument> Tokens => db.GetCollection<BsonDocument>(CollectionName);

    public async Task<string> IssueAsync(UserId userId, CancellationToken ct)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));

        await Tokens.InsertOneAsync(new BsonDocument
        {
            ["_id"] = Hash(token),
            ["userId"] = userId.Value,
            ["createdAt"] = clock.UtcNow.UtcDateTime,
            ["expiresAt"] = clock.UtcNow.Add(Lifetime).UtcDateTime,
        }, cancellationToken: ct);

        return token;
    }

    public async Task<UserId?> RedeemAsync(string token, CancellationToken ct)
    {
        var hash = Hash(token);
        var now = clock.UtcNow.UtcDateTime;

        // Atomic find-and-delete. A read-then-delete would let two concurrent
        // redemptions both succeed — harmless for verification specifically,
        // but the same shape is not harmless elsewhere and the habit matters.
        var claimed = await Tokens.FindOneAndDeleteAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", hash),
                Builders<BsonDocument>.Filter.Gt("expiresAt", now)),
            cancellationToken: ct);

        return claimed is null ? null : new UserId(claimed["userId"].AsString);
    }

    // ── The six-digit code ────────────────────────────────────────────────

    private const string CodeCollectionName = "email_verification_codes";

    /// <summary>
    /// <b>Ten minutes, not twenty-four hours.</b>
    ///
    /// A link sits in a mailbox until somebody gets round to it; a code is read
    /// off the screen and typed within a minute. The window only has to cover
    /// "the mail took a while to arrive" — and every minute past that is a
    /// minute in which a million-combination secret is live.
    /// </summary>
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// <b>Five, and this is what makes six digits safe.</b>
    ///
    /// A million combinations is not much on its own. What bounds it is that
    /// the redemption is authenticated — the server knows which account is
    /// guessing — so the count is per account and the code dies on the fifth
    /// wrong answer. An attacker gets five guesses out of a million and then
    /// has to trigger a new email to the address they are trying to steal,
    /// which is both rate-limited and visible to its owner.
    ///
    /// Five rather than three because a learner mistyping is the common case
    /// and a dead code costs them a round trip through their inbox.
    /// </summary>
    private const int MaxAttempts = 5;

    private IMongoCollection<BsonDocument> Codes =>
        db.GetCollection<BsonDocument>(CodeCollectionName);

    public async Task<string> IssueCodeAsync(UserId userId, CancellationToken ct)
    {
        /*
         * <b>`RandomNumberGenerator`, not `Random`.</b> A predictable six-digit
         * code is not a secret at all, and `Random` seeded from the clock is
         * predictable to anybody who knows roughly when the mail was sent.
         *
         * <b>Six digits including leading zeros.</b> `000123` is a valid code;
         * formatting it as `123` would silently shrink the space and make a
         * whole class of codes impossible to type back.
         */
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var now = clock.UtcNow.UtcDateTime;

        /*
         * <b>Replace, never add.</b> A learner who presses "gửi lại" three
         * times must not end up with three live codes — that multiplies the
         * guessing surface for a convenience nobody asked for. The document is
         * keyed by user, so the newest code is the only one that exists.
         */
        await Codes.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", userId.Value),
            new BsonDocument
            {
                ["_id"] = userId.Value,
                ["codeHash"] = Hash(code),
                ["attempts"] = 0,
                ["createdAt"] = now,
                ["expiresAt"] = now.Add(CodeLifetime),
            },
            new ReplaceOptions { IsUpsert = true },
            ct);

        return code;
    }

    public async Task<CodeRedemption> RedeemCodeAsync(
        UserId userId, string code, CancellationToken ct)
    {
        var now = clock.UtcNow.UtcDateTime;

        /*
         * <b>One statement that both counts the attempt and reads the code.</b>
         *
         * Reading the document, comparing, and then incrementing is three, and
         * an attacker who can make requests concurrently fits as many guesses
         * as they like between the first and the third — which is the attempt
         * cap doing nothing at all. The increment happens first, on the way in,
         * so every guess is paid for whether or not it turns out to be right.
         */
        var claimed = await Codes.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", userId.Value),
                Builders<BsonDocument>.Filter.Gt("expiresAt", now),
                Builders<BsonDocument>.Filter.Lt("attempts", MaxAttempts)),
            Builders<BsonDocument>.Update.Inc("attempts", 1),
            new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After },
            ct);

        if (claimed is null)
        {
            /*
             * Nothing outstanding, expired, or out of attempts. Told apart by a
             * second read, because the learner's next move differs: look at the
             * code again, or press "gửi lại".
             */
            var held = await Codes
                .Find(Builders<BsonDocument>.Filter.Eq("_id", userId.Value))
                .FirstOrDefaultAsync(ct);

            return held is not null && held.GetValue("attempts", 0).AsInt32 >= MaxAttempts
                ? CodeRedemption.TooManyAttempts
                : CodeRedemption.Expired;
        }

        /*
         * <b>A fixed-time comparison, even though the secret is six digits.</b>
         * The window a timing attack opens here is tiny and the cost of closing
         * it is one call — and the habit is what matters: the next thing
         * compared this way will not be six digits.
         */
        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(code)),
            Encoding.UTF8.GetBytes(claimed["codeHash"].AsString));

        if (!matches)
        {
            return claimed["attempts"].AsInt32 >= MaxAttempts
                ? CodeRedemption.TooManyAttempts
                : CodeRedemption.Incorrect;
        }

        // Single use. Deleting rather than marking used keeps the collection
        // bounded by outstanding codes rather than by every account that has
        // ever verified.
        await Codes.DeleteOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", userId.Value), ct);

        return CodeRedemption.Verified;
    }

    public static async Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken ct) =>
        await database.GetCollection<BsonDocument>(CollectionName).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"),
                new CreateIndexOptions { Name = "ttl_verification", ExpireAfter = TimeSpan.Zero }),
            cancellationToken: ct);

    /// <summary>
    /// <b>Codes expire themselves.</b> Without the TTL the collection keeps one
    /// document per account that ever asked for a code, for ever — and each one
    /// is a hash of a live-looking secret long after it stopped being one.
    /// </summary>
    public static async Task EnsureCodeIndexesAsync(IMongoDatabase database, CancellationToken ct) =>
        await database.GetCollection<BsonDocument>(CodeCollectionName).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"),
                new CreateIndexOptions
                {
                    Name = "ttl_verification_codes",
                    ExpireAfter = TimeSpan.Zero,
                }),
            cancellationToken: ct);

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>URL-safe, because this token travels in a link.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Development sender. Writes the link to the log instead of sending mail.
///
/// <b>Not a production implementation, and deliberately loud about it.</b>
/// Registering this outside Development would silently mean nobody ever
/// receives a verification email while the API reports success.
/// </summary>
internal sealed class LoggingVerificationMessageSender(
    ILogger<LoggingVerificationMessageSender> logger) : IVerificationMessageSender
{
    public Task<MessageDelivery> SendAsync(
        Vni.Ielts.Domain.Identity.Email address, string token, CancellationToken ct)
    {
        logger.LogWarning(
            "DEV ONLY — no verification email was sent. "
            + "The development sender does not write addresses or codes to logs.");

        // The whole reason the port returns this: the caller must be able to
        // tell a screen that nothing was sent, instead of the screen guessing
        // from a successful-looking response that a mail is on its way.
        return Task.FromResult(MessageDelivery.NotSent);
    }

    public Task<MessageDelivery> SendPasswordResetAsync(
        Vni.Ielts.Domain.Identity.Email address, string token, CancellationToken ct)
    {
        logger.LogWarning(
            "DEV ONLY — no password-reset email was sent. "
            + "The development sender does not write addresses or tokens to logs.");
        return Task.FromResult(MessageDelivery.NotSent);
    }
}

/// <summary>
/// Password-reset tokens, stored hashed with a one-hour TTL.
///
/// A near-copy of the verification store above, and deliberately not shared
/// with it: a token that proves an address is reachable and a token that hands
/// over an account should not be able to be confused for one another by a
/// mistake in a single filter. → threat T5
/// </summary>
internal sealed class MongoPasswordResetTokens(IMongoDatabase db, IClock clock) : IPasswordResetTokens
{
    private const string CollectionName = "password_reset_tokens";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private IMongoCollection<BsonDocument> Tokens => db.GetCollection<BsonDocument>(CollectionName);

    public async Task<string> IssueAsync(UserId userId, CancellationToken ct)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));

        await Tokens.InsertOneAsync(new BsonDocument
        {
            ["_id"] = Hash(token),
            ["userId"] = userId.Value,
            ["createdAt"] = clock.UtcNow.UtcDateTime,
            ["expiresAt"] = clock.UtcNow.Add(Lifetime).UtcDateTime,
        }, cancellationToken: ct);

        return token;
    }

    public async Task<UserId?> RedeemAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var claimed = await Tokens.FindOneAndDeleteAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", Hash(token)),
                Builders<BsonDocument>.Filter.Gt("expiresAt", clock.UtcNow.UtcDateTime)),
            cancellationToken: ct);

        return claimed is null ? null : new UserId(claimed["userId"].AsString);
    }

    public static async Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken ct) =>
        await database.GetCollection<BsonDocument>(CollectionName).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"),
                new CreateIndexOptions { Name = "ttl_password_reset", ExpireAfter = TimeSpan.Zero }),
            cancellationToken: ct);

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
