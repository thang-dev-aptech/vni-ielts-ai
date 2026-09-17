using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The CMS media library through the real HTTP pipeline — the same reasoning
/// as <see cref="AdminImportEndpointsTests"/>: a refusal proves something only
/// on the route an operator actually calls, after authentication, after the
/// permission check, after the idempotency guard.
///
/// <para>
/// <b>The bytes are real.</b> Every upload below carries a genuine magic
/// number — an MP3's ID3 header — so the server's own sniffer, not a declared
/// content type, decides what gets stored. The garbage-file test is what pins
/// that: declared as <c>audio/mpeg</c>, bytes of an executable header, refused
/// as <c>MEDIA_UNRECOGNISED_FORMAT</c>.
/// </para>
///
/// <para>
/// <b>The blobs land in the Development local store</b> — a folder next to the
/// test binary — because the test process has no object storage. That is the
/// exact deployment state a fresh clone runs in; the port is the same one the
/// S3 store answers in production. Tests delete what they upload.
/// </para>
/// </summary>
public sealed class AdminMediaEndpointsTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private static readonly byte[] Mp3Bytes =
        [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x11, 0x22, 0x33, 0x44];

    private static readonly byte[] ExeBytes =
        [0x4d, 0x5a, 0x90, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06];

    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(HttpClient Client, string Access)> SignInWithRolesAsync(params string[] roleNames)
    {
        var client = NewClient();
        await SsoRoundTripAsync(client);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

            var user = await users.FindByEmailAsync(
                Email.Create("stub.learner@example.com"), default);
            Assert.NotNull(user);

            // <b>Reset, not add.</b> The stub provider always authenticates the
            // same account, and every earlier test's grant is still on it —
            // the admin of the round-trip test would otherwise carry
            // media.read into the "no media permission" case below and turn a
            // refusal test green for the wrong reason. Each sign-in states
            // exactly the roles it means to test.
            foreach (var held in user!.RoleIds) user.RemoveRole(held);

            foreach (var roleName in roleNames)
            {
                var role = await roles.FindByNameAsync(roleName, default);
                Assert.NotNull(role);
                user!.AssignRole(role!.Id);
            }

            await users.SaveAsync(user!, default);
        }

        // Permissions are minted into the access token, so a second round
        // trip after the grant is what carries them.
        var access = await SsoRoundTripAsync(client);
        return (client, access);
    }

    private async Task<(HttpClient Client, string Access)> SignInAsAdminAsync() =>
        await SignInWithRolesAsync(SystemRoles.Admin);

    private static async Task<string> SsoRoundTripAsync(HttpClient client)
    {
        var start = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
        var url = new Uri((await start.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("authorizationUrl").GetString()!);

        var callback = await client.GetAsync(url.PathAndQuery);
        var code = System.Web.HttpUtility.ParseQueryString(callback.Headers.Location!.Query)["code"];

        var complete = await client.PostAsJsonAsync("/api/v1/auth/sso/complete", new { handoffCode = code });
        complete.EnsureSuccessStatusCode();

        return (await complete.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string access)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        return request;
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string fileName)
    {
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg"); // a lie the sniffer must ignore
        content.Add(part, "file", fileName);
        return content;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [SkippableFact]
    public async Task Upload_list_play_retire_and_delete_round_trip()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        var (client, access) = await SignInAsAdminAsync();

        // Upload — declared audio/mpeg, and the bytes really are an MP3.
        using (var upload = Request(HttpMethod.Post, "/api/v1/admin/media", access))
        {
            upload.Content = Upload(Mp3Bytes, "listening-part-1.mp3");
            var created = await client.SendAsync(upload);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var asset = await BodyOf(created);
            var mediaId = asset.GetProperty("mediaId").GetString()!;
            Assert.Equal("audio", asset.GetProperty("kind").GetString());
            Assert.Equal("audio/mpeg", asset.GetProperty("contentType").GetString());
            Assert.Equal(Mp3Bytes.Length, asset.GetProperty("bytes").GetInt64());
            Assert.False(asset.GetProperty("retired").GetBoolean());

            // List — the asset is in the library, newest first.
            using var list = Request(HttpMethod.Get, "/api/v1/admin/media", access);
            var listed = await BodyOf(await client.SendAsync(list));
            var ids = listed.GetProperty("media").EnumerateArray()
                .Select(m => m.GetProperty("mediaId").GetString()).ToArray();
            Assert.Contains(mediaId, ids);

            // Content — the exact bytes come back, with an ETag to revalidate.
            using var content = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/media/{mediaId}/content");
            content.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            var served = await client.SendAsync(content);
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);
            Assert.NotNull(served.Headers.ETag);
            Assert.Equal(Mp3Bytes, await served.Content.ReadAsByteArrayAsync());

            // Retire — the row stays, flagged.
            using var retire = Request(HttpMethod.Post, $"/api/v1/admin/media/{mediaId}/retire", access);
            var retired = await BodyOf(await client.SendAsync(retire));
            Assert.True(retired.GetProperty("retired").GetBoolean());

            // Delete — the row and the bytes go.
            using var delete = Request(HttpMethod.Delete, $"/api/v1/admin/media/{mediaId}", access);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);

            using var after = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/media/{mediaId}/content");
            after.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(after)).StatusCode);

            // The audit trail saw all three acts.
            using (var scope = app.Services.CreateScope())
            {
                var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
                var (entries, _) = await audit.ListAsync(null, nameof(AuditAction.MediaUploaded), 0, 50, default);
                Assert.Contains(entries, e => e.TargetId == mediaId);
                var (retireEntries, _) = await audit.ListAsync(null, nameof(AuditAction.MediaRetired), 0, 50, default);
                Assert.Contains(retireEntries, e => e.TargetId == mediaId);
                var (deleteEntries, _) = await audit.ListAsync(null, nameof(AuditAction.MediaDeleted), 0, 50, default);
                Assert.Contains(deleteEntries, e => e.TargetId == mediaId);
            }
        }
    }

    [SkippableFact]
    public async Task Bytes_declared_audio_but_shaped_like_an_executable_are_refused()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        var (client, access) = await SignInAsAdminAsync();

        using var upload = Request(HttpMethod.Post, "/api/v1/admin/media", access);
        upload.Content = Upload(ExeBytes, "payload.mp3");
        var response = await client.SendAsync(upload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await BodyOf(response);
        Assert.Equal("MEDIA_UNRECOGNISED_FORMAT", problem.GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task An_author_can_upload_but_cannot_retire()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        var (client, access) = await SignInWithRolesAsync(SystemRoles.ContentEditor);

        string mediaId;
        using (var upload = Request(HttpMethod.Post, "/api/v1/admin/media", access))
        {
            upload.Content = Upload(Mp3Bytes, "author-audio.mp3");
            var created = await client.SendAsync(upload);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            mediaId = (await BodyOf(created)).GetProperty("mediaId").GetString()!;
        }

        using (var retire = Request(HttpMethod.Post, $"/api/v1/admin/media/{mediaId}/retire", access))
        {
            var denied = await client.SendAsync(retire);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("PERMISSION_DENIED", (await BodyOf(denied)).GetProperty("code").GetString());
        }

        // Clean up the blob this test filed.
        var (adminClient, adminAccess) = await SignInAsAdminAsync();
        using var delete = Request(HttpMethod.Delete, $"/api/v1/admin/media/{mediaId}", adminAccess);
        await adminClient.SendAsync(delete);
    }

    [SkippableFact]
    public async Task Media_routes_refuse_a_caller_with_no_media_permission_at_all()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        var (client, access) = await SignInWithRolesAsync(SystemRoles.Support);

        using var list = Request(HttpMethod.Get, "/api/v1/admin/media", access);
        var denied = await client.SendAsync(list);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("PERMISSION_DENIED", (await BodyOf(denied)).GetProperty("code").GetString());
    }
}
