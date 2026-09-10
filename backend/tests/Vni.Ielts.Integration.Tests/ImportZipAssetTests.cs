using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure.Content;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Importing;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Structured ZIP audio must survive approval and be served by the existing
/// learner asset endpoint. Before this slice the inspector ignored
/// <c>assets/**</c>, the sandbox was deleted, and GET returned 404.
/// </summary>
public sealed class ImportAssetTests(ImportZipAssetAppFactory app) : IClassFixture<ImportZipAssetAppFactory>
{
    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [SkippableFact]
    public async Task Structured_zip_with_json_and_referenced_mp3_imports_without_ai_parsing()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var reference = UniqueAudioRef("structured");
        var mp3 = SyntheticMp3();
        var upload = await UploadZipAsync(client, access, PackageJson(reference, mp3), reference, mp3);

        Assert.Equal(HttpStatusCode.Accepted, upload.Status.StatusCode);
        Assert.NotNull(upload.Draft);
        Assert.Equal(ExamImportRoute.StructuredPackage, upload.Draft.Route);
        Assert.DoesNotContain(upload.Package.Findings, f => f.Code == "AI_PARSER_UNAVAILABLE");
        Assert.True(upload.Draft.Assets.Count >= 1);
    }

    [SkippableFact]
    public async Task Approved_zip_audio_is_served_with_exact_bytes_and_content_type()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var reference = UniqueAudioRef("served");
        var mp3 = SyntheticMp3("served");
        var upload = await UploadZipAsync(client, access, PackageJson(reference, mp3), reference, mp3);
        Assert.Equal(HttpStatusCode.Accepted, upload.Status.StatusCode);
        var draftId = upload.Package.ImportDraftId!;

        await ConfirmFullChecklistAsync(client, access, draftId);
        var approve = await SendAsync(client, HttpMethod.Post,
            $"/api/v1/admin/import/packages/{draftId}/approve", access);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var asset = await SendAsync(client, HttpMethod.Get,
            $"/api/v1/exams/{reference}", access);
        Assert.True(
            asset.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent,
            $"expected 200/206, got {asset.StatusCode}");
        Assert.Equal("audio/mpeg", asset.Content.Headers.ContentType?.MediaType);
        Assert.Equal(mp3, await asset.Content.ReadAsByteArrayAsync());
    }

    [SkippableFact]
    public async Task Missing_referenced_asset_is_refused_with_asset_missing()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var reference = UniqueAudioRef("missing");
        var zip = ZipOf(("reading/exam.json", PackageJson(reference, SyntheticMp3("declared-absent"))));
        var upload = await PostZipAsync(client, access, zip);

        Assert.Equal(HttpStatusCode.Accepted, upload.Status.StatusCode);
        Assert.Equal(PackageImportStatus.Rejected, upload.Package.Status);
        var finding = upload.Package.Findings.Single(f => f.Code == "ASSET_MISSING");
        Assert.Contains(reference, finding.Pointer, StringComparison.Ordinal);
        Assert.DoesNotContain('\\', finding.Message);
    }

    [SkippableFact]
    public async Task Different_bytes_at_an_existing_destination_are_refused_and_not_overwritten()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var reference = UniqueAudioRef("conflict");
        var original = SyntheticMp3("original");
        var first = await UploadZipAsync(client, access, PackageJson(reference, original, "firstowner"), reference, original);
        Assert.Equal(HttpStatusCode.Accepted, first.Status.StatusCode);
        var firstId = first.Package.ImportDraftId!;
        await ConfirmFullChecklistAsync(client, access, firstId);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post,
            $"/api/v1/admin/import/packages/{firstId}/approve", access)).StatusCode);

        var conflicting = SyntheticMp3("conflict");
        Assert.NotEqual(original, conflicting);
        var second = await UploadZipAsync(client, access, PackageJson(reference, conflicting, "secondowner"), reference, conflicting);
        Assert.Equal(HttpStatusCode.Accepted, second.Status.StatusCode);
        var secondId = second.Package.ImportDraftId!;
        await ConfirmFullChecklistAsync(client, access, secondId);
        var approve = await SendAsync(client, HttpMethod.Post,
            $"/api/v1/admin/import/packages/{secondId}/approve", access);
        var body = await BodyOf(approve);

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Contains("ASSET", body.GetProperty("code").GetString(), StringComparison.Ordinal);

        var served = await SendAsync(client, HttpMethod.Get, $"/api/v1/exams/{reference}", access);
        Assert.Equal(original, await served.Content.ReadAsByteArrayAsync());
    }

    [SkippableFact]
    public async Task Manifest_zip_audio_is_served_after_package_review_approval()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var reference = UniqueAudioRef("manifest-served");
        var mp3 = SyntheticMp3("manifest-served");
        var zip = ManifestZip(PackageJson(reference, mp3, "manifestserved"), reference, mp3);
        var upload = await PostZipAsync(client, access, zip);

        Assert.Equal(HttpStatusCode.Accepted, upload.Status.StatusCode);
        Assert.Equal(PackageImportStatus.NeedsReview, upload.Package.Status);
        Assert.False(string.IsNullOrWhiteSpace(upload.Package.ImportDraftId));
        Assert.NotNull(upload.Draft);
        Assert.True(upload.Draft!.Assets.Count >= 1);

        var draftId = upload.Package.ImportDraftId!;
        await ConfirmFullChecklistAsync(client, access, draftId);
        var approve = await SendAsync(client, HttpMethod.Post,
            $"/api/v1/admin/import/packages/{draftId}/approve", access);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var asset = await SendAsync(client, HttpMethod.Get,
            $"/api/v1/exams/{reference}", access);
        Assert.True(
            asset.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent,
            $"expected 200/206, got {asset.StatusCode}");
        Assert.Equal("audio/mpeg", asset.Content.Headers.ContentType?.MediaType);
        Assert.Equal(mp3, await asset.Content.ReadAsByteArrayAsync());
    }

    [SkippableFact]
    public async Task Manifest_zip_missing_embedded_mp3_is_rejected()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var reference = UniqueAudioRef("manifest-missing");
        var phantom = SyntheticMp3("declared-absent");
        var sha = Convert.ToHexStringLower(SHA256.HashData(phantom));
        var zip = ZipOf(
            ("manifest.json", $$"""
            {
              "formatVersion": "1.0",
              "exams": ["exam.json"],
              "assets": [{ "path": "{{reference}}", "sha256": "{{sha}}" }]
            }
            """),
            ("exam.json", PackageJson(reference, phantom, "manifestmissing")));

        var upload = await PostZipAsync(client, access, zip);

        Assert.Equal(HttpStatusCode.Accepted, upload.Status.StatusCode);
        Assert.Equal(PackageImportStatus.Rejected, upload.Package.Status);
        Assert.Contains(
            upload.Package.Findings,
            f => f.Code is "ASSET_NOT_FOUND" or "ASSET_MISSING");
        Assert.Null(upload.Package.ImportDraftId);
    }

    [SkippableFact]
    public async Task Real_aptis_listening_test_10_manifest_zip_audio_is_served_after_approval()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        const string aptisPath =
            "/home/phat/Projects/1. Projects/aptis_de_zip/aptis-listening-test-10.zip";
        Skip.IfNot(File.Exists(aptisPath), $"Aptis fixture missing at {aptisPath}");

        var (client, access) = await SignInAsAdminAsync();
        var upload = await PostZipAsync(client, access, await File.ReadAllBytesAsync(aptisPath));

        Assert.Equal(HttpStatusCode.Accepted, upload.Status.StatusCode);
        Assert.Equal(PackageImportStatus.NeedsReview, upload.Package.Status);
        Assert.False(string.IsNullOrWhiteSpace(upload.Package.ImportDraftId));
        Assert.NotNull(upload.Draft);
        Assert.True(upload.Draft!.Assets.Count >= 4, $"expected ≥4 staged assets, got {upload.Draft.Assets.Count}");

        var draftId = upload.Package.ImportDraftId!;
        await ConfirmFullChecklistAsync(client, access, draftId);
        await ResolveOpenWarningsAsync(client, access, draftId);

        var approve = await SendAsync(client, HttpMethod.Post,
            $"/api/v1/admin/import/packages/{draftId}/approve", access);
        Assert.True(
            approve.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"approve returned {approve.StatusCode}: {await approve.Content.ReadAsStringAsync()}");

        const string reference = "assets/aptis-listening-t10/1.mp3";
        var expected = await ReadZipEntryAsync(aptisPath, reference);
        var asset = await SendAsync(client, HttpMethod.Get, $"/api/v1/exams/{reference}", access);
        Assert.True(
            asset.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent,
            $"expected 200/206 for {reference}, got {asset.StatusCode}");
        Assert.Equal("audio/mpeg", asset.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expected, await asset.Content.ReadAsByteArrayAsync());
    }

    [SkippableFact]
    public async Task Multi_exam_manifest_stages_per_exam_assets_and_serves_all_after_approval()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var refA = UniqueAudioRef("multi-a");
        var refB = UniqueAudioRef("multi-b");
        var mp3A = SyntheticMp3("multi-a");
        var mp3B = SyntheticMp3("multi-b");
        var zip = MultiExamManifestZip(
            ("listening-a.json", PackageJson(refA, mp3A, "multia"), refA, mp3A),
            ("listening-b.json", PackageJson(refB, mp3B, "multib"), refB, mp3B));

        var upload = await PostZipAsync(client, access, zip);
        Assert.Equal(HttpStatusCode.Accepted, upload.Status.StatusCode);
        Assert.Equal(PackageImportStatus.NeedsReview, upload.Package.Status);
        Assert.Equal(2, upload.Package.ImportDraftIds.Count);

        var drafts = await LoadAllDraftsAsync(upload.Package);
        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, d => Assert.True(d.Assets.Count >= 1));

        foreach (var draftId in upload.Package.ImportDraftIds)
        {
            await ConfirmFullChecklistAsync(client, access, draftId);
            var approve = await SendAsync(client, HttpMethod.Post,
                $"/api/v1/admin/import/packages/{draftId}/approve", access);
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        }

        await AssertAssetServedAsync(client, access, refA, mp3A);
        await AssertAssetServedAsync(client, access, refB, mp3B);
    }

    [SkippableFact]
    public async Task Multi_exam_manifest_missing_one_mp3_rejects_whole_package()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var refA = UniqueAudioRef("multi-miss-a");
        var refB = UniqueAudioRef("multi-miss-b");
        var mp3A = SyntheticMp3("multi-miss-a");
        var phantomB = SyntheticMp3("multi-miss-b-absent");
        var shaA = Convert.ToHexStringLower(SHA256.HashData(mp3A));
        var shaB = Convert.ToHexStringLower(SHA256.HashData(phantomB));
        var zip = ZipOf(
            ("manifest.json", $$"""
            {
              "formatVersion": "1.0",
              "exams": ["listening-a.json", "listening-b.json"],
              "assets": [
                { "path": "{{refA}}", "sha256": "{{shaA}}" },
                { "path": "{{refB}}", "sha256": "{{shaB}}" }
              ]
            }
            """),
            ("listening-a.json", PackageJson(refA, mp3A, "multimissa")),
            ("listening-b.json", PackageJson(refB, phantomB, "multimissb")),
            (refA, mp3A));

        var upload = await PostZipAsync(client, access, zip);
        Assert.Equal(HttpStatusCode.Accepted, upload.Status.StatusCode);
        Assert.Equal(PackageImportStatus.Rejected, upload.Package.Status);
        Assert.Empty(upload.Package.ImportDraftIds);
        Assert.Null(upload.Package.ImportDraftId);
        Assert.Contains(
            upload.Package.Findings,
            f => f.Code is "ASSET_NOT_FOUND" or "ASSET_MISSING");
    }

    [SkippableFact]
    public async Task Multi_exam_shared_asset_promotes_once()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        var (client, access) = await SignInAsAdminAsync();
        var shared = UniqueAudioRef("multi-shared");
        var mp3 = SyntheticMp3("multi-shared");
        var zip = MultiExamManifestZip(
            ("listening-a.json", PackageJson(shared, mp3, "shareda"), shared, mp3),
            ("listening-b.json", PackageJson(shared, mp3, "sharedb"), shared, mp3));

        var upload = await PostZipAsync(client, access, zip);
        Assert.Equal(HttpStatusCode.Accepted, upload.Status.StatusCode);
        Assert.Equal(PackageImportStatus.NeedsReview, upload.Package.Status);
        Assert.Equal(2, upload.Package.ImportDraftIds.Count);

        foreach (var draftId in upload.Package.ImportDraftIds)
        {
            await ConfirmFullChecklistAsync(client, access, draftId);
            var approve = await SendAsync(client, HttpMethod.Post,
                $"/api/v1/admin/import/packages/{draftId}/approve", access);
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        }

        await AssertAssetServedAsync(client, access, shared, mp3);

        using var scope = app.Services.CreateScope();
        var packages = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var refreshed = await packages.FindAsync(upload.Package.Id, default);
        Assert.NotNull(refreshed);
        Assert.Equal(PackageImportStatus.Imported, refreshed!.Status);
        Assert.Equal(2, refreshed.CreatedVersionIds.Count);
    }

    private static async Task ResolveOpenWarningsAsync(HttpClient client, string access, string draftId)
    {
        var get = await client.SendAsync(
            Request(HttpMethod.Get, $"/api/v1/admin/import/packages/{draftId}", access));
        get.EnsureSuccessStatusCode();
        var body = await BodyOf(get);
        if (!body.TryGetProperty("warnings", out var warnings)) return;
        foreach (var warning in warnings.EnumerateArray())
        {
            if (warning.GetProperty("resolved").GetBoolean()) continue;
            var overrideRequest = Request(
                HttpMethod.Post,
                $"/api/v1/admin/import/packages/{draftId}/warnings/{warning.GetProperty("id").GetString()}/override",
                access);
            overrideRequest.Content = JsonContent.Create(new
            {
                reason = "M5 aptis audio verify — override open warnings.",
            });
            var overrideResponse = await client.SendAsync(overrideRequest);
            Assert.Equal(HttpStatusCode.OK, overrideResponse.StatusCode);
        }
    }

    private static async Task<byte[]> ReadZipEntryAsync(string zipPath, string entryName)
    {
        await using var file = File.OpenRead(zipPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);
        await using var stream = entry!.Open();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private async Task<(HttpClient Client, string Access)> SignInAsAdminAsync()
    {
        var client = NewClient();
        await SsoRoundTripAsync(client);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            var admin = await roles.FindByNameAsync(SystemRoles.Admin, default);
            var user = await users.FindByEmailAsync(Email.Create("stub.learner@example.com"), default);
            Assert.NotNull(admin);
            Assert.NotNull(user);
            if (!user!.HasRole(admin!.Id))
            {
                user.AssignRole(admin.Id);
                await users.SaveAsync(user, default);
            }
        }

        return (client, await SsoRoundTripAsync(client));
    }

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

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string access) =>
        client.SendAsync(Request(method, path, access));

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task ConfirmFullChecklistAsync(HttpClient client, string access, string draftId)
    {
        var checklist = Request(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/checklist", access);
        checklist.Content = JsonContent.Create(new
        {
            confirmed = new[]
            {
                "questions", "options", "wordlimits", "acceptedvariants",
                "transcriptandevidence", "assetmapping",
            },
        });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(checklist)).StatusCode);
    }

    private async Task<(HttpResponseMessage Status, ExamPackage Package, ExamImportDraft? Draft)> UploadZipAsync(
        HttpClient client, string access, string json, string audioRef, byte[] mp3)
    {
        var zip = ZipOf(("reading/exam.json", json), (audioRef, mp3));
        return await PostZipAsync(client, access, zip);
    }

    private async Task<(HttpResponseMessage Status, ExamPackage Package, ExamImportDraft? Draft)> PostZipAsync(
        HttpClient client, string access, byte[] zip)
    {
        var request = Request(HttpMethod.Post, "/api/v1/admin/packages", access);
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(zip);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(part, "package", "package.zip");
        request.Content = form;
        var response = await client.SendAsync(request);
        var packageId = (await BodyOf(response)).GetProperty("packageId").GetString()!;

        using var scope = app.Services.CreateScope();
        var packages = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
        var processor = scope.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();
        var package = await packages.FindAsync(packageId, default);
        Assert.NotNull(package);
        await processor.ProcessAsync(package!, default);
        package = await packages.FindAsync(packageId, default);
        Assert.NotNull(package);

        ExamImportDraft? draft = null;
        if (package!.ImportDraftId is not null)
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();
            draft = await drafts.FindAsync(Guid.Parse(package.ImportDraftId), default);
        }

        return (response, package, draft);
    }

    private async Task<IReadOnlyList<ExamImportDraft>> LoadAllDraftsAsync(ExamPackage package)
    {
        using var scope = app.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();
        var loaded = new List<ExamImportDraft>();
        foreach (var id in package.ImportDraftIds)
        {
            var draft = await drafts.FindAsync(Guid.Parse(id), default);
            Assert.NotNull(draft);
            loaded.Add(draft!);
        }

        return loaded;
    }

    private static async Task AssertAssetServedAsync(
        HttpClient client, string access, string reference, byte[] expected)
    {
        var asset = await SendAsync(client, HttpMethod.Get, $"/api/v1/exams/{reference}", access);
        Assert.True(
            asset.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent,
            $"expected 200/206 for {reference}, got {asset.StatusCode}");
        Assert.Equal("audio/mpeg", asset.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expected, await asset.Content.ReadAsByteArrayAsync());
    }

    private static byte[] MultiExamManifestZip(
        params (string ExamName, string ExamJson, string AudioRef, byte[] Mp3)[] exams)
    {
        var assets = exams
            .GroupBy(e => e.AudioRef, StringComparer.Ordinal)
            .Select(g =>
            {
                var mp3 = g.First().Mp3;
                var sha = Convert.ToHexStringLower(SHA256.HashData(mp3));
                return (Path: g.Key, Sha: sha, Bytes: mp3);
            })
            .ToList();

        var examList = string.Join(", ", exams.Select(e => $"\"{e.ExamName}\""));
        var assetList = string.Join(", ", assets.Select(a =>
            $$"""{ "path": "{{a.Path}}", "sha256": "{{a.Sha}}" }"""));

        var entries = new List<(string Name, object Content)>
        {
            ("manifest.json", $$"""
            {
              "formatVersion": "1.0",
              "exams": [{{examList}}],
              "assets": [{{assetList}}]
            }
            """),
        };
        foreach (var exam in exams)
            entries.Add((exam.ExamName, exam.ExamJson));
        foreach (var asset in assets)
            entries.Add((asset.Path, asset.Bytes));

        return ZipOf(entries.ToArray());
    }

    private static byte[] ManifestZip(string examJson, string audioRef, byte[] mp3)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(mp3));
        return ZipOf(
            ("manifest.json", $$"""
            {
              "formatVersion": "1.0",
              "exams": ["exam.json"],
              "assets": [{ "path": "{{audioRef}}", "sha256": "{{sha}}" }]
            }
            """),
            ("exam.json", examJson),
            (audioRef, mp3));
    }

    private static byte[] ZipOf(params (string Name, object Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var output = entry.Open();
                if (content is string text)
                {
                    using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    writer.Write(text);
                }
                else
                {
                    output.Write((byte[])content);
                }
            }
        }

        return stream.ToArray();
    }

    private static byte[] SyntheticMp3(string salt = "zip-asset")
    {
        var payload = Encoding.UTF8.GetBytes(salt + Guid.NewGuid().ToString("n"));
        var bytes = new byte[3 + payload.Length + 8];
        bytes[0] = 0x49;
        bytes[1] = 0x44;
        bytes[2] = 0x33;
        Buffer.BlockCopy(payload, 0, bytes, 3, payload.Length);
        return bytes;
    }

    private static string UniqueAudioRef(string label) =>
        $"assets/aptis-listening-t24/{label}-{Guid.NewGuid():n}.mp3";

    private static string PackageJson(string audioRef, byte[] mp3, string profile = "zipasset")
    {
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(mp3));
        return $$"""
        {
          "formatVersion": "2.0", "formatProfile": "vni-practice",
          "scoringProfileRef": "{{profile}}-{{Guid.NewGuid():n}}",
          "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          "assetManifest": [{ "path": "{{audioRef}}", "sha256": "{{sha256}}", "sizeBytes": {{mp3.Length}} }],
          "title": "ZIP asset HTTP test", "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [
            { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }, { "minRaw": 2, "band": 2 } ] } },
          "sequenceProfile": { "modules": ["reading"] },
          "sections": [{ "module": "reading", "order": 1, "parts": [{
            "order": 1, "kind": "passage", "body": "Evidence here.",
            "audio": "{{audioRef}}",
            "questions": [{
              "id": "q-1", "order": 1, "type": "multiple-select", "marks": 2,
              "options": [{ "key": "A", "text": "Alpha" }, { "key": "B", "text": "Beta" }],
              "group": { "id": "bank-1", "instruction": "Choose." },
              "slots": [
                { "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } },
                { "id": "slot-2", "number": 2, "answerKey": { "accepted": ["B"] } }
              ],
              "explanation": { "shortReason": "Both are stated.", "evidence": ["Evidence here."] }
            }]
          }]}]
        }
        """;
    }
}

public class ImportZipAssetAppFactory : SsoAppFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ObjectStorage:ServiceUrl", "http://localhost:9000");
        builder.UseSetting("ObjectStorage:AccessKey", "vni-local");
        builder.UseSetting("ObjectStorage:SecretKey", "vni-local-dev-only");
        builder.UseSetting("ObjectStorage:ExamAssetsBucket", "vni-exam-assets");
        builder.UseSetting("ObjectStorage:DictationBucket", "vni-audio-90d");
        builder.UseSetting("ObjectStorage:ForcePathStyle", "true");
        builder.UseSetting("ObjectStorage:Region", "us-east-1");
    }
}

public sealed class ImportAssetFaultFactory : ImportZipAssetAppFactory
{
    public MutableImportApprovalCommitHooks Hooks { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IImportApprovalCommitHooks>();
            services.AddSingleton(Hooks);
            services.AddSingleton<IImportApprovalCommitHooks>(Hooks);
        });
    }
}

public sealed class ImportAssetFaultTests(ImportAssetFaultFactory app) : IClassFixture<ImportAssetFaultFactory>
{
    [SkippableFact]
    public async Task Fault_after_promotion_leaves_draft_unapproved_and_catalogue_absent()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        app.Hooks.AfterAssetPromotion = (_, _) => throw new InvalidOperationException("injected-asset-fault");
        try
        {
            var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var start = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
            var url = new Uri((await start.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("authorizationUrl").GetString()!);
            var callback = await client.GetAsync(url.PathAndQuery);
            var code = System.Web.HttpUtility.ParseQueryString(callback.Headers.Location!.Query)["code"];
            var complete = await client.PostAsJsonAsync("/api/v1/auth/sso/complete", new { handoffCode = code });
            complete.EnsureSuccessStatusCode();

            using (var scope = app.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
                var admin = await roles.FindByNameAsync(SystemRoles.Admin, default);
                var user = await users.FindByEmailAsync(Email.Create("stub.learner@example.com"), default);
                if (admin is not null && user is not null && !user.HasRole(admin.Id))
                {
                    user.AssignRole(admin.Id);
                    await users.SaveAsync(user, default);
                }
            }

            var login = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
            var loginUrl = new Uri((await login.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("authorizationUrl").GetString()!);
            var loginCb = await client.GetAsync(loginUrl.PathAndQuery);
            var loginCode = System.Web.HttpUtility.ParseQueryString(loginCb.Headers.Location!.Query)["code"];
            var loginDone = await client.PostAsJsonAsync("/api/v1/auth/sso/complete", new { handoffCode = loginCode });
            var access = (await loginDone.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("accessToken").GetString()!;

            var reference = $"assets/aptis-listening-t24/fault-{Guid.NewGuid():n}.mp3";
            var mp3 = new byte[] { 0x49, 0x44, 0x33, 0x66, 0x61, 0x75, 0x6c, 0x74 };
            var sha = Convert.ToHexStringLower(SHA256.HashData(mp3));
            var json = $$"""
            {
              "formatVersion": "2.0", "formatProfile": "vni-practice",
              "scoringProfileRef": "faultasset-{{Guid.NewGuid():n}}",
              "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              "assetManifest": [{ "path": "{{reference}}", "sha256": "{{sha}}", "sizeBytes": {{mp3.Length}} }],
              "title": "ZIP asset fault", "variant": "academic",
              "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
              "scoringProfile": { "rawToBand": { "reading": [
                { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }, { "minRaw": 2, "band": 2 } ] } },
              "sequenceProfile": { "modules": ["reading"] },
              "sections": [{ "module": "reading", "order": 1, "parts": [{
                "order": 1, "kind": "passage", "body": "Evidence here.",
                "audio": "{{reference}}",
                "questions": [{
                  "id": "q-1", "order": 1, "type": "multiple-select", "marks": 2,
                  "options": [{ "key": "A", "text": "Alpha" }, { "key": "B", "text": "Beta" }],
                  "group": { "id": "bank-1", "instruction": "Choose." },
                  "slots": [
                    { "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } },
                    { "id": "slot-2", "number": 2, "answerKey": { "accepted": ["B"] } }
                  ],
                  "explanation": { "shortReason": "Both are stated.", "evidence": ["Evidence here."] }
                }]
              }]}]
            }
            """;

            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var jsonEntry = archive.CreateEntry("reading/exam.json", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(jsonEntry.Open(), Encoding.UTF8))
                    writer.Write(json);
                var audioEntry = archive.CreateEntry(reference, CompressionLevel.Optimal);
                using var output = audioEntry.Open();
                output.Write(mp3);
            }

            var upload = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/packages");
            upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            upload.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
            var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(zipStream.ToArray());
            part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(part, "package", "package.zip");
            upload.Content = form;
            var uploaded = await client.SendAsync(upload);
            Assert.Equal(HttpStatusCode.Accepted, uploaded.StatusCode);
            var packageId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("packageId").GetString()!;
            string draftId;
            using (var scope = app.Services.CreateScope())
            {
                var packages = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
                var processor = scope.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();
                var package = await packages.FindAsync(packageId, default);
                Assert.NotNull(package);
                await processor.ProcessAsync(package!, default);
                package = await packages.FindAsync(packageId, default);
                Assert.NotNull(package?.ImportDraftId);
                draftId = package!.ImportDraftId!;
            }

            var checklist = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/checklist");
            checklist.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            checklist.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
            checklist.Content = JsonContent.Create(new
            {
                confirmed = new[]
                {
                    "questions", "options", "wordlimits", "acceptedvariants",
                    "transcriptandevidence", "assetmapping",
                },
            });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(checklist)).StatusCode);

            ExamVersionId versionId;
            using (var scope = app.Services.CreateScope())
            {
                var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();
                var draft = await drafts.FindAsync(Guid.Parse(draftId), default);
                versionId = draft!.Version.Id;
            }

            var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/approve");
            approve.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            approve.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
            var approved = await client.SendAsync(approve);
            Assert.True((int)approved.StatusCode >= 500, $"expected 5xx, got {approved.StatusCode}");

            using var after = app.Services.CreateScope();
            var catalogue = after.ServiceProvider.GetRequiredService<IExamCatalogue>();
            var draftsAfter = after.ServiceProvider.GetRequiredService<IImportDraftStore>();
            Assert.Null(await catalogue.FindAsync(versionId, default));
            var remaining = await draftsAfter.FindAsync(Guid.Parse(draftId), default);
            Assert.Equal(ImportApprovalState.ReviewRequired, remaining!.ApprovalState);

            var pending = await MongoImportAssetCleanup.PendingAsync(
                after.ServiceProvider.GetRequiredService<MongoContext>(), default);
            var intent = Assert.Single(pending, i => i.DraftId == draftId);
            Assert.Equal(nameof(ImportAssetCleanupReason.ApprovalCommitFailed), intent.Reason);
            Assert.Equal([reference], intent.References);
        }
        finally
        {
            app.Hooks.AfterAssetPromotion = null;
        }
    }

    [SkippableFact]
    public async Task Cleanup_running_concurrently_with_retry_approval_never_leaves_an_approved_version_pointing_to_deleted_asset()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);
        Skip.IfNot(ObjectStorageAppFactory.MinioAvailable, ObjectStorageAppFactory.MinioSkipReason);

        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var start = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
        var url = new Uri((await start.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("authorizationUrl").GetString()!);
        var callback = await client.GetAsync(url.PathAndQuery);
        var code = System.Web.HttpUtility.ParseQueryString(callback.Headers.Location!.Query)["code"];
        var complete = await client.PostAsJsonAsync("/api/v1/auth/sso/complete", new { handoffCode = code });
        complete.EnsureSuccessStatusCode();

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            var admin = await roles.FindByNameAsync(SystemRoles.Admin, default);
            var user = await users.FindByEmailAsync(Email.Create("stub.learner@example.com"), default);
            if (admin is not null && user is not null && !user.HasRole(admin.Id))
            {
                user.AssignRole(admin.Id);
                await users.SaveAsync(user, default);
            }
        }

        var login = await client.PostAsJsonAsync("/api/v1/auth/sso/google/start", new { });
        var loginUrl = new Uri((await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("authorizationUrl").GetString()!);
        var loginCb = await client.GetAsync(loginUrl.PathAndQuery);
        var loginCode = System.Web.HttpUtility.ParseQueryString(loginCb.Headers.Location!.Query)["code"];
        var loginDone = await client.PostAsJsonAsync("/api/v1/auth/sso/complete", new { handoffCode = loginCode });
        var access = (await loginDone.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;

        var reference = $"assets/aptis-listening-t24/race-{Guid.NewGuid():n}.mp3";
        var mp3 = new byte[] { 0x49, 0x44, 0x33, 0x72, 0x61, 0x63, 0x65 };
        var sha = Convert.ToHexStringLower(SHA256.HashData(mp3));
        var json = $$"""
        {
          "formatVersion": "2.0", "formatProfile": "vni-practice",
          "scoringProfileRef": "raceasset-{{Guid.NewGuid():n}}",
          "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          "assetManifest": [{ "path": "{{reference}}", "sha256": "{{sha}}", "sizeBytes": {{mp3.Length}} }],
          "title": "ZIP asset race", "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [
            { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }, { "minRaw": 2, "band": 2 } ] } },
          "sequenceProfile": { "modules": ["reading"] },
          "sections": [{ "module": "reading", "order": 1, "parts": [{
            "order": 1, "kind": "passage", "body": "Race evidence.",
            "audio": "{{reference}}",
            "questions": [{
              "id": "q-1", "order": 1, "type": "multiple-select", "marks": 2,
              "options": [{ "key": "A", "text": "Alpha" }, { "key": "B", "text": "Beta" }],
              "group": { "id": "bank-1", "instruction": "Choose." },
              "slots": [
                { "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } },
                { "id": "slot-2", "number": 2, "answerKey": { "accepted": ["B"] } }
              ],
              "explanation": { "shortReason": "Both are stated.", "evidence": ["Race evidence."] }
            }]
          }]}]
        }
        """;

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var jsonEntry = archive.CreateEntry("reading/exam.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(jsonEntry.Open(), Encoding.UTF8))
                writer.Write(json);
            var audioEntry = archive.CreateEntry(reference, CompressionLevel.Optimal);
            using var output = audioEntry.Open();
            output.Write(mp3);
        }

        var upload = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/packages");
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        upload.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(zipStream.ToArray());
        part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(part, "package", "package.zip");
        upload.Content = form;
        var uploaded = await client.SendAsync(upload);
        Assert.Equal(HttpStatusCode.Accepted, uploaded.StatusCode);
        var packageId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("packageId").GetString()!;
        string draftId;
        using (var scope = app.Services.CreateScope())
        {
            var packages = scope.ServiceProvider.GetRequiredService<IExamPackageRepository>();
            var processor = scope.ServiceProvider.GetRequiredService<PackageIngestionProcessor>();
            var package = await packages.FindAsync(packageId, default);
            Assert.NotNull(package);
            await processor.ProcessAsync(package!, default);
            package = await packages.FindAsync(packageId, default);
            Assert.NotNull(package?.ImportDraftId);
            draftId = package!.ImportDraftId!;
        }

        var checklist = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/checklist");
        checklist.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        checklist.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        checklist.Content = JsonContent.Create(new
        {
            confirmed = new[]
            {
                "questions", "options", "wordlimits", "acceptedvariants",
                "transcriptandevidence", "assetmapping",
            },
        });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(checklist)).StatusCode);

        ExamVersionId versionId;
        using (var scope = app.Services.CreateScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();
            var draft = await drafts.FindAsync(Guid.Parse(draftId), default);
            versionId = draft!.Version.Id;
        }

        app.Hooks.AfterAssetPromotion = (_, _) => throw new InvalidOperationException("injected-first-fault");
        var firstApprove = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/approve");
        firstApprove.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        firstApprove.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        Assert.True((int)(await client.SendAsync(firstApprove)).StatusCode >= 500);

        app.Hooks.AfterAssetPromotion = null;

        var retryApprove = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/approve");
        retryApprove.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        retryApprove.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        var approveTask = client.SendAsync(retryApprove);
        var cleanupTask = Task.Run(async () =>
        {
            using var scope = app.Services.CreateScope();
            var assets = scope.ServiceProvider.GetRequiredService<IImportExamAssetStore>();
            var catalogue = scope.ServiceProvider.GetRequiredService<IExamCatalogue>();
            await assets.ProcessPendingCleanupAsync(catalogue, default);
        });

        await Task.WhenAll(approveTask, cleanupTask);
        var retry = await approveTask;

        using var after = app.Services.CreateScope();
        var catalogueAfter = after.ServiceProvider.GetRequiredService<IExamCatalogue>();
        var draftStoreAfter = after.ServiceProvider.GetRequiredService<IImportDraftStore>();
        var draftAfter = await draftStoreAfter.FindAsync(Guid.Parse(draftId), default);
        var versionAfter = await catalogueAfter.FindAsync(versionId, default);

        if (retry.StatusCode == HttpStatusCode.OK)
        {
            Assert.NotNull(versionAfter);
            var asset = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/exams/{reference}");
            asset.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            var served = await client.SendAsync(asset);
            Assert.True(served.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent);
        }
        else
        {
            Assert.True((int)retry.StatusCode >= 400);
            Assert.Null(versionAfter);
            Assert.Equal(ImportApprovalState.ReviewRequired, draftAfter!.ApprovalState);
        }

        app.Hooks.AfterAssetPromotion = null;
    }
}
