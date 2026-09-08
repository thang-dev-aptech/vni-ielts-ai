using System.IO.Compression;
using System.Text;

using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

public sealed class RawPackageParsingProcessorTests
{
    [Fact]
    public async Task Candidate_is_persisted_before_package_reaches_needs_review()
    {
        var package = Package();
        var packages = new Packages();
        var candidates = new Candidates(packages);
        var proposals = new Proposals(packages);
        var parser = new Parser();
        var processor = new RawPackageParsingProcessor(
            packages, new Uploads(RawZip()), Validator(), Extractor(), new DirectoryAdjacentDocumentGrouper(), proposals, parser, candidates, new Clock());

        await processor.ProcessAsync(package, default);

        Assert.True(candidates.Saved);
        Assert.True(proposals.Saved);
        Assert.Equal(["exam.docx"], proposals.Proposal!.Groups.Single().EntryPaths);
        Assert.True(proposals.Proposal.NeedsReview);
        Assert.Equal(DocumentExtractionOutcome.Extracted, parser.Received.Single().Outcome);
        Assert.Equal("Synthetic", parser.Received.Single().Chunks.Single().Text);
        Assert.Equal(PackageImportStatus.NeedsReview, package.Status);
        Assert.Equal([PackageImportStatus.Validating, PackageImportStatus.Parsing, PackageImportStatus.NeedsReview], packages.SavedStatuses);
    }


    [Fact]
    public async Task Package_does_not_reach_needs_review_when_candidate_persistence_fails()
    {
        var package = Package();
        var packages = new Packages();
        var processor = new RawPackageParsingProcessor(
            packages, new Uploads(RawZip()), Validator(), Extractor(), new DirectoryAdjacentDocumentGrouper(), new Proposals(packages), new Parser(), new Candidates(packages, fail: true), new Clock());

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(package, default));

        Assert.Equal(PackageImportStatus.Parsing, package.Status);
        Assert.DoesNotContain(PackageImportStatus.NeedsReview, packages.SavedStatuses);
    }

    /// <summary>
    /// <b>A refused provider answer leaves the package where a failed candidate
    /// write leaves it.</b> Reaching <c>NeedsReview</c> is what tells staff a
    /// proposal is waiting for them, and a package that advertises a proposal
    /// it does not have is worse than one that is visibly stuck at
    /// <c>Parsing</c>. Nothing here invents a retry schedule.
    /// </summary>
    [Fact]
    public async Task Package_does_not_reach_needs_review_when_the_parser_refuses_the_answer()
    {
        var package = Package();
        var packages = new Packages();
        var candidates = new Candidates(packages);
        var processor = new RawPackageParsingProcessor(
            packages, new Uploads(RawZip()), Validator(), Extractor(), new DirectoryAdjacentDocumentGrouper(), new Proposals(packages), new RefusingParser(), candidates, new Clock());

        var rejected = await Assert.ThrowsAsync<ExamExtractionRejectedException>(
            () => processor.ProcessAsync(package, default));

        Assert.Equal(ExamExtractionRejection.SchemaInvalid, rejected.Code);
        Assert.False(candidates.Saved);
        Assert.Equal(PackageImportStatus.Parsing, package.Status);
        Assert.DoesNotContain(PackageImportStatus.NeedsReview, packages.SavedStatuses);
    }

    [Fact]
    public async Task Provider_outage_does_not_create_a_candidate_or_advance_toward_Draft()
    {
        var package = Package();
        var packages = new Packages();
        var candidates = new Candidates(packages);
        var processor = new RawPackageParsingProcessor(
            packages, new Uploads(RawZip()), Validator(), Extractor(),
            new DirectoryAdjacentDocumentGrouper(), new Proposals(packages),
            new UnavailableProviderParser(), candidates, new Clock());

        await Assert.ThrowsAsync<TransientExamExtractionException>(
            () => processor.ProcessAsync(package, default));

        Assert.False(candidates.Saved);
        Assert.Equal(PackageImportStatus.Parsing, package.Status);
        Assert.DoesNotContain(PackageImportStatus.NeedsReview, packages.SavedStatuses);
    }

    /// <summary>
    /// <b>The same is true when the answer was fine and the audit record was
    /// not.</b> This is the write the fail-closed parser prevents: a candidate
    /// saved and advertised as reviewable with no record of the provider, model
    /// or prompt that produced it.
    /// </summary>
    [Fact]
    public async Task Package_saves_no_candidate_when_the_provider_run_cannot_be_recorded()
    {
        var package = Package();
        var packages = new Packages();
        var candidates = new Candidates(packages);
        var processor = new RawPackageParsingProcessor(
            packages, new Uploads(RawZip()), Validator(), Extractor(), new DirectoryAdjacentDocumentGrouper(), new Proposals(packages), new UnrecordableParser(), candidates, new Clock());

        await Assert.ThrowsAsync<ExamExtractionRunNotRecordedException>(
            () => processor.ProcessAsync(package, default));

        Assert.False(candidates.Saved);
        Assert.Equal(PackageImportStatus.Parsing, package.Status);
        Assert.DoesNotContain(PackageImportStatus.NeedsReview, packages.SavedStatuses);
    }

    private static ExamPackage Package() => ExamPackage.Create(
        "package-1", ExamPackageSourceKind.Zip, new UserId("author"), "hash", "raw.zip", "upload", DateTimeOffset.UtcNow);

    private static PackageStructuralValidator Validator()
    {
        var root = FindRoot();
        return new PackageStructuralValidator(ExamPackageReader.FromSchemaFile(Path.Combine(root, "contracts/schemas/exam.schema.json")));
    }

    private static SourceDocumentExtractor Extractor() => new(
        new OoxmlDocxTextExtractor(DocumentExtractionLimits.Conservative),
        new PdfPigTextExtractor(),
        DocumentExtractionLimits.Conservative);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))) directory = directory.Parent;
        return directory!.FullName;
    }

    private static byte[] RawZip()
    {
        using var outer = new MemoryStream();
        using (var zip = new ZipArchive(outer, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("exam.docx");
            using var target = entry.Open();
            using var docx = new MemoryStream();
            using (var document = new ZipArchive(docx, ZipArchiveMode.Create, true))
            {
                Add(document, "[Content_Types].xml", "<Types />");
                Add(document, "_rels/.rels", "<Relationships />");
                Add(document, "word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Synthetic</w:t></w:r></w:p></w:body></w:document>");
            }
            docx.Position = 0;
            docx.CopyTo(target);
        }
        return outer.ToArray();
    }

    private static void Add(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Uploads(byte[] content) : IPackageUploadStore
    {
        public Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenAsync(string uploadRef, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(content));
        public Task DeleteAsync(string uploadRef, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Packages : IExamPackageRepository
    {
        public List<PackageImportStatus> SavedStatuses { get; } = [];
        public Task SaveAsync(ExamPackage package, CancellationToken ct) { SavedStatuses.Add(package.Status); return Task.CompletedTask; }
        public Task<ExamPackage?> FindAsync(string packageId, CancellationToken ct) => Task.FromResult<ExamPackage?>(null);
        public Task<IReadOnlyList<ExamPackage>> ListByStatusAsync(PackageImportStatus status, CancellationToken ct) => Task.FromResult<IReadOnlyList<ExamPackage>>([]);
        public Task<IReadOnlyList<ExamPackage>> ListAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ExamPackage>>([]);
        public Task<IReadOnlyList<ExamPackage>> ListUnpurgedTerminalAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ExamPackage>>([]);
        public Task DeleteAsync(string packageId, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Proposals(Packages packages, bool fail = false) : ISourceDocumentGroupingProposalRepository
    {
        public bool Saved { get; private set; }
        public SourceDocumentGroupingProposal? Proposal { get; private set; }
        public Task<SourceDocumentGroupingProposal?> FindAsync(string packageId, CancellationToken ct) =>
            Task.FromResult(Proposal);
        public Task SaveAsync(SourceDocumentGroupingProposal proposal, int expectedVersion, CancellationToken ct)
        {
            Assert.Equal(PackageImportStatus.Parsing, packages.SavedStatuses.Last());
            if (fail) throw new InvalidOperationException("storage failed");
            Saved = true;
            Proposal = proposal;
            return Task.CompletedTask;
        }
    }

    private sealed class Candidates(Packages packages, bool fail = false) : IParsedExamCandidateRepository
    {
        public bool Saved { get; private set; }
        public Task<ParsedExamCandidate?> FindAsync(string packageId, string candidateId, CancellationToken ct) => Task.FromResult<ParsedExamCandidate?>(null);
        public Task<IReadOnlyList<ParsedExamCandidate>> ListByPackageAsync(string packageId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ParsedExamCandidate>>([]);
        public Task SaveAsync(ParsedExamCandidate candidate, int expectedVersion, CancellationToken ct)
        {
            Assert.Equal(PackageImportStatus.Parsing, packages.SavedStatuses.Last());
            if (fail) throw new InvalidOperationException("storage failed");
            Saved = true;
            return Task.CompletedTask;
        }
    }
    private sealed class Parser : IExamContentParser
    {
        public IReadOnlyList<ExtractedSourceDocument> Received { get; private set; } = [];

        public Task<ParsedExamCandidate> ParseAsync(
            IReadOnlyList<ExtractedSourceDocument> documents, CancellationToken ct)
        {
            Received = documents;
            return Task.FromResult(
                ParsedExamCandidate.Create("candidate-1", documents[0].PackageId, null, ParsedExamClassification.NeedsReview, null, [],
                    [new ParsedSourceProvenance(documents[0].EntryPath, null, null, documents[0].Sha256)]));
        }
    }

    private sealed class UnavailableProviderParser : IExamContentParser
    {
        public Task<ParsedExamCandidate> ParseAsync(
            IReadOnlyList<ExtractedSourceDocument> documents, CancellationToken ct) =>
            throw new TransientExamExtractionException("provider unavailable");
    }

    private sealed class UnrecordableParser : IExamContentParser
    {
        public Task<ParsedExamCandidate> ParseAsync(
            IReadOnlyList<ExtractedSourceDocument> documents, CancellationToken ct) =>
            throw new ExamExtractionRunNotRecordedException(documents[0].PackageId);
    }

    private sealed class RefusingParser : IExamContentParser
    {
        public Task<ParsedExamCandidate> ParseAsync(
            IReadOnlyList<ExtractedSourceDocument> documents, CancellationToken ct) =>
            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.SchemaInvalid,
                "The extraction response did not match the contract.");
    }
}
