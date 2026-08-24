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

    public static async Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken ct) =>
        await database.GetCollection<BsonDocument>(CollectionName).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"),
                new CreateIndexOptions { Name = "ttl_verification", ExpireAfter = TimeSpan.Zero }),
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
    public Task SendAsync(Vni.Ielts.Domain.Identity.Email address, string token, CancellationToken ct)
    {
        logger.LogWarning(
            "DEV ONLY — no email was sent. Verification token for {Address}: {Token}",
            address.Value, token);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(
        Vni.Ielts.Domain.Identity.Email address, string token, CancellationToken ct)
    {
        logger.LogWarning(
            "DEV ONLY — no email was sent. Password reset token for {Address}: {Token}",
            address.Value, token);
        return Task.CompletedTask;
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
