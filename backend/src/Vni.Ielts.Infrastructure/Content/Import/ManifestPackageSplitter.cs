using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using PackageFinding = Vni.Ielts.Domain.Exams.PackageFinding;

namespace Vni.Ielts.Infrastructure.Content.Import;

/// <summary>
/// Splits a multi-exam (or single-exam) manifest ZIP into N one-exam sub-ZIPs
/// the structured import pipeline already accepts
/// (<c>manifest.json</c> + exactly one root <c>.json</c> + that exam's assets).
/// </summary>
internal static class ManifestPackageSplitter
{
    public sealed record SubPackage(string ExamPath, string RootExamFileName, MemoryStream Zip);

    public sealed record Result(
        IReadOnlyList<SubPackage> Packages,
        IReadOnlyList<PackageFinding> Findings)
    {
        public bool IsSuccess => Findings.Count == 0 && Packages.Count > 0;
    }

    public static Result Split(Stream zip, IExamPackageValidator validator, UserId createdBy)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentNullException.ThrowIfNull(validator);
        if (!zip.CanSeek)
            throw new ArgumentException("The archive stream must be seekable.", nameof(zip));

        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        var entriesByPath = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            var path = entry.FullName.Replace('\\', '/');
            if (path.StartsWith('/')) path = path[1..];
            entriesByPath[path] = entry;
        }

        if (!entriesByPath.TryGetValue("manifest.json", out var manifestEntry))
        {
            return new Result([],
            [
                new PackageFinding("structural", "MANIFEST_MISSING", "/manifest.json",
                    "The archive has no manifest.json."),
            ]);
        }

        JsonObject? root;
        try
        {
            using var manifestStream = manifestEntry.Open();
            root = JsonNode.Parse(manifestStream) as JsonObject;
        }
        catch (JsonException)
        {
            return new Result([],
            [
                new PackageFinding("structural", "MANIFEST_INVALID", "/manifest.json",
                    "manifest.json is not valid JSON."),
            ]);
        }

        if (root is null)
        {
            return new Result([],
            [
                new PackageFinding("structural", "MANIFEST_INVALID", "/manifest.json",
                    "manifest.json must be a JSON object."),
            ]);
        }

        var formatVersion = root["formatVersion"]?.GetValue<string>() ?? "1.0";
        var examPaths = (root["exams"] as JsonArray)?
            .Select(n => n?.GetValue<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList() ?? [];

        if (examPaths.Count == 0)
        {
            return new Result([],
            [
                new PackageFinding("structural", "MANIFEST_INVALID", "/manifest.json/exams",
                    "manifest.json declares no exam files."),
            ]);
        }

        var declaredAssets = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root["assets"] is JsonArray assetsNode)
        {
            foreach (var node in assetsNode)
            {
                if (node is not JsonObject asset
                    || asset["path"]?.GetValue<string>() is not { Length: > 0 } path
                    || asset["sha256"]?.GetValue<string>() is not { Length: > 0 } sha)
                {
                    return new Result([],
                    [
                        new PackageFinding("structural", "MANIFEST_INVALID", "/manifest.json/assets",
                            "Each asset needs a path and a sha256."),
                    ]);
                }

                declaredAssets[path] = sha;
            }
        }

        var findings = new List<PackageFinding>();
        var usedAssets = new HashSet<string>(StringComparer.Ordinal);
        var rootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = new List<SubPackage>();

        foreach (var examPath in examPaths)
        {
            if (!entriesByPath.TryGetValue(examPath, out var examEntry))
            {
                findings.Add(new PackageFinding(
                    "structural", "EXAM_FILE_NOT_FOUND", "/manifest.json/exams",
                    $"Declared exam file '{examPath}' is not present in the archive."));
                continue;
            }

            string examJson;
            using (var examStream = examEntry.Open())
            using (var reader = new StreamReader(examStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                examJson = reader.ReadToEnd();

            var validation = validator.Validate(
                examJson, ExamDefinitionId.New(), versionNumber: 1);
            if (!validation.IsValid || validation.Version is null)
            {
                findings.AddRange(validation.Findings.Select(f =>
                    new PackageFinding(f.Severity, f.Code, $"/{examPath}{f.Path}", f.Message)));
                continue;
            }

            var referenced = ImportAssetPaths.ZipBindableReferences(validation.Version);
            var subAssets = new List<(string Path, string Sha256, byte[] Bytes)>();
            foreach (var reference in referenced)
            {
                if (!declaredAssets.TryGetValue(reference, out var sha))
                {
                    // Exam may declare assets only in exam.json assetManifest;
                    // still require the bytes in the parent ZIP.
                    sha = "";
                }

                if (!entriesByPath.TryGetValue(reference, out var assetEntry))
                {
                    findings.Add(new PackageFinding(
                        "structural", ImportAssetFindingCodes.Missing, $"/{reference}",
                        $"Referenced asset '{reference}' is not present in the archive."));
                    continue;
                }

                using var assetStream = assetEntry.Open();
                using var buffer = new MemoryStream();
                assetStream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                if (!string.IsNullOrEmpty(sha))
                {
                    var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    if (!string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new PackageFinding(
                            "structural", "CHECKSUM_MISMATCH", $"/{reference}",
                            "This asset's content does not match its declared checksum."));
                        continue;
                    }
                }
                else
                {
                    sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                }

                usedAssets.Add(reference);
                subAssets.Add((reference, sha, bytes));
            }

            var rootExamName = Path.GetFileName(examPath.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(rootExamName)
                || !rootExamName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new PackageFinding(
                    "structural", "EXAM_PATH_INVALID", $"/{examPath}",
                    "Each exam path must end with a .json file name."));
                continue;
            }

            if (!rootNames.Add(rootExamName))
            {
                findings.Add(new PackageFinding(
                    "structural", "DUPLICATE_EXAM_BASENAME", $"/{examPath}",
                    $"Exam file name '{rootExamName}' collides with another exam in this package after flattening."));
                continue;
            }

            var subManifest = BuildSubManifest(formatVersion, rootExamName, subAssets);
            var subZip = BuildSubZip(subManifest, rootExamName, examJson, subAssets);
            packages.Add(new SubPackage(examPath, rootExamName, subZip));
        }

        foreach (var (path, _) in declaredAssets)
        {
            if (!usedAssets.Contains(path))
            {
                findings.Add(new PackageFinding(
                    "structural", ImportAssetFindingCodes.Unreferenced, $"/{path}",
                    $"Declared asset '{path}' is not referenced by any exam in this package."));
            }
        }

        if (findings.Count > 0)
        {
            foreach (var pkg in packages)
                pkg.Zip.Dispose();
            return new Result([], findings);
        }

        zip.Position = 0;
        return new Result(packages, []);
    }

    private static string BuildSubManifest(
        string formatVersion,
        string rootExamName,
        IReadOnlyList<(string Path, string Sha256, byte[] Bytes)> assets)
    {
        var assetArray = new JsonArray();
        foreach (var (path, sha, _) in assets)
        {
            assetArray.Add(new JsonObject
            {
                ["path"] = path,
                ["sha256"] = sha,
            });
        }

        var obj = new JsonObject
        {
            ["formatVersion"] = formatVersion.StartsWith("1.", StringComparison.Ordinal)
                ? formatVersion
                : "1.0",
            ["exams"] = new JsonArray(rootExamName),
            ["assets"] = assetArray,
        };
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static MemoryStream BuildSubZip(
        string manifestJson,
        string rootExamName,
        string examJson,
        IReadOnlyList<(string Path, string Sha256, byte[] Bytes)> assets)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(archive, "manifest.json", manifestJson);
            WriteText(archive, rootExamName, examJson);
            foreach (var (path, _, bytes) in assets)
                WriteBytes(archive, path, bytes);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteText(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(text);
    }

    private static void WriteBytes(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(bytes);
    }
}
