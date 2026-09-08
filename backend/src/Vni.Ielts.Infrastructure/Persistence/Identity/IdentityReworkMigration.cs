using MongoDB.Bson;
using MongoDB.Driver;

namespace Vni.Ielts.Infrastructure.Persistence.Identity;

/// <summary>
/// Moves an existing database onto the 08/09/2026 identity model.
///
/// <para>
/// <b>Runs before <see cref="MongoContext.EnsureIndexesAsync"/>, not inside
/// it.</b> Two of the steps here exist purely so index creation can succeed:
/// the old address index has options the new one cannot be layered on top of,
/// and the new phone index cannot be built at all while duplicate numbers sit
/// in the collection. Creating indexes first turns both into a raw driver
/// error naming an index, at boot, with nothing an operator can act on.
/// </para>
///
/// <para>
/// <b>Idempotent, and safe to run concurrently.</b> Several API instances start
/// together and all of them will run this. Every step is either a no-op the
/// second time or tolerates the error that means another instance did it first.
/// </para>
///
/// <para>
/// This is not a formality: this database carries real accounts and real
/// marked essays.
/// </para>
/// </summary>
internal static class IdentityReworkMigration
{
    /// <summary>Mongo's "index already exists with different options".</summary>
    private const int IndexOptionsConflict = 85;

    /// <summary>Mongo's "an index with this name already exists".</summary>
    private const int IndexKeySpecsConflict = 86;

    private const int IndexNotFound = 27;
    private const int NamespaceNotFound = 26;

    public static async Task RunAsync(IMongoDatabase database, CancellationToken ct)
    {
        var users = database.GetCollection<BsonDocument>("users");
        var identities = database.GetCollection<BsonDocument>("user_identities");

        await RefuseDuplicatePhonesAsync(users, ct);
        await DropLegacyEmailIndexAsync(users, ct);
        await RekeyPasswordIdentitiesAsync(identities, ct);
        await DropVerifiedFlagAsync(users, ct);
        await DropVerificationCollectionsAsync(database, ct);
    }

    /// <summary>
    /// Refuses to start while two accounts share a number, and says which.
    ///
    /// <para>
    /// <b>Likely, not hypothetical.</b> Until now the number was contact
    /// information: <c>SetPhone</c> never checked it against anything, so any
    /// database that has been used has probably collected a collision. It is
    /// about to become the handle people sign in with, and the unique index
    /// will refuse to build.
    /// </para>
    ///
    /// <para>
    /// The numbers are named because that is the only form of this message an
    /// operator can act on — "E11000 duplicate key on ux_users_phone" tells
    /// them a thing failed, not which two accounts to go and fix.
    /// </para>
    /// </summary>
    private static async Task RefuseDuplicatePhonesAsync(
        IMongoCollection<BsonDocument> users, CancellationToken ct)
    {
        var duplicates = await users.Aggregate<BsonDocument>(
            new[]
            {
                new BsonDocument("$match",
                    new BsonDocument("phone", new BsonDocument("$type", "string"))),
                new BsonDocument("$group", new BsonDocument
                {
                    ["_id"] = "$phone",
                    ["count"] = new BsonDocument("$sum", 1),
                    ["users"] = new BsonDocument("$push", "$_id"),
                }),
                new BsonDocument("$match",
                    new BsonDocument("count", new BsonDocument("$gt", 1))),
                new BsonDocument("$limit", 20),
            },
            cancellationToken: ct).ToListAsync(ct);

        if (duplicates.Count == 0) return;

        var described = string.Join(
            "; ",
            duplicates.Select(d =>
                $"{d["_id"].AsString} → {string.Join(", ", d["users"].AsBsonArray.Select(u => u.AsString))}"));

        throw new InvalidOperationException(
            "The phone number is now a unique sign-in handle, and this database already has "
            + $"accounts sharing one. Resolve these before starting: {described}. "
            + "→ IdentityReworkMigration");
    }

    /// <summary>
    /// Removes the old address index so the partial one can be created.
    ///
    /// <para>
    /// <b>A unique index cannot be altered in place.</b> Creating one with the
    /// same name and different options is <c>IndexOptionsConflict</c>, and
    /// <c>collMod</c> only reaches <c>expireAfterSeconds</c> and <c>hidden</c>.
    /// Without this the whole fleet fails to boot, not just one instance.
    /// </para>
    ///
    /// <para>
    /// Both "already gone" and "another instance is doing this right now" are
    /// success. The second matters because every instance runs this at once.
    /// </para>
    /// </summary>
    private static async Task DropLegacyEmailIndexAsync(
        IMongoCollection<BsonDocument> users, CancellationToken ct)
    {
        var existing = await ListIndexesAsync(users, ct);

        var current = existing.FirstOrDefault(
            i => i.GetValue("name", BsonString.Empty).AsString == MongoContext.EmailIndexName);

        // Already the partial one, so this has run before.
        if (current is null || current.Contains("partialFilterExpression")) return;

        try
        {
            await users.Indexes.DropOneAsync(MongoContext.EmailIndexName, ct);
        }
        catch (MongoCommandException e)
            when (e.Code is IndexNotFound or NamespaceNotFound)
        {
            // Another instance won the race, or the collection does not exist
            // yet on a fresh database. Both are the state we wanted.
        }
    }

    /// <summary>
    /// Rewrites password rows from being keyed by address to being keyed by
    /// account.
    ///
    /// <para>
    /// Rows are updated one at a time because the new subject is derived from
    /// each row's own <c>userId</c>, and <c>$set</c> cannot reference another
    /// field. The collection holds one row per login method per account, so
    /// this is small.
    /// </para>
    ///
    /// <para>
    /// A row whose account already has a rewritten password row is deleted
    /// rather than rewritten: the unique index on (provider, subject) would
    /// refuse the duplicate, and the surviving row carries the same hash.
    /// </para>
    /// </summary>
    private static async Task RekeyPasswordIdentitiesAsync(
        IMongoCollection<BsonDocument> identities, CancellationToken ct)
    {
        var legacy = await identities
            .Find(Builders<BsonDocument>.Filter.Eq("provider", "Email"))
            .ToListAsync(ct);

        foreach (var row in legacy)
        {
            var userId = row.GetValue("userId", BsonNull.Value);
            if (userId.IsBsonNull) continue;

            var alreadyRekeyed = await identities
                .Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("provider", "Password"),
                    Builders<BsonDocument>.Filter.Eq("providerUserId", userId)))
                .AnyAsync(ct);

            if (alreadyRekeyed)
            {
                await identities.DeleteOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", row["_id"]), ct);
                continue;
            }

            await identities.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", row["_id"]),
                Builders<BsonDocument>.Update
                    .Set("provider", "Password")
                    .Set("providerUserId", userId),
                cancellationToken: ct);
        }
    }

    /// <summary>
    /// Removes the verified flag. Nothing reads it any more, and a field that
    /// still says "false" invites somebody to wire a gate back onto it.
    /// </summary>
    private static Task DropVerifiedFlagAsync(
        IMongoCollection<BsonDocument> users, CancellationToken ct) =>
        users.UpdateManyAsync(
            Builders<BsonDocument>.Filter.Exists("emailVerified"),
            Builders<BsonDocument>.Update.Unset("emailVerified"),
            cancellationToken: ct);

    /// <summary>
    /// Drops the three stores that backed verification and password reset.
    ///
    /// <para>
    /// Their <c>EnsureIndexesAsync</c> calls were removed from startup in the
    /// same change. Leaving those in place would have recreated all three,
    /// empty, on the very next boot — which looks exactly like the migration
    /// not having run.
    /// </para>
    /// </summary>
    private static async Task DropVerificationCollectionsAsync(
        IMongoDatabase database, CancellationToken ct)
    {
        foreach (var name in new[]
                 {
                     "email_verification_tokens",
                     "email_verification_codes",
                     "password_reset_tokens",
                 })
        {
            try
            {
                await database.DropCollectionAsync(name, ct);
            }
            catch (MongoCommandException e) when (e.Code is NamespaceNotFound)
            {
                // Already gone.
            }
        }
    }

    private static async Task<List<BsonDocument>> ListIndexesAsync(
        IMongoCollection<BsonDocument> collection, CancellationToken ct)
    {
        try
        {
            return await (await collection.Indexes.ListAsync(ct)).ToListAsync(ct);
        }
        catch (MongoCommandException e) when (e.Code is NamespaceNotFound)
        {
            return [];
        }
    }
}
