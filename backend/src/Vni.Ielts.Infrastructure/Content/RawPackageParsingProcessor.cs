using System.IO.Compression;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Plan 01 tracer for raw packages. The parser remains provider-neutral and is
/// supplied by the worker composition root; this processor never creates a Draft.
/// </summary>
public sealed class RawPackageParsingProcessor(
    IExamPackageRepository packages,
    IPackageUploadStore uploads,
    PackageStructuralValidator validator,
    SourceDocumentExtractor extractor,
    DirectoryAdjacentDocumentGrouper grouper,
    ISourceDocumentGroupingProposalRepository groupingProposals,
    IExamContentParser parser,
    IParsedExamCandidateRepository candidates,
    IClock clock)
{
    public async Task ProcessAsync(ExamPackage package, CancellationToken ct)
    {
        if (package.Status != PackageImportStatus.Uploaded) return;

        package.MarkValidating(clock.UtcNow);
        await packages.SaveAsync(package, ct);

        await using var uploaded = await uploads.OpenAsync(package.UploadRef, ct);
        await PackageContentStaging.WithStagedContentAsync(uploaded, async staged =>
        {
            var outcome = validator.ValidateZip(staged, package.UploadedBy);
            if (!outcome.IsValid)
            {
                package.Reject(outcome.Findings, clock.UtcNow);
                await packages.SaveAsync(package, ct);
                return false;
            }

            if (outcome.Classification != PackageSourceClassification.RawSource)
                return false;

            await ProcessValidatedAsync(package, staged, outcome.RawInventory!, ct);
            return true;
        }, ct);
    }

    public async Task ProcessValidatedAsync(
        ExamPackage package, Stream staged, RawPackageInventory inventory, CancellationToken ct)
    {
            package.MarkParsing(clock.UtcNow);
            await packages.SaveAsync(package, ct);

            staged.Position = 0;
            using var archive = new ZipArchive(staged, ZipArchiveMode.Read, leaveOpen: true);
            var documents = new List<ExtractedSourceDocument>();
            foreach (var source in inventory.Entries.Where(entry =>
                entry.MediaType is "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or "application/pdf"))
            {
                var archiveEntry = archive.Entries.Single(entry =>
                    string.Equals(entry.FullName.Replace('\\', '/').TrimStart('/'), source.Path, StringComparison.Ordinal));
                await using var content = archiveEntry.Open();
                documents.Add(extractor.Extract(
                    package.Id, source.Path, source.MediaType, source.Sha256, content, ct));
            }

            var proposal = grouper.Propose(documents);
            if (!string.Equals(proposal.PackageId, package.Id, StringComparison.Ordinal))
                throw new InvalidDataException("Source document grouping package id does not match the source package.");

            // Both review artifacts must exist before the package becomes visible as reviewable.
            await groupingProposals.SaveAsync(proposal, proposal.Version, ct);

            var candidate = await parser.ParseAsync(documents, ct);
            if (!string.Equals(candidate.PackageId, package.Id, StringComparison.Ordinal))
                throw new InvalidDataException("Parsed candidate package id does not match the source package.");

            await candidates.SaveAsync(candidate, candidate.Version, ct);
            var findings = documents
                .Where(document => !document.HasText)
                .Select(document => new PackageFinding(
                    "extraction",
                    $"DOCUMENT_{document.Outcome.ToString().ToUpperInvariant()}",
                    document.EntryPath,
                    ExtractionMessage(document.Outcome)))
                .ToArray();
            package.MarkNeedsReview(clock.UtcNow, findings);
            await packages.SaveAsync(package, ct);
    }

    private static string ExtractionMessage(DocumentExtractionOutcome outcome) => outcome switch
    {
        DocumentExtractionOutcome.Empty => "The document contains no extractable text and needs staff review or OCR.",
        DocumentExtractionOutcome.Encrypted => "The document is encrypted and must be unlocked before parsing.",
        DocumentExtractionOutcome.Unsupported => "The document uses unsupported or externally linked content and needs staff review.",
        DocumentExtractionOutcome.LimitExceeded => "The extracted document exceeds the configured processing limits.",
        _ => "The document is malformed and could not be extracted.",
    };
}
