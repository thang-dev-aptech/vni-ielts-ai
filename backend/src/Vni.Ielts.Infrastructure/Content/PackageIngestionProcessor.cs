using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content.Import;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;
using Vni.Ielts.Infrastructure.Persistence.Importing;
using PackageFinding = Vni.Ielts.Domain.Exams.PackageFinding;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Drives one <see cref="ExamPackage"/> through structural validation into
/// review: <c>Uploaded → Validating → {Rejected, NeedsReview (ZIP via
/// <see cref="ExamPackageImportPipeline"/>), Imported (JSON-only)}</c>.
///
/// <b>Every manifest ZIP (1 or N exams) is split into one-exam sub-ZIPs</b>
/// then imported to N drafts under <see cref="PackageImportStatus.NeedsReview"/>
/// (Hướng 1, 2026-09-10). <c>ReadyToImport</c>/<c>/confirm</c> remain only
/// for legacy in-flight packages. Instant <c>ImportClaimedAsync</c> remains
/// only for bare JSON uploads.
/// </summary>
public sealed class PackageIngestionProcessor(
    IExamPackageRepository packages,
    IPackageUploadStore uploads,
    PackageStructuralValidator validator,
    RawPackageParsingProcessor rawParsing,
    IPackageImportTransaction import,
    ExamPackageImportPipeline pipeline,
    IExamPackageValidator examValidator,
    IImportExamAssetStore examAssets,
    IUserRepository users,
    IClock clock,
    ILogger<PackageIngestionProcessor> logger,
    MongoContext mongoContext,
    IImportLinkageCommitHooks linkageHooks)
{
    public Task ProcessAsync(ExamPackage package, CancellationToken ct) =>
        ProcessAsync(package, "default-worker", null, ct);

    public async Task ProcessAsync(
        ExamPackage package,
        string workerId,
        TimeSpan? leaseDuration,
        CancellationToken ct,
        bool checklistRequired = true)
    {
        ArgumentNullException.ThrowIfNull(package);
        var duration = leaseDuration ?? TimeSpan.FromMinutes(5);
        var claimed = await packages.TryClaimAsync(package.Id, workerId, clock.UtcNow, duration, ct);
        if (claimed is null)
        {
            logger.LogInformation("Package {PackageId} was already claimed by another worker.", package.Id);
            return;
        }
        package = claimed;
        var claim = new PackageClaim(package.Id, workerId, package.ClaimFence, package.LeaseUntil!.Value);
        await using var heartbeat = PackageClaimHeartbeat.Start(packages, claim, duration, clock, ct);
        var processingCt = heartbeat.Token;

        try
        {
            await using var gridFsStream = await uploads.OpenAsync(package.UploadRef, processingCt);

            Stream stream = gridFsStream;
            FileStream? spooled = null;
            if (!gridFsStream.CanSeek)
            {
                spooled = new FileStream(
                    Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                    81_920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
                await gridFsStream.CopyToAsync(spooled, processingCt);
                spooled.Position = 0;
                stream = spooled;
            }

            try
            {
                if (package.SourceKind == ExamPackageSourceKind.Zip)
                {
                    bool hasManifest;
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
                    {
                        hasManifest = archive.GetEntry("manifest.json") is not null;
                    }
                    stream.Position = 0;

                    if (hasManifest)
                    {
                        var outcome = await PackageContentStaging.WithStagedContentAsync(
                            stream,
                            staged => Task.FromResult(validator.ValidateZip(staged, package.UploadedBy)),
                            processingCt);

                        if (!outcome.IsValid)
                        {
                            var expectedVersion = package.Version;
                            package.Reject(outcome.Findings, clock.UtcNow);
                            await packages.ReplaceClaimedAsync(package, expectedVersion, claim, clock.UtcNow, processingCt);
                            return;
                        }

                        // Hướng 1: every manifest ZIP (1 or N exams) → split → N drafts → NeedsReview.
                        // ReadyToImport /confirm is deprecated for newly processed packages.
                        stream.Position = 0;
                        await ImportManifestZipAsync(
                            package, stream, claim, checklistRequired, processingCt);
                        return;
                    }

                    // Check for raw docx / pdf source files
                    bool isRawSource;
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
                    {
                        var nonDirEntries = archive.Entries.Where(e => !e.FullName.EndsWith('/')).ToList();
                        isRawSource = nonDirEntries.Any(e =>
                            e.FullName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
                            e.FullName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) &&
                            !nonDirEntries.Any(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                    }
                    stream.Position = 0;

                    if (isRawSource)
                    {
                        var outcome = await PackageContentStaging.WithStagedContentAsync(
                            stream,
                            staged => Task.FromResult(validator.ValidateZip(staged, package.UploadedBy)),
                            processingCt);

                        if (!outcome.IsValid)
                        {
                            var expectedVersion = package.Version;
                            package.Reject(outcome.Findings, clock.UtcNow);
                            await packages.ReplaceClaimedAsync(package, expectedVersion, claim, clock.UtcNow, processingCt);
                            return;
                        }

                        if (outcome.Classification == PackageSourceClassification.RawSource)
                        {
                            stream.Position = 0;
                            await PackageContentStaging.WithStagedContentAsync(
                                stream,
                                async staged =>
                                {
                                    await rawParsing.ProcessValidatedAsync(package, staged, outcome.RawInventory!, claim, processingCt);
                                    return true;
                                },
                                ct);
                            return;
                        }
                    }

                    // Structured import package (exam.json / skill folders)
                    await ImportZipViaPipelineAsync(
                        package, stream, claim, checklistRequired, processingCt);
                    return;
                }
                else // Json package
                {
                    var outcome = await PackageContentStaging.WithStagedContentAsync(
                        stream,
                        staged => Task.FromResult(validator.ValidateJson(staged, package.UploadedBy)),
                        processingCt);

                    if (!outcome.IsValid)
                    {
                        var expectedVersion = package.Version;
                        package.Reject(outcome.Findings, clock.UtcNow);
                        await packages.ReplaceClaimedAsync(package, expectedVersion, claim, clock.UtcNow, processingCt);
                        return;
                    }

                    var auditEntry = await ImportAuditAsync(package, package.UploadedBy, outcome.Versions.Select(v => v.Id).ToArray(), processingCt);
                    await import.ImportClaimedAsync(package, package.Version, claim, outcome.Versions, auditEntry, processingCt);
                }
            }
            finally
            {
                if (spooled is not null) await spooled.DisposeAsync();
            }
        }
        catch (OperationCanceledException) when (processingCt.IsCancellationRequested)
        {
            logger.LogInformation("Processing cancelled for package {PackageId}; attempting to release claim.", package.Id);
            try
            {
                using var releaseCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await packages.ReleaseClaimAsync(claim, clock.UtcNow, releaseCts.Token);
            }
            catch (Exception releaseEx)
            {
                logger.LogWarning(releaseEx, "Could not release claim on package {PackageId} during cancellation; expiring lease will allow recovery.", package.Id);
            }
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Package {PackageId} failed during structural validation.", package.Id);
            try
            {
                var expectedVersion = package.Version;
                package.MarkFailed("INGESTION_FAILED", "An unexpected error occurred during package processing.", clock.UtcNow);
                await packages.ReplaceClaimedAsync(package, expectedVersion, claim, clock.UtcNow, processingCt);
            }
            catch (Exception replaceEx)
            {
                logger.LogError(replaceEx, "Failed to persist Failed status for package {PackageId}.", package.Id);
            }
        }
    }

    /// <summary>
    /// Manifest ZIP (1 or N exams): split into one-exam sub-ZIPs, create drafts,
    /// then mark the package NeedsReview with the full draft id list.
    /// </summary>
    private async Task ImportManifestZipAsync(
        ExamPackage package,
        Stream stream,
        PackageClaim claim,
        bool checklistRequired,
        CancellationToken processingCt)
    {
        var split = ManifestPackageSplitter.Split(stream, examValidator, package.UploadedBy);
        if (!split.IsSuccess)
        {
            var expectedVersion = package.Version;
            package.Reject(split.Findings, clock.UtcNow);
            await packages.ReplaceClaimedAsync(package, expectedVersion, claim, clock.UtcNow, processingCt);
            return;
        }

        var multiExam = split.Packages.Count > 1;
        var accepted = new List<ExamImportDraft>();
        var aggregatedFindings = new List<PackageFinding>();

        try
        {
            foreach (var sub in split.Packages)
            {
                var definitionId = new ExamDefinitionId($"pkg-{package.Sha256[..16]}-{sub.RootExamFileName}");
                try
                {
                    sub.Zip.Position = 0;
                    using var archive = new ZipArchive(sub.Zip, ZipArchiveMode.Read, leaveOpen: true);
                    var examEntry = archive.GetEntry(sub.RootExamFileName);
                    if (examEntry is not null)
                    {
                        using var entryStream = examEntry.Open();
                        var doc = JsonNode.Parse(entryStream);
                        if (doc?["definitionId"]?.GetValue<string>() is { Length: > 0 } defStr)
                            definitionId = new ExamDefinitionId(defStr);
                    }
                }
                catch
                {
                    // pipeline validates
                }

                sub.Zip.Position = 0;
                var attempt = await pipeline.ImportAsync(
                    sub.Zip,
                    definitionId: definitionId,
                    versionNumber: 1,
                    checklistRequired: checklistRequired,
                    processingCt,
                    createdBy: package.UploadedBy,
                    createdAt: clock.UtcNow,
                    packageId: package.Id,
                    saveDraft: false,
                    draftStabilityKey: multiExam ? sub.ExamPath : null);

                aggregatedFindings.AddRange(attempt.Findings.Select(f =>
                    new PackageFinding(f.Severity, f.Code, f.Path, f.Message)));

                if (!attempt.IsAccepted || attempt.Draft is null)
                {
                    await CompensateStagedDraftsAsync(accepted, processingCt);
                    var expectedVersion = package.Version;
                    package.Reject(
                        aggregatedFindings.Count > 0
                            ? aggregatedFindings
                            : [new PackageFinding("error", "IMPORT_REJECTED", "/", "Sub-package import was refused.")],
                        clock.UtcNow);
                    await packages.ReplaceClaimedAsync(package, expectedVersion, claim, clock.UtcNow, processingCt);
                    return;
                }

                accepted.Add(attempt.Draft);
            }

            await PersistDraftsAndMarkNeedsReviewAsync(
                package, claim, accepted, aggregatedFindings, processingCt);
        }
        finally
        {
            foreach (var sub in split.Packages)
                await sub.Zip.DisposeAsync();
        }
    }

    /// <summary>
    /// Skill-folder ZIP (no manifest): one pipeline pass → one draft → NeedsReview.
    /// </summary>
    private async Task ImportZipViaPipelineAsync(
        ExamPackage package,
        Stream stream,
        PackageClaim claim,
        bool checklistRequired,
        CancellationToken processingCt)
    {
        ExamDefinitionId definitionId = new ExamDefinitionId($"pkg-{package.Sha256[..16]}");
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var jsonEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    Path.GetFileName(e.FullName),
                    "manifest.json",
                    StringComparison.OrdinalIgnoreCase));
            if (jsonEntry is not null)
            {
                try
                {
                    using var entryStream = jsonEntry.Open();
                    var doc = JsonNode.Parse(entryStream);
                    if (doc?["definitionId"]?.GetValue<string>() is { Length: > 0 } defStr)
                        definitionId = new ExamDefinitionId(defStr);
                }
                catch
                {
                    // pipeline validates
                }
            }
        }
        stream.Position = 0;

        var attempt = await pipeline.ImportAsync(
            stream,
            definitionId: definitionId,
            versionNumber: 1,
            checklistRequired: checklistRequired,
            processingCt,
            createdBy: package.UploadedBy,
            createdAt: clock.UtcNow,
            packageId: package.Id,
            saveDraft: false);

        var mappedFindings = attempt.Findings
            .Select(f => new PackageFinding(f.Severity, f.Code, f.Path, f.Message))
            .ToList();

        if (!attempt.IsAccepted || attempt.Draft is null)
        {
            var expectedVersion = package.Version;
            package.Reject(mappedFindings, clock.UtcNow);
            await packages.ReplaceClaimedAsync(package, expectedVersion, claim, clock.UtcNow, processingCt);
            return;
        }

        await PersistDraftsAndMarkNeedsReviewAsync(
            package, claim, [attempt.Draft], mappedFindings, processingCt);
    }

    private async Task PersistDraftsAndMarkNeedsReviewAsync(
        ExamPackage package,
        PackageClaim claim,
        IReadOnlyList<ExamImportDraft> drafts,
        IReadOnlyList<PackageFinding> findings,
        CancellationToken processingCt)
    {
        using var session = await mongoContext.Database.Client.StartSessionAsync(cancellationToken: processingCt);
        session.StartTransaction();
        try
        {
            foreach (var draft in drafts)
            {
                var draftDoc = MongoImportDraftStore.ToDocument(draft);
                var existingDoc = await mongoContext.ImportDrafts
                    .Find(session, Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Id, draftDoc.Id))
                    .FirstOrDefaultAsync(processingCt);

                if (existingDoc is null)
                {
                    await mongoContext.ImportDrafts.InsertOneAsync(session, draftDoc, cancellationToken: processingCt);
                }
                else if (string.IsNullOrWhiteSpace(existingDoc.PackageId))
                {
                    await mongoContext.ImportDrafts.UpdateOneAsync(
                        session,
                        Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Id, draftDoc.Id),
                        Builders<ExamImportDraftDocument>.Update.Set(d => d.PackageId, package.Id),
                        cancellationToken: processingCt);
                }
                else if (existingDoc.PackageId != package.Id)
                {
                    await session.AbortTransactionAsync(processingCt);
                    throw new InvalidOperationException(
                        $"Draft {draftDoc.Id} is owned by package {existingDoc.PackageId} and cannot be linked to package {package.Id}.");
                }

                await linkageHooks.AfterDraftInsertAsync(draft.Id, package.Id, processingCt);
            }

            var expectedVersion = package.Version;
            var draftIds = drafts.Select(d => d.Id.ToString("D")).ToArray();
            package.MarkNeedsReview(draftIds, clock.UtcNow, findings);
            var pkgDoc = package.ToDocument();

            var replaceResult = await mongoContext.ExamPackages.ReplaceOneAsync(
                session,
                Builders<ExamPackageDocument>.Filter.And(
                    Builders<ExamPackageDocument>.Filter.Eq(p => p.Id, package.Id),
                    Builders<ExamPackageDocument>.Filter.Eq(p => p.Version, expectedVersion),
                    Builders<ExamPackageDocument>.Filter.Eq(p => p.ClaimOwner, claim.Owner),
                    Builders<ExamPackageDocument>.Filter.Eq(p => p.ClaimFence, claim.Fence),
                    Builders<ExamPackageDocument>.Filter.Gt(p => p.LeaseUntil, clock.UtcNow.UtcDateTime)),
                pkgDoc,
                cancellationToken: processingCt);

            if (replaceResult.MatchedCount == 0)
            {
                await session.AbortTransactionAsync(processingCt);
                throw new PackageConcurrencyException(package.Id, expectedVersion);
            }

            foreach (var draft in drafts)
                await linkageHooks.AfterPackageUpdateAsync(draft.Id, package.Id, processingCt);

            await session.CommitTransactionAsync(processingCt);
        }
        catch
        {
            if (session.IsInTransaction)
                await session.AbortTransactionAsync(processingCt);
            await CompensateStagedDraftsAsync(drafts, CancellationToken.None);
            throw;
        }
    }

    private async Task CompensateStagedDraftsAsync(
        IReadOnlyList<ExamImportDraft> drafts, CancellationToken ct)
    {
        foreach (var draft in drafts)
        {
            var staged = ImportAssetPaths.StagedReferences(draft.Assets);
            if (staged.Count > 0)
            {
                try
                {
                    using var compensation = ImportAssetCompensation.Start();
                    await examAssets.RecordCleanupIntentAsync(
                        draft.Id,
                        staged,
                        ImportAssetCleanupReason.DraftSaveFailed,
                        compensation.Token);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to record asset cleanup for draft {DraftId} during manifest import compensation.",
                        draft.Id);
                }
            }

            try
            {
                await mongoContext.ImportDrafts.DeleteOneAsync(d => d.Id == draft.Id.ToString("D"), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to delete orphan draft {DraftId} during manifest import compensation.",
                    draft.Id);
            }
        }
    }

    private async Task<AuditEntry> ImportAuditAsync(
        ExamPackage package, UserId actor, IReadOnlyList<ExamVersionId> createdIds, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(actor, ct);
        return AuditEntry.Record(
            actor, user?.Email?.Value ?? "(tài khoản đã xoá)", AuditAction.PackageImported,
            "package", package.Id, package.FileName, clock.UtcNow,
            new Dictionary<string, string>
            {
                ["examCount"] = createdIds.Count.ToString(),
                ["examVersionIds"] = string.Join(",", createdIds.Select(id => id.Value)),
            });
    }
}
