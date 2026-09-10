using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Infrastructure.Content;
using Vni.Ielts.Infrastructure.Content.Import;

namespace Vni.Ielts.Infrastructure.Tests.Content.Import;

public sealed class ManifestPackageSplitterTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly IExamPackageValidator Validator = new ExamPackageValidator(
        ExamPackageReader.FromSchemaFile(Path.Combine(RepoRoot, "contracts/schemas/exam.schema.json")));
    private static readonly UserId Actor = UserId.New();

    [Fact]
    public void Per_exam_assets_are_isolated_into_matching_sub_zips()
    {
        var a = Mp3("a");
        var b = Mp3("b");
        using var zip = Open(BuildZip(
            exams: ["reading.json", "listening.json"],
            assets: [("assets/audio/a.mp3", a), ("assets/audio/b.mp3", b)],
            files:
            [
                ("reading.json", ExamJson("reading-a", "assets/audio/a.mp3", a)),
                ("listening.json", ExamJson("listening-b", "assets/audio/b.mp3", b)),
                ("assets/audio/a.mp3", a),
                ("assets/audio/b.mp3", b),
            ]));

        var result = ManifestPackageSplitter.Split(zip, Validator, Actor);

        Assert.True(result.IsSuccess, Describe(result));
        Assert.Equal(2, result.Packages.Count);
        Assert.Equal(["reading.json", "listening.json"], result.Packages.Select(p => p.ExamPath));
        Assert.Equal(["assets/audio/a.mp3"], ListAssets(result.Packages[0].Zip));
        Assert.Equal(["assets/audio/b.mp3"], ListAssets(result.Packages[1].Zip));
        Assert.Contains("reading.json", ListRootJson(result.Packages[0].Zip));
        Assert.Contains("listening.json", ListRootJson(result.Packages[1].Zip));
    }

    [Fact]
    public void Unreferenced_declared_asset_yields_asset_unreferenced()
    {
        var a = Mp3("a");
        var orphan = Mp3("orphan");
        using var zip = Open(BuildZip(
            exams: ["reading.json"],
            assets: [("assets/audio/a.mp3", a), ("assets/audio/orphan.mp3", orphan)],
            files:
            [
                ("reading.json", ExamJson("reading-a", "assets/audio/a.mp3", a)),
                ("assets/audio/a.mp3", a),
                ("assets/audio/orphan.mp3", orphan),
            ]));

        var result = ManifestPackageSplitter.Split(zip, Validator, Actor);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Packages);
        Assert.Contains(result.Findings, f => f.Code == ImportAssetFindingCodes.Unreferenced);
    }

    [Fact]
    public void Shared_asset_is_copied_into_each_referencing_sub_zip()
    {
        var shared = Mp3("shared");
        using var zip = Open(BuildZip(
            exams: ["reading.json", "listening.json"],
            assets: [("assets/audio/shared.mp3", shared)],
            files:
            [
                ("reading.json", ExamJson("reading-shared", "assets/audio/shared.mp3", shared)),
                ("listening.json", ExamJson("listening-shared", "assets/audio/shared.mp3", shared)),
                ("assets/audio/shared.mp3", shared),
            ]));

        var result = ManifestPackageSplitter.Split(zip, Validator, Actor);

        Assert.True(result.IsSuccess, Describe(result));
        Assert.Equal(2, result.Packages.Count);
        Assert.Equal(["assets/audio/shared.mp3"], ListAssets(result.Packages[0].Zip));
        Assert.Equal(["assets/audio/shared.mp3"], ListAssets(result.Packages[1].Zip));
    }

    [Fact]
    public void Exam_in_subdirectory_is_flattened_to_root_basename()
    {
        var a = Mp3("nested");
        using var zip = Open(BuildZip(
            exams: ["exams/reading.json"],
            assets: [("assets/audio/a.mp3", a)],
            files:
            [
                ("exams/reading.json", ExamJson("nested-reading", "assets/audio/a.mp3", a)),
                ("assets/audio/a.mp3", a),
            ]));

        var result = ManifestPackageSplitter.Split(zip, Validator, Actor);

        Assert.True(result.IsSuccess, Describe(result));
        var sub = Assert.Single(result.Packages);
        Assert.Equal("exams/reading.json", sub.ExamPath);
        Assert.Equal("reading.json", sub.RootExamFileName);
        Assert.Equal(["reading.json"], ListRootJson(sub.Zip));
        Assert.DoesNotContain(ListAll(sub.Zip), p => p.StartsWith("exams/", StringComparison.Ordinal));
    }

    private static string Describe(ManifestPackageSplitter.Result result) =>
        string.Join(" | ", result.Findings.Select(f => $"{f.Code}:{f.Pointer}:{f.Message}"));

    private static MemoryStream Open(byte[] bytes) => new(bytes);

    private static IReadOnlyList<string> ListAssets(MemoryStream zip)
    {
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .Where(p => p.StartsWith("assets/", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ListRootJson(MemoryStream zip)
    {
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .Where(p => !p.Contains('/') && p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(p, "manifest.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ListAll(MemoryStream zip)
    {
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();
    }

    private static byte[] Mp3(string salt)
    {
        var payload = Encoding.UTF8.GetBytes(salt);
        var bytes = new byte[3 + payload.Length];
        bytes[0] = 0x49; bytes[1] = 0x44; bytes[2] = 0x33;
        Buffer.BlockCopy(payload, 0, bytes, 3, payload.Length);
        return bytes;
    }

    private static string ExamJson(string profile, string audioRef, byte[] mp3)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(mp3));
        var module = profile.Contains("listening", StringComparison.Ordinal) ? "listening" : "reading";
        return $$"""
        {
          "formatVersion": "2.0", "formatProfile": "vni-practice",
          "scoringProfileRef": "{{profile}}-{{Guid.NewGuid():n}}",
          "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          "assetManifest": [{ "path": "{{audioRef}}", "sha256": "{{sha}}", "sizeBytes": {{mp3.Length}} }],
          "title": "Splitter {{profile}}", "variant": "academic",
          "timingProfile": { "sections": { "{{module}}": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "{{module}}": [
            { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }, { "minRaw": 2, "band": 2 } ] } },
          "sequenceProfile": { "modules": ["{{module}}"] },
          "sections": [{ "module": "{{module}}", "order": 1, "parts": [{
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

    private static byte[] BuildZip(
        string[] exams,
        (string Path, byte[] Bytes)[] assets,
        (string Name, object Content)[] files)
    {
        var examsJson = new JsonArray(exams.Select(e => JsonValue.Create(e)).ToArray());
        var assetsJson = new JsonArray();
        foreach (var (path, bytes) in assets)
        {
            assetsJson.Add(new JsonObject
            {
                ["path"] = path,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            });
        }

        var manifest = new JsonObject
        {
            ["formatVersion"] = "1.0",
            ["exams"] = examsJson,
            ["assets"] = assetsJson,
        }.ToJsonString();

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "manifest.json", manifest);
            foreach (var (name, content) in files)
            {
                if (content is string text) Write(archive, name, text);
                else Write(archive, name, (byte[])content);
            }
        }

        return stream.ToArray();
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void Write(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(bytes);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "contracts/schemas/exam.schema.json")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }
}
