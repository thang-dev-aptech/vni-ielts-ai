using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Infrastructure.Security.Sso;

/// <summary>
/// In-flight authorization requests, stored hashed with a TTL.
///
/// <para>
/// Hashed for the same reason verification and refresh tokens are: a database
/// dump must not yield usable values. Here it protects the PKCE verifier in
/// particular — someone holding a verifier and an intercepted code can
/// complete the exchange, which is the whole attack PKCE exists to stop.
/// </para>
///
/// <para>
/// Mongo's TTL monitor runs about once a minute, so expiry is enforced by the
/// query as well as by the index. The index is for cleanup; the
/// <c>expiresAt</c> filter is the actual rule.
/// </para>
/// </summary>
internal sealed class MongoSsoStateStore(IMongoDatabase db, IClock clock) : ISsoStateStore
{
    public const string CollectionName = "sso_states";

    private IMongoCollection<BsonDocument> States => db.GetCollection<BsonDocument>(CollectionName);

    public async Task StoreAsync(SsoState state, CancellationToken ct) =>
        await States.InsertOneAsync(new BsonDocument
        {
            ["_id"] = SecretHash.Of(state.State),
            ["provider"] = state.Provider.ToString(),
            ["codeVerifier"] = state.CodeVerifier,
            ["nonce"] = state.Nonce,
            ["returnTo"] = state.ReturnTo is null ? BsonNull.Value : state.ReturnTo,
            ["createdAt"] = clock.UtcNow.UtcDateTime,
            ["expiresAt"] = state.ExpiresAt.UtcDateTime,
        }, cancellationToken: ct);

    public async Task<SsoState?> ConsumeAsync(string state, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state))
            return null;

        // Find-and-delete in one operation. A read followed by a delete lets
        // two concurrent callbacks both succeed on one state, which is the
        // replay this value exists to prevent.
        var claimed = await States.FindOneAndDeleteAsync(
            Builders<BsonDocument>.Filter.Eq("_id", SecretHash.Of(state)),
            cancellationToken: ct);

        if (claimed is null)
            return null;

        return new SsoState(
            state,
            Enum.Parse<IdentityProvider>(claimed["provider"].AsString),
            claimed["codeVerifier"].AsString,
            claimed["nonce"].AsString,
            claimed["returnTo"].IsBsonNull ? null : claimed["returnTo"].AsString,
            new DateTimeOffset(
                DateTime.SpecifyKind(claimed["expiresAt"].ToUniversalTime(), DateTimeKind.Utc)));
    }

    public static async Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken ct) =>
        await database.GetCollection<BsonDocument>(CollectionName).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"),
                new CreateIndexOptions { Name = "ttl_sso_state", ExpireAfter = TimeSpan.Zero }),
            cancellationToken: ct);
}

/// <summary>
/// The one-time code the callback hands back to the client.
///
/// <para>
/// Sixty seconds, single use, stored hashed, and opaque rather than signed.
/// A signed token here would be usable by anyone who read it out of the
/// redirect until it expired, with no way to revoke it. → ADR-0014
/// </para>
/// </summary>
internal sealed class MongoHandoffCodeStore(IMongoDatabase db, IClock clock) : IHandoffCodeStore
{
    public const string CollectionName = "sso_handoff_codes";

    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private IMongoCollection<BsonDocument> Codes => db.GetCollection<BsonDocument>(CollectionName);

    public async Task<string> IssueAsync(UserId userId, CancellationToken ct)
    {
        // 256 bits from a CSPRNG, so there is nothing to guess and no
        // dictionary to defend against — SHA-256 at rest is the right cost,
        // not Argon2id.
        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        await Codes.InsertOneAsync(new BsonDocument
        {
            ["_id"] = SecretHash.Of(code),
            ["userId"] = userId.Value,
            ["createdAt"] = clock.UtcNow.UtcDateTime,
            ["expiresAt"] = clock.UtcNow.Add(Lifetime).UtcDateTime,
        }, cancellationToken: ct);

        return code;
    }

    public async Task<UserId?> ConsumeAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var claimed = await Codes.FindOneAndDeleteAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", SecretHash.Of(code)),
                Builders<BsonDocument>.Filter.Gt("expiresAt", clock.UtcNow.UtcDateTime)),
            cancellationToken: ct);

        return claimed is null ? null : new UserId(claimed["userId"].AsString);
    }

    public static async Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken ct) =>
        await database.GetCollection<BsonDocument>(CollectionName).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"),
                new CreateIndexOptions { Name = "ttl_sso_handoff", ExpireAfter = TimeSpan.Zero }),
            cancellationToken: ct);
}

internal static class SecretHash
{
    public static string Of(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
