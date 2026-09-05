using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Infrastructure.Persistence;

namespace Vni.Ielts.Infrastructure.Security;

/// <summary>
/// A consecutive-failure counter per address, in Mongo.
///
/// <b>One document per address, and it expires itself.</b> A TTL index removes
/// the record once its window has passed, so nothing accumulates and no job
/// has to sweep. This is the opposite call from the audit log, which
/// deliberately has no TTL — a failed sign-in from four months ago is not
/// evidence of anything, and keeping every address anyone ever typed at the
/// login form is a personal-data liability with no purpose. → PDPL,
/// storage limitation
///
/// <b>The counter is not the security boundary.</b> A determined attacker with
/// many addresses still gets <see cref="MaxFailures"/> guesses each; this
/// turns unlimited guessing into bounded guessing, which is what makes a
/// password policy meaningful. It does not replace one.
/// </summary>
public sealed class MongoLoginThrottle(MongoContext context, IClock clock) : ILoginThrottle
{
    /// <summary>
    /// Consecutive failures before the address is locked.
    ///
    /// Ten, not three. A learner typing on a phone keyboard with a password
    /// manager fighting them genuinely gets it wrong several times in a row,
    /// and a lockout that fires on ordinary human clumsiness produces support
    /// tickets rather than security. Ten guesses against a password that meets
    /// the policy is not a meaningful attack.
    /// </summary>
    private const int MaxFailures = 10;

    /// <summary>How long a locked address stays locked, and the counting window.</summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

    private IMongoCollection<BsonDocument> Attempts =>
        context.Database.GetCollection<BsonDocument>("login_attempts");

    public async Task<bool> IsLockedAsync(string email, CancellationToken ct)
    {
        var doc = await Attempts
            .Find(Builders<BsonDocument>.Filter.Eq("_id", Key(email)))
            .FirstOrDefaultAsync(ct);

        if (doc is null) return false;

        // Read the count rather than a stored flag: the TTL index removes the
        // document eventually, but "eventually" is up to a minute in Mongo, and
        // a lock that outlives its cooldown by a minute is a support ticket.
        return doc.GetValue("failures", 0).ToInt32() >= MaxFailures
            && doc.GetValue("expiresAt", BsonNull.Value) is BsonDateTime expiry
            && expiry.ToUniversalTime() > clock.UtcNow.UtcDateTime;
    }

    public Task RecordFailureAsync(string email, CancellationToken ct) =>
        Attempts.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", Key(email)),
            Builders<BsonDocument>.Update
                .Inc("failures", 1)
                // Every failure pushes the window out. Consecutive failures are
                // what matters; a guess an hour after the last one is not part
                // of the same burst, and the TTL will have removed the record.
                .Set("expiresAt", clock.UtcNow.Add(Cooldown).UtcDateTime),
            new UpdateOptions { IsUpsert = true },
            ct);

    public Task ClearAsync(string email, CancellationToken ct) =>
        Attempts.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", Key(email)), ct);

    /// <summary>
    /// The address, lower-cased and trimmed.
    ///
    /// Not hashed. It is short-lived by construction and an operator debugging
    /// a lockout needs to be able to find it; hashing here would buy privacy
    /// that the TTL already provides and cost the only thing the record is for.
    /// </summary>
    private static string Key(string email) => email.Trim().ToLowerInvariant();

    internal static async Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken ct)
    {
        var attempts = database.GetCollection<BsonDocument>("login_attempts");

        await attempts.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"),
                new CreateIndexOptions
                {
                    Name = "ix_login_attempts_ttl",
                    ExpireAfter = TimeSpan.Zero,
                }),
            cancellationToken: ct);
    }
}
