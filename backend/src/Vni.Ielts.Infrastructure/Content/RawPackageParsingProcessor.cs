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
    IExamContentParser parser,
    IRawPackageParsingTransaction transaction,
    IClock clock)
{
    public RawPackageParsingProcessor(
        IExamPackageRepository packages,
        IPackageUploadStore uploads,
        PackageStructuralValidator validator,
        SourceDocumentExtractor extractor,
        DirectoryAdjacentDocumentGrouper grouper,
        ISourceDocumentGroupingProposalRepository groupingProposals,
        IExamContentParser parser,
        IParsedExamCandidateRepository candidates,
        IClock clock)
        : this(packages, uploads, validator, extractor, grouper, parser,
            new RepositoryRawPackageParsingTransaction(packages, groupingProposals, candidates, clock), clock)
    {
    }

    private sealed class RepositoryRawPackageParsingTransaction(
        IExamPackageRepository packages,
        ISourceDocumentGroupingProposalRepository proposals,
        IParsedExamCandidateRepository candidates,
        IClock clock) : IRawPackageParsingTransaction
    {
        public async Task CommitAsync(
            ExamPackage package, int expectedVersion, PackageClaim claim,
            SourceDocumentGroupingProposal proposal, ParsedExamCandidate candidate,
            IReadOnlyList<PackageFinding> findings, CancellationToken ct)
        {
            await proposals.SaveAsync(proposal, proposal.Version, ct);
            await candidates.SaveAsync(candidate, candidate.Version, ct);
            package.MarkNeedsReview(clock.UtcNow, findings);
            await packages.ReplaceClaimedAsync(package, expectedVersion, claim, clock.UtcNow, ct);
        }
    }
    public Task ProcessAsync(ExamPackage package, CancellationToken ct) =>
        ProcessAsync(package, "default-worker", null, ct);

    public async Task ProcessAsync(
        ExamPackage package, string workerId, TimeSpan? leaseDuration, CancellationToken ct)
    {
        if (package.Status != PackageImportStatus.Uploaded) return;

        var duration = leaseDuration ?? TimeSpan.FromMinutes(5);
        var claimed = await packages.TryClaimAsync(package.Id, workerId, clock.UtcNow, duration, ct);
        if (claimed is null) return;
        package = claimed;
        var claim = new PackageClaim(package.Id, workerId, package.ClaimFence, package.LeaseUntil!.Value);

        await using var uploaded = await uploads.OpenAsync(package.UploadRef, ct);
        await PackageContentStaging.WithStagedContentAsync(uploaded, async staged =>
        {
            var outcome = validator.ValidateZip(staged, package.UploadedBy);
            if (!outcome.IsValid)
            {
                var expectedVersion = package.Version;
                package.Reject(outcome.Findings, clock.UtcNow);
                await packages.ReplaceClaimedAsync(package, expectedVersion, claim, clock.UtcNow, ct);
                return false;
            }

            if (outcome.Classification != PackageSourceClassification.RawSource)
                return false;

            await ProcessValidatedAsync(package, staged, outcome.RawInventory!, claim, ct);
            return true;
        }, ct);
    }

    public Task ProcessValidatedAsync(
        ExamPackage package, Stream staged, RawPackageInventory inventory, PackageClaim claim, CancellationToken ct) =>
        ProcessValidatedCoreAsync(package, staged, inventory, claim, ct);

    private async Task ProcessValidatedCoreAsync(
        ExamPackage package,
        Stream staged,
        RawPackageInventory inventory,
        PackageClaim claim,
        CancellationToken ct)
    {
        var expectedVersion = package.Version;
        package.MarkParsing(clock.UtcNow);
        await packages.ReplaceClaimedAsync(package, expectedVersion, claim, clock.UtcNow, ct);

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

        var candidate = await parser.ParseAsync(documents, ct);
        if (!string.Equals(candidate.PackageId, package.Id, StringComparison.Ordinal))
            throw new InvalidDataException("Parsed candidate package id does not match the source package.");

        var proposal = grouper.Propose(documents);
        if (!string.Equals(proposal.PackageId, package.Id, StringComparison.Ordinal))
            throw new InvalidDataException("Source document grouping package id does not match the source package.");

        var findings = documents
            .Where(document => !document.HasText)
            .Select(document => new PackageFinding(
                "extraction",
                $"DOCUMENT_{document.Outcome.ToString().ToUpperInvariant()}",
                document.EntryPath,
                ExtractionMessage(document.Outcome)))
            .ToArray();

        await transaction.CommitAsync(package, package.Version, claim, proposal, candidate, findings, ct);
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
