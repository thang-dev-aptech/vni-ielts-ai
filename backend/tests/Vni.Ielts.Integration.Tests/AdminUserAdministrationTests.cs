using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Api.Common;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// I2-10 Mongo concurrency for last-admin guards and staff invitations.
/// Adapted for main: phone registration + <c>identifier</c> login; list filter
/// is <c>hasEmail</c> (not feature <c>emailVerified</c>).
/// </summary>
public sealed class AdminUserAdministrationTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";
    private const string Password = "mot-mat-khau-du-dai-2026";

    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private IMongoDatabase Db() => new MongoClient(ConnectionString).GetDatabase(app.Database);

    private static string NewPhone() => $"09{Random.Shared.NextInt64(0, 100_000_000):D8}";

    private static async Task<(string Access, string UserId, string Phone)> RegisterAsync(
        HttpClient client, string? phone = null)
    {
        phone ??= NewPhone();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new { phone, password = Password, displayName = "Admin ops" }),
        };
        request.Headers.TryAddWithoutValidation(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var session = body.GetProperty("session");
        return (
            session.GetProperty("accessToken").GetString()!,
            session.GetProperty("userId").GetString()!,
            phone);
    }

    private static async Task<(string Access, string UserId)> LoginAsync(HttpClient client, string identifier)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { identifier, password = Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("userId").GetString()!);
    }

    private async Task<string> RoleIdAsync(string roleName)
    {
        var role = await Db().GetCollection<BsonDocument>("roles")
            .Find(Builders<BsonDocument>.Filter.Eq("name", roleName))
            .FirstOrDefaultAsync();
        Assert.NotNull(role);
        return role!["_id"].AsString;
    }

    private async Task GrantRoleDirectlyAsync(string userId, string roleName)
    {
        var roleId = await RoleIdAsync(roleName);
        await Db().GetCollection<BsonDocument>("users").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", userId),
            Builders<BsonDocument>.Update.AddToSet("roleIds", roleId));
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, string access, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
            request.Headers.Add(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
        }
        return request;
    }

    private async Task<(HttpClient Client, string Access, string UserId, string Phone)> RegisterAdminAsync()
    {
        var client = NewClient();
        var (_, userId, phone) = await RegisterAsync(client);
        await GrantRoleDirectlyAsync(userId, "admin");
        var (access, _) = await LoginAsync(client, phone);
        return (client, access, userId, phone);
    }

    [SkippableFact]
    public async Task Invalid_user_list_filters_return_400()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        var (client, access, _, _) = await RegisterAdminAsync();

        var role = await client.SendAsync(Authed(HttpMethod.Get, "/api/v1/admin/users?role=not-a-role", access));
        Assert.Equal(HttpStatusCode.BadRequest, role.StatusCode);

        var status = await client.SendAsync(Authed(HttpMethod.Get, "/api/v1/admin/users?status=whatever", access));
        Assert.Equal(HttpStatusCode.BadRequest, status.StatusCode);

        // Main uses hasEmail (ADR-0018); feature used emailVerified.
        var hasEmail = await client.SendAsync(Authed(HttpMethod.Get, "/api/v1/admin/users?hasEmail=maybe", access));
        Assert.Equal(HttpStatusCode.BadRequest, hasEmail.StatusCode);
    }

    [SkippableFact]
    public async Task Cannot_suspend_the_sole_active_admin()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        var (client, access, actorId, _) = await RegisterAdminAsync();

        var targetClient = NewClient();
        var (_, targetId, _) = await RegisterAsync(targetClient);
        await GrantRoleDirectlyAsync(targetId, "admin");

        // Actor keeps user.suspend in the JWT but is not an active admin after
        // we pull the role — token still carries permissions until expiry.
        var adminRoleId = await RoleIdAsync("admin");
        await Db().GetCollection<BsonDocument>("users").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", actorId),
            Builders<BsonDocument>.Update.Pull("roleIds", adminRoleId));

        // Suspend every other admin so target is the sole active admin.
        var admins = await Db().GetCollection<BsonDocument>("users")
            .Find(Builders<BsonDocument>.Filter.AnyEq("roleIds", adminRoleId))
            .ToListAsync();
        foreach (var doc in admins)
        {
            var id = doc["_id"].AsString;
            if (id == targetId) continue;
            await Db().GetCollection<BsonDocument>("users").UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", id),
                Builders<BsonDocument>.Update.Set("status", "Suspended"));
        }

        var response = await client.SendAsync(
            Authed(HttpMethod.Post, $"/api/v1/admin/users/{targetId}/suspend", access, new { }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LAST_ADMIN", body.GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task Concurrent_suspend_of_last_two_admins_leaves_at_least_one_active()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        var (client, access, actorId, _) = await RegisterAdminAsync();

        var aClient = NewClient();
        var (_, adminA, _) = await RegisterAsync(aClient);
        await GrantRoleDirectlyAsync(adminA, "admin");

        var bClient = NewClient();
        var (_, adminB, _) = await RegisterAsync(bClient);
        await GrantRoleDirectlyAsync(adminB, "admin");

        var adminRoleId = await RoleIdAsync("admin");
        await Db().GetCollection<BsonDocument>("users").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", actorId),
            Builders<BsonDocument>.Update.Pull("roleIds", adminRoleId));

        var admins = await Db().GetCollection<BsonDocument>("users")
            .Find(Builders<BsonDocument>.Filter.AnyEq("roleIds", adminRoleId))
            .ToListAsync();
        foreach (var doc in admins)
        {
            var id = doc["_id"].AsString;
            if (id == adminA || id == adminB) continue;
            await Db().GetCollection<BsonDocument>("users").UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", id),
                Builders<BsonDocument>.Update.Set("status", "Suspended"));
        }

        var suspendA = client.SendAsync(
            Authed(HttpMethod.Post, $"/api/v1/admin/users/{adminA}/suspend", access, new { }));
        var suspendB = client.SendAsync(
            Authed(HttpMethod.Post, $"/api/v1/admin/users/{adminB}/suspend", access, new { }));
        await Task.WhenAll(suspendA, suspendB);

        var responses = new[] { await suspendA, await suspendB };
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        var activeAdmins = await Db().GetCollection<BsonDocument>("users").CountDocumentsAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("status", "Active"),
                Builders<BsonDocument>.Filter.AnyEq("roleIds", adminRoleId)));
        Assert.True(activeAdmins >= 1, $"Expected at least one active admin, found {activeAdmins}.");
    }

    [SkippableFact]
    public async Task Concurrent_admin_role_revoke_of_last_two_leaves_at_least_one_admin()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        var (client, access, actorId, _) = await RegisterAdminAsync();

        var aClient = NewClient();
        var (_, adminA, _) = await RegisterAsync(aClient);
        await GrantRoleDirectlyAsync(adminA, "admin");

        var bClient = NewClient();
        var (_, adminB, _) = await RegisterAsync(bClient);
        await GrantRoleDirectlyAsync(adminB, "admin");

        var adminRoleId = await RoleIdAsync("admin");
        await Db().GetCollection<BsonDocument>("users").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", actorId),
            Builders<BsonDocument>.Update.Pull("roleIds", adminRoleId));

        var admins = await Db().GetCollection<BsonDocument>("users")
            .Find(Builders<BsonDocument>.Filter.AnyEq("roleIds", adminRoleId))
            .ToListAsync();
        foreach (var doc in admins)
        {
            var id = doc["_id"].AsString;
            if (id == adminA || id == adminB) continue;
            await Db().GetCollection<BsonDocument>("users").UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", id),
                Builders<BsonDocument>.Update.Set("status", "Suspended"));
        }

        var revokeA = client.SendAsync(Authed(
            HttpMethod.Post, $"/api/v1/admin/users/{adminA}/roles", access,
            new { roleId = adminRoleId, grant = false }));
        var revokeB = client.SendAsync(Authed(
            HttpMethod.Post, $"/api/v1/admin/users/{adminB}/roles", access,
            new { roleId = adminRoleId, grant = false }));
        await Task.WhenAll(revokeA, revokeB);

        var responses = new[] { await revokeA, await revokeB };
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        var activeAdmins = await Db().GetCollection<BsonDocument>("users").CountDocumentsAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("status", "Active"),
                Builders<BsonDocument>.Filter.AnyEq("roleIds", adminRoleId)));
        Assert.True(activeAdmins >= 1, $"Expected at least one active admin, found {activeAdmins}.");
    }

    [SkippableFact]
    public async Task Privacy_execute_is_fail_closed()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        var (client, access, _, _) = await RegisterAdminAsync();

        var targetClient = NewClient();
        var (_, targetId, _) = await RegisterAsync(targetClient);

        var created = await client.SendAsync(Authed(
            HttpMethod.Post, $"/api/v1/admin/users/{targetId}/privacy-requests", access,
            new { type = "anonymize", reason = "dsar" }));
        created.EnsureSuccessStatusCode();
        var requestId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestId").GetString();

        var (approver, approverAccess, _, _) = await RegisterAdminAsync();

        var approved = await approver.SendAsync(Authed(
            HttpMethod.Post, $"/api/v1/admin/privacy-requests/{requestId}/approve", approverAccess, new { }));
        approved.EnsureSuccessStatusCode();

        var executed = await approver.SendAsync(Authed(
            HttpMethod.Post, $"/api/v1/admin/privacy-requests/{requestId}/execute", approverAccess, new { }));
        Assert.Equal(HttpStatusCode.Conflict, executed.StatusCode);
        var body = await executed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("POLICY_NOT_CONFIGURED", body.GetProperty("code").GetString());
    }

    private async Task InsertPendingInvitationAsync(
        string invitationId, string email, string roleId, string invitedBy, string tokenHash,
        DateTime expiresAtUtc)
    {
        await Db().GetCollection<BsonDocument>("staff_invitations").InsertOneAsync(new BsonDocument
        {
            { "_id", invitationId },
            { "email", email },
            { "roleIds", new BsonArray { roleId } },
            { "invitedBy", invitedBy },
            { "createdAt", DateTime.UtcNow },
            { "expiresAt", expiresAtUtc },
            { "tokenHash", tokenHash },
            { "status", "Pending" },
        });
    }

    private static string HashToken(string raw)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [SkippableFact]
    public async Task Staff_invitation_accept_is_single_use_and_concurrent_safe()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        var stamp = Guid.NewGuid().ToString("n")[..8];
        var (_, _, actorId, _) = await RegisterAdminAsync();
        var examAuthorId = await RoleIdAsync("exam-author");
        var raw = $"token-{stamp}-abcdefghijklmnopqrstuvwxyz";
        var invitationId = Guid.NewGuid().ToString("n");
        var inviteeEmail = $"invitee-{stamp}@example.com";
        await InsertPendingInvitationAsync(
            invitationId, inviteeEmail, examAuthorId, actorId, HashToken(raw),
            DateTime.UtcNow.AddHours(2));

        async Task<HttpResponseMessage> AcceptAsync(string displayName)
        {
            var c = NewClient();
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/staff-invitations/accept")
            {
                Content = JsonContent.Create(new { token = raw, password = Password, displayName }),
            };
            req.Headers.Add(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
            return await c.SendAsync(req);
        }

        var acceptA = AcceptAsync("Invitee A");
        var acceptB = AcceptAsync("Invitee B");
        await Task.WhenAll(acceptA, acceptB);
        var responses = new[] { await acceptA, await acceptB };
        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(1, responses.Count(r => !r.IsSuccessStatusCode));

        var users = await Db().GetCollection<BsonDocument>("users")
            .Find(Builders<BsonDocument>.Filter.Eq("email", inviteeEmail))
            .ToListAsync();
        Assert.Single(users);

        var identities = await Db().GetCollection<BsonDocument>("user_identities")
            .Find(Builders<BsonDocument>.Filter.Eq("userId", users[0]["_id"].AsString))
            .ToListAsync();
        Assert.Single(identities);

        var reuse = await AcceptAsync("Reuse");
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
    }

    [SkippableFact]
    public async Task Staff_invitation_rejects_revoked_expired_and_stale_after_resend()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        var stamp = Guid.NewGuid().ToString("n")[..8];
        var (client, access, actorId, _) = await RegisterAdminAsync();
        var examAuthorId = await RoleIdAsync("exam-author");

        var revokedRaw = $"revoked-{stamp}-abcdefghijklmnop";
        await InsertPendingInvitationAsync(
            Guid.NewGuid().ToString("n"), $"rev-{stamp}@example.com", examAuthorId, actorId,
            HashToken(revokedRaw), DateTime.UtcNow.AddHours(2));
        await Db().GetCollection<BsonDocument>("staff_invitations").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("email", $"rev-{stamp}@example.com"),
            Builders<BsonDocument>.Update.Set("status", "Revoked"));

        var expiredRaw = $"expired-{stamp}-abcdefghijklmnop";
        await InsertPendingInvitationAsync(
            Guid.NewGuid().ToString("n"), $"exp-{stamp}@example.com", examAuthorId, actorId,
            HashToken(expiredRaw), DateTime.UtcNow.AddHours(-1));

        async Task<HttpStatusCode> Accept(string token)
        {
            var c = NewClient();
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/staff-invitations/accept")
            {
                Content = JsonContent.Create(new { token, password = Password, displayName = "X" }),
            };
            req.Headers.Add(IdempotencyMiddleware.HeaderName, Guid.NewGuid().ToString("n"));
            return (await c.SendAsync(req)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.BadRequest, await Accept(revokedRaw));
        Assert.Equal(HttpStatusCode.BadRequest, await Accept(expiredRaw));

        var invite = await client.SendAsync(Authed(
            HttpMethod.Post, "/api/v1/admin/invitations", access,
            new { email = $"live-{stamp}@example.com", displayName = "Live", roles = new[] { "exam-author" } }));
        invite.EnsureSuccessStatusCode();
        var invitationId = (await invite.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("invitationId").GetString()!;

        var oldDoc = await Db().GetCollection<BsonDocument>("staff_invitations")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", invitationId))
            .FirstAsync();
        var oldHash = oldDoc["tokenHash"].AsString;
        var oldRaw = $"stale-{stamp}-abcdefghijklmnopqr";
        await Db().GetCollection<BsonDocument>("staff_invitations").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", invitationId),
            Builders<BsonDocument>.Update.Set("tokenHash", HashToken(oldRaw)));

        var resend = await client.SendAsync(Authed(
            HttpMethod.Post, $"/api/v1/admin/invitations/{invitationId}/resend", access, new { }));
        resend.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, await Accept(oldRaw));
        Assert.NotEqual(oldHash,
            (await Db().GetCollection<BsonDocument>("staff_invitations")
                .Find(Builders<BsonDocument>.Filter.Eq("_id", invitationId)).FirstAsync())["tokenHash"].AsString);
    }

    [SkippableFact]
    public async Task Staff_invite_requires_permission_and_rejects_learner_role()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        var stamp = Guid.NewGuid().ToString("n")[..8];
        var learnerClient = NewClient();
        var (_, learnerId, phone) = await RegisterAsync(learnerClient);
        var (access, _) = await LoginAsync(learnerClient, phone);

        var forbidden = await learnerClient.SendAsync(Authed(
            HttpMethod.Post, "/api/v1/admin/invitations", access,
            new { email = $"x-{stamp}@example.com", displayName = "X", roles = new[] { "exam-author" } }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var (admin, adminAccess, _, _) = await RegisterAdminAsync();
        var learnerRole = await admin.SendAsync(Authed(
            HttpMethod.Post, "/api/v1/admin/invitations", adminAccess,
            new { email = $"badrole-{stamp}@example.com", displayName = "Bad", roles = new[] { "learner" } }));
        Assert.Equal(HttpStatusCode.NotFound, learnerRole.StatusCode);
        _ = learnerId;
    }
}
