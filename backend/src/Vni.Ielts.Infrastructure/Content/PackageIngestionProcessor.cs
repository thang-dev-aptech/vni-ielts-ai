using Microsoft.Extensions.Logging;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Drives one <see cref="ExamPackage"/> through Plan 03's structural gate and,
/// for a single-source package, straight into Plan 05's import: <c>Uploaded →
/// Validating → {Rejected, Imported (one exam), ReadyToImport (multi-exam
/// ZIP, waits for <c>POST .../confirm</c>)}</c>.
///
/// <b>No separate confirm for one exam.</b> A single JSON upload — or a ZIP
/// that turns out to contain exactly one exam — has nothing "uncertain" left
/// to ask a human about once validation passes, per the plan's own reasoning
/// for why Phase 2 content skips the review-candidate step Phase 6's AI
/// output needs. A thin, synchronous piece of logic on purpose — the
/// polling/scheduling half lives in <c>Vni.Ielts.Worker</c>'s
/// <c>PackageIngestionWorker</c>, so this class can be tested without a timer
/// or a real host.
/// </summary>
public sealed class PackageIngestionProcessor(
    IExamPackageRepository packages,
    IPackageUploadStore uploads,
    PackageStructuralValidator validator,
    RawPackageParsingProcessor rawParsing,
    IPackageImportTransaction import,
    IUserRepository users,
    IAuditLog audit,
    IClock clock,
    ILogger<PackageIngestionProcessor> logger)
{
    public async Task ProcessAsync(ExamPackage package, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Status != PackageImportStatus.Uploaded) return;

        try
        {
            // No malware/AV scan runs here — `[NEEDS VALIDATION]` in
            // phase-2-content-import-plan.md, needs an ADR before a scanner
            // is added to the stack. Skipping straight to Validating is
            // truthful about what actually ran, rather than a Scanning status
            // that implies a scan happened.
            package.MarkValidating(clock.UtcNow);
            await packages.SaveAsync(package, ct);

            // Staged to a local temp file rather than buffered into memory —
            // see PackageContentStaging's doc comment for why a 1.3 GB
            // package made this necessary rather than theoretical.
            await using var gridFsStream = await uploads.OpenAsync(package.UploadRef, ct);
            var outcome = await PackageContentStaging.WithStagedContentAsync(
                gridFsStream,
                // The uploader is the actor here — there is no separate
                // confirmer for a package that imports itself.
                // `ValidateZip`/`ValidateJson` stamp this as `ExamVersion.CreatedBy`.
                staged => Task.FromResult(package.SourceKind == ExamPackageSourceKind.Json
                    ? validator.ValidateJson(staged, package.UploadedBy)
                    : validator.ValidateZip(staged, package.UploadedBy)),
                ct);

            if (!outcome.IsValid)
            {
                package.Reject(outcome.Findings, clock.UtcNow);
                await packages.SaveAsync(package, ct);
                return;
            }

            if (outcome.Classification == PackageSourceClassification.RawSource)
            {
                await using var rawUpload = await uploads.OpenAsync(package.UploadRef, ct);
                await PackageContentStaging.WithStagedContentAsync(
                    rawUpload,
                    async staged =>
                    {
                        await rawParsing.ProcessValidatedAsync(package, staged, outcome.RawInventory!, ct);
                        return true;
                    },
                    ct);
                return;
            }

            if (outcome.Entries.Count > 1)
            {
                // A multi-exam ZIP — surfaced for an operator to confirm
                // before N drafts are created at once, not because the
                // content is uncertain.
                package.MarkReadyToImport(outcome.Entries, clock.UtcNow);
                await packages.SaveAsync(package, ct);
                return;
            }

            var createdIds = await import.ImportAsync(package, package.Version, outcome.Versions, ct);
            await RecordAsync(package, package.UploadedBy, createdIds, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Package {PackageId} failed during structural validation.", package.Id);
            package.MarkFailed(clock.UtcNow);
            await packages.SaveAsync(package, ct);
        }
    }

    private async Task RecordAsync(
        ExamPackage package, UserId actor, IReadOnlyList<ExamVersionId> createdIds, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(actor, ct);

        await audit.AppendAsync(
            AuditEntry.Record(
                actor, user?.Email?.Value ?? "(tài khoản đã xoá)", AuditAction.PackageImported,
                "package", package.Id, package.FileName, clock.UtcNow,
                new Dictionary<string, string>
                {
                    ["examCount"] = createdIds.Count.ToString(),
                    ["examVersionIds"] = string.Join(",", createdIds.Select(id => id.Value)),
                }),
            ct);
    }
}
