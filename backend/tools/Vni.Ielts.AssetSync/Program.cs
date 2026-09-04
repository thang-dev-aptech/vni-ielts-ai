// Vni.Ielts.AssetSync — moves exam media between fixtures/exams/assets and the
// object-storage folder the API reads it from.
//
// Why this exists (2026-09-04): the Cambridge and VOL 9 batch left 51 Listening
// recordings (~510 MB) in fixtures/exams/assets. The owner decided they live in
// Cloudflare R2, not in git — one bucket, one folder per class (ADR-0016). A
// clean checkout therefore has the papers' JSON but not their audio; `pull`
// fetches it, `push` publishes it. Keys are exactly what the API reads:
// {ExamAssetsPrefix}{file name}, because a package reference `assets/x.mp3` is
// stored under the key `x.mp3` inside the class's folder.
//
//   dotnet run --project backend/tools/Vni.Ielts.AssetSync -- push [--secrets <path>] [--dry-run]
//   dotnet run --project backend/tools/Vni.Ielts.AssetSync -- pull [--secrets <path>] [--dry-run]
//
// Configuration comes from the same ObjectStorage section the API uses —
// `backend/src/Vni.Ielts.Api/secrets.develop.json` by default, or the
// ObjectStorage__* environment variables, which win. Nothing here is logged
// but bucket, prefix and file names.

using System.Security.Cryptography;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

var command = args.Length > 0 ? args[0] : "help";
var dryRun = args.Contains("--dry-run");
var secretsPath = OptionValue(args, "--secrets")
    ?? Path.Combine(RepoRoot(), "backend", "src", "Vni.Ielts.Api", "secrets.develop.json");

if (command is not ("push" or "pull"))
{
    Console.Error.WriteLine("usage: Vni.Ielts.AssetSync push|pull [--secrets <path>] [--dry-run]");
    return 2;
}

var storage = ObjectStorage.Load(secretsPath);
if (!storage.IsConfigured)
{
    Console.Error.WriteLine($"ObjectStorage is not configured in {secretsPath} (ServiceUrl, AccessKey, SecretKey).");
    return 2;
}

var assetsDir = Path.Combine(RepoRoot(), "fixtures", "exams", "assets");
Directory.CreateDirectory(assetsDir);

IAmazonS3 client = new AmazonS3Client(
    new BasicAWSCredentials(storage.AccessKey, storage.SecretKey),
    new AmazonS3Config
    {
        ServiceURL = storage.ServiceUrl,
        ForcePathStyle = storage.ForcePathStyle,
        AuthenticationRegion = storage.Region,
        // AWS SDK v4 signs uploads as aws-chunked with a trailing checksum by
        // default; R2 answers "STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER not
        // implemented". Checksums only when an operation requires them keeps
        // the plain SigV4 body R2 and MinIO both accept. Same setting as the API.
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    });

Console.WriteLine($"{command}: {assetsDir}  <->  {storage.ExamAssetsBucket}/{storage.ExamAssetsPrefix}{(dryRun ? "  (dry run)" : string.Empty)}");

return command == "push"
    ? await PushAsync(client, storage, assetsDir, dryRun)
    : await PullAsync(client, storage, assetsDir, dryRun);

static async Task<int> PushAsync(IAmazonS3 client, ObjectStorage storage, string assetsDir, bool dryRun)
{
    var remote = await ListRemoteAsync(client, storage);
    var uploaded = 0; var skipped = 0;

    foreach (var file in Directory.EnumerateFiles(assetsDir).OrderBy(f => f, StringComparer.Ordinal))
    {
        var name = Path.GetFileName(file);
        if (name.StartsWith('.')) continue;

        var key = storage.ExamAssetsPrefix + name;
        var info = new FileInfo(file);

        // Same size already there → same file, for our purposes; a re-upload
        // of 500 MB to fix nothing is what makes people stop running the tool.
        if (remote.TryGetValue(key, out var size) && size == info.Length)
        {
            skipped++;
            continue;
        }

        Console.WriteLine($"  put {key}  ({info.Length / 1_048_576.0:F1} MB)");
        uploaded++;
        if (dryRun) continue;

        await using var stream = File.OpenRead(file);
        var request = new PutObjectRequest
        {
            BucketName = storage.ExamAssetsBucket,
            Key = key,
            InputStream = stream,
            ContentType = ContentTypeFor(name),
            AutoCloseStream = false,
            UseChunkEncoding = false,
        };
        request.Metadata["sha256"] = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
        stream.Position = 0;
        await client.PutObjectAsync(request);
    }

    Console.WriteLine($"push done: {uploaded} uploaded, {skipped} already present.");
    return 0;
}

static async Task<int> PullAsync(IAmazonS3 client, ObjectStorage storage, string assetsDir, bool dryRun)
{
    var remote = await ListRemoteAsync(client, storage);
    var downloaded = 0; var skipped = 0;

    foreach (var (key, size) in remote.OrderBy(kv => kv.Key, StringComparer.Ordinal))
    {
        var name = key[storage.ExamAssetsPrefix.Length..];
        if (name.Length == 0 || name.Contains('/')) continue; // imports/… and other sub-folders are not fixtures

        var target = Path.Combine(assetsDir, name);
        if (File.Exists(target) && new FileInfo(target).Length == size)
        {
            skipped++;
            continue;
        }

        Console.WriteLine($"  get {key}  ({size / 1_048_576.0:F1} MB)");
        downloaded++;
        if (dryRun) continue;

        using var response = await client.GetObjectAsync(storage.ExamAssetsBucket, key);
        await using var output = File.Create(target);
        await response.ResponseStream.CopyToAsync(output);
    }

    Console.WriteLine($"pull done: {downloaded} downloaded, {skipped} already present.");
    return 0;
}

static async Task<Dictionary<string, long>> ListRemoteAsync(IAmazonS3 client, ObjectStorage storage)
{
    var result = new Dictionary<string, long>(StringComparer.Ordinal);
    var request = new ListObjectsV2Request { BucketName = storage.ExamAssetsBucket, Prefix = storage.ExamAssetsPrefix };

    ListObjectsV2Response response;
    do
    {
        response = await client.ListObjectsV2Async(request);
        foreach (var entry in response.S3Objects ?? [])
            result[entry.Key] = entry.Size ?? 0;
        request.ContinuationToken = response.NextContinuationToken;
    }
    while (response.IsTruncated == true);

    return result;
}

static string ContentTypeFor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
{
    ".mp3" => "audio/mpeg",
    ".m4a" or ".mp4" => "audio/mp4",
    ".wav" => "audio/wav",
    ".ogg" or ".oga" => "audio/ogg",
    ".webm" => "audio/webm",
    ".png" => "image/png",
    ".jpg" or ".jpeg" => "image/jpeg",
    ".svg" => "image/svg+xml",
    _ => "application/octet-stream",
};

static string? OptionValue(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string RepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "fixtures", "exams"))) return dir.FullName;
    }

    // `dotnet run` from the repository root.
    return Directory.GetCurrentDirectory();
}

sealed record ObjectStorage(
    string ServiceUrl, string AccessKey, string SecretKey, string Region, bool ForcePathStyle,
    string ExamAssetsBucket, string ExamAssetsPrefix)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServiceUrl) && !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>The API's own precedence: environment variables over the secrets file.</summary>
    public static ObjectStorage Load(string secretsPath)
    {
        JsonElement? section = null;
        if (File.Exists(secretsPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(secretsPath));
            if (doc.RootElement.TryGetProperty("ObjectStorage", out var found)) section = found.Clone();
        }

        string Get(string key, string fallback = "")
        {
            var env = Environment.GetEnvironmentVariable($"ObjectStorage__{key}");
            if (!string.IsNullOrWhiteSpace(env)) return env;
            return section is { } s && s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? fallback
                : fallback;
        }

        var forcePathStyle = section is { } sec && sec.TryGetProperty("ForcePathStyle", out var fps)
            ? fps.ValueKind == JsonValueKind.True
            : true;

        var prefix = Get("ExamAssetsPrefix").Trim().Trim('/');
        return new ObjectStorage(
            Get("ServiceUrl"), Get("AccessKey"), Get("SecretKey"), Get("Region", "auto"), forcePathStyle,
            Get("ExamAssetsBucket", "vni-exam-assets"), prefix.Length == 0 ? string.Empty : prefix + "/");
    }
}
