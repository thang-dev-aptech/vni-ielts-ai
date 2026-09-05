using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Infrastructure.Persistence.Identity;

internal sealed class MongoUserRepository(MongoContext ctx) : IUserRepository
{
    public async Task<User?> FindByIdAsync(UserId id, CancellationToken ct)
    {
        var doc = await ctx.Users.Find(u => u.Id == id.Value).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<User?> FindByEmailAsync(Email email, CancellationToken ct)
    {
        var doc = await ctx.Users.Find(u => u.Email == email.Value).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<bool> EmailExistsAsync(Email email, CancellationToken ct) =>
        await ctx.Users.Find(u => u.Email == email.Value).AnyAsync(ct);

    /// <summary>
    /// A page of accounts for the CMS.
    ///
    /// Paged from the start rather than "list them all and filter in the UI":
    /// the second works on the fifty accounts a dev database holds and falls
    /// over in the first real week.
    /// </summary>
    public async Task<(IReadOnlyList<User> Users, long Total)> ListAsync(
        string? search, int skip, int take, CancellationToken ct)
    {
        var filter = Builders<UserDocument>.Filter.Empty;

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Escaped before it reaches a regex. An unescaped search box hands
            // the database a pattern chosen by the caller, and `(a+)+$` is the
            // classic way to hang one.
            var pattern = System.Text.RegularExpressions.Regex.Escape(search.Trim());

            filter = Builders<UserDocument>.Filter.Or(
                Builders<UserDocument>.Filter.Regex(
                    u => u.Email, new BsonRegularExpression(pattern, "i")),
                Builders<UserDocument>.Filter.Regex(
                    u => u.DisplayName, new BsonRegularExpression(pattern, "i")));
        }

        var total = await ctx.Users.CountDocumentsAsync(filter, cancellationToken: ct);

        var documents = await ctx.Users
            .Find(filter)
            .SortByDescending(u => u.CreatedAt)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(ct);

        return ([.. documents.Select(d => d.ToDomain())], total);
    }

    /// <summary>
    /// Inserts, translating a unique-index violation into a domain-meaningful
    /// exception.
    ///
    /// The check in RegisterUser is a courtesy that produces a clean message;
    /// the unique index is the guarantee. Two registrations arriving together
    /// both pass the check, and one insert loses — without this translation
    /// that surfaced as an unhandled 500 rather than the 409 the caller
    /// already knows how to render.
    /// </summary>
    public async Task AddAsync(User user, CancellationToken ct)
    {
        try
        {
            await ctx.Users.InsertOneAsync(user.ToDocument(), cancellationToken: ct);
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new DuplicateEmailException(user.Email.Value, e);
        }
    }

    public Task SaveAsync(User user, CancellationToken ct) =>
        ctx.Users.ReplaceOneAsync(
            u => u.Id == user.Id.Value,
            user.ToDocument(),
            new ReplaceOptions { IsUpsert = false },
            ct);
}

internal sealed class MongoUserIdentityRepository(MongoContext ctx) : IUserIdentityRepository
{
    public async Task<UserIdentity?> FindByProviderAsync(
        IdentityProvider provider, string providerUserId, CancellationToken ct)
    {
        var name = provider.ToString();
        var doc = await ctx.UserIdentities
            .Find(i => i.Provider == name && i.ProviderUserId == providerUserId)
            .FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<IReadOnlyList<UserIdentity>> ListForUserAsync(UserId userId, CancellationToken ct)
    {
        var docs = await ctx.UserIdentities.Find(i => i.UserId == userId.Value).ToListAsync(ct);
        return [.. docs.Select(d => d.ToDomain())];
    }

    public async Task AddAsync(UserIdentity identity, CancellationToken ct)
    {
        try
        {
            await ctx.UserIdentities.InsertOneAsync(identity.ToDocument(), cancellationToken: ct);
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // ux_identities_provider_subject. The unique index is what actually
            // enforces one identity per provider account; this turns losing the
            // race into something the use case can handle.
            throw new DuplicateIdentityException(
                identity.Provider.ToString(), identity.ProviderUserId, e);
        }
    }

    public Task SaveAsync(UserIdentity identity, CancellationToken ct) =>
        ctx.UserIdentities.ReplaceOneAsync(
            i => i.Id == identity.Id.Value, identity.ToDocument(), cancellationToken: ct);
}

internal sealed class MongoRoleRepository(MongoContext ctx) : IRoleRepository
{
    public async Task<Role?> FindByIdAsync(RoleId id, CancellationToken ct)
    {
        var doc = await ctx.Roles.Find(r => r.Id == id.Value).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<Role?> FindByNameAsync(string name, CancellationToken ct)
    {
        var doc = await ctx.Roles.Find(r => r.Name == name).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct)
    {
        var docs = await ctx.Roles.Find(FilterDefinition<RoleDocument>.Empty).ToListAsync(ct);
        return [.. docs.Select(d => d.ToDomain())];
    }

    public Task AddAsync(Role role, CancellationToken ct) =>
        ctx.Roles.InsertOneAsync(role.ToDocument(), cancellationToken: ct);
}

/// <summary>
/// Unions the permissions of every role a user holds.
///
/// Deliberately <b>not</b> a <c>$lookup</c>. A join in a repository is a
/// relational query wearing a disguise and will not survive the move to
/// PostgreSQL cleanly — rule 1 of the Mongo usage rules. Roles number in the
/// single digits, so one extra round trip fetching them all is cheaper than
/// the coupling.
/// </summary>
internal sealed class MongoPermissionResolver(MongoContext ctx) : IPermissionResolver
{
    public async Task<IReadOnlyCollection<string>> ResolveAsync(User user, CancellationToken ct)
    {
        if (user.RoleIds.Count == 0)
            return [];

        var ids = user.RoleIds.Select(r => r.Value).ToArray();
        var docs = await ctx.Roles.Find(r => ids.Contains(r.Id)).ToListAsync(ct);

        return [.. docs.SelectMany(d => d.Permissions).Distinct(StringComparer.Ordinal)];
    }
}
