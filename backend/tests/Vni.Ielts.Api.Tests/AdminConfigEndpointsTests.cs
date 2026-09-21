using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Ai.Importing;
using Vni.Ielts.Infrastructure.Assessment;
using Vni.Ielts.Infrastructure.Content.Import;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Security;
using Vni.Ielts.Infrastructure.Security.Sso;

namespace Vni.Ielts.Api.Tests;

/// <summary>
/// <c>config-verification</c> — prove <c>GET /api/v1/admin/config</c> is
/// read-only, permission-gated, matches bound <c>IOptions</c>, and cannot leak
/// the sentinel secrets the host was deliberately given the chance to print.
///
/// <b>Every secret assertion is a pair.</b> First the host really holds the
/// sentinel (read back from <c>IOptions</c>); then the JSON body does not.
/// Asserting absence alone would pass on an empty payload that never saw the
/// secret at all.
/// </summary>
public sealed class AdminConfigEndpointsTests : IClassFixture<ConfigAppFactory>
{
    private const string OpenAiApiKey = "SENTINEL-OPENAI-API-KEY-do-not-leak-9f3c";
    private const string GeminiApiKey = "SENTINEL-GEMINI-API-KEY-do-not-leak-a1b2";
    // Gemini carries the secret endpoint; OpenAi BaseUrl stays empty so
    // writing-marking can be "available" under LearnerPersonal egress (vendor
    // endpoint only). The OpenAi key is still the credential leak sentinel.
    private const string GeminiBaseUrl = "https://sentinel-gemini-secret.example/v1";
    private const string GoogleClientSecret = "SENTINEL-GOOGLE-CLIENT-SECRET-do-not-leak";
    private const string JwtSigningKey = "SENTINEL_JWT_SIGNING_KEY_xxxxxxxxxxxxxxxx";

    // Distinctive archive caps so the projection can be told apart from
    // appsettings.json defaults (2000 / 512 MiB / …).
    private const int ArchiveMaxEntries = 177;
    private const long ArchiveMaxTotal = 111L * 1024 * 1024;
    private const long ArchiveMaxEntry = 55L * 1024 * 1024;
    private const int ArchiveRatio = 17;
    private const long ArchiveMaxBytes = 88L * 1024 * 1024;
    private const int ArchiveTimeout = 42;

    private const string WritingRubricVersion = "config-verification-rubric-v1";
    private const string WritingPromptVersion = "config-verification-prompt-v1";
    private const string WritingModel = "gpt-config-verification";

    private readonly ConfigAppFactory _app;

    public AdminConfigEndpointsTests(ConfigAppFactory app) => _app = app;

    [SkippableFact]
    public async Task Config_read_is_required_and_no_update_route_is_exposed()
    {
        Skip.IfNot(ConfigAppFactory.MongoAvailable, ConfigAppFactory.SkipReason);

        var client = _app.CreateClient();

        var anonymous = await client.GetAsync("/api/v1/admin/config");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using (var noPerm = Authed(HttpMethod.Get, "/api/v1/admin/config", Token()))
        {
            var forbidden = await client.SendAsync(noPerm);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var allowed = Authed(HttpMethod.Get, "/api/v1/admin/config", Token(PermissionKeys.ConfigRead)))
        {
            var ok = await client.SendAsync(allowed);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var write = Authed(method, "/api/v1/admin/config", Token(PermissionKeys.ConfigRead));
            write.Content = JsonContent.Create(new { });
            var response = await client.SendAsync(write);
            // No write handler is mapped. ASP.NET may answer 404/405, or 400 when
            // a method reaches the group with a body it cannot bind — never 2xx.
            Assert.False(
                response.IsSuccessStatusCode,
                $"{method} /api/v1/admin/config returned {response.StatusCode}; the surface is read-only.");
        }

        // Endpoint metadata: the group maps GET only — config.update is unused.
        var endpoints = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("admin/config", StringComparison.OrdinalIgnoreCase) == true
                || (e.DisplayName?.Contains("AdminGetRuntimeConfig", StringComparison.Ordinal) ?? false))
            .ToList();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, e =>
        {
            var methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            Assert.DoesNotContain(methods, m =>
                m.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || m.Equals("PUT", StringComparison.OrdinalIgnoreCase)
                || m.Equals("PATCH", StringComparison.OrdinalIgnoreCase)
                || m.Equals("DELETE", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(methods, m => m.Equals("GET", StringComparison.OrdinalIgnoreCase));
        });
    }

    [SkippableFact]
    public async Task Response_matches_IOptions_and_never_leaks_sentinel_secrets()
    {
        Skip.IfNot(ConfigAppFactory.MongoAvailable, ConfigAppFactory.SkipReason);

        // Pair half 1 — the host really holds every sentinel.
        using (var scope = _app.Services.CreateScope())
        {
            var ai = scope.ServiceProvider.GetRequiredService<IOptions<AiOptions>>().Value;
            var assessment = scope.ServiceProvider.GetRequiredService<IOptions<AssessmentOptions>>().Value;
            var archive = scope.ServiceProvider.GetRequiredService<IOptions<ImportArchiveOptions>>().Value;
            var jwt = scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;
            var mongo = scope.ServiceProvider.GetRequiredService<IOptions<MongoOptions>>().Value;
            var sso = scope.ServiceProvider.GetRequiredService<IOptions<SsoOptions>>().Value;

            Assert.Equal(OpenAiApiKey, ai.OpenAi.ApiKey);
            Assert.True(string.IsNullOrEmpty(ai.OpenAi.BaseUrl));
            Assert.Equal(WritingModel, ai.OpenAi.Model);
            Assert.Equal(GeminiApiKey, ai.Gemini.ApiKey);
            Assert.Equal(GeminiBaseUrl, ai.Gemini.BaseUrl);

            Assert.Equal(JwtSigningKey, jwt.SigningKey);
            Assert.Contains(ConfigAppFactory.MongoConnectionSentinel, mongo.ConnectionString, StringComparison.Ordinal);
            Assert.Equal(GoogleClientSecret, sso.Google.ClientSecret);

            Assert.Equal(WritingRubricVersion, assessment.Writing.Version);
            Assert.Equal(1m, assessment.Writing.TaskWeights?.Task1);
            Assert.Equal(2m, assessment.Writing.TaskWeights?.Task2);
            Assert.Equal("vi", assessment.Writing.FeedbackLanguage);
            Assert.Equal("per-criterion", assessment.Writing.CriterionGranularity);
            Assert.Equal("OpenAi", assessment.WritingMarking.PrimaryProvider);
            Assert.Equal(WritingPromptVersion, assessment.WritingMarking.PromptVersion);

            Assert.Equal(ArchiveMaxEntries, archive.MaxEntries);
            Assert.Equal(ArchiveMaxTotal, archive.MaxTotalUncompressedBytes);
            Assert.Equal(ArchiveMaxEntry, archive.MaxEntryUncompressedBytes);
            Assert.Equal(ArchiveRatio, archive.MaxCompressionRatio);
            Assert.Equal(ArchiveMaxBytes, archive.MaxArchiveBytes);
            Assert.Equal(ArchiveTimeout, archive.ExtractionTimeoutSeconds);

            // Unconfigured import skills stay unavailable — null model/version.
            var parser = scope.ServiceProvider.GetRequiredService<IOptions<ExamParserOptions>>().Value;
            var transcription = scope.ServiceProvider.GetRequiredService<IOptions<AudioTranscriptionOptions>>().Value;
            Assert.False(parser.IsConfigured(ai));
            Assert.False(transcription.IsConfigured(ai));
        }

        var client = _app.CreateClient();
        using var request = Authed(HttpMethod.Get, "/api/v1/admin/config", Token(PermissionKeys.ConfigRead));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bodyText = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(bodyText);
        var root = body.RootElement;

        // Pair half 2 — none of the sentinels appear in any field or value.
        foreach (var secret in new[]
                 {
                     OpenAiApiKey, GeminiApiKey, GeminiBaseUrl,
                     GoogleClientSecret, JwtSigningKey,
                     ConfigAppFactory.MongoConnectionSentinel,
                 })
        {
            Assert.DoesNotContain(secret, bodyText, StringComparison.Ordinal);
        }

        Assert.False(JsonContainsKey(root, "apiKey"));
        Assert.False(JsonContainsKey(root, "ApiKey"));
        Assert.False(JsonContainsKey(root, "clientSecret"));
        Assert.False(JsonContainsKey(root, "ClientSecret"));
        Assert.False(JsonContainsKey(root, "accessKey"));
        Assert.False(JsonContainsKey(root, "secretKey"));
        Assert.False(JsonContainsKey(root, "connectionString"));
        Assert.False(JsonContainsKey(root, "baseUrl"));
        Assert.False(JsonContainsKey(root, "BaseUrl"));
        Assert.False(JsonContainsKey(root, "signingKey"));

        // Projection matches IOptions — writing skill available with safe ids.
        var skills = root.GetProperty("ai").GetProperty("skills");
        var writing = FindSkill(skills, "writing-marking");
        Assert.Equal("available", writing.GetProperty("status").GetString());
        Assert.Equal("OpenAi", writing.GetProperty("provider").GetString());
        Assert.Equal(WritingModel, writing.GetProperty("model").GetString());
        Assert.Equal(WritingPromptVersion, writing.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, writing.GetProperty("fallback").ValueKind);

        var importParser = FindSkill(skills, "import-parser");
        Assert.Equal("unavailable", importParser.GetProperty("status").GetString());
        Assert.True(
            importParser.GetProperty("provider").ValueKind is JsonValueKind.Null
            || string.IsNullOrEmpty(importParser.GetProperty("provider").GetString()));
        Assert.Equal(JsonValueKind.Null, importParser.GetProperty("model").ValueKind);
        Assert.Equal(JsonValueKind.Null, importParser.GetProperty("fallback").ValueKind);

        var importTranscription = FindSkill(skills, "import-transcription");
        Assert.Equal("unavailable", importTranscription.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, importTranscription.GetProperty("model").ValueKind);

        var writingPanel = root.GetProperty("writing");
        Assert.Equal(WritingRubricVersion, writingPanel.GetProperty("rubricVersion").GetString());
        Assert.Equal(1, writingPanel.GetProperty("task1Weight").GetDecimal());
        Assert.Equal(2, writingPanel.GetProperty("task2Weight").GetDecimal());
        Assert.Equal("vi", writingPanel.GetProperty("feedbackLanguage").GetString());
        Assert.Equal("per-criterion", writingPanel.GetProperty("criterionGranularity").GetString());

        var archivePanel = root.GetProperty("importArchive");
        Assert.Equal(ArchiveMaxEntries, archivePanel.GetProperty("maxEntries").GetInt32());
        Assert.Equal(ArchiveMaxTotal, archivePanel.GetProperty("maxTotalUncompressedBytes").GetInt64());
        Assert.Equal(ArchiveMaxEntry, archivePanel.GetProperty("maxEntryUncompressedBytes").GetInt64());
        Assert.Equal(ArchiveRatio, archivePanel.GetProperty("maxCompressionRatio").GetInt32());
        Assert.Equal(ArchiveMaxBytes, archivePanel.GetProperty("maxArchiveBytes").GetInt64());
        Assert.Equal(ArchiveTimeout, archivePanel.GetProperty("extractionTimeoutSeconds").GetInt32());

        // Token pricing stays Pending — no invented price fields.
        var token = root.GetProperty("tokenPricing");
        Assert.Equal("pending", token.GetProperty("status").GetString());
        var blockers = token.GetProperty("blockers").EnumerateArray().Select(b => b.GetString()).ToArray();
        Assert.Contains("B-5a", blockers);
        Assert.Contains("B-5b", blockers);
        Assert.False(JsonContainsKey(token, "price"));
        Assert.False(JsonContainsKey(token, "amount"));
        Assert.False(JsonContainsKey(token, "cost"));
        Assert.False(JsonContainsKey(token, "vni"));
    }

    private static JsonElement FindSkill(JsonElement skills, string name) =>
        skills.EnumerateArray().Single(s => s.GetProperty("skill").GetString() == name);

    private static bool JsonContainsKey(JsonElement element, string key)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (JsonContainsKey(prop.Value, key))
                        return true;
                }

                return false;
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(child => JsonContainsKey(child, key));
            default:
                return false;
        }
    }

    private static string Token(params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "config-verification-operator"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n")),
            new("name", "Config Verification"),
        };
        claims.AddRange(permissions.Select(p => new Claim("perm", p)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey)),
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

    private static HttpRequestMessage Authed(HttpMethod method, string path, string access)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return request;
    }
}

/// <summary>
/// Boots the API with sentinel secrets wired through <c>UseSetting</c>, so the
/// config endpoint has every opportunity to leak them — and a throwaway Mongo
/// database so <c>InitialiseInfrastructureAsync</c> can finish.
/// </summary>
public sealed class ConfigAppFactory : WebApplicationFactory<Program>
{
    public const string MongoConnectionSentinel = "sentinel_conn_do_not_leak";

    private readonly string _database = $"vni_ielts_api_config_{Guid.NewGuid():n}";

    // Prefer the docker-compose mapped port (27018); fall back to a host
    // mongod on 27017 (common on developer machines running rs0 directly).
    private static readonly Lazy<(bool Ok, int Port)> _mongo = new(() =>
    {
        foreach (var port in new[] { 27018, 27017 })
        {
            Exception? failure = null;
            for (var attempt = 1; attempt <= MongoProbeAttempts; attempt++)
            {
                try
                {
                    var client = new MongoClient(
                        new MongoClientSettings
                        {
                            Server = new MongoServerAddress("localhost", port),
                            DirectConnection = true,
                            ServerSelectionTimeout = TimeSpan.FromSeconds(3),
                            ConnectTimeout = TimeSpan.FromSeconds(3),
                        });

                    client.ListDatabaseNames().MoveNext();
                    return (true, port);
                }
                catch (Exception e)
                {
                    failure = e;
                    if (attempt < MongoProbeAttempts) Thread.Sleep(TimeSpan.FromSeconds(1));
                }
            }

            _ = failure;
        }

        if (MongoRequired)
        {
            throw new InvalidOperationException(
                "VNI_REQUIRE_MONGO is set and no MongoDB answered on localhost:27018 or :27017.");
        }

        return (false, 27018);
    });

    public static bool MongoAvailable => _mongo.Value.Ok;

    private static bool MongoRequired =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VNI_REQUIRE_MONGO"));

    private const int MongoProbeAttempts = 3;

    public const string SkipReason =
        "No MongoDB on localhost:27018 or :27017. Start it with "
        + "`docker compose -f infra/docker/compose.yaml up -d`.";

    private string ConnectionStringForHost =>
        $"mongodb://localhost:{_mongo.Value.Port}/?directConnection=true&appName={MongoConnectionSentinel}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);

        builder.UseSetting("Mongo:ConnectionString", ConnectionStringForHost);
        builder.UseSetting("Mongo:Database", _database);
        builder.UseSetting("Jwt:SigningKey", AdminConfigEndpointsTests_JwtKey);
        builder.UseSetting("Sso:EnableStubProvider", "true");
        builder.UseSetting("Sso:Google:ClientId", string.Empty);
        builder.UseSetting("Sso:Google:ClientSecret", "SENTINEL-GOOGLE-CLIENT-SECRET-do-not-leak");
        builder.UseSetting("Sso:ClientBaseUrl", "http://localhost:5173");
        builder.UseSetting("Sso:Google:RedirectUri", "http://localhost/api/v1/auth/sso/google/callback");

        // Writing marking available under LearnerPersonal egress rules: vendor
        // endpoint (no BaseUrl), SyntheticDataOnly off, cross-border allowed.
        builder.UseSetting("Ai:AllowCrossBorderTransfer", "true");
        builder.UseSetting("Ai:OpenAi:ApiKey", "SENTINEL-OPENAI-API-KEY-do-not-leak-9f3c");
        builder.UseSetting("Ai:OpenAi:BaseUrl", string.Empty);
        builder.UseSetting("Ai:OpenAi:Model", "gpt-config-verification");
        builder.UseSetting("Ai:OpenAi:SyntheticDataOnly", "false");
        builder.UseSetting("Ai:Gemini:ApiKey", "SENTINEL-GEMINI-API-KEY-do-not-leak-a1b2");
        builder.UseSetting("Ai:Gemini:BaseUrl", "https://sentinel-gemini-secret.example/v1");
        builder.UseSetting("Ai:Gemini:Model", "gemini-config-verification");
        // Synthetic-only: a non-vendor BaseUrl with SyntheticDataOnly=false is
        // refused at startup. The BaseUrl remains the secret-endpoint sentinel.
        builder.UseSetting("Ai:Gemini:SyntheticDataOnly", "true");

        builder.UseSetting("Assessment:Writing:Version", "config-verification-rubric-v1");
        builder.UseSetting("Assessment:Writing:DescriptorSource", "config-verification");
        builder.UseSetting("Assessment:Writing:FeedbackLanguage", "vi");
        builder.UseSetting("Assessment:Writing:CriterionGranularity", "per-criterion");
        builder.UseSetting("Assessment:Writing:TaskWeights:Task1", "1");
        builder.UseSetting("Assessment:Writing:TaskWeights:Task2", "2");
        builder.UseSetting("Assessment:WritingMarking:Enabled", "true");
        builder.UseSetting("Assessment:WritingMarking:PrimaryProvider", "OpenAi");
        builder.UseSetting("Assessment:WritingMarking:PromptVersion", "config-verification-prompt-v1");
        builder.UseSetting("Assessment:Speaking:Version", string.Empty);
        builder.UseSetting("Assessment:Speaking:DescriptorSource", string.Empty);

        // Import skills deliberately unconfigured → unavailable / null.
        builder.UseSetting("Import:Parser:Provider", string.Empty);
        builder.UseSetting("Import:Parser:Model", string.Empty);
        builder.UseSetting("Import:Transcription:Provider", string.Empty);
        builder.UseSetting("Import:Transcription:Model", string.Empty);

        builder.UseSetting("Import:Archive:MaxEntries", "177");
        builder.UseSetting("Import:Archive:MaxTotalUncompressedBytes", (111L * 1024 * 1024).ToString());
        builder.UseSetting("Import:Archive:MaxEntryUncompressedBytes", (55L * 1024 * 1024).ToString());
        builder.UseSetting("Import:Archive:MaxCompressionRatio", "17");
        builder.UseSetting("Import:Archive:MaxArchiveBytes", (88L * 1024 * 1024).ToString());
        builder.UseSetting("Import:Archive:ExtractionTimeoutSeconds", "42");
    }

    // Must match AdminConfigEndpointsTests.JwtSigningKey — duplicated here so
    // ConfigureWebHost can set it without reaching into private constants of
    // the test class before the fixture is constructed.
    private const string AdminConfigEndpointsTests_JwtKey =
        "SENTINEL_JWT_SIGNING_KEY_xxxxxxxxxxxxxxxx";

    public override async ValueTask DisposeAsync()
    {
        if (MongoAvailable)
        {
            await new MongoClient($"mongodb://localhost:{_mongo.Value.Port}/?directConnection=true")
                .DropDatabaseAsync(_database);
        }

        await base.DisposeAsync();
    }
}
