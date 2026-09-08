using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure.Persistence;

namespace Vni.Ielts.Infrastructure.Security;

/// <summary>
/// Development / fail-soft sender: logs the link and reports <see cref="MessageDelivery.NotSent"/>.
/// Production SMTP can replace this registration without changing Application.
/// </summary>
internal sealed class LoggingStaffMessageSender(ILogger<LoggingStaffMessageSender> log) : IVerificationMessageSender
{
    public Task<MessageDelivery> SendPasswordResetAsync(Email address, string token, CancellationToken ct)
    {
        log.LogInformation(
            "Staff password-reset token issued for {Email} (not delivered — logging sender).",
            address.Value);
        // Token deliberately omitted from the log: a dump of application logs
        // must not become a mailbox of live reset links.
        _ = token;
        return Task.FromResult(MessageDelivery.NotSent);
    }

    public Task<MessageDelivery> SendStaffInvitationAsync(Email address, string token, CancellationToken ct)
    {
        log.LogInformation(
            "Staff invitation issued for {Email} (not delivered — logging sender).",
            address.Value);
        _ = token;
        return Task.FromResult(MessageDelivery.NotSent);
    }
}

[BsonIgnoreExtraElements]
internal sealed class PasswordResetTokenDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("tokenHash")]
    public string TokenHash { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [BsonElement("usedAt")]
    [BsonIgnoreIfNull]
    public DateTime? UsedAt { get; set; }
}

/// <summary>One-hour, single-use password-reset tokens for CMS force-reset mail.</summary>
internal sealed class MongoPasswordResetTokens(MongoContext ctx) : IPasswordResetTokens
{
    private IMongoCollection<PasswordResetTokenDocument> Tokens =>
        ctx.Database.GetCollection<PasswordResetTokenDocument>("password_reset_tokens");

    public async Task<string> IssueAsync(UserId userId, CancellationToken ct)
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))
            .ToLowerInvariant();

        await Tokens.InsertOneAsync(new PasswordResetTokenDocument
        {
            Id = Guid.NewGuid().ToString("n"),
            TokenHash = hash,
            UserId = userId.Value,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        }, cancellationToken: ct);

        return raw;
    }

    public async Task<UserId?> RedeemAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)))
            .ToLowerInvariant();
        var now = DateTime.UtcNow;

        var updated = await Tokens.FindOneAndUpdateAsync(
            Builders<PasswordResetTokenDocument>.Filter.And(
                Builders<PasswordResetTokenDocument>.Filter.Eq(t => t.TokenHash, hash),
                Builders<PasswordResetTokenDocument>.Filter.Eq(t => t.UsedAt, null),
                Builders<PasswordResetTokenDocument>.Filter.Gt(t => t.ExpiresAt, now)),
            Builders<PasswordResetTokenDocument>.Update.Set(t => t.UsedAt, now),
            cancellationToken: ct);

        return updated is null ? null : new UserId(updated.UserId);
    }
}
