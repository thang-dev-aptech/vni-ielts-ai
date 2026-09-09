using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The layer <c>TestServer</c> cannot see: Kestrel's 1 MB global body cap
/// against the three admin multipart upload routes. Same reason
/// <see cref="KestrelTransportTests"/> exists for Speaking recordings.
/// </summary>
public sealed class KestrelMultipartUploadTests(KestrelAdminAppFactory app)
    : IClassFixture<KestrelAdminAppFactory>
{
    /// <summary>
    /// Over Kestrel's 1 MB default and under every route's own business
    /// ceiling (import archive, 2 GB package, 50 MB audio).
    /// </summary>
    private const int OversizedBytes = 2 * 1024 * 1024;

    private HttpClient NewClient()
    {
        app.ClientOptions.AllowAutoRedirect = false;
        return app.CreateClient();
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, string access)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        return request;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(HttpClient Client, string Access)> SignInAsAdminAsync()
    {
        var client = NewClient();
        const string password = "mot-mat-khau-du-dai-2026";
        var phone = $"09{Random.Shared.NextInt64(0, 100_000_000):D8}";

        var register = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new { phone, password, displayName = "Điều hành viên" }),
        };
        register.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("n"));
        var registered = await client.SendAsync(register);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var firstAccess = (await BodyOf(registered))
            .GetProperty("session").GetProperty("accessToken").GetString()!;

        var me = Authed(HttpMethod.Get, "/api/v1/me", firstAccess);
        var meResponse = await client.SendAsync(me);
        meResponse.EnsureSuccessStatusCode();
        var userId = (await BodyOf(meResponse)).GetProperty("userId").GetString()!;

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

            var admin = await roles.FindByNameAsync(SystemRoles.Admin, default);
            Assert.NotNull(admin);

            var user = await users.FindByIdAsync(new UserId(userId), default);
            Assert.NotNull(user);
            user!.AssignRole(admin!.Id);
            await users.SaveAsync(user, default);
        }

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { identifier = phone, password });
        login.EnsureSuccessStatusCode();
        var access = (await BodyOf(login)).GetProperty("accessToken").GetString()!;
        return (client, access);
    }

    private static byte[] StoredZip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    private static string OversizedExamJson() =>
        """
        {
          "formatVersion": "2.0", "formatProfile": "vni-practice", "scoringProfileRef": "kestrel-upload",
          "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          "title": "Kestrel oversized import", "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [
            { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 } ] } },
          "sequenceProfile": { "modules": ["reading"] },
          "sections": [{ "module": "reading", "order": 1, "parts": [{ "order": 1, "kind": "passage",
            "body": "
        """
        + new string('x', OversizedBytes)
        + """
        ", "questions": [{
              "id": "q-1", "order": 1, "type": "multiple-select", "marks": 1,
              "options": [{ "key": "A", "text": "Alpha" }],
              "group": { "id": "bank-1", "instruction": "Choose." },
              "slots": [{ "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } }],
              "explanation": { "shortReason": "Stated.", "evidence": ["pad"] }
            }]
          }]}]
        }
        """;

    [SkippableFact]
    public async Task A_package_import_larger_than_kestrels_global_limit_is_accepted()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var zip = StoredZip(("reading/exam.json", OversizedExamJson()));
        Assert.True(zip.Length > 1024 * 1024);

        var request = Authed(HttpMethod.Post, "/api/v1/admin/import/packages", access);
        var part = new ByteArrayContent(zip);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        var form = new MultipartFormDataContent { { part, "file", "package.zip" } };
        request.Content = form;

        var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"expected 201, got {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [SkippableFact]
    public async Task A_manifest_package_larger_than_kestrels_global_limit_is_accepted()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var zip = StoredZip(("pad.bin", new string('x', OversizedBytes)));
        Assert.True(zip.Length > 1024 * 1024);

        var request = Authed(HttpMethod.Post, "/api/v1/admin/packages", access);
        var part = new ByteArrayContent(zip);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        var form = new MultipartFormDataContent { { part, "package", "package.zip" } };
        request.Content = form;

        var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode == HttpStatusCode.Accepted,
            $"expected 202, got {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [SkippableFact]
    public async Task A_media_upload_larger_than_kestrels_global_limit_is_accepted()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var audio = new byte[OversizedBytes];
        audio[0] = 0x49;
        audio[1] = 0x44;
        audio[2] = 0x33;

        var request = Authed(HttpMethod.Post, "/api/v1/admin/media", access);
        var part = new ByteArrayContent(audio);
        part.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        var form = new MultipartFormDataContent { { part, "file", "pad.mp3" } };
        request.Content = form;

        var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"expected 200, got {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}

/// <summary>
/// <see cref="SsoAppFactory"/> on a real Kestrel listener rather than
/// <c>TestServer</c>. Same constructor-before-build rule as
/// <see cref="KestrelExamAppFactory"/>.
/// </summary>
public sealed class KestrelAdminAppFactory : SsoAppFactory
{
    public KestrelAdminAppFactory()
    {
        UseKestrel(0);
        ClientOptions.AllowAutoRedirect = false;
    }
}
