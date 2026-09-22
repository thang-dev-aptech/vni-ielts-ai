using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Vni.Ielts.Api.Endpoints;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Api.Tests;

/// <summary>
/// Batch import HTTP surface — start/resume multi-package imports, get batch status,
/// authorization gating, and audit trails that never carry archive contents.
///
/// No SSO round trip — mints a JWT directly against the host's own signing key,
/// following the same pattern as <see cref="AdminEvaluationEndpointsTests"/>.
/// </summary>
public sealed class AdminImportBatchEndpointsTests : IClassFixture<BatchImportAppFactory>
{
    private readonly BatchImportAppFactory _app;

    public AdminImportBatchEndpointsTests(BatchImportAppFactory app) => _app = app;

    private HttpClient NewClient() => _app.CreateClient();

    private static byte[] CreateValidZip(string examJson = DefaultExamJson)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void WriteEntry(string path, string content)
            {
                var entry = zip.CreateEntry(path);
                using var writer = entry.Open();
                writer.Write(Encoding.UTF8.GetBytes(content));
            }

            WriteEntry("reading/paper/reading-paper.json", examJson);
            WriteEntry("reading/key/reading-key.json", AnswerKeyJson);
            WriteEntry("listening/paper/listening-paper.json", examJson);
            WriteEntry("listening/key/listening-key.json", AnswerKeyJson);
        }

        return ms.ToArray();
    }

    private static byte[] CreateBombZip()
    {
        using var ms = new MemoryStream();
        using var zip = new ZipArchive(ms, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("bomb.txt");
        using var w = entry.Open();
        w.Write(new byte[100_000_000]);
        zip.Dispose();
        return ms.ToArray();
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        return request;
    }

    private static string Token(params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, $"batch-test-{Guid.NewGuid():n}"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n")),
            new("name", "Batch Test"),
        };
        claims.AddRange(permissions.Select(p => new Claim("perm", p)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(BatchImportAppFactory.JwtSigningKey)),
            SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: "vni-ielts",
            audience: "vni-ielts-clients",
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    [SkippableFact]
    public async Task POST_batches_requires_authorization()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        var client = NewClient();
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(CreateValidZip());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part, "file", "exam1.zip");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/import/batches");
        request.Content = content;

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task POST_batches_requires_package_upload_permission()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        var client = NewClient();
        var accessToken = Token("some.other.permission");

        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(CreateValidZip());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part, "file", "exam1.zip");

        var request = Authed(HttpMethod.Post, "/api/v1/admin/import/batches", accessToken);
        request.Content = content;

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task POST_batches_rejects_bomb_archive()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        var client = NewClient();
        var accessToken = Token(PermissionKeys.PackageUpload);

        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(CreateBombZip());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part, "file", "bomb.zip");

        var request = Authed(HttpMethod.Post, "/api/v1/admin/import/batches", accessToken);
        request.Content = content;

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [SkippableFact]
    public async Task POST_batches_accepts_and_processes_multiple_valid_packages()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        var client = NewClient();
        var accessToken = Token(PermissionKeys.PackageUpload);

        var content = new MultipartFormDataContent();
        var zip1 = CreateValidZip();
        var part1 = new ByteArrayContent(zip1);
        part1.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part1, "file", "exam1.zip");

        var zip2 = CreateValidZip();
        var part2 = new ByteArrayContent(zip2);
        part2.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part2, "file", "exam2.zip");

        var request = Authed(HttpMethod.Post, "/api/v1/admin/import/batches", accessToken);
        request.Content = content;

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ImportBatchStatusView>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Items.Count);
        Assert.Equal("exam1.zip", body.Items[0].ItemId);
        Assert.Equal("exam2.zip", body.Items[1].ItemId);
    }

    [SkippableFact]
    public async Task POST_batches_returns_batch_id_and_per_item_state()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        var client = NewClient();
        var accessToken = Token(PermissionKeys.PackageUpload);

        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(CreateValidZip());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part, "file", "exam.zip");

        var request = Authed(HttpMethod.Post, "/api/v1/admin/import/batches", accessToken);
        request.Content = content;

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ImportBatchStatusView>();
        Assert.NotNull(body);
        Assert.NotEmpty(body.BatchId);
        Assert.Single(body.Items);
        Assert.NotNull(body.Items[0].State);
    }

    [SkippableFact]
    public async Task POST_batches_rejects_empty_file_list()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        var client = NewClient();
        var accessToken = Token(PermissionKeys.PackageUpload);

        var content = new MultipartFormDataContent();

        var request = Authed(HttpMethod.Post, "/api/v1/admin/import/batches", accessToken);
        request.Content = content;

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [SkippableFact]
    public async Task GET_batches_requires_authorization()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        var client = NewClient();
        var response = await client.GetAsync("/api/v1/admin/import/batches/batch-123");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task GET_batches_requires_package_read_or_upload_permission()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        var client = NewClient();
        var accessToken = Token("some.other.permission");
        var request = Authed(HttpMethod.Get, "/api/v1/admin/import/batches/batch-123", accessToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task GET_batches_returns_404_for_nonexistent_batch()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        var client = NewClient();
        var accessToken = Token(PermissionKeys.PackageRead);
        var request = Authed(HttpMethod.Get, "/api/v1/admin/import/batches/nonexistent", accessToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task GET_batches_returns_status_after_POST()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        var client = NewClient();
        var uploadToken = Token(PermissionKeys.PackageUpload);
        var readToken = Token(PermissionKeys.PackageRead);

        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(CreateValidZip());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part, "file", "exam.zip");

        var uploadRequest = Authed(HttpMethod.Post, "/api/v1/admin/import/batches", uploadToken);
        uploadRequest.Content = content;

        var uploadResponse = await client.SendAsync(uploadRequest);
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<ImportBatchStatusView>();
        var batchId = uploadBody!.BatchId;

        var getRequest = Authed(HttpMethod.Get, $"/api/v1/admin/import/batches/{Uri.EscapeDataString(batchId)}", readToken);
        var getResponse = await client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var body = await getResponse.Content.ReadFromJsonAsync<ImportBatchStatusView>();
        Assert.NotNull(body);
        Assert.Equal(batchId, body.BatchId);
        Assert.NotEmpty(body.Items);
    }

    [SkippableFact]
    public async Task Batch_status_never_exposes_archive_contents()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        var client = NewClient();
        var accessToken = Token(PermissionKeys.PackageUpload, PermissionKeys.PackageRead);

        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(CreateValidZip());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(part, "file", "exam.zip");

        var uploadRequest = Authed(HttpMethod.Post, "/api/v1/admin/import/batches", accessToken);
        uploadRequest.Content = content;

        var uploadResponse = await client.SendAsync(uploadRequest);
        var responseText = await uploadResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<?xml", responseText);
        Assert.DoesNotContain("PK\u0003\u0004", responseText);
    }

    private const string DefaultExamJson = @"{
  ""formatVersion"": ""1.0"",
  ""id"": ""reading-test-01"",
  ""title"": ""Test Exam"",
  ""description"": ""A test exam"",
  ""variant"": ""Academic"",
  ""parts"": [
    {
      ""id"": ""part-1"",
      ""title"": ""Part 1"",
      ""passage"": ""Test passage"",
      ""questions"": [
        {
          ""id"": ""q1"",
          ""type"": ""MultipleChoice"",
          ""prompt"": ""Question 1"",
          ""choices"": [""A"", ""B"", ""C"", ""D""],
          ""correctAnswer"": 0
        }
      ]
    }
  ]
}";

    private const string AnswerKeyJson = @"{
  ""version"": ""1.0"",
  ""answers"": [
    {
      ""questionId"": ""q1"",
      ""correctAnswer"": ""A""
    }
  ]
}";
}

/// <summary>
/// Boots the real API with a throwaway Mongo database — no SSO.
/// </summary>
public sealed class BatchImportAppFactory : WebApplicationFactory<Program>
{
    public const string JwtSigningKey = "BATCH_IMPORT_TEST_SIGNING_KEY_xxxxxxxx";

    private readonly string _database = $"vni_ielts_api_batch_{Guid.NewGuid():n}";

    private static readonly Lazy<(bool Ok, int Port)> _mongo = new(() =>
    {
        foreach (var port in new[] { 27018, 27017 })
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var client = new MongoClient(new MongoClientSettings
                    {
                        Server = new MongoServerAddress("localhost", port),
                        DirectConnection = true,
                        ServerSelectionTimeout = TimeSpan.FromSeconds(3),
                        ConnectTimeout = TimeSpan.FromSeconds(3),
                    });
                    client.ListDatabaseNames().MoveNext();
                    return (true, port);
                }
                catch
                {
                    if (attempt < 3) Thread.Sleep(TimeSpan.FromSeconds(1));
                }
            }
        }

        return (false, 27018);
    });

    public static bool MongoAvailable => _mongo.Value.Ok;
    public static string SkipReason => $"MongoDB not available on port {_mongo.Value.Port}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(Microsoft.AspNetCore.Hosting.WebHostDefaults.EnvironmentKey, "Development");

        builder.UseSetting(
            "Mongo:ConnectionString", $"mongodb://localhost:{_mongo.Value.Port}/?directConnection=true");
        builder.UseSetting("Mongo:Database", _database);
        builder.UseSetting("Jwt:SigningKey", JwtSigningKey);
        builder.UseSetting("Sso:EnableStubProvider", "true");
        builder.UseSetting("Sso:Google:ClientId", string.Empty);
        builder.UseSetting("Sso:Google:ClientSecret", string.Empty);
        builder.UseSetting("Sso:ClientBaseUrl", "http://localhost:5173");
        builder.UseSetting("Sso:Google:RedirectUri", "http://localhost/api/v1/auth/sso/google/callback");
    }
}
