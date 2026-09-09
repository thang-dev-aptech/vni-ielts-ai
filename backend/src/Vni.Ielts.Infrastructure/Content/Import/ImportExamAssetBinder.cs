using System.Security.Cryptography;
using System.Text.Json;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;
using PackageFinding = Vni.Ielts.Application.Importing.PackageFinding;

namespace Vni.Ielts.Infrastructure.Content.Import;

internal static class ImportExamAssetBinder
{
    public static async Task<(IReadOnlyList<PackageFinding> Findings, IReadOnlyList<ImportAssetManifestEntry> Manifest)>
        BindAsync(
            IImportExamAssetStore store,
            ExamVersion version,
            string packageJson,
            string sandboxDirectory,
            IReadOnlyList<string> zipAssetEntries,
            Guid draftId,
            CancellationToken ct)
    {
        var zipIndex = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in zipAssetEntries)
        {
            if (!ImportAssetPaths.TryCanonicalize(entry, out var canonical, out var pathCode))
            {
                return ([ImportAssetPaths.Finding(
                    pathCode ?? ImportAssetFindingCodes.PathInvalid, entry,
                    "Asset path is not a canonical assets/ reference.")], []);
            }

            if (!zipIndex.TryAdd(canonical, entry))
            {
                return ([ImportAssetPaths.Finding(
                    ImportAssetFindingCodes.PathInvalid, canonical,
                    "Asset path is a duplicate after normalisation.")], []);
            }
        }

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in ImportAssetPaths.ZipBindableReferences(version))
        {
            if (!ImportAssetPaths.TryCanonicalize(raw, out var canonical, out var pathCode))
            {
                return ([ImportAssetPaths.Finding(
                    pathCode ?? ImportAssetFindingCodes.PathInvalid, raw,
                    "Exam asset reference is not a canonical assets/ path.")], []);
            }

            referenced.Add(canonical);
        }

        foreach (var zipKey in zipIndex.Keys)
        {
            if (!referenced.Contains(zipKey))
            {
                return ([ImportAssetPaths.Finding(
                    ImportAssetFindingCodes.Unreferenced, zipKey,
                    "Package contains an asset that no exam reference uses.")], []);
            }
        }

        var declared = DeclaredChecksums(packageJson);
        var manifest = new List<ImportAssetManifestEntry>(referenced.Count);
        var staged = new List<string>();

        try
        {
            foreach (var reference in referenced.OrderBy(r => r, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();

                if (zipIndex.TryGetValue(reference, out var relative))
                {
                    var absolute = Path.GetFullPath(Path.Combine(sandboxDirectory, relative));
                    var sandboxRoot = Path.GetFullPath(sandboxDirectory) + Path.DirectorySeparatorChar;
                    if (!absolute.StartsWith(sandboxRoot, StringComparison.Ordinal))
                    {
                        return ([ImportAssetPaths.Finding(
                            ImportAssetFindingCodes.PathInvalid, reference,
                            "Asset path resolves outside the package sandbox.")], []);
                    }

                    var hashed = await HashAndProbeAsync(absolute, reference, ct);
                    if (hashed.Finding is not null) return ([hashed.Finding], []);

                    if (declared.TryGetValue(reference, out var expected)
                        && !string.Equals(expected.Sha256, hashed.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        return ([ImportAssetPaths.Finding(
                            ImportAssetFindingCodes.ChecksumMismatch, reference,
                            "Asset bytes do not match the package checksum.")], []);
                    }

                    await using var upload = File.OpenRead(absolute);
                    var stagedAsset = await store.StageAsync(
                        draftId, reference, upload, hashed.ContentType!, hashed.Length, hashed.Sha256, ct);
                    staged.Add(reference);
                    manifest.Add(new ImportAssetManifestEntry(
                        stagedAsset.Reference, stagedAsset.StagingKey, stagedAsset.ContentType,
                        stagedAsset.Length, stagedAsset.Sha256));
                    continue;
                }

                var existing = await store.CheckFinalAsync(reference, ct);
                if (existing.Kind != ImportAssetAvailabilityKind.Present
                    || existing.Sha256 is null || existing.Length is null || existing.ContentType is null)
                {
                    return ([ImportAssetPaths.Finding(
                        ImportAssetFindingCodes.Missing, reference,
                        "Referenced asset is not in the package and is not already stored.")], []);
                }

                if (declared.TryGetValue(reference, out var declaredExisting)
                    && !string.Equals(declaredExisting.Sha256, existing.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return ([ImportAssetPaths.Finding(
                        ImportAssetFindingCodes.Conflict, reference,
                        "Existing stored asset does not match the package checksum.")], []);
                }

                manifest.Add(new ImportAssetManifestEntry(
                    reference, StagingKey: string.Empty, existing.ContentType, existing.Length.Value, existing.Sha256));
            }
        }
        catch (ImportExamAssetsUnavailableException)
        {
            if (staged.Count > 0)
            {
                using var compensation = ImportAssetCompensation.Start();
                await store.RecordCleanupIntentAsync(
                    draftId, staged, ImportAssetCleanupReason.StagingAbandoned, compensation.Token);
            }

            return ([ImportAssetPaths.Finding(
                ImportAssetFindingCodes.StoreUnavailable, "/",
                "Object storage is required to import package media.")], []);
        }
        catch
        {
            if (staged.Count > 0)
            {
                using var compensation = ImportAssetCompensation.Start();
                await store.RecordCleanupIntentAsync(
                    draftId, staged, ImportAssetCleanupReason.DraftSaveFailed, compensation.Token);
            }

            throw;
        }

        return ([], manifest);
    }

    private static Dictionary<string, (string Sha256, long? Length)> DeclaredChecksums(string packageJson)
    {
        using var document = JsonDocument.Parse(packageJson);
        if (!document.RootElement.TryGetProperty("assetManifest", out var manifest)
            || manifest.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, (string, long?)>(StringComparer.Ordinal);
        }

        var map = new Dictionary<string, (string, long?)>(StringComparer.Ordinal);
        foreach (var item in manifest.EnumerateArray())
        {
            var path = item.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
            var sha = item.TryGetProperty("sha256", out var shaEl) ? shaEl.GetString() : null;
            if (path is null || sha is null) continue;
            long? length = item.TryGetProperty("sizeBytes", out var sizeEl)
                && sizeEl.ValueKind == JsonValueKind.Number
                    ? sizeEl.GetInt64()
                    : null;
            map[path] = (sha, length);
        }

        return map;
    }

    private static async Task<(string? ContentType, long Length, string Sha256, PackageFinding? Finding)>
        HashAndProbeAsync(string absolutePath, string reference, CancellationToken ct)
    {
        await using var stream = File.OpenRead(absolutePath);
        var header = new byte[16];
        var headerRead = await stream.ReadAsync(header, ct);
        var probe = MediaContentProbe.Probe(header.AsSpan(0, headerRead));
        if (probe is null)
        {
            return (null, 0, "", ImportAssetPaths.Finding(
                ImportAssetFindingCodes.Invalid, reference,
                "Referenced file is not an accepted audio or image type."));
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(header.AsSpan(0, headerRead));
        var buffer = new byte[81_920];
        long length = headerRead;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            hasher.AppendData(buffer.AsSpan(0, read));
            length += read;
        }

        return (probe, length, Convert.ToHexStringLower(hasher.GetHashAndReset()), null);
    }
}
