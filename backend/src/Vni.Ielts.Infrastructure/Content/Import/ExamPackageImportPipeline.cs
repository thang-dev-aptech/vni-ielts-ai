using System.Text;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using PackageFinding = Vni.Ielts.Application.Importing.PackageFinding;

namespace Vni.Ielts.Infrastructure.Content.Import;

/// <summary>
/// Glues the exam-import engine into one call an HTTP endpoint can make: ZIP
/// bytes in, a persisted review draft (or a refusal) out.
///
/// <b>Follows the sequence <c>backend/tools/Vni.Ielts.ExamImporter/Program.cs</c>
/// already proves — inspect, extract to a sandbox, structured-or-AI-parsed,
/// validate, save.</b> S6b's job was to give that existing sequence an HTTP
/// door, not to invent a new one. → docs/security/zip-ingestion-security.md
/// </summary>
public sealed class ExamPackageImportPipeline(
    IExamPackageArchiveInspector inspector,
    ISourceDocumentExtractor extractor,
    ExamImportWorkflow workflow,
    IImportDraftStore drafts,
    IOptions<ImportArchiveOptions> archiveOptions)
{
    /// <param name="zip">
    /// Must be seekable. Inspection reads the archive's central directory,
    /// which lives at the end of the file, and extraction re-reads the same
    /// bytes — <see cref="IExamPackageArchiveInspector"/>'s own remarks call
    /// this the reason a two-step usage needs the upload persisted once and
    /// opened twice. A buffered upload (ASP.NET Core spools a multipart file
    /// part to disk or memory before handing it back as an <c>IFormFile</c>)
    /// satisfies this without the caller doing anything extra; a raw,
    /// forward-only request body would not, and is rejected here rather than
    /// silently mis-reading a truncated archive.
    /// </param>
    public async Task<ExamImportAttempt> ImportAsync(
        Stream zip, ExamDefinitionId definitionId, int versionNumber, bool checklistRequired,
        CancellationToken ct, UserId? createdBy = null, DateTimeOffset? createdAt = null)
    {
        if (!zip.CanSeek)
        {
            throw new ArgumentException(
                "The archive stream must be seekable — inspection and extraction both read it in full. "
                + "Buffer the upload to a seekable stream before calling this.", nameof(zip));
        }

        var limits = archiveOptions.Value.ToLimits();
        var inspection = await inspector.InspectAsync(zip, limits, ct);
        if (!inspection.IsAcceptable)
            return ExamImportAttempt.Rejected(inspection.Findings);

        zip.Position = 0;
        var sandboxRoot = Path.Combine(Path.GetTempPath(), "vni-import", Guid.NewGuid().ToString("n"));
        var extraction = await inspector.ExtractToSandboxAsync(zip, inspection, sandboxRoot, limits, ct);

        try
        {
            if (!extraction.IsSuccess || extraction.SandboxDirectory is null)
                return ExamImportAttempt.Rejected(extraction.Findings);

            return await ImportFromSandboxAsync(
                inspection.Layout, extraction.SandboxDirectory, definitionId, versionNumber,
                checklistRequired, createdBy, createdAt, ct);
        }
        catch (ExamSourceParsingUnavailableException e)
        {
            return ExamImportAttempt.Rejected(
                [new PackageFinding("error", "AI_PARSER_UNAVAILABLE", "/", e.Message)]);
        }
        finally
        {
            // Best-effort. A stray temp directory is a disk-hygiene problem,
            // not a correctness one, and must never turn a completed import
            // into a failed response.
            if (Directory.Exists(sandboxRoot))
            {
                try { Directory.Delete(sandboxRoot, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// <b>Structured route:</b> the archive holds exactly one accepted file
    /// and it is JSON — a package already assembled by a human, or by the
    /// operator CLI, which already ran <see cref="FabricatedAnswerKeyGuard"/>
    /// itself before anyone would upload its output here. Running the guard
    /// again on a package the CLI has already keyed would flag every
    /// legitimate answer key it wrote, so this route deliberately does not
    /// call it — see <see cref="GuardAgainstFabricatedAnswersAsync"/> for
    /// where the guard actually runs.
    ///
    /// <b>AI-parsed route:</b> every other shape. <c>P-18</c>: the folder name
    /// alone decides the skill, and a missing folder is simply not present —
    /// <see cref="PackageLayout.PresentSkills"/> already encodes exactly that.
    /// Every file under every present skill folder is extracted and
    /// concatenated under a heading per file, the same way the operator CLI
    /// concatenates a paper and its answer key under headings rather than
    /// sending two requests: a model asked to align content across two calls
    /// has to remember the first, which is what it is worst at.
    /// </summary>
    private async Task<ExamImportAttempt> ImportFromSandboxAsync(
        PackageLayout layout, string sandboxDirectory, ExamDefinitionId definitionId, int versionNumber,
        bool checklistRequired, UserId? createdBy, DateTimeOffset? createdAt, CancellationToken ct)
    {
        var allEntries = layout.AcceptedEntries.ToArray();

        if (allEntries.Length == 1 && allEntries[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var packageJson = await File.ReadAllTextAsync(Path.Combine(sandboxDirectory, allEntries[0]), ct);
            return await workflow.ImportStructuredAsync(
                packageJson, definitionId, versionNumber, checklistRequired, ct, createdBy, createdAt);
        }

        var combined = new StringBuilder();
        foreach (var skill in layout.PresentSkills)
        {
            foreach (var relativePath in layout.EntriesBySkill[skill])
            {
                var extracted = await extractor.ExtractAsync(
                    sandboxDirectory, relativePath, SourceExtractionLimits.Default, ct);

                if (!extracted.IsSuccess || extracted.Source is null)
                    return ExamImportAttempt.Rejected(extracted.Findings);

                combined.AppendLine($"## {skill} / {relativePath}");
                combined.AppendLine(extracted.Source.Text);
                combined.AppendLine();
            }
        }

        var text = combined.ToString();
        var hash = ExamImportWorkflow.Hash(text);
        var source = new ExtractedImportSource(
            "package", "text/plain", text, hash, hash, ImportDataClassification.Restricted);

        var attempt = await workflow.ImportExtractedAsync(
            source, definitionId, versionNumber, checklistRequired, ct, createdBy, createdAt);
        if (!attempt.IsAccepted || attempt.Draft is null) return attempt;

        return await GuardAgainstFabricatedAnswersAsync(attempt.Draft, ct);
    }

    /// <summary>
    /// <b>Runs after the draft is already saved, not before.</b> The parser
    /// runs exactly once, inside <see cref="ExamImportWorkflow.ImportExtractedAsync"/>.
    /// Calling it a second time here to check its output before persisting
    /// would double the AI cost of every AI-parsed upload, and — because the
    /// model is not deterministic — could check different output from what
    /// actually got saved. <see cref="ExamImportWorkflow"/> itself is off
    /// limits to this slice (touched only by the review-lifecycle work this
    /// session) and exposes no hook between parsing and persisting, so this
    /// is the seam that exists.
    ///
    /// <b>Every finding becomes a review warning, not a silent rejection.</b>
    /// That reuses exactly the "cảnh báo, bắt buộc ghi lý do khi bỏ qua" shape
    /// `P-19` already requires for every other import warning: a fabricated
    /// answer key blocks approval (<see cref="ImportReviewWorkflow.ApproveAsync"/>
    /// refuses while any warning is unresolved) until a reviewer overrides it
    /// with a reason, and that override is what
    /// <c>AuditAction.WarningOverridden</c> records. A draft that is on disk
    /// with the problem named and gated is more useful to an operator than
    /// one silently discarded after a paid AI call — the same judgement
    /// <c>FabricatedAnswerKeyGuard</c>'s own remarks make about the CLI path
    /// ("the draft is on disk for inspection").
    /// </summary>
    private async Task<ExamImportAttempt> GuardAgainstFabricatedAnswersAsync(
        ExamImportDraft draft, CancellationToken ct)
    {
        var fabricated = FabricatedAnswerKeyGuard
            .Inspect(draft.PackageJson, sourceIncludesAnswerKey: false)
            .Where(f => f.Path.EndsWith("/answerKey", StringComparison.Ordinal))
            .ToArray();

        if (fabricated.Length == 0) return ExamImportAttempt.Accepted(draft);

        var asWarnings = fabricated
            .Select((f, i) => new ImportReviewWarning(
                $"FABRICATED_ANSWER_KEY:{i}", ImportReviewCategory.AcceptedVariants, f.Path, f.Message, false))
            .ToArray();

        var updated = draft with
        {
            Warnings = [.. draft.Warnings, .. asWarnings],
            Revision = draft.Revision + 1,
        };

        var replaced = await drafts.ReplaceAsync(updated, draft.Revision, ct);
        return ExamImportAttempt.Accepted(replaced ? updated : draft);
    }
}
