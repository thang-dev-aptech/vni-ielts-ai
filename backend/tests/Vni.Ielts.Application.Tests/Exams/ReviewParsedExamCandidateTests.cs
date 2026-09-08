using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Tests.Exams;

public sealed class ReviewParsedExamCandidateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class Repository(ParsedExamCandidate? candidate) : IParsedExamCandidateRepository
    {
        public List<int> ExpectedVersions { get; } = [];

        public Task<ParsedExamCandidate?> FindAsync(string packageId, string candidateId, CancellationToken ct) =>
            Task.FromResult(candidate is not null && candidate.PackageId == packageId && candidate.Id == candidateId
                ? candidate
                : null);

        public Task<IReadOnlyList<ParsedExamCandidate>> ListByPackageAsync(string packageId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ParsedExamCandidate>>(
                candidate is not null && candidate.PackageId == packageId ? [candidate] : []);

        public Task SaveAsync(ParsedExamCandidate saved, int expectedVersion, CancellationToken ct)
        {
            ExpectedVersions.Add(expectedVersion);
            return Task.CompletedTask;
        }
    }

    private sealed class AuditLog : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task AppendAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<AuditEntry> Entries, long Total)> ListAsync(
            string? actorId, string? action, int skip, int take, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<AuditEntry> Entries, long Total)>((Entries, Entries.Count));
    }

    [Fact]
    public async Task Lookup_requires_package_read_permission()
    {
        var result = await new GetParsedExamCandidate(new Repository(CreateCandidate()))
            .HandleAsync(new GetParsedExamCandidateQuery(Actor([]), "package-1", "candidate-1"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PermissionDenied, result.Error.Code);
    }

    [Fact]
    public async Task Missing_candidate_returns_stable_not_found_error()
    {
        var result = await new GetParsedExamCandidate(new Repository(null))
            .HandleAsync(new GetParsedExamCandidateQuery(
                Actor([PermissionKeys.PackageRead]), "package-1", "candidate-1"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ParsedCandidateNotFound, result.Error.Code);
    }

    [Fact]
    public async Task Mutation_requires_exam_review_permission()
    {
        var repository = new Repository(CreateCandidate());
        var audit = new AuditLog();
        var result = await CreateSut(repository, audit).CorrectTitleAsync(
            new CorrectParsedCandidateTitleCommand(Actor([]), "package-1", "candidate-1", 0, "New"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PermissionDenied, result.Error.Code);
        Assert.Empty(repository.ExpectedVersions);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Stale_version_is_refused_before_mutation()
    {
        var candidate = CreateCandidate();
        candidate.CorrectTitle(new UserId("prior"), "Prior edit", Now.AddMinutes(-1));
        var repository = new Repository(candidate);
        var audit = new AuditLog();

        var result = await CreateSut(repository, audit).CorrectTitleAsync(
            new CorrectParsedCandidateTitleCommand(Reviewer(), "package-1", "candidate-1", 0, "Stale edit"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ParsedCandidateVersionConflict, result.Error.Code);
        Assert.Equal("Prior edit", candidate.Title);
        Assert.Empty(repository.ExpectedVersions);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Correction_persists_expected_version_and_appends_audit()
    {
        var candidate = CreateCandidate();
        var repository = new Repository(candidate);
        var audit = new AuditLog();

        var result = await CreateSut(repository, audit).CorrectClassificationAsync(
            new CorrectParsedCandidateClassificationCommand(
                Reviewer(), "package-1", "candidate-1", 0, ParsedExamClassification.Reading), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, candidate.Version);
        Assert.Equal([0], repository.ExpectedVersions);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditAction.ParsedCandidateCorrected, entry.Action);
        Assert.Equal("Classification", entry.Detail["field"]);
    }

    [Fact]
    public async Task Rejection_persists_actor_and_time()
    {
        var candidate = CreateCandidate();
        var repository = new Repository(candidate);
        var audit = new AuditLog();

        var result = await CreateSut(repository, audit).RejectAsync(
            new RejectParsedCandidateCommand(Reviewer(), "package-1", "candidate-1", 0), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParsedCandidateStatus.Rejected, candidate.Status);
        Assert.Equal(new UserId("reviewer-1"), candidate.RejectedBy);
        Assert.Equal(Now, candidate.RejectedAt);
        Assert.Equal(AuditAction.ParsedCandidateRejected, Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task Confirmation_is_audited_once_and_retry_is_idempotent()
    {
        var candidate = CreateCandidate(ParsedExamClassification.Reading);
        var repository = new Repository(candidate);
        var audit = new AuditLog();
        var sut = CreateSut(repository, audit);

        var first = await sut.ConfirmAsync(
            new ConfirmParsedCandidateCommand(Reviewer(), "package-1", "candidate-1", 0), default);
        var retry = await sut.ConfirmAsync(
            new ConfirmParsedCandidateCommand(Reviewer(), "package-1", "candidate-1", 0), default);

        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.Equal(ParsedCandidateStatus.Confirmed, candidate.Status);
        Assert.Equal(new UserId("reviewer-1"), candidate.ConfirmedBy);
        Assert.Equal(Now, candidate.ConfirmedAt);
        Assert.Equal([0], repository.ExpectedVersions);
        Assert.Equal(AuditAction.ParsedCandidateConfirmed, Assert.Single(audit.Entries).Action);
    }

    private static ReviewParsedExamCandidate CreateSut(Repository repository, AuditLog audit) =>
        new(repository, audit, new FixedClock());

    private static ParsedCandidateActor Reviewer() => Actor([PermissionKeys.ExamReview]);

    private static ParsedCandidateActor Actor(IReadOnlyCollection<string> permissions) =>
        new(new UserId("reviewer-1"), "reviewer@example.test", permissions);

    private static ParsedExamCandidate CreateCandidate(
        ParsedExamClassification classification = ParsedExamClassification.NeedsReview)
    {
        var source = new ParsedSourceProvenance("exam.pdf", 1, "Reading", null);
        var question = new ParsedQuestionCandidate(
            "q1", 1, QuestionType.MultipleChoice, "Choose one.",
            [new ParsedQuestionOptionCandidate("A", "Answer")],
            new ParsedAnswerKeyCandidate(["A"], null), source);
        var part = new ParsedPartCandidate("p1", 1, "Part 1", null, [question], source);
        var module = new ParsedModuleCandidate(
            ExamModule.Reading, classification, 0.8m, [part], source);

        return ParsedExamCandidate.Create(
            "candidate-1", "package-1", "Mock exam", classification, 0.8m, [module], [source]);
    }
}
